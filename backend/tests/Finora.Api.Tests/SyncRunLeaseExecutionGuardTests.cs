using Finora.Application.FinancialData;
using Finora.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica o watchdog de lease/deadline com relógio real curto e store sintético, sem PostgreSQL ou broker.
[TestClass]
public sealed class SyncRunLeaseExecutionGuardTests
{
    // O guard só conclui após obter renovação, mantendo o monitor ativo até o fim da operação.
    [TestMethod]
    public async Task SuccessfulRenewalKeepsTheOperationRunning()
    {
        var store = new RecordingLeaseStore((grant, _) => Task.FromResult<SyncRunLeaseGrant?>(
            grant with { ExpiresAtUtc = grant.ExpiresAtUtc.AddSeconds(30) }));
        using var services = CreateServices(store);
        var guard = new SyncRunLeaseExecutionGuard(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(10));

        var result = await guard.ExecuteWithLeaseAsync(
            CreateGrant(),
            async cancellationToken =>
            {
                await store.Renewed.Task.WaitAsync(cancellationToken);
                return "continued";
            },
            CancellationToken.None);

        Assert.AreEqual("continued", result);
        Assert.AreEqual(1, store.RenewalCount);
    }

    // Perda da renovação cancela o token do importador e impede que ele retorne um resultado de sucesso.
    [TestMethod]
    public async Task FailedRenewalCancelsTheActiveOperationAndFailsClosed()
    {
        var store = new RecordingLeaseStore((_, _) => Task.FromResult<SyncRunLeaseGrant?>(null));
        using var services = CreateServices(store);
        var guard = new SyncRunLeaseExecutionGuard(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(10));
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsExactlyAsync<SyncRunLeaseLostException>(async () =>
            await guard.ExecuteWithLeaseAsync(
                CreateGrant(),
                async cancellationToken =>
                {
                    operationStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return true;
                },
                CancellationToken.None));

        Assert.IsTrue(operationStarted.Task.IsCompleted);
        Assert.AreEqual(1, store.RenewalCount);
    }

    // O prazo da tentativa interrompe a coleta independentemente do próximo tick de renovação.
    [TestMethod]
    public async Task DeadlineCancelsTheOperationWithoutWaitingForLeaseExpiry()
    {
        var store = new RecordingLeaseStore((grant, _) => Task.FromResult<SyncRunLeaseGrant?>(grant));
        using var services = CreateServices(store);
        var guard = new SyncRunLeaseExecutionGuard(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            TimeSpan.FromSeconds(5));
        var grant = CreateGrant() with { AttemptDeadlineAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(75) };

        await Assert.ThrowsExactlyAsync<SyncRunAttemptDeadlineExceededException>(async () =>
            await guard.ExecuteWithLeaseAsync(
                grant,
                async cancellationToken =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return true;
                },
                CancellationToken.None));

        Assert.AreEqual(0, store.RenewalCount);
    }

    // O provider fornece scopes reais para que cada renovação resolva o contrato scoped usado em produção.
    private static ServiceProvider CreateServices(RecordingLeaseStore store) =>
        new ServiceCollection()
            .AddSingleton<ISyncRunLeaseStore>(store)
            .BuildServiceProvider();

    // Datas coerentes reproduzem a forma validada pelo store sem usar dados de usuário ou conexão reais.
    private static SyncRunLeaseGrant CreateGrant()
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        return new SyncRunLeaseGrant(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            AttemptNumber: 1,
            FencingToken: 1,
            AcquiredAtUtc: startedAtUtc,
            ExpiresAtUtc: startedAtUtc.AddSeconds(120),
            RequestedFrom: new DateOnly(2025, 1, 1),
            RequestedToExclusive: new DateOnly(2026, 1, 1),
            AttemptStartedAtUtc: startedAtUtc,
            AttemptDeadlineAtUtc: startedAtUtc.AddMinutes(10));
    }

    // O fake registra renovação e devolve o resultado definido pelo cenário sem persistir estado compartilhado.
    private sealed class RecordingLeaseStore(
        Func<SyncRunLeaseGrant, DateTimeOffset, Task<SyncRunLeaseGrant?>> renew)
        : ISyncRunLeaseStore
    {
        private int _renewalCount;
        public int RenewalCount => Volatile.Read(ref _renewalCount);
        public TaskCompletionSource Renewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SyncRunLeaseAcquisitionResult> AcquireAsync(
            Guid syncRunId,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<SyncRunLeaseGrant?> RenewAsync(
            SyncRunLeaseGrant grant,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _renewalCount);
            var result = await renew(grant, nowUtc);
            Renewed.TrySetResult();
            return result;
        }

        public Task<bool> ScheduleRetryAsync(
            SyncRunLeaseGrant grant,
            DateTimeOffset nowUtc,
            DateTimeOffset nextAttemptAtUtc,
            string failureCode,
            int? pagesRead,
            int? receivedCount,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CompleteFailureAsync(
            SyncRunLeaseGrant grant,
            DateTimeOffset finishedAtUtc,
            string failureCode,
            int? pagesRead,
            int? receivedCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
