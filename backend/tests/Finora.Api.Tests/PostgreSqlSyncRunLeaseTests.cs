using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Prova a posse/fencing no PostgreSQL descartável, com contextos independentes e sem transporte ou provedor externo.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunLeaseTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    // O lock PostgreSQL na conexão escolhe um único vencedor mesmo com duas SyncRuns e contextos concorrentes.
    [TestMethod]
    public async Task ConcurrentAcquisitionGrantsOneLeaseForTheConnection()
    {
        var database = await SeedAsync(runCount: 2);

        var results = await Task.WhenAll(database.SyncRunIds.Select(syncRunId =>
            AcquireWithNewContextAsync(database.ConnectionString, syncRunId, InitialTime)));

        Assert.AreEqual(1, results.Count(result => result.Status == SyncRunLeaseAcquisitionStatus.Acquired));
        Assert.AreEqual(1, results.Count(result => result.Status == SyncRunLeaseAcquisitionStatus.Busy));
        var grant = results.Single(result => result.Grant is not null).Grant!;
        Assert.AreEqual(1L, grant.FencingToken);
        Assert.AreEqual(InitialTime, grant.AcquiredAtUtc);
        Assert.AreEqual(InitialTime.AddSeconds(120), grant.ExpiresAtUtc);

        await using var verification = CreateContext(database.ConnectionString);
        var persisted = await verification.Set<SyncRunLease>().AsNoTracking().SingleAsync();
        Assert.AreEqual(grant.ConnectionId, persisted.ConnectionId);
        Assert.AreEqual(grant.SyncRunId, persisted.SyncRunId);
        Assert.AreEqual(1, await verification.Set<SyncRunLease>().CountAsync());
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync(item => item.Id == grant.SyncRunId);
        Assert.AreEqual(1, run.AttemptNumber);
        Assert.AreEqual(SyncRun.ApprovedAttemptLimit, run.AttemptLimit);
        Assert.AreEqual(InitialTime, run.StartedAtUtc);
        Assert.AreEqual(InitialTime, run.AttemptStartedAtUtc);
        Assert.AreEqual(InitialTime.AddMinutes(10), run.AttemptDeadlineAtUtc);
        Assert.IsNull(run.NextAttemptAtUtc);
        Assert.IsNull(run.PagesRead);
        Assert.IsNull(run.ReceivedCount);
        Assert.IsNull(run.UniqueCount);
        Assert.IsNull(run.PersistedCount);
    }

    // Renovação preserva o fencing; após expiração, takeover da mesma execução eleva geração e invalida claim antigo.
    [TestMethod]
    public async Task RenewalExtendsLeaseAndExpiredTakeoverAdvancesFencingGeneration()
    {
        var database = await SeedAsync(runCount: 1);
        var acquired = await AcquireWithNewContextAsync(
            database.ConnectionString, database.SyncRunIds[0], InitialTime);
        Assert.AreEqual(SyncRunLeaseAcquisitionStatus.Acquired, acquired.Status);
        var originalGrant = acquired.Grant!;

        var renewedAt = InitialTime.AddSeconds(30);
        var renewed = await RenewWithNewContextAsync(database.ConnectionString, originalGrant, renewedAt);
        Assert.IsNotNull(renewed);
        Assert.AreEqual(originalGrant.FencingToken, renewed.FencingToken);
        Assert.AreEqual(originalGrant.SyncRunId, renewed.SyncRunId);
        Assert.AreEqual(1, renewed.AttemptNumber);
        Assert.AreEqual(InitialTime.AddSeconds(150), renewed.ExpiresAtUtc);

        var takeover = await AcquireWithNewContextAsync(
            database.ConnectionString, database.SyncRunIds[0], renewed.ExpiresAtUtc);
        Assert.AreEqual(SyncRunLeaseAcquisitionStatus.Acquired, takeover.Status);
        Assert.AreEqual(2, takeover.Grant!.AttemptNumber);
        Assert.AreEqual(originalGrant.FencingToken + 1, takeover.Grant.FencingToken);
        Assert.AreEqual(renewed.ExpiresAtUtc, takeover.Grant.AcquiredAtUtc);

        var staleRenewal = await RenewWithNewContextAsync(
            database.ConnectionString, originalGrant, takeover.Grant.AcquiredAtUtc);
        Assert.IsNull(staleRenewal);

        await using var verification = CreateContext(database.ConnectionString);
        var persisted = await verification.Set<SyncRunLease>().AsNoTracking().SingleAsync();
        Assert.AreEqual(takeover.Grant.FencingToken, persisted.FencingToken);
        Assert.AreEqual(2, persisted.AttemptNumber);
        Assert.AreEqual(1, await verification.Set<SyncRunLease>().CountAsync());
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync(item => item.Id == takeover.Grant.SyncRunId);
        Assert.AreEqual(2, run.AttemptNumber);
        Assert.AreEqual(SyncRun.ApprovedAttemptLimit, run.AttemptLimit);
        Assert.AreEqual(InitialTime, run.StartedAtUtc);
        Assert.AreEqual(takeover.Grant.AcquiredAtUtc, run.AttemptStartedAtUtc);
        Assert.AreEqual(takeover.Grant.AcquiredAtUtc.AddMinutes(10), run.AttemptDeadlineAtUtc);
        Assert.IsNull(run.NextAttemptAtUtc);
        Assert.AreEqual(new DateOnly(2025, 11, 22), run.RequestedFrom);
        Assert.AreEqual(new DateOnly(2026, 11, 23), run.RequestedToExclusive);
        Assert.IsNull(run.PagesRead);
        Assert.IsNull(run.ReceivedCount);
        Assert.IsNull(run.UniqueCount);
        Assert.IsNull(run.PersistedCount);
    }

    // Backoffs de 1/5/15 minutos são datas duráveis; o método retorna sem segurar lease até o instante futuro.
    [TestMethod]
    public async Task RetryHandoffPersistsScheduleAndResumesOnlyAtDueTime()
    {
        foreach (var delayMinutes in new[] { 1, 5, 15 })
        {
            await AssertRetryHandoffAsync(delayMinutes);
        }
    }

    // Orçamento sobrevive a novas instâncias/contextos e a terceira falha fecha a mesma SyncRun sem quarta tentativa.
    [TestMethod]
    public async Task AttemptBudgetSurvivesRetriesAndTerminalizesTheAcceptedRun()
    {
        var database = await SeedAsync(runCount: 1);
        var syncRunId = database.SyncRunIds[0];
        var first = await AcquireWithNewContextAsync(database.ConnectionString, syncRunId, InitialTime);
        var firstGrant = first.Grant!;
        var firstFailureAt = InitialTime.AddSeconds(30);
        Assert.IsTrue(await ScheduleRetryWithNewContextAsync(
            database.ConnectionString,
            firstGrant,
            firstFailureAt,
            firstFailureAt.AddMinutes(1),
            SyncRunFailureCodes.ProviderUnavailable));

        var secondAt = firstFailureAt.AddMinutes(1);
        var second = await AcquireWithNewContextAsync(database.ConnectionString, syncRunId, secondAt);
        Assert.AreEqual(2, second.Grant!.AttemptNumber);
        var secondFailureAt = secondAt.AddSeconds(30);
        Assert.IsTrue(await ScheduleRetryWithNewContextAsync(
            database.ConnectionString,
            second.Grant,
            secondFailureAt,
            secondFailureAt.AddMinutes(5),
            SyncRunFailureCodes.ProviderTimeout));

        var thirdAt = secondFailureAt.AddMinutes(5);
        var third = await AcquireWithNewContextAsync(database.ConnectionString, syncRunId, thirdAt);
        Assert.AreEqual(3, third.Grant!.AttemptNumber);
        var terminalAt = thirdAt.AddSeconds(30);
        await using (var context = CreateContext(database.ConnectionString))
        {
            Assert.IsTrue(await new SyncRunLeaseStore(context).CompleteFailureAsync(
                third.Grant,
                terminalAt,
                SyncRunFailureCodes.ProviderTimeout,
                pagesRead: 4,
                receivedCount: 10,
                nextAttemptAtUtc: null,
                CancellationToken.None));
        }

        var duplicate = await AcquireWithNewContextAsync(
            database.ConnectionString, syncRunId, terminalAt.AddMinutes(1));
        Assert.AreEqual(SyncRunLeaseAcquisitionStatus.AlreadyTerminal, duplicate.Status);

        await using var verification = CreateContext(database.ConnectionString);
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(syncRunId, run.Id);
        Assert.AreEqual(SyncRunState.Failed, run.State);
        Assert.AreEqual(3, run.AttemptNumber);
        Assert.AreEqual(SyncRun.ApprovedAttemptLimit, run.AttemptLimit);
        Assert.AreEqual(SyncRunFailureCodes.ProviderTimeout, run.FailureCode);
        Assert.AreEqual(terminalAt, run.FinishedAtUtc);
        Assert.IsNull(run.NextAttemptAtUtc);
        Assert.AreEqual(4, run.PagesRead);
        Assert.AreEqual(10, run.ReceivedCount);
        Assert.AreEqual(1, await verification.Set<SyncRun>().CountAsync());
        Assert.AreEqual(terminalAt, await verification.Set<SyncRunLease>()
            .Select(lease => lease.ExpiresAtUtc).SingleAsync());
    }

    // Retry-After acima do teto fecha a execução, libera posse e persiste o instante original sem truncamento.
    [TestMethod]
    public async Task RetryAfterAboveLimitIsPersistedExactlyWithoutAutomaticRetry()
    {
        var database = await SeedAsync(runCount: 1);
        var acquired = await AcquireWithNewContextAsync(
            database.ConnectionString, database.SyncRunIds[0], InitialTime);
        var grant = acquired.Grant!;
        var failedAt = InitialTime.AddSeconds(90);
        var retryAfterAt = InitialTime.AddMinutes(23);

        await using (var context = CreateContext(database.ConnectionString))
        {
            Assert.IsTrue(await new SyncRunLeaseStore(context).CompleteFailureAsync(
                grant,
                failedAt,
                SyncRunFailureCodes.RateLimited,
                pagesRead: 2,
                receivedCount: 6,
                nextAttemptAtUtc: retryAfterAt,
                CancellationToken.None));
        }

        await using var verification = CreateContext(database.ConnectionString);
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(SyncRunState.Failed, run.State);
        Assert.AreEqual(SyncRunFailureCodes.RateLimited, run.FailureCode);
        Assert.AreEqual(retryAfterAt, run.NextAttemptAtUtc);
        Assert.AreEqual(failedAt, run.FinishedAtUtc);
        Assert.AreEqual(failedAt, await verification.Set<SyncRunLease>()
            .Select(lease => lease.ExpiresAtUtc).SingleAsync());
    }

    // Cada faixa reinicia somente o schema descartável para não compartilhar tentativa ou estado entre cenários.
    private static async Task AssertRetryHandoffAsync(int delayMinutes)
    {
        var database = await SeedAsync(runCount: 1);
        var acquired = await AcquireWithNewContextAsync(
            database.ConnectionString, database.SyncRunIds[0], InitialTime);
        Assert.AreEqual(SyncRunLeaseAcquisitionStatus.Acquired, acquired.Status);
        var grant = acquired.Grant!;
        var failureAt = InitialTime.AddSeconds(90);
        var nextAttemptAt = failureAt.AddMinutes(delayMinutes);

        var handedOff = await ScheduleRetryWithNewContextAsync(
            database.ConnectionString,
            grant,
            failureAt,
            nextAttemptAt,
            SyncRunFailureCodes.ProviderUnavailable);
        Assert.IsTrue(handedOff);

        await using (var beforeDue = CreateContext(database.ConnectionString))
        {
            var run = await beforeDue.Set<SyncRun>().AsNoTracking().SingleAsync();
            var lease = await beforeDue.Set<SyncRunLease>().AsNoTracking().SingleAsync();
            Assert.AreEqual(SyncRunState.Pending, run.State);
            Assert.AreEqual(1, run.AttemptNumber);
            Assert.AreEqual(nextAttemptAt, run.NextAttemptAtUtc);
            Assert.AreEqual(SyncRunFailureCodes.ProviderUnavailable, run.FailureCode);
            Assert.AreEqual(failureAt, lease.ExpiresAtUtc);
            Assert.IsNull(run.PagesRead);
            Assert.IsNull(run.ReceivedCount);
            Assert.IsNull(run.UniqueCount);
            Assert.IsNull(run.PersistedCount);
        }

        var beforeSchedule = await AcquireWithNewContextAsync(
            database.ConnectionString, database.SyncRunIds[0], nextAttemptAt.AddSeconds(-1));
        Assert.AreEqual(SyncRunLeaseAcquisitionStatus.NotEligible, beforeSchedule.Status);
        var resumed = await AcquireWithNewContextAsync(
            database.ConnectionString, database.SyncRunIds[0], nextAttemptAt);
        Assert.AreEqual(SyncRunLeaseAcquisitionStatus.Acquired, resumed.Status);
        Assert.AreEqual(2, resumed.Grant!.AttemptNumber);
        Assert.AreEqual(grant.FencingToken + 1, resumed.Grant.FencingToken);
        Assert.AreEqual(nextAttemptAt, resumed.Grant.AcquiredAtUtc);

        await using var verification = CreateContext(database.ConnectionString);
        var resumedRun = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(new DateOnly(2025, 11, 22), resumedRun.RequestedFrom);
        Assert.AreEqual(new DateOnly(2026, 11, 23), resumedRun.RequestedToExclusive);
        Assert.IsNull(resumedRun.NextAttemptAtUtc);
        Assert.IsNull(resumedRun.PagesRead);
        Assert.IsNull(resumedRun.ReceivedCount);
        Assert.IsNull(resumedRun.UniqueCount);
        Assert.IsNull(resumedRun.PersistedCount);
    }

    // Cria schema somente com EnsureCreated e sem migration ou dependência de RabbitMQ.
    private static async Task<LeaseTestDatabase> SeedAsync(int runCount)
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        Assert.IsTrue(await fixture.Context.Database.EnsureCreatedAsync());

        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        fixture.Context.Users.Add(new ApplicationUser(userId)
        {
            UserName = "sync-lease@example.invalid",
            NormalizedUserName = "SYNC-LEASE@EXAMPLE.INVALID",
            Email = "sync-lease@example.invalid",
            NormalizedEmail = "SYNC-LEASE@EXAMPLE.INVALID"
        });
        fixture.Context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(connectionId, userId, "pluggy", "synthetic-lease-item"));

        var runIds = Enumerable.Range(0, runCount).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var runId in runIds)
        {
            fixture.Context.Set<SyncRun>().Add(SyncRun.Create(
                runId,
                userId,
                connectionId,
                InitialTime,
                new DateOnly(2025, 11, 22),
                new DateOnly(2026, 11, 23),
                nextAttemptAtUtc: InitialTime));
        }

        await fixture.Context.SaveChangesAsync();
        return new LeaseTestDatabase(settings.ConnectionString, connectionId, runIds);
    }

    // Cada contender usa seu próprio DbContext, como workers em instâncias/processos independentes.
    private static async Task<SyncRunLeaseAcquisitionResult> AcquireWithNewContextAsync(
        string connectionString,
        Guid syncRunId,
        DateTimeOffset nowUtc)
    {
        await using var context = CreateContext(connectionString);
        var store = new SyncRunLeaseStore(context);
        return await store.AcquireAsync(syncRunId, nowUtc, CancellationToken.None);
    }

    // A renovação recebe exatamente o claim emitido pela aquisição e não compartilha tracking entre operações.
    private static async Task<SyncRunLeaseGrant?> RenewWithNewContextAsync(
        string connectionString,
        SyncRunLeaseGrant grant,
        DateTimeOffset nowUtc)
    {
        await using var context = CreateContext(connectionString);
        var store = new SyncRunLeaseStore(context);
        return await store.RenewAsync(grant, nowUtc, CancellationToken.None);
    }

    // A transação confirma agenda/estado e expiração antes de devolver controle ao futuro handler de mensagem.
    private static async Task<bool> ScheduleRetryWithNewContextAsync(
        string connectionString,
        SyncRunLeaseGrant grant,
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        string failureCode)
    {
        await using var context = CreateContext(connectionString);
        var store = new SyncRunLeaseStore(context);
        return await store.ScheduleRetryAsync(
            grant, nowUtc, nextAttemptAtUtc, failureCode, pagesRead: null, receivedCount: null,
            CancellationToken.None);
    }

    // As opções Npgsql reais usam apenas a connection string previamente validada para o banco descartável.
    private static FinoraDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, connectionString);
        return new FinoraDbContext(options.Options);
    }

    // IDs sintéticos permitem comparar a posse sem guardar identidade real nem expor connection strings em evidências.
    private sealed record LeaseTestDatabase(string ConnectionString, Guid ConnectionId, Guid[] SyncRunIds);
}
