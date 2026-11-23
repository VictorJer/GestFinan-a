using Finora.Application.FinancialData;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Infrastructure.Persistence;

/// <summary>Renova em scopes independentes do importador e cancela a tentativa quando lease/deadline deixam de valer.</summary>
public sealed class SyncRunLeaseExecutionGuard(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    TimeSpan? renewalInterval = null) : ISyncRunLeaseExecutionGuard
{
    private readonly TimeSpan _renewalInterval = renewalInterval ?? TimeSpan.FromSeconds(30);

    public async Task<T> ExecuteWithLeaseAsync<T>(
        SyncRunLeaseGrant grant,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(operation);
        if (grant.SyncRunId == Guid.Empty || grant.ConnectionId == Guid.Empty || grant.UserId == Guid.Empty
            || grant.AttemptNumber < 1 || grant.FencingToken < 1
            || grant.AttemptDeadlineAtUtc <= grant.AttemptStartedAtUtc
            || _renewalInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("O contexto da tentativa não é válido.", nameof(grant));
        }

        using var monitorCancellation = new CancellationTokenSource();
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorFailure = new TaskCompletionSource<SyncRunLeaseExecutionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // O monitor usa scopes/DbContexts novos, pois a leitura HTTP e a promoção pertencem ao scope do importador.
        void SignalFailure(SyncRunLeaseExecutionFailure failure)
        {
            if (monitorFailure.TrySetResult(failure))
            {
                monitorCancellation.Cancel();
            }
        }

        var renewalTask = RenewUntilFailureAsync(grant, monitorCancellation.Token, SignalFailure);
        var deadlineTask = CancelAtDeadlineAsync(grant, monitorCancellation.Token, SignalFailure);
        try
        {
            var operationTask = operation(operationCancellation.Token);
            var completed = await Task.WhenAny(operationTask, monitorFailure.Task);
            if (monitorFailure.Task.IsCompleted)
            {
                operationCancellation.Cancel();
                await DrainOperationAsync(operationTask);
                await Task.WhenAll(renewalTask, deadlineTask);
                ThrowForFailure(await monitorFailure.Task);
            }

            var result = await operationTask;
            monitorCancellation.Cancel();
            await Task.WhenAll(renewalTask, deadlineTask);
            if (monitorFailure.Task.IsCompleted)
            {
                ThrowForFailure(await monitorFailure.Task);
            }

            return result;
        }
        finally
        {
            monitorCancellation.Cancel();
            operationCancellation.Cancel();
            await Task.WhenAll(renewalTask, deadlineTask);
        }
    }

    // Cada renovação possui um scope PostgreSQL separado e atualiza o claim até falha ou parada do importador.
    private async Task RenewUntilFailureAsync(
        SyncRunLeaseGrant grant,
        CancellationToken cancellationToken,
        Action<SyncRunLeaseExecutionFailure> signalFailure)
    {
        using var timer = new PeriodicTimer(_renewalInterval, timeProvider);
        var currentGrant = grant;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
                if (nowUtc >= currentGrant.AttemptDeadlineAtUtc)
                {
                    signalFailure(SyncRunLeaseExecutionFailure.DeadlineExceeded);
                    return;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var leaseStore = scope.ServiceProvider.GetRequiredService<ISyncRunLeaseStore>();
                var renewed = await leaseStore.RenewAsync(currentGrant, nowUtc, cancellationToken);
                if (renewed is null)
                {
                    signalFailure(SyncRunLeaseExecutionFailure.LeaseLost);
                    return;
                }

                currentGrant = renewed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // O fim do importador ou outro watchdog interrompe o loop sem criar nova tentativa.
        }
        catch (Exception)
        {
            // Falha/timeout de persistência da renovação invalida a tentativa de forma fechada.
            signalFailure(SyncRunLeaseExecutionFailure.LeaseLost);
        }
    }

    // O deadline cancela o trabalho pelo relógio injetado mesmo sem esperar o próximo tick de renovação.
    private async Task CancelAtDeadlineAsync(
        SyncRunLeaseGrant grant,
        CancellationToken cancellationToken,
        Action<SyncRunLeaseExecutionFailure> signalFailure)
    {
        try
        {
            var remaining = grant.AttemptDeadlineAtUtc - timeProvider.GetUtcNow().ToUniversalTime();
            if (remaining <= TimeSpan.Zero)
            {
                signalFailure(SyncRunLeaseExecutionFailure.DeadlineExceeded);
                return;
            }

            await Task.Delay(remaining, timeProvider, cancellationToken);
            signalFailure(SyncRunLeaseExecutionFailure.DeadlineExceeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // O trabalho concluiu ou outro limite já sinalizou cancelamento.
        }
    }

    // Uma chamada externa cooperativa termina pelo token; exceção de negócio original não substitui o fencing perdido.
    private static async Task DrainOperationAsync(Task operationTask)
    {
        try
        {
            await operationTask;
        }
        catch (Exception)
        {
            // A causa autoritativa já é a perda da posse ou do deadline, sem eco de exceções externas.
        }
    }

    // Converte somente dois estados internos fixos; mensagens externas nunca atravessam a fronteira Application.
    private static void ThrowForFailure(SyncRunLeaseExecutionFailure failure)
    {
        if (failure == SyncRunLeaseExecutionFailure.DeadlineExceeded)
        {
            throw new SyncRunAttemptDeadlineExceededException();
        }

        throw new SyncRunLeaseLostException();
    }

    private enum SyncRunLeaseExecutionFailure
    {
        LeaseLost,
        DeadlineExceeded
    }
}
