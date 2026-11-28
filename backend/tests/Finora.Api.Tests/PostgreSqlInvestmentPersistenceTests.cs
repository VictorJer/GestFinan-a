using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Exercita a tabela da carteira somente no banco descartável validado pela fixture do projeto.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlInvestmentPersistenceTests
{
    private static readonly Guid UserId = Guid.Parse("6da1efc3-7cf6-4620-a3c1-5cdff8c341ce");
    private static readonly Guid MissingUserId = Guid.Parse("d8352605-6478-467a-a557-c9352f46da19");

    // PostgreSQL confirma a FK do titular, as constraints de carteira, o round-trip e rollback transacional.
    [TestMethod]
    public async Task PostgreSqlEnforcesInvestmentOwnershipValuesAndRollback()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        Assert.IsTrue(await fixture.Context.Database.EnsureCreatedAsync());

        fixture.Context.Users.Add(new ApplicationUser(UserId));
        await fixture.Context.SaveChangesAsync();

        var persisted = Investment.Create(
            Guid.Parse("e5eb41d7-91c5-4123-9578-f453340c9f60"),
            UserId,
            "Carteira sintética",
            InvestmentClassCatalog.RealEstateFunds,
            1234.50m,
            1300.75m);
        fixture.Context.Add(persisted);
        await fixture.Context.SaveChangesAsync();

        // Contexto independente comprova precisão e titular persistidos, sem depender do tracking da escrita.
        await using (var readContext = CreateContext())
        {
            var roundTrip = await readContext.Set<Investment>().AsNoTracking().SingleAsync();
            Assert.AreEqual(UserId, roundTrip.UserId);
            Assert.AreEqual("Carteira sintética", roundTrip.Name);
            Assert.AreEqual(InvestmentClassCatalog.RealEstateFunds, roundTrip.Class);
            Assert.AreEqual(1234.50m, roundTrip.AppliedAmount);
            Assert.AreEqual(1300.75m, roundTrip.CurrentAmount);
        }

        // FK impede que SQL/EF grave uma carteira para um titular que não existe.
        await using (var invalidOwnerContext = CreateContext())
        {
            invalidOwnerContext.Add(Investment.Create(
                Guid.NewGuid(),
                MissingUserId,
                "Titular ausente",
                InvestmentClassCatalog.FixedIncome,
                1m,
                1m));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => invalidOwnerContext.SaveChangesAsync());
            Assert.IsInstanceOfType<PostgresException>(exception.InnerException);
            Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
        }

        // CHECKs de classe e valores continuam ativos mesmo quando a escrita contorna o Domain.
        await AssertDirectCheckViolationAsync($"""
            INSERT INTO "Investment" ("Id", "UserId", "Name", "Class", "AppliedAmount", "CurrentAmount")
            VALUES ({Guid.NewGuid()}, {UserId}, 'Classe inválida', 'unknown_class', 1, 1);
            """);
        await AssertDirectCheckViolationAsync($"""
            INSERT INTO "Investment" ("Id", "UserId", "Name", "Class", "AppliedAmount", "CurrentAmount")
            VALUES ({Guid.NewGuid()}, {UserId}, 'Valor negativo', 'fixed_income', -1, 1);
            """);

        // O rollback deixa a carteira anterior inalterada, conforme será exigido dos casos de uso transacionais.
        await using (var transactionContext = CreateContext())
        await using (var transaction = await transactionContext.Database.BeginTransactionAsync())
        {
            transactionContext.Add(Investment.Create(
                Guid.NewGuid(),
                UserId,
                "Registro revertido",
                InvestmentClassCatalog.VariableIncome,
                2m,
                3m));
            await transactionContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verificationContext = CreateContext();
        Assert.AreEqual(1, await verificationContext.Set<Investment>().CountAsync());
    }

    // Cada verificação reabre o contexto e usa somente o destino validado pela configuração dedicada de testes.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, settings.ConnectionString);
        return new FinoraDbContext(options.Options);
    }

    // SQL interpolado é parametrizado; o SQLSTATE confirma que a restrição foi aplicada pelo PostgreSQL.
    private static async Task AssertDirectCheckViolationAsync(FormattableString command)
    {
        await using var context = CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => context.Database.ExecuteSqlInterpolatedAsync(command));
        Assert.AreEqual(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }
}
