using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// A migration e as constraints novas rodam somente no banco PostgreSQL descartável protegido pela fixture.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlRecurrencePersistenceMigrationTests
{
    internal const string MigrationId = "20261126003137_Fnr038RecurrenceAssistance";
    private const string PreviousMigrationId = "20261124043312_Fnr016InitialCategories";
    private static readonly Guid UserOneId = Guid.Parse("2d952e96-7c63-408e-9279-7c3f6f2acac1");
    private static readonly Guid UserTwoId = Guid.Parse("bafc9172-49ec-4c5f-9e14-50f65adba7f6");
    private static readonly Guid ConnectionId = Guid.Parse("6551e0f6-39ed-497c-90e5-9c341548e0ea");
    private static readonly Guid CardId = Guid.Parse("40902e1a-894e-4628-9d18-478fe3ac6b03");
    private static readonly Guid FirstTransactionId = Guid.Parse("f67f49e1-99bb-4aa0-9e18-0c1b48465a90");
    private static readonly Guid SecondTransactionId = Guid.Parse("8cfd530c-dd4e-420f-b29f-1620b6ea94a2");
    private static readonly Guid ThirdTransactionId = Guid.Parse("8d1a942a-247b-457d-a990-77533d3d2702");
    private static readonly Guid CandidateId = Guid.Parse("71af78d0-6189-4ecc-a4a9-0423f80fd383");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 25);
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    // Instalação limpa cria as duas tabelas; nova chamada administrativa não duplica nem altera o schema.
    [TestMethod]
    public async Task MigrationAppliesFromEmptySchemaAndCanBeReapplied()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();

        await fixture.Context.Database.MigrateAsync();
        Assert.IsTrue((await fixture.Context.Database.GetAppliedMigrationsAsync()).Contains(MigrationId));
        CollectionAssert.IsSubsetOf(
            new[] { "RecurrenceCandidate", "RecurrenceDecision" },
            await PostgreSqlPersistenceTests.ReadPublicTablesAsync(fixture.Context));
        await fixture.Context.Database.MigrateAsync();

        Assert.AreEqual(0, await fixture.Context.Set<RecurrenceCandidate>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Set<RecurrenceDecision>().CountAsync());
    }

    // Upgrade conserva grafo/anotação e o PostgreSQL bloqueia duplicidade, trio inválido e titular cruzado.
    [TestMethod]
    public async Task UpgradePreservesFinancialRowsAndEnforcesCandidateDecisionOwnership()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var context = fixture.Context;
        await context.Database.MigrateAsync(PreviousMigrationId);
        await SeedExistingFinancialGraphAsync(context);

        await context.Database.MigrateAsync(MigrationId);
        Assert.AreEqual(3, await context.Set<FinancialTransaction>().CountAsync());
        var annotation = await context.Set<TransactionAnnotation>().SingleAsync();
        Assert.AreEqual(FirstTransactionId, annotation.TransactionId);
        Assert.AreEqual("anotação sintética preservada", annotation.Note);

        var candidate = RecurrenceCandidate.Create(
            CandidateId, UserOneId, "recurrence-v1", FirstTransactionId, SecondTransactionId, ThirdTransactionId,
            EvaluatedAtUtc);
        var decision = RecurrenceDecision.Create(
            CandidateId, UserOneId, RecurrenceDecisionKind.Subscription, EvaluatedAtUtc.AddMinutes(1));
        context.AddRange(candidate, decision);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persistedCandidate = await context.Set<RecurrenceCandidate>().AsNoTracking().SingleAsync();
        var persistedDecision = await context.Set<RecurrenceDecision>().AsNoTracking().SingleAsync();
        Assert.AreEqual("recurrence-v1", persistedCandidate.PolicyVersion);
        Assert.IsTrue(persistedCandidate.IsCurrent);
        Assert.AreEqual(ThirdTransactionId, persistedCandidate.ThirdOccurrenceTransactionId);
        Assert.AreEqual(RecurrenceDecisionKind.Subscription, persistedDecision.Kind);
        Assert.AreEqual(UserOneId, persistedDecision.UserId);

        await AssertSqlStateAsync(PostgresErrorCodes.UniqueViolation, () => InsertCandidateAsync(
            context, Guid.NewGuid(), UserOneId, "recurrence-v1", FirstTransactionId,
            SecondTransactionId, ThirdTransactionId));
        await AssertSqlStateAsync(PostgresErrorCodes.ForeignKeyViolation, () => InsertCandidateAsync(
            context, Guid.NewGuid(), UserTwoId, "recurrence-v1", FirstTransactionId,
            SecondTransactionId, ThirdTransactionId));
        await AssertSqlStateAsync(PostgresErrorCodes.ForeignKeyViolation, () => InsertDecisionAsync(
            context, CandidateId, UserTwoId, "Rejected"));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => InsertCandidateAsync(
            context, Guid.NewGuid(), UserOneId, "synthetic-invalid-v1", FirstTransactionId,
            FirstTransactionId, ThirdTransactionId));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => InsertDecisionAsync(
            context, CandidateId, UserOneId, "Unknown"));

        // O downgrade é recusado com qualquer dado FNR-038 e a transação deixa decisão/candidato intactos.
        await Assert.ThrowsAsync<Exception>(async () =>
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigrationId));
        context.ChangeTracker.Clear();
        Assert.IsTrue((await context.Database.GetAppliedMigrationsAsync()).Contains(MigrationId));
        Assert.AreEqual(1, await context.Set<RecurrenceCandidate>().CountAsync());
        Assert.AreEqual(RecurrenceDecisionKind.Subscription,
            await context.Set<RecurrenceDecision>().Select(item => item.Kind).SingleAsync());

        // A reaplicação administrativa após rollback recusado não modifica as escolhas persistidas.
        await context.Database.MigrateAsync();
        Assert.AreEqual(1, await context.Set<RecurrenceCandidate>().CountAsync());
        Assert.AreEqual(1, await context.Set<RecurrenceDecision>().CountAsync());
    }

    // Semeia titular, cartão, três movimentos e anotação antes do upgrade, usando somente dados sintéticos.
    private static async Task SeedExistingFinancialGraphAsync(FinoraDbContext context)
    {
        context.Users.AddRange(new ApplicationUser(UserOneId), new ApplicationUser(UserTwoId));
        await context.SaveChangesAsync();
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId, UserOneId, "pluggy", "fnr038-synthetic-item"));
        await context.SaveChangesAsync();
        context.Set<CardAccount>().Add(CardAccount.Create(
            CardId, UserOneId, ConnectionId, "fnr038-synthetic-card", "BRL"));
        await context.SaveChangesAsync();

        var transactions = new[]
        {
            CreateTransaction(FirstTransactionId, "synthetic-september", "2026-09-05"),
            CreateTransaction(SecondTransactionId, "synthetic-october", "2026-10-06"),
            CreateTransaction(ThirdTransactionId, "synthetic-november", "2026-11-07")
        };
        context.Set<FinancialTransaction>().AddRange(transactions);
        await context.SaveChangesAsync();

        var category = Category.Create(Guid.NewGuid(), UserOneId, false, "Sintética", "SINTETICA");
        context.Set<Category>().Add(category);
        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            FirstTransactionId, UserOneId, category.Id, UserOneId,
            "anotação sintética preservada", EvaluatedAtUtc));
        await context.SaveChangesAsync();
    }

    // O snapshot representa apenas os campos necessários para satisfazer as FKs da migration.
    private static FinancialTransaction CreateTransaction(Guid id, string externalId, string date)
    {
        var facts = new TransactionFacts(
            externalId, "fnr038-synthetic-card", "Assinatura sintética", 25m,
            "BRL", date, "DEBIT", "POSTED", "PURCHASE");
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);
        return FinancialTransaction.Create(
            id, UserOneId, CardId, normalization, "transaction-normalizer-v1", ReferenceDate);
    }

    // SQL parametrizado contorna as fábricas para conferir as constraints reais do PostgreSQL.
    private static Task<int> InsertCandidateAsync(
        FinoraDbContext context,
        Guid candidateId,
        Guid userId,
        string policyVersion,
        Guid firstOccurrenceId,
        Guid secondOccurrenceId,
        Guid thirdOccurrenceId) => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RecurrenceCandidate" (
                "Id", "UserId", "PolicyVersion", "FirstOccurrenceTransactionId",
                "SecondOccurrenceTransactionId", "ThirdOccurrenceTransactionId",
                "CreatedAtUtc", "LastEvaluatedAtUtc", "IsCurrent")
            VALUES (
                {candidateId}, {userId}, {policyVersion}, {firstOccurrenceId},
                {secondOccurrenceId}, {thirdOccurrenceId}, {EvaluatedAtUtc}, {EvaluatedAtUtc}, TRUE);
            """);

    // Decisões escritas fora do EF não podem associar outro titular nem um enum não suportado.
    private static Task<int> InsertDecisionAsync(
        FinoraDbContext context,
        Guid candidateId,
        Guid userId,
        string kind) => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RecurrenceDecision" ("CandidateId", "UserId", "Kind", "UpdatedAtUtc")
            VALUES ({candidateId}, {userId}, {kind}, {EvaluatedAtUtc});
            """);

    // Compara o SQLSTATE sem depender do texto localizado retornado pelo provider.
    private static async Task AssertSqlStateAsync(string expectedSqlState, Func<Task<int>> action)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.AreEqual(expectedSqlState, exception.SqlState);
    }
}
