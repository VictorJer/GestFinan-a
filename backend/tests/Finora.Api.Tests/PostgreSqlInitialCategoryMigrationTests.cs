using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Migrations alteram o schema public compartilhado e só podem rodar no PostgreSQL local descartável.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlInitialCategoryMigrationTests
{
    private const string PreviousMigration = "20261123013815_Fnr013SyncRunExecutionRecovery";
    private const string CatalogMigration = "20261124043312_Fnr016InitialCategories";
    private static readonly Guid UserId = Guid.Parse("ccde8c67-2213-479d-8843-465a8bdc7bbb");

    // Instalação limpa, upgrade com categoria pessoal e reaplicação do SQL preservam identidades sem duplicar catálogo.
    [TestMethod]
    public async Task FreshInstallUpgradeAndDirectReapplicationKeepExactlySevenEntries()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var database = fixture.Context;

        await database.Database.MigrateAsync();
        await AssertCatalogAsync(database);
        await database.Database.MigrateAsync();
        await AssertCatalogAsync(database);

        // Retorna apenas à migration anterior: Down retém categorias para não romper futuras referências.
        await database.Database.MigrateAsync(PreviousMigration);
        var (personalId, transactionId) = await SeedPersonalAnnotationAsync(database, "Projetos", "PROJETOS");

        await database.Database.MigrateAsync();
        await AssertCatalogAsync(database);
        Assert.AreEqual(1, await database.Set<Category>().CountAsync(category => !category.IsSystem));
        await AssertAnnotationAsync(database, personalId, transactionId);

        // Testa o bloco idempotente independente do histórico de migrations do EF.
        var block = new Fnr016InitialCategories().UpOperations.OfType<SqlOperation>().Single().Sql;
        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            await database.Database.ExecuteSqlRawAsync(block);
            await transaction.CommitAsync();
        }
        await AssertCatalogAsync(database);
        Assert.AreEqual(1, await database.Set<Category>().CountAsync(category => !category.IsSystem));
        await AssertAnnotationAsync(database, personalId, transactionId);
        Assert.IsTrue((await database.Database.GetAppliedMigrationsAsync()).Contains(CatalogMigration));
    }

    // Conflito tardio reverte também inserções iniciais, sem apagar a categoria pessoal preexistente.
    [TestMethod]
    public async Task ReservedPersonalKeyAbortsUpgradeAndRollsBackEntireCatalog()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var database = fixture.Context;
        await database.Database.MigrateAsync(PreviousMigration);
        var (personalId, transactionId) = await SeedPersonalAnnotationAsync(database, "Saude", "SAUDE");

        await Assert.ThrowsAsync<Exception>(async () => await database.Database.MigrateAsync());
        Assert.AreEqual(0, await database.Set<Category>().CountAsync(category => category.IsSystem));
        Assert.AreEqual(personalId, await database.Set<Category>().Where(category => !category.IsSystem)
            .Select(category => category.Id).SingleAsync());
        await AssertAnnotationAsync(database, personalId, transactionId);
        Assert.IsFalse((await database.Database.GetAppliedMigrationsAsync()).Contains(CatalogMigration));
    }

    // Um ID reservado ocupado pelo escopo pessoal não pode ser reatribuído ao sistema silenciosamente.
    [TestMethod]
    public async Task ReservedIdInPersonalScopeRejectsCatalogWithoutOverwritingOwner()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var database = fixture.Context;
        await database.Database.MigrateAsync(PreviousMigration);
        database.Users.Add(new ApplicationUser(UserId));
        var reservedId = SystemCategoryCatalog.Categories[0].Id;
        database.Set<Category>().Add(Category.Create(reservedId, UserId, false, "Pessoal", "PESSOAL"));
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<Exception>(async () => await database.Database.MigrateAsync());
        var original = await database.Set<Category>().AsNoTracking().SingleAsync(category => category.Id == reservedId);
        Assert.AreEqual(UserId, original.UserId);
        Assert.AreEqual("PESSOAL", original.NormalizedName);
        Assert.AreEqual(0, await database.Set<Category>().CountAsync(category => category.IsSystem));
    }

    // Nome de sistema preexistente com outra identidade interrompe upgrade, inclusive quando a chave difere.
    [TestMethod]
    public async Task ConflictingSystemNameRejectsCatalogAndPreservesOriginalRow()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var database = fixture.Context;
        await database.Database.MigrateAsync(PreviousMigration);
        var priorId = Guid.NewGuid();
        database.Set<Category>().Add(Category.Create(priorId, null, true, "Outros", "OUTRA-CHAVE"));
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<Exception>(async () => await database.Database.MigrateAsync());
        Assert.AreEqual(1, await database.Set<Category>().CountAsync(category => category.IsSystem));
        Assert.AreEqual(priorId, await database.Set<Category>().Select(category => category.Id).SingleAsync());
    }

    // O grafo mínimo possui anotação própria para provar que o provisionamento não toca vínculos preexistentes.
    private static async Task<(Guid PersonalId, Guid TransactionId)> SeedPersonalAnnotationAsync(
        Finora.Infrastructure.Persistence.FinoraDbContext context, string name, string key)
    {
        var connectionId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var personalId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser(UserId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().Add(FinancialConnection.Create(connectionId, UserId, "pluggy", "fnr016-item"));
        await context.SaveChangesAsync();
        context.Set<CardAccount>().Add(CardAccount.Create(cardId, UserId, connectionId, "fnr016-card", "BRL"));
        await context.SaveChangesAsync();
        context.Set<Category>().Add(Category.Create(personalId, UserId, false, name, key));
        var referenceDate = new DateOnly(2026, 11, 24);
        var facts = new TransactionFacts("fnr016-transaction", "fnr016-card", "item sintético", 10m,
            "BRL", "2026-11-24", "DEBIT", "POSTED", "PURCHASE");
        context.Set<FinancialTransaction>().Add(FinancialTransaction.Create(transactionId, UserId, cardId,
            TransactionNormalizer.Normalize(facts, referenceDate), "fnr016-test-v1", referenceDate));
        await context.SaveChangesAsync();
        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(transactionId, UserId, personalId,
            UserId, "anotação sintética", new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();
        return (personalId, transactionId);
    }

    // A mesma referência e texto permanecem após migração ou erro transacional tardio.
    private static async Task AssertAnnotationAsync(Finora.Infrastructure.Persistence.FinoraDbContext context,
        Guid personalId, Guid transactionId)
    {
        var annotation = await context.Set<TransactionAnnotation>().AsNoTracking()
            .SingleAsync(item => item.TransactionId == transactionId);
        Assert.AreEqual(personalId, annotation.CategoryId);
        Assert.AreEqual(UserId, annotation.CategoryScopeId);
        Assert.AreEqual("anotação sintética", annotation.Note);
    }

    // Verifica IDs, ordem lógica e ausência de oitava categoria, não apenas a contagem de linhas.
    private static async Task AssertCatalogAsync(Finora.Infrastructure.Persistence.FinoraDbContext context)
    {
        var entries = await context.Set<Category>().AsNoTracking().Where(category => category.IsSystem).ToListAsync();
        Assert.HasCount(7, entries);
        foreach (var (id, name, key) in SystemCategoryCatalog.Categories)
        {
            var entry = entries.Single(category => category.Id == id);
            Assert.AreEqual(name, entry.Name);
            Assert.AreEqual(key, entry.NormalizedName);
            Assert.AreEqual(Guid.Empty, entry.ScopeId);
            Assert.IsNull(entry.UserId);
        }
    }
}
