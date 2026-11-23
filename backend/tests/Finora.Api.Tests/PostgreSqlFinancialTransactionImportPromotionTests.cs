using Finora.Application.Pluggy;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Finora.Api.Tests;

// Confirma promoção transacional e separação temporal entre leitura externa e gravação PostgreSQL.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialTransactionImportPromotionTests
{
    private static readonly Guid UserId = Guid.Parse("1927eed2-ea22-4753-a1aa-166e4f969bbe");
    private static readonly Guid ConnectionId = Guid.Parse("8b12d08a-01be-4daf-825a-c5d4e417c799");
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ReferenceDate = new(2026, 11, 20);

    // Leitura ocorre sem transação; promoção grava cartões, movimentos, SyncRun e último sucesso em um commit.
    [TestMethod]
    public async Task CompleteSnapshotPromotesAfterExternalReadAndRecordsTerminalSuccess()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);

        await using var context = CreateContext();
        var connection = new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "synthetic-item", null);
        var snapshot = Snapshot();
        var connectionReader = new FixedConnectionReader(connection);
        var sourceReader = new TransactionStateProbeReader(context, snapshot);
        var promoter = new FinancialTransactionImportPromoter(context);
        var handler = new ImportTransactionsHandler(
            connectionReader, sourceReader, promoter, new FixedTimeProvider(StartedAtUtc));

        var result = await handler.Handle(
            new ImportTransactionsCommand(UserId, ConnectionId), CancellationToken.None);

        Assert.IsTrue(sourceReader.ReadWithNoTransaction);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.AccountCount);
        Assert.AreEqual(1, result.TransactionCount);
        Assert.AreEqual(1, result.PersistedCount);
        Assert.AreEqual(1, await context.Set<CardAccount>().CountAsync());
        Assert.AreEqual(1, await context.Set<FinancialTransaction>().CountAsync());
        var storedConnection = await context.Set<FinancialConnection>().SingleAsync();
        Assert.AreEqual(StartedAtUtc, storedConnection.LastAttemptAtUtc);
        Assert.AreEqual(StartedAtUtc, storedConnection.LastSuccessfulImportAtUtc);
        var run = await context.Set<SyncRun>().SingleAsync();
        Assert.AreEqual(SyncRunState.Succeeded, run.State);
        Assert.AreEqual(SyncCoverageState.CompleteForRequestedWindow, run.CoverageState);
        Assert.AreEqual(2, run.PagesRead);
        Assert.AreEqual(1, run.ReceivedCount);
        Assert.AreEqual(1, run.UniqueCount);
        Assert.AreEqual(1, run.PersistedCount);
    }

    // Falha no último registro reverte cartão/movimento anteriores e não avança o último sucesso.
    [TestMethod]
    public async Task FailureAfterFinancialWritesRollsBackEntirePromotion()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);

        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION reject_import_sync_run() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic failure'; END $$;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_import_sync_run BEFORE INSERT ON \"SyncRun\" FOR EACH ROW EXECUTE FUNCTION reject_import_sync_run();");
        try
        {
            var connection = new TransactionImportConnection(
                ConnectionId, UserId, "pluggy", "synthetic-item", null);
            var result = await new FinancialTransactionImportPromoter(context).PromoteAsync(
                connection, Snapshot(), ReferenceDate, StartedAtUtc, StartedAtUtc.AddMinutes(1), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(TransactionImportFailureCode.PersistenceFailed, result.FailureCode);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS reject_import_sync_run ON \"SyncRun\";");
            await context.Database.ExecuteSqlRawAsync("DROP FUNCTION IF EXISTS reject_import_sync_run();");
        }

        await using var verification = CreateContext();
        Assert.AreEqual(0, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(0, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(0, await verification.Set<SyncRun>().CountAsync());
        Assert.IsNull(await verification.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
    }

    // Uma falha externa registra somente metadados parciais e preserva o último sucesso confirmado.
    [TestMethod]
    public async Task FailedCollectionRecordsPartialCoverageWithoutRenewingLastSuccess()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context, StartedAtUtc.AddDays(-1));

        await using var context = CreateContext();
        var connection = new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "synthetic-item", StartedAtUtc.AddDays(-1));
        var result = await new FinancialTransactionImportPromoter(context).RecordFailureAsync(
            connection,
            ReferenceDate.AddMonths(-12),
            StartedAtUtc,
            StartedAtUtc.AddMinutes(2),
            TransactionImportFailureCode.ProviderTimeout,
            pagesRead: 3,
            receivedCount: 8,
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, await context.Set<CardAccount>().CountAsync());
        Assert.AreEqual(0, await context.Set<FinancialTransaction>().CountAsync());
        var storedConnection = await context.Set<FinancialConnection>().SingleAsync();
        Assert.AreEqual(StartedAtUtc.AddDays(-1), storedConnection.LastSuccessfulImportAtUtc);
        var run = await context.Set<SyncRun>().SingleAsync();
        Assert.AreEqual(SyncRunState.Failed, run.State);
        Assert.AreEqual(SyncCoverageState.Partial, run.CoverageState);
        Assert.AreEqual(3, run.PagesRead);
        Assert.AreEqual(8, run.ReceivedCount);
        Assert.IsNull(run.UniqueCount);
        Assert.IsNull(run.PersistedCount);
        Assert.AreEqual(SyncRunFailureCodes.ProviderTimeout, run.FailureCode);
    }

    // Fim de paginação sem movimentos é uma execução completa da consulta, não uma afirmação de gasto zero histórico.
    [TestMethod]
    public async Task EmptyCompletedWindowRecordsZeroObservedRows()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);

        await using var context = CreateContext();
        var connection = new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "synthetic-item", null);
        var emptySnapshot = Snapshot() with
        {
            Transactions = [],
            ReceivedCount = 0,
            ObservedFrom = null,
            ObservedTo = null
        };
        var result = await new FinancialTransactionImportPromoter(context).PromoteAsync(
            connection,
            emptySnapshot,
            ReferenceDate,
            StartedAtUtc,
            StartedAtUtc.AddSeconds(1),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        var run = await context.Set<SyncRun>().SingleAsync();
        Assert.AreEqual(SyncRunState.Succeeded, run.State);
        Assert.AreEqual(SyncCoverageState.CompleteForRequestedWindow, run.CoverageState);
        Assert.AreEqual(0, run.ReceivedCount);
        Assert.AreEqual(0, run.UniqueCount);
        Assert.AreEqual(0, run.PersistedCount);
        Assert.IsNull(run.ObservedFrom);
        Assert.IsNull(run.ObservedTo);
    }

    // A aceitação assíncrona e a promoção atualizam a mesma SyncRun no commit dos movimentos e do último sucesso.
    [TestMethod]
    public async Task AcceptedAttemptPromotionCompletesTheAcceptedRunAtomically()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);
        var syncRunId = await SeedAcceptedRunAsync(fixture.Context);
        var grant = await AcquireAcceptedRunAsync(syncRunId, StartedAtUtc);
        var finishedAtUtc = StartedAtUtc.AddMinutes(1);

        await using (var context = CreateContext())
        {
            var result = await new FinancialTransactionImportPromoter(
                context, new FixedTimeProvider(finishedAtUtc)).PromoteAttemptAsync(
                    grant,
                    new TransactionImportConnection(ConnectionId, UserId, "pluggy", "synthetic-item", null),
                    Snapshot(),
                    new DateOnly(2026, 11, 20),
                    finishedAtUtc,
                    CancellationToken.None);
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, result.PersistedCount);
        }

        await using var verification = CreateContext();
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(syncRunId, run.Id);
        Assert.AreEqual(SyncRunState.Succeeded, run.State);
        Assert.AreEqual(1, run.AttemptNumber);
        Assert.AreEqual(StartedAtUtc, run.CreatedAtUtc);
        Assert.AreEqual(StartedAtUtc, run.AttemptStartedAtUtc);
        Assert.AreEqual(StartedAtUtc.AddMinutes(10), run.AttemptDeadlineAtUtc);
        Assert.AreEqual(finishedAtUtc, run.FinishedAtUtc);
        Assert.AreEqual(2, run.PagesRead);
        Assert.AreEqual(1, run.ReceivedCount);
        Assert.AreEqual(1, run.UniqueCount);
        Assert.AreEqual(1, run.PersistedCount);
        Assert.AreEqual(SyncCoverageState.CompleteForRequestedWindow, run.CoverageState);
        Assert.AreEqual(1, await verification.Set<SyncRun>().CountAsync());
        Assert.AreEqual(1, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(1, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(finishedAtUtc, await verification.Set<SyncRunLease>()
            .Select(lease => lease.ExpiresAtUtc).SingleAsync());
        Assert.AreEqual(finishedAtUtc, await verification.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
    }

    // Handoff de falha transitória agenda retry na execução aceita e libera o lease sem efeito financeiro.
    [TestMethod]
    public async Task AcceptedAttemptFailureUpdatesTheSameRunWithoutFinancialEffects()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context, StartedAtUtc.AddDays(-1));
        var syncRunId = await SeedAcceptedRunAsync(fixture.Context);
        var grant = await AcquireAcceptedRunAsync(syncRunId, StartedAtUtc);
        var failedAtUtc = StartedAtUtc.AddMinutes(1);

        await using (var context = CreateContext())
        {
            var result = await new SyncRunLeaseStore(context).ScheduleRetryAsync(
                    grant,
                    failedAtUtc,
                    failedAtUtc.AddMinutes(1),
                    SyncRunFailureCodes.ProviderTimeout,
                    pagesRead: 2,
                    receivedCount: 8,
                    CancellationToken.None);
            Assert.IsTrue(result);
        }

        await using var verification = CreateContext();
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(syncRunId, run.Id);
        Assert.AreEqual(SyncRunState.Pending, run.State);
        Assert.AreEqual(1, run.AttemptNumber);
        Assert.AreEqual(SyncRunFailureCodes.ProviderTimeout, run.FailureCode);
        Assert.AreEqual(failedAtUtc.AddMinutes(1), run.NextAttemptAtUtc);
        Assert.AreEqual(2, run.PagesRead);
        Assert.AreEqual(8, run.ReceivedCount);
        Assert.IsNull(run.FinishedAtUtc);
        Assert.AreEqual(1, await verification.Set<SyncRun>().CountAsync());
        Assert.AreEqual(0, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(0, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(StartedAtUtc.AddDays(-1), await verification.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
        Assert.AreEqual(failedAtUtc, await verification.Set<SyncRunLease>()
            .Select(lease => lease.ExpiresAtUtc).SingleAsync());
    }

    // Uma tentativa cujo fencing foi substituído não grava cartões, movimentos, sucesso nem terminaliza a execução.
    [TestMethod]
    public async Task SupersededAttemptCannotPromoteFinancialEffectsOrCompleteRun()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);
        var syncRunId = await SeedAcceptedRunAsync(fixture.Context);
        var staleGrant = await AcquireAcceptedRunAsync(syncRunId, StartedAtUtc);
        var takeoverAtUtc = staleGrant.ExpiresAtUtc;
        var currentGrant = await AcquireAcceptedRunAsync(syncRunId, takeoverAtUtc);
        Assert.AreEqual(2, currentGrant.AttemptNumber);
        Assert.AreEqual(staleGrant.FencingToken + 1, currentGrant.FencingToken);

        await using (var context = CreateContext())
        {
            var nowUtc = takeoverAtUtc.AddSeconds(1);
            var result = await new FinancialTransactionImportPromoter(
                context, new FixedTimeProvider(nowUtc)).PromoteAttemptAsync(
                    staleGrant,
                    new TransactionImportConnection(ConnectionId, UserId, "pluggy", "synthetic-item", null),
                    Snapshot(),
                    new DateOnly(2026, 11, 20),
                    StartedAtUtc.AddMinutes(1),
                    CancellationToken.None);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(TransactionImportFailureCode.LeaseLost, result.FailureCode);
        }

        await using var verification = CreateContext();
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(syncRunId, run.Id);
        Assert.AreEqual(SyncRunState.Running, run.State);
        Assert.AreEqual(2, run.AttemptNumber);
        Assert.IsNull(run.FinishedAtUtc);
        Assert.AreEqual(0, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(0, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.IsNull(await verification.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
    }

    // Deadline expirado barra a promoção antes das gravações mesmo que o snapshot externo esteja completo.
    [TestMethod]
    public async Task AttemptPastDeadlineCannotPromoteEvenWhenLeaseClaimIsPresent()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);
        var syncRunId = await SeedAcceptedRunAsync(fixture.Context);
        var grant = await AcquireAcceptedRunAsync(syncRunId, StartedAtUtc);
        var deadline = grant.AttemptDeadlineAtUtc;

        await using (var context = CreateContext())
        {
            var result = await new FinancialTransactionImportPromoter(
                context, new FixedTimeProvider(deadline)).PromoteAttemptAsync(
                    grant,
                    new TransactionImportConnection(ConnectionId, UserId, "pluggy", "synthetic-item", null),
                    Snapshot(),
                    new DateOnly(2026, 11, 20),
                    deadline,
                    CancellationToken.None);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(TransactionImportFailureCode.AttemptDeadlineExceeded, result.FailureCode);
        }

        await using var verification = CreateContext();
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(syncRunId, run.Id);
        Assert.AreEqual(SyncRunState.Running, run.State);
        Assert.IsNull(run.FinishedAtUtc);
        Assert.AreEqual(0, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(0, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.IsNull(await verification.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
    }

    // Promoção antiga seguida de nova aceita ambas; invertida, a antiga é recusada sob o lock da conexão.
    [TestMethod]
    public async Task PromotionOrderPreventsStaleSnapshotAndAllowsNewerSnapshot()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);

        var connection = new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "synthetic-item", null);
        var older = StartedAtUtc;
        var newer = StartedAtUtc.AddMinutes(10);
        await using var firstContext = CreateContext();
        var oldFirst = await new FinancialTransactionImportPromoter(firstContext).PromoteAsync(
            connection, Snapshot(), ReferenceDate, older, older.AddMinutes(1), CancellationToken.None);
        Assert.IsTrue(oldFirst.Succeeded);

        await using var secondContext = CreateContext();
        var newAfterOld = await new FinancialTransactionImportPromoter(secondContext).PromoteAsync(
            connection, Snapshot(), ReferenceDate, newer, newer.AddMinutes(1), CancellationToken.None);
        Assert.IsTrue(newAfterOld.Succeeded);

        await using var thirdContext = CreateContext();
        var oldAfterNew = await new FinancialTransactionImportPromoter(thirdContext).PromoteAsync(
            connection, Snapshot(), ReferenceDate, older, older.AddMinutes(2), CancellationToken.None);

        Assert.IsFalse(oldAfterNew.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.StaleSnapshot, oldAfterNew.FailureCode);
        await using var verification = CreateContext();
        Assert.AreEqual(1, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(2, await verification.Set<SyncRun>().CountAsync());
        Assert.AreEqual(newer.AddMinutes(1), await verification.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
    }

    // Nova execução com os mesmos IDs atualiza os mesmos UUIDs sem duplicar movimentos ou cartões.
    [TestMethod]
    public async Task ReexecutingSameSnapshotKeepsStableFinancialIdentity()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);

        var connection = new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "synthetic-item", null);
        await using var firstContext = CreateContext();
        var first = await new FinancialTransactionImportPromoter(firstContext).PromoteAsync(
            connection, Snapshot(), ReferenceDate, StartedAtUtc, StartedAtUtc.AddMinutes(1), CancellationToken.None);
        var firstTransactionId = await firstContext.Set<FinancialTransaction>().Select(value => value.Id).SingleAsync();

        await using var retryContext = CreateContext();
        var retryStartedAt = StartedAtUtc.AddMinutes(2);
        var retry = await new FinancialTransactionImportPromoter(retryContext).PromoteAsync(
            connection, Snapshot(), ReferenceDate, retryStartedAt, retryStartedAt.AddMinutes(1), CancellationToken.None);

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(retry.Succeeded);
        await using var verification = CreateContext();
        Assert.AreEqual(firstTransactionId, await verification.Set<FinancialTransaction>().Select(value => value.Id).SingleAsync());
        Assert.AreEqual(1, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(2, await verification.Set<SyncRun>().CountAsync());
    }

    // Perda de acknowledgement após commit físico só retorna sucesso quando nova conexão confirma o estado completo.
    [TestMethod]
    public async Task LostCommitAcknowledgementIsVerifiedIndependentlyBeforeSuccess()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);

        var interceptor = new FailAfterPhysicalCommitInterceptor();
        await using var context = CreateContext(interceptor);
        var connection = new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "synthetic-item", null);
        var result = await new FinancialTransactionImportPromoter(context).PromoteAsync(
            connection, Snapshot(), ReferenceDate, StartedAtUtc, StartedAtUtc.AddMinutes(1), CancellationToken.None);

        Assert.AreEqual(1, interceptor.Calls);
        Assert.IsTrue(result.Succeeded);
        await using var verification = CreateContext();
        Assert.AreEqual(1, await verification.Set<SyncRun>().CountAsync(run => run.State == SyncRunState.Succeeded));
        Assert.AreEqual(1, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(1, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(StartedAtUtc.AddMinutes(1), await verification.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
    }

    // Falha antes do commit também é verificada em contexto novo e nunca é anunciada como promoção confirmada.
    [TestMethod]
    public async Task FailedCommitWithoutIndependentSuccessReturnsUncertainOutcome()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);

        await using var context = CreateContext(new FailBeforePhysicalCommitInterceptor());
        var connection = new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "synthetic-item", null);
        var result = await new FinancialTransactionImportPromoter(context).PromoteAsync(
            connection, Snapshot(), ReferenceDate, StartedAtUtc, StartedAtUtc.AddMinutes(1), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.CommitUncertain, result.FailureCode);
        await using var verification = CreateContext();
        Assert.AreEqual(0, await verification.Set<SyncRun>().CountAsync());
        Assert.AreEqual(0, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(0, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.IsNull(await verification.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
    }

    // Semeia titular/conexão e aplica a migration de identidade para testar somente a cadeia vigente.
    private static async Task SeedConnectionAsync(FinoraDbContext context, DateTimeOffset? lastSuccessfulImportAtUtc = null)
    {
        context.Users.Add(new ApplicationUser(UserId));
        await context.SaveChangesAsync();
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId, UserId, "pluggy", "synthetic-item", lastSuccessfulImportAtUtc: lastSuccessfulImportAtUtc));
        await context.SaveChangesAsync();
        await context.Database.MigrateAsync();
    }

    // Cria uma intenção aceita com a mesma janela usada pelo snapshot sintético desta prova.
    private static async Task<Guid> SeedAcceptedRunAsync(FinoraDbContext context)
    {
        var syncRunId = Guid.NewGuid();
        context.Set<SyncRun>().Add(SyncRun.Create(
            syncRunId,
            UserId,
            ConnectionId,
            StartedAtUtc,
            new DateOnly(2025, 11, 20),
            new DateOnly(2026, 11, 21),
            nextAttemptAtUtc: StartedAtUtc));
        await context.SaveChangesAsync();
        return syncRunId;
    }

    // A aquisição real do store persiste estado Running e lease para o claim consumido pelo promotor.
    private static async Task<SyncRunLeaseGrant> AcquireAcceptedRunAsync(
        Guid syncRunId,
        DateTimeOffset acquiredAtUtc)
    {
        await using var context = CreateContext();
        var result = await new SyncRunLeaseStore(context).AcquireAsync(
            syncRunId, acquiredAtUtc, CancellationToken.None);
        Assert.AreEqual(SyncRunLeaseAcquisitionStatus.Acquired, result.Status);
        return result.Grant!;
    }

    // Fixture completa informa contagens/intervalo conhecidos sem apresentar registros sintéticos como amostra real.
    private static TransactionImportSnapshot Snapshot() => new(
        [new TransactionImportAccount("synthetic-account", "CREDIT", "CREDIT_CARD", "BRL")],
        [new PluggyTransactionData(
            "synthetic-transaction", "synthetic-account", "description synthetic", 12.3456m,
            "BRL", "2026-11-18", "DEBIT", "POSTED", "TARIFA")],
        new DateOnly(2025, 11, 20),
        true,
        PagesRead: 2,
        ReceivedCount: 1,
        ObservedFrom: new DateOnly(2026, 11, 18),
        ObservedTo: new DateOnly(2026, 11, 18));

    // Contexto independente usa exclusivamente o destino dedicado validado pela fixture PostgreSQL.
    private static FinoraDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, settings.ConnectionString);
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new FinoraDbContext(options.Options);
    }

    private sealed class FixedConnectionReader(TransactionImportConnection connection)
        : ITransactionImportConnectionReader
    {
        public Task<TransactionImportConnection?> FindOwnedAsync(
            Guid connectionId,
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult<TransactionImportConnection?>(
                connection.Id == connectionId && connection.UserId == userId ? connection : null);
    }

    private sealed class TransactionStateProbeReader(
        FinoraDbContext context,
        TransactionImportSnapshot snapshot) : ITransactionImportReader
    {
        public bool ReadWithNoTransaction { get; private set; }

        public Task<TransactionImportReadResult> ReadAsync(
            string externalItemId,
            DateOnly dateFrom,
            CancellationToken cancellationToken)
        {
            ReadWithNoTransaction = context.Database.CurrentTransaction is null;
            return Task.FromResult(new TransactionImportReadResult(snapshot, null));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Interceptor falha após o provider concluir fisicamente a transação para exercitar verificação read-after-commit.
    private sealed class FailAfterPhysicalCommitInterceptor : DbTransactionInterceptor
    {
        public int Calls { get; private set; }

        public override Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromException(new InvalidOperationException("synthetic lost acknowledgement"));
        }
    }

    // Interrompe a confirmação antes do provider para comprovar que a verificação externa não infere sucesso.
    private sealed class FailBeforePhysicalCommitInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult>(new InvalidOperationException("synthetic pre-commit failure"));
    }
}
