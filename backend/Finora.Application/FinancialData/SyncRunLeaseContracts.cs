namespace Finora.Application.FinancialData;

// O claim carrega somente a geração necessária à renovação/fencing, sem credenciais ou fatos financeiros.
public sealed record SyncRunLeaseGrant(
    Guid ConnectionId,
    Guid UserId,
    Guid SyncRunId,
    int AttemptNumber,
    long FencingToken,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateOnly RequestedFrom,
    DateOnly? RequestedToExclusive,
    DateTimeOffset AttemptStartedAtUtc,
    DateTimeOffset AttemptDeadlineAtUtc);

public enum SyncRunLeaseAcquisitionStatus
{
    Acquired,
    Busy,
    NotEligible,
    RunUnavailable,
    AlreadyTerminal
}

// Busy não revela a identidade ou a validade detalhada da posse atual.
public sealed record SyncRunLeaseAcquisitionResult(
    SyncRunLeaseAcquisitionStatus Status,
    SyncRunLeaseGrant? Grant = null);

/// <summary>Persiste aquisição, renovação e fencing em transações curtas coordenadas pela conexão.</summary>
public interface ISyncRunLeaseStore
{
    Task<SyncRunLeaseAcquisitionResult> AcquireAsync(
        Guid syncRunId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<SyncRunLeaseGrant?> RenewAsync(
        SyncRunLeaseGrant grant,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<bool> ScheduleRetryAsync(
        SyncRunLeaseGrant grant,
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        string failureCode,
        int? pagesRead,
        int? receivedCount,
        CancellationToken cancellationToken);

    Task<bool> CompleteFailureAsync(
        SyncRunLeaseGrant grant,
        DateTimeOffset finishedAtUtc,
        string failureCode,
        int? pagesRead,
        int? receivedCount,
        DateTimeOffset? nextAttemptAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>Renova a posse fora das chamadas externas e cancela a tentativa se lease ou deadline deixarem de valer.</summary>
public interface ISyncRunLeaseExecutionGuard
{
    Task<T> ExecuteWithLeaseAsync<T>(
        SyncRunLeaseGrant grant,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);
}
