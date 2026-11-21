using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Api.Tests;

// Valida troca/replay de item somente em PostgreSQL descartável e com identidades externas sintéticas.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialTransactionReconnectionTests
{
    private static readonly Guid UserId = Guid.Parse("d80b14c3-a4e6-4c5f-9ba0-f8bb61fd5674");
    private static readonly Guid OtherUserId = Guid.Parse("772ff8cc-2d57-40c0-8b15-0031b895532a");
    private static readonly Guid ConnectionId = Guid.Parse("78ab9934-7930-4266-997d-61e66415d473");
    private static readonly Guid OtherConnectionId = Guid.Parse("52de09cf-6be6-4369-befc-f448771c304e");
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ReferenceDate = new(2026, 11, 20);

    // A troca só ocorre com IDs de cartão idênticos e preserva UUID/anotação; item aposentado não pode ser reativado.
    [TestMethod]
    public async Task VerifiedReconnectionPreservesHistoryAndRejectsOldItemReplay()
    {
        await using var fixture = await CreateMigratedConnectionAsync();
        var originalConnection = Connection("old-item");
        var originalSnapshot = Snapshot("synthetic-card", "synthetic-transaction");
        await using (var importContext = CreateContext())
        {
            var imported = await new FinancialTransactionImportPromoter(importContext).PromoteAsync(
                originalConnection, originalSnapshot, ReferenceDate,
                StartedAtUtc, StartedAtUtc.AddMinutes(1), CancellationToken.None);
            Assert.IsTrue(imported.Succeeded);
            var transaction = await importContext.Set<FinancialTransaction>().SingleAsync();
            importContext.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
                transaction.Id, UserId, null, null, "synthetic private note", StartedAtUtc));
            await importContext.SaveChangesAsync();
        }

        var reconnectStartedAt = StartedAtUtc.AddMinutes(10);
        var replacementSnapshot = Snapshot("synthetic-card", "synthetic-transaction");
        await using (var reconnectContext = CreateContext())
        {
            var handler = new ReconnectTransactionConnectionHandler(
                new FixedConnectionReader(originalConnection),
                new FixedImportReader(replacementSnapshot),
                new FinancialTransactionImportPromoter(reconnectContext),
                new FixedTimeProvider(reconnectStartedAt));
            var result = await handler.Handle(
                new ReconnectTransactionConnectionCommand(UserId, ConnectionId, "new-item"),
                CancellationToken.None);
            Assert.IsTrue(result.Succeeded);
        }

        await using (var verification = CreateContext())
        {
            var connection = await verification.Set<FinancialConnection>().SingleAsync();
            Assert.AreEqual("new-item", connection.ExternalItemId);
            Assert.AreEqual(reconnectStartedAt, connection.LastSuccessfulImportAtUtc);
            var identities = await verification.Set<FinancialConnectionItemIdentity>()
                .OrderBy(identity => identity.ExternalItemId).ToArrayAsync();
            Assert.AreEqual(2, identities.Length);
            Assert.IsNotNull(identities.Single(identity => identity.ExternalItemId == "old-item").RetiredAtUtc);
            Assert.IsNull(identities.Single(identity => identity.ExternalItemId == "new-item").RetiredAtUtc);
            Assert.AreEqual(1, await verification.Set<CardAccount>().CountAsync());
            Assert.AreEqual(1, await verification.Set<FinancialTransaction>().CountAsync());
            Assert.AreEqual("synthetic private note", await verification.Set<TransactionAnnotation>()
                .Select(annotation => annotation.Note).SingleAsync());
        }

        await using (var replayContext = CreateContext())
        {
            var currentConnection = Connection("new-item", StartedAtUtc.AddMinutes(10).AddSeconds(1));
            var replay = await new FinancialTransactionImportPromoter(replayContext).PromoteReconnectionAsync(
                currentConnection,
                "old-item",
                replacementSnapshot,
                ReferenceDate,
                reconnectStartedAt.AddMinutes(2),
                reconnectStartedAt.AddMinutes(3),
                CancellationToken.None);
            Assert.IsFalse(replay.Succeeded);
            Assert.AreEqual(TransactionImportFailureCode.IdentityConflict, replay.FailureCode);
        }

        await using var finalState = CreateContext();
        Assert.AreEqual("new-item", await finalState.Set<FinancialConnection>().Select(value => value.ExternalItemId).SingleAsync());
        Assert.AreEqual(reconnectStartedAt, await finalState.Set<FinancialConnection>()
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
        Assert.AreEqual(2, await finalState.Set<SyncRun>().CountAsync());
    }

    // Conta com IDs diferentes ou item já ativo em outro titular falha sem atualizar estado confirmado.
    [TestMethod]
    public async Task ReconnectionRejectsUnprovenCardsAndCrossOwnerItemCollision()
    {
        await using var fixture = await CreateMigratedConnectionAsync(includeOtherOwner: true);
        await using var seed = CreateContext();
        seed.Set<CardAccount>().Add(CardAccount.Create(
            Guid.NewGuid(), UserId, ConnectionId, "synthetic-card", "BRL", "same visible card", "4242"));
        await seed.SaveChangesAsync();

        await using (var collisionContext = CreateContext())
        {
            var collision = await new FinancialTransactionImportPromoter(collisionContext).PromoteReconnectionAsync(
                Connection("old-item"),
                "other-owner-item",
                Snapshot("synthetic-card", "synthetic-transaction"),
                ReferenceDate,
                StartedAtUtc,
                StartedAtUtc.AddMinutes(1),
                CancellationToken.None);
            Assert.IsFalse(collision.Succeeded);
            Assert.AreEqual(TransactionImportFailureCode.IdentityConflict, collision.FailureCode);
        }

        await using (var mismatchContext = CreateContext())
        {
            var mismatch = await new FinancialTransactionImportPromoter(mismatchContext).PromoteReconnectionAsync(
                Connection("old-item"),
                "unproven-item",
                Snapshot("new-card-id", "synthetic-transaction"),
                ReferenceDate,
                StartedAtUtc,
                StartedAtUtc.AddMinutes(1),
                CancellationToken.None);
            Assert.IsFalse(mismatch.Succeeded);
            Assert.AreEqual(TransactionImportFailureCode.IdentityConflict, mismatch.FailureCode);
        }

        await using var verification = CreateContext();
        Assert.AreEqual("old-item", await verification.Set<FinancialConnection>().Where(value => value.Id == ConnectionId)
            .Select(value => value.ExternalItemId).SingleAsync());
        Assert.IsNull(await verification.Set<FinancialConnection>().Where(value => value.Id == ConnectionId)
            .Select(value => value.LastSuccessfulImportAtUtc).SingleAsync());
        Assert.AreEqual(1, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(0, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(0, await verification.Set<SyncRun>().CountAsync());
    }

    // A migration cria o schema-base, semeia conexões legadas e depois valida migration/backfill de identidade.
    private static async Task<DisposablePostgresMigrationFixture> CreateMigratedConnectionAsync(bool includeOtherOwner = false)
    {
        var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        fixture.Context.Users.Add(new ApplicationUser(UserId));
        if (includeOtherOwner)
        {
            fixture.Context.Users.Add(new ApplicationUser(OtherUserId));
        }

        await fixture.Context.SaveChangesAsync();
        fixture.Context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId, UserId, "pluggy", "old-item"));
        if (includeOtherOwner)
        {
            fixture.Context.Set<FinancialConnection>().Add(FinancialConnection.Create(
                OtherConnectionId, OtherUserId, "pluggy", "other-owner-item"));
        }

        await fixture.Context.SaveChangesAsync();
        await fixture.Context.Database.MigrateAsync();
        return fixture;
    }

    // Snapshot sintético usa uma conta e um movimento individual sem representar amostra externa real.
    private static TransactionImportSnapshot Snapshot(string accountId, string transactionId) => new(
        [new TransactionImportAccount(accountId, "CREDIT", "CREDIT_CARD", "BRL")],
        [new PluggyTransactionData(
            transactionId, accountId, "synthetic description", 14.25m,
            "BRL", "2026-11-18", "DEBIT", "POSTED", "TARIFA")],
        new DateOnly(2025, 11, 20), true, 2, 1,
        new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 18));

    // Projeção representa somente a conexão autorizada; segredos e nomes não entram no teste.
    private static TransactionImportConnection Connection(string externalItemId, DateTimeOffset? lastSuccess = null) =>
        new(ConnectionId, UserId, "pluggy", externalItemId, lastSuccess);

    // Contexto dedicado usa somente a connection string validada pela fixture descartável.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, settings.ConnectionString);
        return new FinoraDbContext(options.Options);
    }

    private sealed class FixedConnectionReader(TransactionImportConnection connection)
        : ITransactionImportConnectionReader
    {
        public Task<TransactionImportConnection?> FindOwnedAsync(
            Guid connectionId,
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult<TransactionImportConnection?>(
                connectionId == connection.Id && userId == connection.UserId ? connection : null);
    }

    private sealed class FixedImportReader(TransactionImportSnapshot snapshot) : ITransactionImportReader
    {
        public Task<TransactionImportReadResult> ReadAsync(
            string externalItemId,
            DateOnly dateFrom,
            CancellationToken cancellationToken) => Task.FromResult(
                new TransactionImportReadResult(snapshot, null));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
