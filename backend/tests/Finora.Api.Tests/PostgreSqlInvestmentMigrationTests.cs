using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Executa a migration da carteira apenas no PostgreSQL descartável protegido pela fixture.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlInvestmentMigrationTests
{
    internal const string MigrationId = "20261128011938_AddInvestmentPortfolio";
    private const string PreviousMigrationId = "20261126003137_Fnr038RecurrenceAssistance";
    private static readonly Guid UserId = Guid.Parse("0310d42f-d03b-4ac8-939e-d18601610dd6");
    private static readonly Guid ConnectionId = Guid.Parse("92713cd6-9051-4c77-8e0f-5cb9490c045a");
    private static readonly Guid CardId = Guid.Parse("4df846d5-8cb5-4db1-b80f-8b52d5884b6d");
    private static readonly Guid TransactionId = Guid.Parse("eb952a3f-b2f4-44ef-a251-a6ae9054d0d6");
    private static readonly Guid CategoryId = Guid.Parse("1ef1268b-2d30-4d20-8700-4aaf1ea37b67");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 27);
    private static readonly DateTimeOffset AnnotatedAtUtc = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    // A instalação em schema vazio inclui a migration, cria tabela sem seed e deixa a carteira vazia.
    [TestMethod]
    public async Task MigrationAppliesFromEmptySchemaWithoutSeed()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();

        await fixture.Context.Database.MigrateAsync(MigrationId);

        var appliedMigrations = (await fixture.Context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.IsTrue(appliedMigrations.Contains(MigrationId, StringComparer.Ordinal));
        CollectionAssert.IsSubsetOf(
            new[] { "Investment" },
            await PostgreSqlPersistenceTests.ReadPublicTablesAsync(fixture.Context));
        Assert.AreEqual(0, await fixture.Context.Set<Investment>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Users.CountAsync());
    }

    // O upgrade parte do último schema P0, preserva seu grafo sintético e adiciona somente uma carteira vazia.
    [TestMethod]
    public async Task MigrationUpgradePreservesExistingFinancialGraph()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var context = fixture.Context;
        await context.Database.MigrateAsync(PreviousMigrationId);
        await SeedExistingFinancialGraphAsync(context);

        await context.Database.MigrateAsync(MigrationId);

        Assert.AreEqual(1, await context.Users.CountAsync());
        Assert.AreEqual(1, await context.Set<FinancialConnection>().CountAsync());
        Assert.AreEqual(1, await context.Set<CardAccount>().CountAsync());
        Assert.AreEqual(1, await context.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(1, await context.Set<Category>().CountAsync(category => !category.IsSystem));
        Assert.AreEqual(1, await context.Set<TransactionAnnotation>().CountAsync());
        Assert.AreEqual(0, await context.Set<Investment>().CountAsync());

        // Leitura sem tracking comprova que IDs, titular e anotação P0 sobreviveram ao upgrade aditivo.
        var transaction = await context.Set<FinancialTransaction>().AsNoTracking().SingleAsync();
        var annotation = await context.Set<TransactionAnnotation>().AsNoTracking().SingleAsync();
        Assert.AreEqual(TransactionId, transaction.Id);
        Assert.AreEqual(UserId, transaction.UserId);
        Assert.AreEqual(TransactionId, annotation.TransactionId);
        Assert.AreEqual("anotação sintética preservada", annotation.Note);
    }

    // Cria titular, conexão, cartão, movimento e anotação exclusivamente sintéticos antes da nova migration.
    private static async Task SeedExistingFinancialGraphAsync(FinoraDbContext context)
    {
        context.Users.Add(new ApplicationUser(UserId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId, UserId, "pluggy", "fnr029-synthetic-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().Add(CardAccount.Create(
            CardId, UserId, ConnectionId, "fnr029-synthetic-card", "BRL"));
        await context.SaveChangesAsync();

        context.Set<FinancialTransaction>().Add(CreateTransaction());
        await context.SaveChangesAsync();

        var category = Category.Create(CategoryId, UserId, isSystem: false, "Sintética", "SINTETICA");
        context.Set<Category>().Add(category);
        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            TransactionId, UserId, CategoryId, UserId, "anotação sintética preservada", AnnotatedAtUtc));
        await context.SaveChangesAsync();
    }

    // Normaliza uma transação sintética pela política existente para preparar o mesmo esquema P0 da produção.
    private static FinancialTransaction CreateTransaction()
    {
        var facts = new TransactionFacts(
            "fnr029-synthetic-transaction",
            "fnr029-synthetic-card",
            "Movimento sintético",
            25m,
            "BRL",
            "2026-11-26",
            "DEBIT",
            "POSTED",
            "PURCHASE");
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);
        return FinancialTransaction.Create(
            TransactionId,
            UserId,
            CardId,
            normalization,
            "transaction-normalizer-v1",
            ReferenceDate);
    }
}
