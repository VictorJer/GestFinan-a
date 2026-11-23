using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Messaging;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finora.Infrastructure.Persistence;

/// <summary>Persiste a aceitação e resolve a colisão conhecida da chave sem revelar execuções de outra identidade.</summary>
public sealed class SyncRunAcceptanceStore(
    FinoraDbContext dbContext,
    SyncRunExecutionMessageSender? executionMessageSender = null,
    TimeProvider? timeProvider = null,
    ConnectionUpdatePolicyOptions? policyOptions = null) : ISyncRunAcceptanceStore
{
    private const string IdempotencyIndexName = "UX_SyncRun_IdempotencyKeyHash";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConnectionUpdatePolicyOptions _policyOptions = policyOptions ?? new ConnectionUpdatePolicyOptions();

    public async Task<SyncRunAcceptanceResult> AcceptOrGetAsync(
        SyncRun candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.IdempotencyKeyHash is null || candidate.RequestFingerprint is null)
        {
            throw new ArgumentException("A execução candidata precisa carregar os fingerprints de idempotência.", nameof(candidate));
        }

        // Replays são resolvidos antes de recalcular titularidade ou a janela automática da solicitação.
        var existing = await FindByKeyHashAsync(candidate.IdempotencyKeyHash, cancellationToken);
        if (existing is not null)
        {
            return SyncRunAcceptancePolicy.ResolveExisting(existing, candidate);
        }

        // Sem bus outbox configurado, o adapter falha fechado em vez de persistir uma aceitação sem obrigação.
        var messageSender = executionMessageSender
            ?? throw new InvalidOperationException("A outbox de execução não está configurada.");
        try
        {
            return await PersistCandidateWithOutboxAsync(candidate, messageSender, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyCollision(exception))
        {
            // A constraint única decide a corrida; remove o par perdedor do tracking antes de reler o vencedor.
            DetachUncommittedAcceptance(candidate);
            existing = await FindByKeyHashAsync(candidate.IdempotencyKeyHash, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return SyncRunAcceptancePolicy.ResolveExisting(existing, candidate);
        }
        catch
        {
            // Uma falha não convergida não pode deixar a candidata no tracking para um SaveChanges posterior do escopo.
            DetachUncommittedAcceptance(candidate);
            throw;
        }
    }

    // Enfileira antes de SaveChanges para que entidade e comando sejam gravados no contexto/transação EF correntes.
    private async Task<SyncRunAcceptanceResult> PersistCandidateWithOutboxAsync(
        SyncRun candidate,
        SyncRunExecutionMessageSender messageSender,
        CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var commitOutcomeIndeterminate = false;
        try
        {
            // O lock de linha na conexão serializa novas intenções sem depender de locks em memória entre hosts.
            var ownedConnections = await dbContext.Set<FinancialConnection>()
                .FromSqlInterpolated($"""SELECT * FROM "FinancialConnection" WHERE "Id" = {candidate.ConnectionId} AND "UserId" = {candidate.UserId} FOR UPDATE""")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var ownedConnection = ownedConnections.SingleOrDefault();
            if (ownedConnection is null)
            {
                return new SyncRunAcceptanceResult(SyncRunAcceptanceStatus.ConnectionUnavailable);
            }

            // O instante autoritativo é capturado somente depois de obter o lock persistente da conexão.
            var evaluatedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();

            // Após aguardar o lock, a releitura distingue replay da mesma chave de uma nova intenção concorrente.
            var existing = await FindByKeyHashAsync(candidate.IdempotencyKeyHash!, cancellationToken);
            if (existing is not null)
            {
                return SyncRunAcceptancePolicy.ResolveExisting(existing, candidate);
            }

            // Pending e Running ocupam a conexão; duplicatas preexistentes indicam corrupção e falham fechadas.
            var activeRuns = await dbContext.Set<SyncRun>()
                .AsNoTracking()
                .Where(run => run.ConnectionId == candidate.ConnectionId
                    && run.UserId == candidate.UserId
                    && (run.State == SyncRunState.Pending || run.State == SyncRunState.Running))
                .OrderBy(run => run.CreatedAtUtc)
                .ThenBy(run => run.Id)
                .Take(2)
                .ToArrayAsync(cancellationToken);
            if (activeRuns.Length > 1)
            {
                throw new InvalidOperationException("A conexão possui mais de uma execução ativa.");
            }

            if (activeRuns.Length == 1)
            {
                return SyncRunAcceptancePolicy.ToActiveExecutionConflict(activeRuns[0]);
            }

            // O fingerprint preserva a distinção entre entrada e manual sem alterar os registros históricos.
            var isOnEntryRequest = SyncRunIdempotency.IsOnEntryRequest(
                candidate.ConnectionId,
                candidate.RequestFingerprint);

            // Um único registro ordenado fornece o último aceite para cooldown e falha terminal de autorização.
            var latestIntent = await dbContext.Set<SyncRun>()
                .AsNoTracking()
                .Where(run => run.ConnectionId == candidate.ConnectionId && run.UserId == candidate.UserId)
                .OrderByDescending(run => run.CreatedAtUtc)
                .ThenByDescending(run => run.Id)
                .Select(run => new
                {
                    run.CreatedAtUtc,
                    run.State,
                    run.FailureCode
                })
                .FirstOrDefaultAsync(cancellationToken);

            // Falha AUTH_REQUIRED terminal suspende novas entradas até ação manual, sem consultar nem renovar o provedor.
            if (isOnEntryRequest
                && latestIntent is not null
                && ConnectionUpdatePolicy.IsAutomaticEntrySuppressed(latestIntent.State, latestIntent.FailureCode))
            {
                return new SyncRunAcceptanceResult(SyncRunAcceptanceStatus.ReauthorizationRequired);
            }

            // O último sucesso decide apenas a entrada automática; a intenção manual continua disponível por pedido explícito.
            if (isOnEntryRequest
                && !ConnectionUpdatePolicy.IsUpdateRequired(
                    ownedConnection.LastSuccessfulImportAtUtc,
                    evaluatedAtUtc,
                    _policyOptions.StaleAfter))
            {
                return new SyncRunAcceptanceResult(SyncRunAcceptanceStatus.NotRequired);
            }

            if (latestIntent is not null)
            {
                // O cooldown usa CreatedAtUtc da intenção aceita, mesmo quando a execução terminou em falha.
                var nextAllowedAtUtc = latestIntent.CreatedAtUtc.Add(_policyOptions.MinimumRequestInterval);
                if (evaluatedAtUtc < nextAllowedAtUtc)
                {
                    return new SyncRunAcceptanceResult(
                        SyncRunAcceptanceStatus.MinimumIntervalActive,
                        NextAllowedAtUtc: nextAllowedAtUtc);
                }
            }

            // A janela, CreatedAtUtc e mensagem inicial usam um único instante após a serialização PostgreSQL.
            var acceptedCandidate = candidate.RecreateAcceptanceAt(evaluatedAtUtc);

            dbContext.Set<SyncRun>().Add(acceptedCandidate);
            if (!acceptedCandidate.TryMarkExecutionMessageEnqueued(acceptedCandidate.CreatedAtUtc))
            {
                throw new InvalidOperationException("A execução aceita não pode marcar sua mensagem inicial.");
            }

            await messageSender.SendAsync(acceptedCandidate.Id, acceptedCandidate.CreatedAtUtc, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Uma exceção durante CommitAsync não prova rollback; o chamador deve repetir a mesma chave para reconciliar.
            try
            {
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                commitOutcomeIndeterminate = true;
                throw new SyncRunAcceptanceIndeterminateException(exception);
            }

            return SyncRunAcceptancePolicy.ToResult(acceptedCandidate, SyncRunAcceptanceStatus.Accepted);
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch when (commitOutcomeIndeterminate)
            {
                // Falha no cleanup não pode reclassificar o commit já incerto nem afirmar que houve rollback.
            }
        }
    }

    // Descarta estado rastreado desta tentativa; o detach não prova rollback nem altera o resultado no banco.
    private void DetachUncommittedAcceptance(SyncRun candidate)
    {
        // A entidade retemporizada preserva o ID da candidata recebida pelo handler.
        foreach (var candidateEntry in dbContext.ChangeTracker.Entries<SyncRun>()
                     .Where(entry => entry.Entity.Id == candidate.Id && entry.State == EntityState.Added).ToArray())
        {
            candidateEntry.State = EntityState.Detached;
        }

        foreach (var entry in dbContext.ChangeTracker.Entries<OutboxMessage>()
                     .Where(entry => entry.State == EntityState.Added).ToArray())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in dbContext.ChangeTracker.Entries<OutboxState>()
                     .Where(entry => entry.State == EntityState.Added).ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    // A chave tem índice global; a projeção sem tracking retorna apenas a execução já vinculada.
    private Task<SyncRun?> FindByKeyHashAsync(string keyHash, CancellationToken cancellationToken) =>
        dbContext.Set<SyncRun>()
            .AsNoTracking()
            .SingleOrDefaultAsync(run => run.IdempotencyKeyHash == keyHash, cancellationToken);

    // Somente conflito no índice de idempotência é convergido; violações de integridade diferentes continuam visíveis.
    private static bool IsIdempotencyKeyCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(postgresException.ConstraintName, IdempotencyIndexName, StringComparison.Ordinal);

}
