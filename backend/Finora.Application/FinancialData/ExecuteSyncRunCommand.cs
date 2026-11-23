using Finora.Application.Pluggy;
using MediatR;

namespace Finora.Application.FinancialData;

// A mensagem de transporte solicita a execução por ID; dados do titular e do intervalo vêm do registro persistido.
public sealed record ExecuteSyncRunCommand(Guid SyncRunId) : IRequest<ExecuteSyncRunResult>;

public enum ExecuteSyncRunStatus
{
    Executed,
    Busy,
    NotEligible,
    RunUnavailable,
    AlreadyTerminal,
    LeaseLost,
    AttemptDeadlineExceeded,
    HandoffFailed
}

// O resultado interno não contém o snapshot, somente o estado de coordenação e o resumo técnico sanitizado.
public sealed record ExecuteSyncRunResult(
    ExecuteSyncRunStatus Status,
    TransactionImportResult? ImportResult = null);

public sealed class SyncRunLeaseLostException()
    : InvalidOperationException("A tentativa perdeu sua posse persistida.");

public sealed class SyncRunAttemptDeadlineExceededException()
    : InvalidOperationException("O limite temporal da tentativa foi atingido.");

/// <summary>Executa uma tentativa usando contexto obtido da SyncRun e da posse persistida.</summary>
public sealed class ExecuteSyncRunHandler(
    ISyncRunLeaseStore leaseStore,
    ISyncRunLeaseExecutionGuard leaseGuard,
    ISyncRunAttemptExecutor attemptExecutor,
    TimeProvider timeProvider) : IRequestHandler<ExecuteSyncRunCommand, ExecuteSyncRunResult>
{
    public async Task<ExecuteSyncRunResult> Handle(
        ExecuteSyncRunCommand request,
        CancellationToken cancellationToken)
    {
        if (request.SyncRunId == Guid.Empty)
        {
            return new ExecuteSyncRunResult(ExecuteSyncRunStatus.RunUnavailable);
        }

        // A aquisição coordena por conexão e devolve owner, janela e tentativa lidos da SyncRun sob lock.
        var acquisition = await leaseStore.AcquireAsync(
            request.SyncRunId,
            timeProvider.GetUtcNow().ToUniversalTime(),
            cancellationToken);
        if (acquisition.Status != SyncRunLeaseAcquisitionStatus.Acquired || acquisition.Grant is null)
        {
            var status = acquisition.Status switch
            {
                SyncRunLeaseAcquisitionStatus.Busy => ExecuteSyncRunStatus.Busy,
                SyncRunLeaseAcquisitionStatus.NotEligible => ExecuteSyncRunStatus.NotEligible,
                SyncRunLeaseAcquisitionStatus.AlreadyTerminal => ExecuteSyncRunStatus.AlreadyTerminal,
                _ => ExecuteSyncRunStatus.RunUnavailable
            };
            return new ExecuteSyncRunResult(status);
        }

        // O guard renova em transações curtas e interrompe o importador ao perder posse ou alcançar o deadline.
        try
        {
            var result = await leaseGuard.ExecuteWithLeaseAsync(
                acquisition.Grant,
                token => attemptExecutor.ExecuteAttemptAsync(acquisition.Grant, token),
                cancellationToken);
            if (result.Succeeded)
            {
                return new ExecuteSyncRunResult(ExecuteSyncRunStatus.Executed, result);
            }

            // A conclusão de negócio só confirma a entrega após persistir retry limitado ou falha terminal.
            return await PersistFailureOutcomeAsync(acquisition.Grant, result, cancellationToken);
        }
        catch (SyncRunLeaseLostException)
        {
            return new ExecuteSyncRunResult(ExecuteSyncRunStatus.LeaseLost);
        }
        catch (SyncRunAttemptDeadlineExceededException)
        {
            return new ExecuteSyncRunResult(ExecuteSyncRunStatus.AttemptDeadlineExceeded);
        }
    }

    // A política separa falha de negócio, resultado incerto e handoff durável sem consumir tentativas de transporte.
    private async Task<ExecuteSyncRunResult> PersistFailureOutcomeAsync(
        SyncRunLeaseGrant grant,
        TransactionImportResult result,
        CancellationToken cancellationToken)
    {
        var failureCode = result.FailureCode ?? TransactionImportFailureCode.InvalidResponse;
        if (failureCode == TransactionImportFailureCode.LeaseLost)
        {
            return new ExecuteSyncRunResult(ExecuteSyncRunStatus.LeaseLost, result);
        }

        if (failureCode == TransactionImportFailureCode.AttemptDeadlineExceeded)
        {
            return new ExecuteSyncRunResult(ExecuteSyncRunStatus.AttemptDeadlineExceeded, result);
        }

        // Commit incerto não é reclassificado como rollback nem recebe handoff automático por uma leitura vazia.
        if (failureCode == TransactionImportFailureCode.CommitUncertain)
        {
            return new ExecuteSyncRunResult(ExecuteSyncRunStatus.HandoffFailed, result);
        }

        var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var persistedCode = SyncRunFailurePolicy.ToPersistedCode(failureCode);
        var retryAfterAtUtc = result.RetryAfterAtUtc?.ToUniversalTime();
        if (SyncRunFailurePolicy.ExceedsAutomaticRetryLimit(retryAfterAtUtc, nowUtc))
        {
            // O prazo original permanece consultável, mas a política aprovada bloqueia retry automático acima de 15 min.
            var completed = await leaseStore.CompleteFailureAsync(
                grant,
                nowUtc,
                persistedCode,
                result.PagesRead,
                result.ReceivedCount,
                retryAfterAtUtc,
                cancellationToken);
            return new ExecuteSyncRunResult(
                completed ? ExecuteSyncRunStatus.Executed : ExecuteSyncRunStatus.HandoffFailed,
                result);
        }

        if (SyncRunFailurePolicy.IsRetryable(failureCode)
            && grant.AttemptNumber < Finora.Domain.FinancialData.SyncRun.ApprovedAttemptLimit)
        {
            var nextAttemptAtUtc = SyncRunFailurePolicy.GetNextAttemptAtUtc(
                grant.AttemptNumber, nowUtc, retryAfterAtUtc);
            var scheduled = await leaseStore.ScheduleRetryAsync(
                grant,
                nowUtc,
                nextAttemptAtUtc,
                persistedCode,
                result.PagesRead,
                result.ReceivedCount,
                cancellationToken);
            return new ExecuteSyncRunResult(
                scheduled ? ExecuteSyncRunStatus.Executed : ExecuteSyncRunStatus.HandoffFailed,
                result);
        }

        var terminal = await leaseStore.CompleteFailureAsync(
            grant,
            nowUtc,
            persistedCode,
            result.PagesRead,
            result.ReceivedCount,
            nextAttemptAtUtc: null,
            cancellationToken);
        return new ExecuteSyncRunResult(
            terminal ? ExecuteSyncRunStatus.Executed : ExecuteSyncRunStatus.HandoffFailed,
            result);
    }
}
