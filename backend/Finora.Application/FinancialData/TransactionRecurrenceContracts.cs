using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;

namespace Finora.Application.FinancialData;

// A projeção interna liga o candidato ao trio estável e à decisão vigente, sem copiar os fatos financeiros.
public sealed record PersistedRecurrenceCandidate(
    Guid Id,
    string PolicyVersion,
    IReadOnlyList<Guid> OccurrenceTransactionIds,
    bool IsCurrent,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastEvaluatedAtUtc,
    RecurrenceDecisionKind? Decision,
    DateTimeOffset? DecisionUpdatedAtUtc);

// Estados distinguem escolha inválida de candidato ausente sem revelar qualquer informação de titularidade.
public enum RecurrenceDecisionPersistenceStatus
{
    Updated,
    CandidateNotFound,
    InvalidRequest
}

// O port oferece persistência interna; rotas e DTOs HTTP permanecem para o gate de revisão da tarefa 4.1.
public interface ITransactionRecurrenceStore
{
    Task<IReadOnlyList<FinancialTransaction>> ReadTransactionsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task SynchronizeCandidatesAsync(
        Guid userId,
        string policyVersion,
        IReadOnlyList<TransactionRecurrenceCandidate> evaluatedCandidates,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PersistedRecurrenceCandidate>> ReadCandidatesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<RecurrenceDecisionPersistenceStatus> SaveDecisionAsync(
        Guid userId,
        Guid candidateId,
        RecurrenceDecisionKind kind,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken);
}
