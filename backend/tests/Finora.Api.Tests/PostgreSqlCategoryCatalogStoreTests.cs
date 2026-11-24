using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Provas de unicidade no PostgreSQL real mantêm IDs de titulares sintéticos e nunca dependem do login administrativo.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlCategoryCatalogStoreTests
{
    private static readonly Guid OwnerId = Guid.Parse("b9646ecb-d327-42cd-b98c-3dc2980426c3");

    // Um lock compartilhado bloqueia os dois INSERTs depois dos prechecks sem bloquear suas leituras.
    [TestMethod]
    public async Task ConcurrentInsertionsInSameScopeCommitExactlyOneCategory()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync();
        fixture.Context.Users.Add(new ApplicationUser(OwnerId));
        await fixture.Context.SaveChangesAsync();
        Assert.IsTrue(CategoryNamePolicy.TryNormalize("Projetos", out var name));

        await using var blocker = new NpgsqlConnection(DisposablePostgresTestConfiguration.ReadRequired().ConnectionString);
        await blocker.OpenAsync();
        await using var held = await blocker.BeginTransactionAsync();
        await using (var lockCommand = blocker.CreateCommand())
        {
            lockCommand.Transaction = held;
            lockCommand.CommandText = "LOCK TABLE \"Category\" IN SHARE MODE";
            await lockCommand.ExecuteNonQueryAsync();
        }

        // Os contextos independentes chegam à mesma constraint enquanto a tabela bloqueia somente gravações.
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var first = new CategoryCatalogStore(firstContext).CreatePersonalAsync(OwnerId, name!, default);
        var second = new CategoryCatalogStore(secondContext).CreatePersonalAsync(OwnerId, name!, default);
        var waitingInserts = 0;
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                waitingInserts = await fixture.Context.Database.SqlQueryRaw<int>("""
                    SELECT count(*)::int AS "Value" FROM pg_stat_activity
                    WHERE datname = current_database() AND wait_event_type = 'Lock'
                      AND query LIKE '%INSERT INTO "Category"%'
                    """).SingleAsync();
                if (waitingInserts >= 2) { break; }
                await Task.Delay(50);
            }

            Assert.IsTrue(waitingInserts >= 2, "Os dois INSERTs devem atingir a barreira de escrita no PostgreSQL.");
            Assert.IsFalse(first.IsCompleted || second.IsCompleted, "As duas gravações devem esperar a barreira SQL.");
        }
        finally
        {
            await held.CommitAsync();
        }

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, results.Count(id => id.HasValue));
        Assert.AreEqual(1, await fixture.Context.Set<Category>().CountAsync(category => !category.IsSystem));
    }

    // A constraint de FK não pode se tornar falso conflito de nome; duplicidade anterior deve continuar 409.
    [TestMethod]
    public async Task PrecheckReturnsConflictButUnrelatedConstraintRemainsDatabaseFailure()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync();
        fixture.Context.Users.Add(new ApplicationUser(OwnerId));
        await fixture.Context.SaveChangesAsync();
        Assert.IsTrue(CategoryNamePolicy.TryNormalize("Projetos", out var name));
        var store = new CategoryCatalogStore(fixture.Context);
        Assert.IsNotNull(await store.CreatePersonalAsync(OwnerId, name!, default));
        Assert.IsNull(await store.CreatePersonalAsync(OwnerId, name!, default));

        await using var separate = CreateContext();
        var missingOwner = Guid.Parse("d33155bc-6157-47c9-83c5-fc52bc94f599");
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await new CategoryCatalogStore(separate).CreatePersonalAsync(missingOwner, name!, default));
    }

    // A fixture usa apenas a variável de teste allowlisted e nunca a connection string do aplicativo.
    private static FinoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, DisposablePostgresTestConfiguration.ReadRequired().ConnectionString);
        return new FinoraDbContext(options.Options);
    }
}
