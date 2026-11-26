using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Persiste resultados versionados por identidade de movimento e mantém a escolha do titular fora da sincronização.
public sealed class TransactionRecurrenceStore(FinoraDbContext dbContext) : ITransactionRecurrenceStore
{
    // O avaliador recebe somente movimentos do titular e a ordenação não participa da identidade do candidato.
    public async Task<IReadOnlyList<FinancialTransaction>> ReadTransactionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<FinancialTransaction>();
        }

        return await dbContext.Set<FinancialTransaction>()
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId)
            .OrderBy(transaction => transaction.Id)
            .ToArrayAsync(cancellationToken);
    }

    // Reavaliação serializada por titular faz upsert do trio e apenas desativa candidatos ausentes do resultado atual.
    public async Task SynchronizeCandidatesAsync(
        Guid userId,
        string policyVersion,
        IReadOnlyList<TransactionRecurrenceCandidate> evaluatedCandidates,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        ValidateEvaluationRequest(userId, policyVersion, evaluatedCandidates, evaluatedAtUtc);
        var evaluatedByIdentity = evaluatedCandidates.ToDictionary(
            GetIdentity,
            candidate => candidate);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockOwnerAsync(userId, cancellationToken);

        // A linha do titular funciona como mutex só durante a curta reconciliação persistente, não durante leitura externa.
        var existingCandidates = await dbContext.Set<RecurrenceCandidate>()
            .Where(candidate => candidate.UserId == userId && candidate.PolicyVersion == policyVersion)
            .ToArrayAsync(cancellationToken);
        foreach (var existing in existingCandidates)
        {
            var identity = GetIdentity(existing);
            var remainsCurrent = evaluatedByIdentity.Remove(identity);
            existing.RecordEvaluation(remainsCurrent, evaluatedAtUtc);
        }

        // Identidades ainda no conjunto representam novos trios; IDs de origem permanecem as únicas chaves do padrão.
        foreach (var (identity, candidate) in evaluatedByIdentity)
        {
            dbContext.Set<RecurrenceCandidate>().Add(RecurrenceCandidate.Create(
                Guid.NewGuid(),
                userId,
                identity.PolicyVersion,
                identity.FirstOccurrenceTransactionId,
                identity.SecondOccurrenceTransactionId,
                identity.ThirdOccurrenceTransactionId,
                evaluatedAtUtc));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // A consulta devolve também decisões de candidatos inativos para que reavaliação não esconda escolhas anteriores.
    public async Task<IReadOnlyList<PersistedRecurrenceCandidate>> ReadCandidatesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<PersistedRecurrenceCandidate>();
        }

        var candidates = await dbContext.Set<RecurrenceCandidate>()
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .OrderBy(candidate => candidate.PolicyVersion)
            .ThenBy(candidate => candidate.CreatedAtUtc)
            .ThenBy(candidate => candidate.Id)
            .ToArrayAsync(cancellationToken);
        var candidateIds = candidates.Select(candidate => candidate.Id).ToArray();
        var decisions = await dbContext.Set<RecurrenceDecision>()
            .AsNoTracking()
            .Where(decision => decision.UserId == userId && candidateIds.Contains(decision.CandidateId))
            .ToDictionaryAsync(decision => decision.CandidateId, cancellationToken);

        return candidates.Select(candidate =>
        {
            decisions.TryGetValue(candidate.Id, out var decision);
            return new PersistedRecurrenceCandidate(
                candidate.Id,
                candidate.PolicyVersion,
                Array.AsReadOnly(new[]
                {
                    candidate.FirstOccurrenceTransactionId,
                    candidate.SecondOccurrenceTransactionId,
                    candidate.ThirdOccurrenceTransactionId
                }),
                candidate.IsCurrent,
                candidate.CreatedAtUtc,
                candidate.LastEvaluatedAtUtc,
                decision?.Kind,
                decision?.UpdatedAtUtc);
        }).ToArray();
    }

    // A decisão só muda por chamada explícita; o lock no candidato evita primeira escrita concorrente e lost update.
    public async Task<RecurrenceDecisionPersistenceStatus> SaveDecisionAsync(
        Guid userId,
        Guid candidateId,
        RecurrenceDecisionKind kind,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty
            || candidateId == Guid.Empty
            || !Enum.IsDefined(kind)
            || updatedAtUtc.Offset != TimeSpan.Zero)
        {
            return RecurrenceDecisionPersistenceStatus.InvalidRequest;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var candidate = await dbContext.Set<RecurrenceCandidate>()
            .FromSqlInterpolated($"""
                SELECT * FROM "RecurrenceCandidate"
                WHERE "Id" = {candidateId} AND "UserId" = {userId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            return RecurrenceDecisionPersistenceStatus.CandidateNotFound;
        }

        var existing = await dbContext.Set<RecurrenceDecision>()
            .SingleOrDefaultAsync(decision => decision.CandidateId == candidateId && decision.UserId == userId,
                cancellationToken);
        if (existing is null)
        {
            dbContext.Set<RecurrenceDecision>().Add(RecurrenceDecision.Create(candidateId, userId, kind, updatedAtUtc));
        }
        else
        {
            existing.ChangeKind(kind, updatedAtUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RecurrenceDecisionPersistenceStatus.Updated;
    }

    // Reavaliação aceita somente a versão disponível e trios produzidos para o mesmo titular/versão.
    private static void ValidateEvaluationRequest(
        Guid userId,
        string policyVersion,
        IReadOnlyList<TransactionRecurrenceCandidate> evaluatedCandidates,
        DateTimeOffset evaluatedAtUtc)
    {
        if (userId == Guid.Empty
            || !string.Equals(policyVersion, TransactionRecurrenceEvaluator.PolicyVersion, StringComparison.Ordinal)
            || evaluatedCandidates is null
            || evaluatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A avaliação de recorrência não atende à versão/titular disponíveis.");
        }

        var identities = new HashSet<CandidateIdentity>();
        foreach (var candidate in evaluatedCandidates)
        {
            if (candidate.UserId != userId
                || !string.Equals(candidate.PolicyVersion, policyVersion, StringComparison.Ordinal)
                || candidate.OccurrenceTransactionIds.Count != 3
                || candidate.OccurrenceTransactionIds.Any(id => id == Guid.Empty)
                || candidate.OccurrenceTransactionIds.Distinct().Count() != 3
                || !identities.Add(GetIdentity(candidate)))
            {
                throw new ArgumentException("O conjunto avaliado contém titular, versão ou trio inválido.", nameof(evaluatedCandidates));
            }
        }
    }

    // O lock na linha Identity serializa reavaliações simultâneas do mesmo titular entre instâncias da aplicação.
    private async Task LockOwnerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var owner = await dbContext.Users.FromSqlInterpolated($"""
            SELECT * FROM "AspNetUsers" WHERE "Id" = {userId} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (owner is null)
        {
            throw new InvalidOperationException("A reavaliação exige um titular persistido.");
        }
    }

    // A chave lógica usa política e IDs internos cronológicos, nunca campos mutáveis da origem.
    private static CandidateIdentity GetIdentity(TransactionRecurrenceCandidate candidate) => new(
        candidate.PolicyVersion,
        candidate.OccurrenceTransactionIds[0],
        candidate.OccurrenceTransactionIds[1],
        candidate.OccurrenceTransactionIds[2]);

    // O mesmo formato lógico permite comparar linhas persistidas e saídas novas sem serialização textual.
    private static CandidateIdentity GetIdentity(RecurrenceCandidate candidate) => new(
        candidate.PolicyVersion,
        candidate.FirstOccurrenceTransactionId,
        candidate.SecondOccurrenceTransactionId,
        candidate.ThirdOccurrenceTransactionId);

    private readonly record struct CandidateIdentity(
        string PolicyVersion,
        Guid FirstOccurrenceTransactionId,
        Guid SecondOccurrenceTransactionId,
        Guid ThirdOccurrenceTransactionId);
}
