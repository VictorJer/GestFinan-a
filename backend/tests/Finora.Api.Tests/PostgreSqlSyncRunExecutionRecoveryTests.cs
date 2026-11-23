using System.Data.Common;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Messaging;
using Finora.Infrastructure.Persistence;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida o reconciliador somente contra PostgreSQL descartável e transporte in-memory, sem executar importação.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunExecutionRecoveryTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    // Uma execução pendente vencida recebe exatamente uma obrigação; o marcador impede duplicata no ciclo seguinte.
    [TestMethod]
    public async Task DuePendingRunIsEnqueuedOnceWithoutExecutingIt()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(nowUtc: InitialTime, nextAttemptAtUtc: InitialTime);

        var firstPass = await fixture.ReconcileAsync();
        var secondPass = await fixture.ReconcileAsync();

        Assert.AreEqual(1, firstPass);
        Assert.AreEqual(0, secondPass);
        await using var context = CreateContext(fixture.ConnectionString);
        var run = await context.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(SyncRunState.Pending, run.State);
        Assert.AreEqual(0, run.AttemptNumber);
        Assert.AreEqual(InitialTime, run.ExecutionMessageEnqueuedAtUtc);
        Assert.AreEqual(1, await context.Set<OutboxMessage>().CountAsync());
        Assert.AreEqual(0, await context.Set<SyncRunLease>().CountAsync());
    }

    // O próximo prazo impede reenfileiramento antecipado; a mesma execução continua pendente e sem marcador.
    [TestMethod]
    public async Task PendingRunIsNotEnqueuedBeforeItsPersistedRetryDeadline()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(
            nowUtc: InitialTime,
            nextAttemptAtUtc: InitialTime.AddMinutes(5));

        Assert.AreEqual(0, await fixture.ReconcileAsync());

        await using var context = CreateContext(fixture.ConnectionString);
        var run = await context.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(SyncRunState.Pending, run.State);
        Assert.IsNull(run.ExecutionMessageEnqueuedAtUtc);
        Assert.AreEqual(0, await context.Set<OutboxMessage>().CountAsync());
    }

    // Duas instâncias que selecionam o mesmo ID convergem no lock da conexão para uma única mensagem outbox.
    [TestMethod]
    public async Task ConcurrentRecoveryPassesEnqueueOnlyOneMessage()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(
            nowUtc: InitialTime,
            nextAttemptAtUtc: InitialTime);

        var passResults = await Task.WhenAll(fixture.ReconcileAsync(), fixture.ReconcileAsync());

        Assert.AreEqual(1, passResults.Sum());
        await using var context = CreateContext(fixture.ConnectionString);
        Assert.AreEqual(InitialTime, (await context.Set<SyncRun>().AsNoTracking().SingleAsync())
            .ExecutionMessageEnqueuedAtUtc);
        Assert.AreEqual(1, await context.Set<OutboxMessage>().CountAsync());
    }

    // Lease ainda válida impede qualquer reenvio; lease expirada gera mensagem, mas não inicia tentativa no reconciliador.
    [TestMethod]
    public async Task RunningExecutionIsReenqueuedOnlyAfterItsLeaseExpires()
    {
        var activeNow = InitialTime.AddSeconds(30);
        await using (var activeFixture = await RecoveryFixture.CreateAsync(
                         nowUtc: activeNow,
                         runningAttemptNumber: 1,
                         leaseExpiresAtUtc: InitialTime.AddSeconds(150)))
        {
            Assert.AreEqual(0, await activeFixture.ReconcileAsync());
            await using var activeContext = CreateContext(activeFixture.ConnectionString);
            Assert.AreEqual(0, await activeContext.Set<OutboxMessage>().CountAsync());
            Assert.IsNull((await activeContext.Set<SyncRun>().AsNoTracking().SingleAsync())
                .ExecutionMessageEnqueuedAtUtc);
        }

        var expiredNow = InitialTime.AddSeconds(121);
        await using var expiredFixture = await RecoveryFixture.CreateAsync(
            nowUtc: expiredNow,
            runningAttemptNumber: 1,
            leaseExpiresAtUtc: InitialTime.AddSeconds(120));

        Assert.AreEqual(1, await expiredFixture.ReconcileAsync());

        await using var expiredContext = CreateContext(expiredFixture.ConnectionString);
        var recoveredRun = await expiredContext.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(SyncRunState.Running, recoveredRun.State);
        Assert.AreEqual(1, recoveredRun.AttemptNumber);
        Assert.AreEqual(expiredNow, recoveredRun.ExecutionMessageEnqueuedAtUtc);
        Assert.AreEqual(1, await expiredContext.Set<OutboxMessage>().CountAsync());
    }

    // Lease expirada após a terceira tentativa fecha a mesma execução com UNKNOWN e não cria outra mensagem.
    [TestMethod]
    public async Task AbandonedRunAtAttemptLimitBecomesUnknownWithoutReenqueue()
    {
        var recoveredAtUtc = InitialTime.AddMinutes(11);
        await using var fixture = await RecoveryFixture.CreateAsync(
            nowUtc: recoveredAtUtc,
            runningAttemptNumber: SyncRun.ApprovedAttemptLimit,
            leaseExpiresAtUtc: InitialTime.AddSeconds(120));

        Assert.AreEqual(1, await fixture.ReconcileAsync());

        await using var context = CreateContext(fixture.ConnectionString);
        var run = await context.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(SyncRunState.Failed, run.State);
        Assert.AreEqual(SyncRunFailureCodes.Unknown, run.FailureCode);
        Assert.AreEqual(SyncRun.ApprovedAttemptLimit, run.AttemptNumber);
        Assert.AreEqual(recoveredAtUtc, run.FinishedAtUtc);
        Assert.IsNull(run.ExecutionMessageEnqueuedAtUtc);
        Assert.AreEqual(0, await context.Set<OutboxMessage>().CountAsync());
    }

    // Falha antes do commit reverte marcador e mensagem; uma nova instância retoma a obrigação persistente.
    [TestMethod]
    public async Task FailedCommitRollsBackMarkerAndOutboxThenRestartRecovers()
    {
        var interceptor = new RecoveryCommitInterceptor();
        await using var fixture = await RecoveryFixture.CreateAsync(
            nowUtc: InitialTime,
            nextAttemptAtUtc: InitialTime,
            interceptor: interceptor);
        interceptor.FailBeforeNextCommit();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.ReconcileAsync());

        await using (var afterRollback = CreateContext(fixture.ConnectionString))
        {
            Assert.IsNull((await afterRollback.Set<SyncRun>().AsNoTracking().SingleAsync())
                .ExecutionMessageEnqueuedAtUtc);
            Assert.AreEqual(0, await afterRollback.Set<OutboxMessage>().CountAsync());
        }

        Assert.AreEqual(1, await fixture.ReconcileAsync());
        await using var afterRestart = CreateContext(fixture.ConnectionString);
        Assert.AreEqual(InitialTime, (await afterRestart.Set<SyncRun>().AsNoTracking().SingleAsync())
            .ExecutionMessageEnqueuedAtUtc);
        Assert.AreEqual(1, await afterRestart.Set<OutboxMessage>().CountAsync());
    }

    // Exceção após commit modela confirmação perdida: o reinício encontra marcador e não duplica a mensagem já gravada.
    [TestMethod]
    public async Task LostCommitConfirmationDoesNotDuplicateCommittedOutboxMessage()
    {
        var interceptor = new RecoveryCommitInterceptor();
        await using var fixture = await RecoveryFixture.CreateAsync(
            nowUtc: InitialTime,
            nextAttemptAtUtc: InitialTime,
            interceptor: interceptor);
        interceptor.LoseNextCommitConfirmation();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.ReconcileAsync());

        await using (var afterCommit = CreateContext(fixture.ConnectionString))
        {
            Assert.AreEqual(InitialTime, (await afterCommit.Set<SyncRun>().AsNoTracking().SingleAsync())
                .ExecutionMessageEnqueuedAtUtc);
            Assert.AreEqual(1, await afterCommit.Set<OutboxMessage>().CountAsync());
        }

        Assert.AreEqual(0, await fixture.ReconcileAsync());
        await using var afterRestart = CreateContext(fixture.ConnectionString);
        Assert.AreEqual(1, await afterRestart.Set<OutboxMessage>().CountAsync());
    }

    // As opções Npgsql usam somente o destino descartável já validado pela fixture compartilhada de testes.
    private static FinoraDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, connectionString);
        return new FinoraDbContext(options.Options);
    }

    // Relógio fixo permite avançar lease/deadline sem sleeps ou depender do relógio da máquina.
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    // A fixture cria uma única execução sintética e compõe bus outbox in-memory sem iniciar broker.
    private sealed class RecoveryFixture : IAsyncDisposable
    {
        private readonly DisposablePostgresMigrationFixture _database;

        private RecoveryFixture(
            DisposablePostgresMigrationFixture database,
            string connectionString,
            IHost host)
        {
            _database = database;
            ConnectionString = connectionString;
            Host = host;
        }

        internal string ConnectionString { get; }
        private IHost Host { get; }

        // O schema e as identidades são recriados por teste; o host apenas persiste mensagens na outbox EF.
        internal static async Task<RecoveryFixture> CreateAsync(
            DateTimeOffset nowUtc,
            DateTimeOffset? nextAttemptAtUtc = null,
            int runningAttemptNumber = 0,
            DateTimeOffset? leaseExpiresAtUtc = null,
            RecoveryCommitInterceptor? interceptor = null)
        {
            var database = DisposablePostgresMigrationFixture.CreateValidated();
            try
            {
                await database.ResetToEmptySchemaAsync();
                Assert.IsTrue(await database.Context.Database.EnsureCreatedAsync());

                var userId = Guid.NewGuid();
                var connectionId = Guid.NewGuid();
                var syncRunId = Guid.NewGuid();
                database.Context.Users.Add(new ApplicationUser(userId)
                {
                    UserName = "sync-recovery@example.invalid",
                    NormalizedUserName = "SYNC-RECOVERY@EXAMPLE.INVALID",
                    Email = "sync-recovery@example.invalid",
                    NormalizedEmail = "SYNC-RECOVERY@EXAMPLE.INVALID"
                });
                database.Context.Set<FinancialConnection>().Add(
                    FinancialConnection.Create(connectionId, userId, "pluggy", "synthetic-recovery-item"));

                var isRunning = runningAttemptNumber > 0;
                DateTimeOffset? attemptStartedAtUtc = isRunning ? InitialTime : null;
                var run = SyncRun.Create(
                    syncRunId,
                    userId,
                    connectionId,
                    InitialTime,
                    new DateOnly(2025, 11, 23),
                    new DateOnly(2026, 11, 24),
                    startedAtUtc: attemptStartedAtUtc,
                    state: isRunning ? SyncRunState.Running : SyncRunState.Pending,
                    attemptNumber: runningAttemptNumber,
                    attemptLimit: SyncRun.ApprovedAttemptLimit,
                    attemptStartedAtUtc: attemptStartedAtUtc,
                    attemptDeadlineAtUtc: attemptStartedAtUtc?.Add(SyncRun.MaximumAttemptDuration),
                    nextAttemptAtUtc: nextAttemptAtUtc);
                database.Context.Set<SyncRun>().Add(run);

                if (leaseExpiresAtUtc is { } expiresAtUtc)
                {
                    database.Context.Set<SyncRunLease>().Add(SyncRunLease.Create(
                        connectionId,
                        userId,
                        syncRunId,
                        runningAttemptNumber,
                        InitialTime,
                        expiresAtUtc,
                        fencingToken: runningAttemptNumber));
                }

                await database.Context.SaveChangesAsync();
                var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
                var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
                builder.Services.AddDbContext<FinoraDbContext>(options =>
                {
                    FinoraDbContextConfiguration.Configure(options, connectionString);
                    if (interceptor is not null)
                    {
                        options.AddInterceptors(interceptor);
                    }
                });
                builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(nowUtc));
                builder.Services.AddFinoraSyncRunExecutionOutbox(registration =>
                    registration.UsingInMemory((_, bus) =>
                        bus.ReceiveEndpoint(SyncRunExecutionTopology.QueueName, _ => { })));

                return new RecoveryFixture(database, connectionString, builder.Build());
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        // Cada passagem usa um scope novo, igual ao ciclo do hosted service após falha ou reinício.
        internal async Task<int> ReconcileAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<SyncRunExecutionRecoveryCoordinator>()
                .EnqueueRecoverableExecutionsAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            Host.Dispose();
            await _database.DisposeAsync();
        }
    }

    // Injeta falha conhecida antes do commit ou confirmação perdida depois que PostgreSQL já confirmou.
    private sealed class RecoveryCommitInterceptor : DbTransactionInterceptor
    {
        private int _failBeforeCommit;
        private int _loseCommitConfirmation;

        internal void FailBeforeNextCommit() => Interlocked.Exchange(ref _failBeforeCommit, 1);
        internal void LoseNextCommitConfirmation() => Interlocked.Exchange(ref _loseCommitConfirmation, 1);

        // A falha sintética ocorre antes do commit físico e permite provar rollback do par marcador/outbox.
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _failBeforeCommit, 0) == 1
                ? ValueTask.FromException<InterceptionResult>(new InvalidOperationException("synthetic pre-commit failure"))
                : ValueTask.FromResult(result);

        // A exceção posterior simula resultado incerto sem desfazer o commit já realizado pelo provider.
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _loseCommitConfirmation, 0) == 1
                ? Task.FromException(new InvalidOperationException("synthetic lost commit confirmation"))
                : Task.CompletedTask;
    }
}
