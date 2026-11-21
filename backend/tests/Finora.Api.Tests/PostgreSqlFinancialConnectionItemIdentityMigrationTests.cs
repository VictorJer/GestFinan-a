using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finora.Api.Tests;

// Valida backfill e recusa de colisões da migration apenas no PostgreSQL descartável autorizado.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialConnectionItemIdentityMigrationTests
{
    private static readonly Guid FirstUserId = Guid.Parse("4837c564-c6cb-4164-b705-dc45ecf9c9e4");
    private static readonly Guid SecondUserId = Guid.Parse("818a7dd0-bf95-41a0-a6a0-cd8c6d824979");
    private static readonly Guid FirstConnectionId = Guid.Parse("b8f41d8a-fd19-44cb-b854-e0dc5459be0d");
    private static readonly Guid SecondConnectionId = Guid.Parse("39df1669-1a34-46fc-babd-e647c12f5fe6");

    // Upgrade de schema existente preenche cada item ativo sem aposentá-lo.
    [TestMethod]
    public async Task UpgradeBackfillsExistingActiveItems()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionsAsync(fixture.Context, sameExternalItemId: false);

        await fixture.Context.Database.MigrateAsync();

        var identities = await fixture.Context.Set<FinancialConnectionItemIdentity>()
            .OrderBy(value => value.ExternalItemId).ToArrayAsync();
        Assert.AreEqual(2, identities.Length);
        Assert.IsTrue(identities.All(value => value.RetiredAtUtc is null));
        Assert.IsTrue((await fixture.Context.Database.GetPendingMigrationsAsync()).Count() == 0);
    }

    // Banco vazio recebe a migration e cria registro de identidade vazio, sem seed fictício.
    [TestMethod]
    public async Task FreshSchemaCreatesEmptyIdentityRegistry()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();

        await fixture.Context.Database.MigrateAsync();

        Assert.AreEqual(0, await fixture.Context.Set<FinancialConnectionItemIdentity>().CountAsync());
        Assert.IsTrue((await fixture.Context.Database.GetPendingMigrationsAsync()).Count() == 0);
    }

    // IDs atuais duplicados entre titulares fazem o backfill falhar e não deixam migration parcial aplicada.
    [TestMethod]
    public async Task UpgradeRejectsPreexistingCrossOwnerItemCollision()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionsAsync(fixture.Context, sameExternalItemId: true);

        var rejected = false;
        try
        {
            await fixture.Context.Database.MigrateAsync();
        }
        catch (PostgresException)
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "A chave global deve impedir seleção silenciosa de titular para item repetido.");
        var applied = await fixture.Context.Database.GetAppliedMigrationsAsync();
        Assert.IsFalse(applied.Any(value => value.EndsWith(
            "AddFinancialConnectionItemIdentityHistory", StringComparison.Ordinal)));
    }

    // Cria vínculos legados válidos; a unicidade histórica anterior era escopada por titular.
    private static async Task SeedConnectionsAsync(FinoraDbContext context, bool sameExternalItemId)
    {
        context.Users.AddRange(new ApplicationUser(FirstUserId), new ApplicationUser(SecondUserId));
        await context.SaveChangesAsync();
        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(FirstConnectionId, FirstUserId, "pluggy", "existing-item"),
            FinancialConnection.Create(
                SecondConnectionId, SecondUserId, "pluggy", sameExternalItemId ? "existing-item" : "other-item"));
        await context.SaveChangesAsync();
    }
}
