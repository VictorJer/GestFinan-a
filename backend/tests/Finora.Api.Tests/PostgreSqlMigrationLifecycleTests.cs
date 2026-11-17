using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// A fixture opera no schema public compartilhado e precisa ser serializada com os demais testes PostgreSQL.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlMigrationLifecycleTests
{
    // Exercita base vazia, upgrade incremental, reaplicação idempotente e downgrade/reaplicação no destino allowlisted.
    [TestMethod]
    public async Task DisposableDatabaseSupportsTheApprovedMigrationLifecycle()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var context = fixture.Context;

        await PostgreSqlPersistenceTests.AssertDatabaseIsEmptyAsync(context);

        await context.Database.MigrateAsync(PostgreSqlPersistenceTests.SmokeMigrationId);
        await AssertAppliedMigrationsAsync(context, PostgreSqlPersistenceTests.SmokeMigrationId);
        var smokeTables = await PostgreSqlPersistenceTests.ReadPublicTablesAsync(context);
        CollectionAssert.AreEquivalent(new[] { "__EFMigrationsHistory" }, smokeTables);

        // Atualiza a base que contém somente a migration smoke para o schema financeiro atual.
        await context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await AssertAppliedMigrationsAsync(
            context,
            PostgreSqlPersistenceTests.SmokeMigrationId,
            PostgreSqlPersistenceTests.CoreMigrationId);
        var migratedTables = await PostgreSqlPersistenceTests.ReadPublicTablesAsync(context);
        var expectedCoreTables = new[]
        {
            "AspNetUsers",
            "AspNetUserClaims",
            "AspNetUserLogins",
            "AspNetUserTokens",
            "CardAccount",
            "Category",
            "FinancialConnection",
            "FinancialTransaction",
            "SyncRun",
            "TransactionAnnotation",
            "__EFMigrationsHistory"
        };
        CollectionAssert.AreEquivalent(expectedCoreTables, migratedTables);

        // Reaplicar o mesmo alvo não duplica registro de migration nem estruturas públicas.
        await context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await AssertAppliedMigrationsAsync(
            context,
            PostgreSqlPersistenceTests.SmokeMigrationId,
            PostgreSqlPersistenceTests.CoreMigrationId);
        CollectionAssert.AreEquivalent(
            expectedCoreTables,
            await PostgreSqlPersistenceTests.ReadPublicTablesAsync(context));

        // O downgrade remove somente as estruturas FNR-007 neste banco descartável e a reaplicação as restaura.
        await context.Database.MigrateAsync(PostgreSqlPersistenceTests.SmokeMigrationId);
        await AssertAppliedMigrationsAsync(context, PostgreSqlPersistenceTests.SmokeMigrationId);
        CollectionAssert.AreEquivalent(
            new[] { "__EFMigrationsHistory" },
            await PostgreSqlPersistenceTests.ReadPublicTablesAsync(context));

        await context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await AssertAppliedMigrationsAsync(
            context,
            PostgreSqlPersistenceTests.SmokeMigrationId,
            PostgreSqlPersistenceTests.CoreMigrationId);
        CollectionAssert.AreEquivalent(
            expectedCoreTables,
            await PostgreSqlPersistenceTests.ReadPublicTablesAsync(context));
    }

    // Compara o histórico PostgreSQL com a sequência exata esperada em cada fronteira de migration.
    private static async Task AssertAppliedMigrationsAsync(
        Microsoft.EntityFrameworkCore.DbContext context,
        params string[] expectedMigrations)
    {
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        CollectionAssert.AreEqual(expectedMigrations, appliedMigrations);
    }
}
