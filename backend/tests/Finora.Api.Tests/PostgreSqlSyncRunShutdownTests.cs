using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Prova cancelamento de coleta e retomada da mesma SyncRun em PostgreSQL descartável, sem broker nem provedor real.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunShutdownTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly RequestedFrom = new(2025, 11, 23);
    private static readonly DateOnly RequestedToExclusive = new(2026, 11, 24);

    // Cancelar a coleta propaga shutdown, mantém tentativa sem resultado e permite takeover após lease expirar.
    [TestMethod]
    public async Task CancelledAttemptRemainsRecoverableAndRestartResumesSameRun()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await using var database = DisposablePostgresMigrationFixture.CreateValidated();
        await database.ResetToEmptySchemaAsync();
        Assert.IsTrue(await database.Context.Database.EnsureCreatedAsync());

        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var syncRunId = Guid.NewGuid();
        database.Context.Users.Add(new ApplicationUser(userId)
        {
            UserName = "sync-shutdown@example.invalid",
            NormalizedUserName = "SYNC-SHUTDOWN@EXAMPLE.INVALID",
            Email = "sync-shutdown@example.invalid",
            NormalizedEmail = "SYNC-SHUTDOWN@EXAMPLE.INVALID"
        });
        database.Context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(connectionId, userId, "pluggy", "synthetic-shutdown-item"));
        database.Context.Set<SyncRun>().Add(SyncRun.Create(
            syncRunId,
            userId,
            connectionId,
            InitialTime,
            RequestedFrom,
            RequestedToExclusive,
            executionMessageEnqueuedAtUtc: InitialTime));
        await database.Context.SaveChangesAsync();

        var executor = new BlockingAttemptExecutor();
        var services = new ServiceCollection();
        services.AddDbContext<FinoraDbContext>(options =>
            FinoraDbContextConfiguration.Configure(options, settings.ConnectionString));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(InitialTime));
        services.AddFinoraSyncRunAcceptancePersistence();
        services.AddSingleton<ISyncRunAttemptExecutor>(executor);
        await using var provider = services.BuildServiceProvider();

        using (var cancellation = new CancellationTokenSource())
        {
            await using var scope = provider.CreateAsyncScope();
            var handler = new ExecuteSyncRunHandler(
                scope.ServiceProvider.GetRequiredService<ISyncRunLeaseStore>(),
                scope.ServiceProvider.GetRequiredService<ISyncRunLeaseExecutionGuard>(),
                executor,
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            var attempt = handler.Handle(new ExecuteSyncRunCommand(syncRunId), cancellation.Token);
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await attempt);
        }

        await using (var afterShutdown = CreateContext(settings.ConnectionString))
        {
            var interrupted = await afterShutdown.Set<SyncRun>().AsNoTracking().SingleAsync();
            Assert.AreEqual(syncRunId, interrupted.Id);
            Assert.AreEqual(SyncRunState.Running, interrupted.State);
            Assert.AreEqual(1, interrupted.AttemptNumber);
            Assert.IsNull(interrupted.FinishedAtUtc);
            Assert.IsNull(interrupted.FailureCode);
            Assert.IsNull(interrupted.ExecutionMessageEnqueuedAtUtc);
            Assert.AreEqual(RequestedFrom, interrupted.RequestedFrom);
            Assert.AreEqual(RequestedToExclusive, interrupted.RequestedToExclusive);
            Assert.IsNull(await afterShutdown.Set<FinancialConnection>().AsNoTracking()
                .Select(connection => connection.LastSuccessfulImportAtUtc)
                .SingleAsync());
            Assert.AreEqual(1, await afterShutdown.Set<SyncRun>().CountAsync());
        }

        // Um scope novo representa a API reiniciada; depois da expiração, o store retoma o mesmo ID e incrementa fencing.
        await using (var restartedContext = CreateContext(settings.ConnectionString))
        {
            var resumed = await new SyncRunLeaseStore(restartedContext).AcquireAsync(
                syncRunId,
                InitialTime.AddSeconds(121),
                CancellationToken.None);

            Assert.AreEqual(SyncRunLeaseAcquisitionStatus.Acquired, resumed.Status);
            Assert.AreEqual(syncRunId, resumed.Grant!.SyncRunId);
            Assert.AreEqual(2, resumed.Grant.AttemptNumber);
            Assert.AreEqual(2, resumed.Grant.FencingToken);
            Assert.AreEqual(RequestedFrom, resumed.Grant.RequestedFrom);
            Assert.AreEqual(RequestedToExclusive, resumed.Grant.RequestedToExclusive);
            var resumedRun = await restartedContext.Set<SyncRun>().AsNoTracking().SingleAsync();
            Assert.AreEqual(SyncRunState.Running, resumedRun.State);
            Assert.AreEqual(2, resumedRun.AttemptNumber);
            Assert.AreEqual(syncRunId, resumedRun.Id);
        }
    }

    // As opções de leitura e retomada usam apenas a connection string descartável já conferida pela fixture.
    private static FinoraDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, connectionString);
        return new FinoraDbContext(options.Options);
    }

    // O instante fixo permite avançar explicitamente a posse sem esperar o tempo real.
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    // O fake segura a execução como uma coleta externa bloqueante e termina somente pelo token de shutdown.
    private sealed class BlockingAttemptExecutor : ISyncRunAttemptExecutor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TransactionImportResult> ExecuteAttemptAsync(
            SyncRunLeaseGrant execution,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new TransactionImportResult(true, null);
        }
    }
}
