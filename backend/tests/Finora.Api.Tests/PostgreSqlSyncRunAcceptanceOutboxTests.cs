using System.Security.Cryptography;
using System.Text;
using System.Data.Common;
using Finora.Application.FinancialData;
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

// Exercita somente o contexto e o database PostgreSQL descartável allowlisted; não inicia RabbitMQ nem usa dados reais.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunAcceptanceOutboxTests
{
    private static readonly Guid UserId = Guid.Parse("a71641fc-dc4c-45ba-aea5-51d373bb4053");
    private static readonly Guid ConnectionId = Guid.Parse("1a9ef6de-73db-4e79-99e9-dddd69991a6b");
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    // Uma falha durante CommitAsync é classificada como indeterminada e o banco descartável prova ausência do par.
    [TestMethod]
    public async Task FailedCommitLeavesNoAcceptedRunOrOutboxMessage()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        var interceptor = new FailBeforeCommitInterceptor();
        using var host = CreateHost(settings.ConnectionString, interceptor);
        await SeedConnectionAsync(host);

        var candidate = CreateCandidate(Guid.NewGuid(), "rollback-key");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISyncRunAcceptanceStore>();
            interceptor.FailNextCommit();
            await Assert.ThrowsExactlyAsync<SyncRunAcceptanceIndeterminateException>(
                () => store.AcceptOrGetAsync(candidate, CancellationToken.None));
        }

        await using var verification = host.Services.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.AreEqual(0, await dbContext.Set<SyncRun>().CountAsync());
        Assert.AreEqual(0, await dbContext.Set<OutboxMessage>().CountAsync());
    }

    // Commit grava o par na mesma transação e replay da chave não acrescenta segunda execução ou mensagem.
    [TestMethod]
    public async Task CommitPersistsRunAndOutboxTogetherAndReplayDoesNotDuplicateEither()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        using var host = CreateHost(
            settings.ConnectionString,
            new FailBeforeCommitInterceptor(),
            new AcceptanceTimeProvider(CreatedAtUtc));
        await SeedConnectionAsync(host);
        var firstCandidate = CreateCandidate(Guid.NewGuid(), "committed-key");
        SyncRunAcceptanceResult first;
        SyncRunAcceptanceResult replay;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISyncRunAcceptanceStore>();
            first = await store.AcceptOrGetAsync(firstCandidate, CancellationToken.None);
            replay = await store.AcceptOrGetAsync(CreateCandidate(Guid.NewGuid(), "committed-key"), CancellationToken.None);
        }

        await using var verification = host.Services.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, first.Status);
        Assert.AreEqual(SyncRunAcceptanceStatus.Replayed, replay.Status);
        Assert.AreEqual(first.SyncRunId, replay.SyncRunId);
        Assert.AreEqual(1, await dbContext.Set<SyncRun>().CountAsync());
        Assert.AreEqual(1, await dbContext.Set<OutboxMessage>().CountAsync());
        Assert.AreEqual(CreatedAtUtc, (await dbContext.Set<SyncRun>().AsNoTracking().SingleAsync())
            .ExecutionMessageEnqueuedAtUtc);
    }

    // Entrada com sucesso exatamente no limiar é recente e não pode criar execução nem obrigação de entrega.
    [TestMethod]
    public async Task RecentOnEntryReturnsNotRequiredWithoutPersistingRunOrOutbox()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        var clock = new AcceptanceTimeProvider(CreatedAtUtc);
        using var host = CreateHost(settings.ConnectionString, new FailBeforeCommitInterceptor(), clock);
        await SeedConnectionAsync(host, CreatedAtUtc.AddHours(-24));

        SyncRunAcceptanceResult result;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISyncRunAcceptanceStore>();
            result = await store.AcceptOrGetAsync(
                CreateCandidate(Guid.NewGuid(), "entry-recent-key", SyncRunRequestMode.OnEntry),
                CancellationToken.None);
        }

        Assert.AreEqual(SyncRunAcceptanceStatus.NotRequired, result.Status);
        Assert.IsFalse(result.IsAccepted);
        await using var verification = host.Services.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.AreEqual(0, await dbContext.Set<SyncRun>().CountAsync());
        Assert.AreEqual(0, await dbContext.Set<OutboxMessage>().CountAsync());
    }

    // Entrada antiga e pedido manual compartilham cooldown persistido, inclusive depois de falha terminal.
    [TestMethod]
    public async Task StaleEntryAndManualRequestsShareIntervalIncludingExactBoundary()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        var clock = new AcceptanceTimeProvider(CreatedAtUtc);
        using var host = CreateHost(settings.ConnectionString, new FailBeforeCommitInterceptor(), clock);
        await SeedConnectionAsync(host, CreatedAtUtc.AddHours(-24).AddTicks(-10));

        var first = await AcceptAsync(host, CreateCandidate(
            Guid.NewGuid(), "entry-stale-first-key", SyncRunRequestMode.OnEntry));
        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, first.Status);
        await MarkRunFailedAsync(host, first.SyncRunId!.Value, CreatedAtUtc);

        clock.Set(CreatedAtUtc.AddMinutes(1));
        var earlyManual = await AcceptAsync(host, CreateCandidate(Guid.NewGuid(), "manual-too-early-key"));
        Assert.AreEqual(SyncRunAcceptanceStatus.MinimumIntervalActive, earlyManual.Status);
        Assert.IsFalse(earlyManual.IsAccepted);
        Assert.AreEqual(CreatedAtUtc.AddMinutes(5), earlyManual.NextAllowedAtUtc);

        // A igualdade no fim dos cinco minutos libera uma nova intenção manual, mesmo com os dados ainda antigos.
        clock.Set(CreatedAtUtc.AddMinutes(5));
        var manualAtBoundary = await AcceptAsync(host, CreateCandidate(Guid.NewGuid(), "manual-at-boundary-key"));
        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, manualAtBoundary.Status);
        Assert.AreEqual(CreatedAtUtc.AddMinutes(5), await ReadRunCreatedAtAsync(host, manualAtBoundary.SyncRunId!.Value));
        await MarkRunFailedAsync(host, manualAtBoundary.SyncRunId.Value, CreatedAtUtc.AddMinutes(5));

        // O fluxo de entrada também respeita a intenção manual aceita recentemente, sem iniciar outro replay.
        clock.Set(CreatedAtUtc.AddMinutes(6));
        var entryAfterManual = await AcceptAsync(host, CreateCandidate(
            Guid.NewGuid(), "entry-after-manual-key", SyncRunRequestMode.OnEntry));
        Assert.AreEqual(SyncRunAcceptanceStatus.MinimumIntervalActive, entryAfterManual.Status);
        Assert.AreEqual(CreatedAtUtc.AddMinutes(10), entryAfterManual.NextAllowedAtUtc);

        await using var verification = host.Services.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.AreEqual(2, await dbContext.Set<SyncRun>().CountAsync());
        Assert.AreEqual(2, await dbContext.Set<OutboxMessage>().CountAsync());
    }

    // Uma nova composição de serviços recupera o cooldown do histórico PostgreSQL, sem depender de cache do processo anterior.
    [TestMethod]
    public async Task PersistedCooldownSurvivesAcceptanceStoreRestart()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        var firstClock = new AcceptanceTimeProvider(CreatedAtUtc);
        Guid acceptedRunId;

        using (var firstHost = CreateHost(settings.ConnectionString, new FailBeforeCommitInterceptor(), firstClock))
        {
            await SeedConnectionAsync(firstHost);
            var accepted = await AcceptAsync(firstHost, CreateCandidate(Guid.NewGuid(), "restart-original-key"));
            Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, accepted.Status);
            acceptedRunId = accepted.SyncRunId!.Value;
            await MarkRunFailedAsync(firstHost, acceptedRunId, CreatedAtUtc);
        }

        var restartedClock = new AcceptanceTimeProvider(CreatedAtUtc.AddMinutes(1));
        using var restartedHost = CreateHost(
            settings.ConnectionString,
            new FailBeforeCommitInterceptor(),
            restartedClock);
        var blocked = await AcceptAsync(restartedHost, CreateCandidate(Guid.NewGuid(), "restart-new-key"));

        Assert.AreEqual(SyncRunAcceptanceStatus.MinimumIntervalActive, blocked.Status);
        Assert.AreEqual(CreatedAtUtc.AddMinutes(5), blocked.NextAllowedAtUtc);
        await using var verification = restartedHost.Services.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.AreEqual(1, await dbContext.Set<SyncRun>().CountAsync());
        Assert.AreEqual(1, await dbContext.Set<OutboxMessage>().CountAsync());
    }

    // Falha terminal AUTH_REQUIRED suspende novas entradas mesmo após o cooldown e conserva replay/histórico.
    [TestMethod]
    public async Task TerminalAuthorizationFailureSuppressesEntryButKeepsManualRequestAndHistory()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        var clock = new AcceptanceTimeProvider(CreatedAtUtc);
        using var host = CreateHost(settings.ConnectionString, new FailBeforeCommitInterceptor(), clock);
        await SeedConnectionAsync(host);
        const string originalKey = "auth-required-entry-original-key";

        var accepted = await AcceptAsync(host, CreateCandidate(
            Guid.NewGuid(), originalKey, SyncRunRequestMode.OnEntry));
        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, accepted.Status);
        var originalRunId = accepted.SyncRunId!.Value;
        await MarkRunFailedAsync(
            host,
            originalRunId,
            CreatedAtUtc,
            SyncRunFailureCodes.AuthenticationRequired);

        clock.Set(CreatedAtUtc.AddMinutes(1));
        var replay = await AcceptAsync(host, CreateCandidate(
            Guid.NewGuid(), originalKey, SyncRunRequestMode.OnEntry));
        var repeatedEntry = await AcceptAsync(host, CreateCandidate(
            Guid.NewGuid(), "auth-required-entry-new-key", SyncRunRequestMode.OnEntry));
        var manualBeforeInterval = await AcceptAsync(host, CreateCandidate(
            Guid.NewGuid(), "auth-required-manual-too-early-key"));

        Assert.AreEqual(SyncRunAcceptanceStatus.Replayed, replay.Status);
        Assert.AreEqual(originalRunId, replay.SyncRunId);
        Assert.AreEqual(SyncRunAcceptanceStatus.ReauthorizationRequired, repeatedEntry.Status);
        Assert.IsFalse(repeatedEntry.IsAccepted);
        Assert.AreEqual(SyncRunAcceptanceStatus.MinimumIntervalActive, manualBeforeInterval.Status);
        Assert.IsFalse(manualBeforeInterval.IsAccepted);
        Assert.AreEqual(CreatedAtUtc.AddMinutes(5), manualBeforeInterval.NextAllowedAtUtc);

        // Passado o intervalo, entrada continua suspensa; só o pedido manual explícito pode prosseguir.
        clock.Set(CreatedAtUtc.AddMinutes(6));
        var entryAfterInterval = await AcceptAsync(host, CreateCandidate(
            Guid.NewGuid(), "auth-required-entry-after-interval-key", SyncRunRequestMode.OnEntry));
        var manualAfterInterval = await AcceptAsync(host, CreateCandidate(
            Guid.NewGuid(), "auth-required-manual-after-interval-key"));
        Assert.AreEqual(SyncRunAcceptanceStatus.ReauthorizationRequired, entryAfterInterval.Status);
        Assert.IsFalse(entryAfterInterval.IsAccepted);
        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, manualAfterInterval.Status);

        await using var verification = host.Services.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<FinoraDbContext>();
        var runs = await dbContext.Set<SyncRun>().AsNoTracking().OrderBy(run => run.CreatedAtUtc).ToArrayAsync();
        Assert.AreEqual(2, runs.Length);
        Assert.AreEqual(SyncRunState.Failed, runs[0].State);
        Assert.AreEqual(SyncRunFailureCodes.AuthenticationRequired, runs[0].FailureCode);
        Assert.AreEqual(manualAfterInterval.SyncRunId, runs[1].Id);
        Assert.IsNull(await dbContext.Set<FinancialConnection>().AsNoTracking()
            .Where(connection => connection.Id == ConnectionId)
            .Select(connection => connection.LastSuccessfulImportAtUtc)
            .SingleAsync());
        Assert.AreEqual(2, await dbContext.Set<OutboxMessage>().CountAsync());
    }

    // O relógio muda enquanto o pedido aguarda o lock; avaliação e CreatedAt precisam refletir o instante pós-lock.
    [TestMethod]
    public async Task AcceptanceClockIsCapturedAfterWaitingForConnectionLock()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        var clock = new AcceptanceTimeProvider(CreatedAtUtc);
        using var firstHost = CreateHost(settings.ConnectionString, new FailBeforeCommitInterceptor(), clock);
        await SeedConnectionAsync(firstHost);
        var original = await AcceptAsync(firstHost, CreateCandidate(Guid.NewGuid(), "lock-clock-original-key"));
        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, original.Status);
        await MarkRunFailedAsync(firstHost, original.SyncRunId!.Value, CreatedAtUtc);

        var lockObserver = new ConnectionLockCommandInterceptor();
        using var waitingHost = CreateHost(
            settings.ConnectionString,
            new FailBeforeCommitInterceptor(),
            clock,
            lockObserver);
        var blockerOptions = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(blockerOptions, settings.ConnectionString);
        await using var blockerContext = new FinoraDbContext(blockerOptions.Options);
        await using var blockerTransaction = await blockerContext.Database.BeginTransactionAsync();
        _ = await blockerContext.Set<FinancialConnection>()
            .FromSqlInterpolated($"""SELECT * FROM "FinancialConnection" WHERE "Id" = {ConnectionId} FOR UPDATE""")
            .AsNoTracking()
            .SingleAsync();

        var pendingAcceptance = AcceptAsync(
            waitingHost,
            CreateCandidate(Guid.NewGuid(), "lock-clock-boundary-key"));
        await lockObserver.ConnectionLockCommandStarted.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Set(CreatedAtUtc.AddMinutes(5));
        await blockerTransaction.CommitAsync();

        var afterLock = await pendingAcceptance.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, afterLock.Status);
        Assert.AreEqual(CreatedAtUtc.AddMinutes(5), await ReadRunCreatedAtAsync(waitingHost, afterLock.SyncRunId!.Value));
    }

    // A composição usa transporte in-memory sem iniciar seu host; a bus outbox grava pelo DbContext scoped.
    private static IHost CreateHost(
        string connectionString,
        FailBeforeCommitInterceptor interceptor,
        TimeProvider? clock = null,
        DbCommandInterceptor? commandInterceptor = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<FinoraDbContext>(options =>
        {
            FinoraDbContextConfiguration.Configure(options, connectionString);
            options.AddInterceptors(interceptor);
            if (commandInterceptor is not null) { options.AddInterceptors(commandInterceptor); }
        });
        builder.Services.AddSingleton<TimeProvider>(clock ?? TimeProvider.System);
        builder.Services.AddSingleton(new ConnectionUpdatePolicyOptions());
        builder.Services.AddFinoraSyncRunAcceptancePersistence();
        builder.Services.AddFinoraSyncRunExecutionOutbox(registration =>
            registration.UsingInMemory((_, configurator) =>
                configurator.ReceiveEndpoint(SyncRunExecutionTopology.QueueName, _ => { })));
        return builder.Build();
    }

    // A fixture revalida host/database antes do reset; o schema é efêmero e não compartilha volume persistente.
    private static async Task ResetDisposableDatabaseAsync()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
    }

    // Titular e conexão sintéticos satisfazem as FKs exigidas antes de aceitar uma execução de teste.
    private static async Task SeedConnectionAsync(IHost host, DateTimeOffset? lastSuccessfulImportAtUtc = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.IsTrue(await dbContext.Database.EnsureCreatedAsync());
        dbContext.Users.Add(new ApplicationUser(UserId)
        {
            UserName = "sync-outbox@example.invalid",
            NormalizedUserName = "SYNC-OUTBOX@EXAMPLE.INVALID",
            Email = "sync-outbox@example.invalid",
            NormalizedEmail = "SYNC-OUTBOX@EXAMPLE.INVALID"
        });
        dbContext.Set<FinancialConnection>().Add(
            FinancialConnection.Create(
                ConnectionId,
                UserId,
                "pluggy",
                "synthetic-outbox-item",
                lastSuccessfulImportAtUtc: lastSuccessfulImportAtUtc));
        await dbContext.SaveChangesAsync();
    }

    // Cada pedido usa um scope real do store, mantendo os testes próximos do limite transacional de produção.
    private static async Task<SyncRunAcceptanceResult> AcceptAsync(IHost host, SyncRun candidate)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISyncRunAcceptanceStore>();
        return await store.AcceptOrGetAsync(candidate, CancellationToken.None);
    }

    // A execução é encerrada como falha terminal para demonstrar que o cooldown usa a intenção aceita, não o sucesso.
    private static async Task MarkRunFailedAsync(
        IHost host,
        Guid syncRunId,
        DateTimeOffset acceptedAtUtc,
        string failureCode = SyncRunFailureCodes.ProviderUnavailable)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        var run = await dbContext.Set<SyncRun>().SingleAsync(value => value.Id == syncRunId);
        Assert.IsTrue(run.TryBeginAttempt(acceptedAtUtc));
        Assert.IsTrue(run.TryCompleteAttemptFailure(
            1,
            acceptedAtUtc.AddSeconds(30),
            failureCode,
            pagesRead: 0,
            receivedCount: 0));
        await dbContext.SaveChangesAsync();
    }

    // Consulta a criação gravada em um scope novo para comprovar o timestamp efetivamente usado no PostgreSQL.
    private static async Task<DateTimeOffset> ReadRunCreatedAtAsync(IHost host, Guid syncRunId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        return await dbContext.Set<SyncRun>()
            .AsNoTracking()
            .Where(value => value.Id == syncRunId)
            .Select(value => value.CreatedAtUtc)
            .SingleAsync();
    }

    // A chave é armazenada como SHA-256 e o fingerprint acompanha o formato validado pelo domínio.
    private static SyncRun CreateCandidate(
        Guid runId,
        string idempotencyKey,
        SyncRunRequestMode mode = SyncRunRequestMode.Manual)
    {
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(idempotencyKey)));
        return SyncRun.Create(
            runId,
            UserId,
            ConnectionId,
            CreatedAtUtc,
            new DateOnly(2025, 11, 22),
            new DateOnly(2026, 11, 23),
            idempotencyKeyHash: keyHash,
            requestFingerprint: SyncRunIdempotency.CreateRequestFingerprint(ConnectionId, mode));
    }

    // Relógio substituível permite avançar o tempo entre commits sem dormir nem depender do relógio da máquina.
    private sealed class AcceptanceTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Set(DateTimeOffset value) => _current = value;
    }

    // Observa o início da consulta de lock sem alterar seu SQL nem depender de delays entre processos.
    private sealed class ConnectionLockCommandInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _connectionLockCommandStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task ConnectionLockCommandStarted => _connectionLockCommandStarted.Task;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                _connectionLockCommandStarted.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }
    }

    // O interceptor injeta uma falha controlada uma única vez, depois de SaveChanges e antes do commit PostgreSQL.
    private sealed class FailBeforeCommitInterceptor : DbTransactionInterceptor
    {
        private int _failNextCommit;

        public void FailNextCommit() => Interlocked.Exchange(ref _failNextCommit, 1);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _failNextCommit, 0) == 1
                ? ValueTask.FromException<InterceptionResult>(new InvalidOperationException("synthetic pre-commit failure"))
                : ValueTask.FromResult(result);
    }
}
