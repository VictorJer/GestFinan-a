using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Round-trips use only the approved disposable PostgreSQL target and compare each stored monetary value individually.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialValueRoundTripTests
{
    private static readonly Guid UserId = Guid.Parse("8cc286e2-4e68-43f0-bec0-1d23b072bf7b");
    private static readonly Guid ConnectionId = Guid.Parse("e6a7f220-2904-45dd-912b-85475f31a932");
    private static readonly Guid CardAccountId = Guid.Parse("6165edf1-b4eb-4ddd-b5de-8e7493dcbf3a");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);

    // Valores positivos, negativos, zero e limites de numeric(19,4) devem voltar sem soma ou conversão cambial.
    [TestMethod]
    public async Task PostgreSqlRoundTripsEachDecimalAndCurrencyExactly()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedRequiredParentsAsync(fixture.Context);

        var expected = new[]
        {
            (Id: Guid.NewGuid(), ExternalId: "positive-small", Amount: 0.0001m, Currency: "BRL"),
            (Id: Guid.NewGuid(), ExternalId: "negative-fraction", Amount: -42.1234m, Currency: "USD"),
            (Id: Guid.NewGuid(), ExternalId: "zero-value", Amount: 0m, Currency: "EUR"),
            (Id: Guid.NewGuid(), ExternalId: "positive-limit", Amount: 999_999_999_999_999.9999m, Currency: "JPY"),
            (Id: Guid.NewGuid(), ExternalId: "negative-limit", Amount: -999_999_999_999_999.9999m, Currency: "KWD")
        };

        // Uma gravação persiste valores independentes; cada lançamento conserva sua própria moeda e escala.
        await using (var writeContext = CreateContext())
        {
            writeContext.Set<FinancialTransaction>().AddRange(expected.Select(value =>
                CreateTransaction(value.Id, value.ExternalId, value.Amount, value.Currency)));
            await writeContext.SaveChangesAsync();
        }

        // Um contexto novo compara cada linha com seu valor original, sem totalização nem normalização monetária.
        await using var readContext = CreateContext();
        foreach (var value in expected)
        {
            var stored = await readContext.Set<FinancialTransaction>()
                .SingleAsync(transaction => transaction.Id == value.Id);

            Assert.AreEqual(value.Amount, stored.Amount, $"Valor alterado para {value.ExternalId}.");
            Assert.AreEqual(value.Currency, stored.CurrencyCode, $"Moeda alterada para {value.ExternalId}.");
            Assert.AreEqual(value.ExternalId, stored.ExternalId);
        }
    }

    // Os dois SaveChanges rejeitam overflow e quinta casa decimal antes do provider e deixam o banco sem a linha tentada.
    [TestMethod]
    public async Task SyncAndAsyncSaveRejectInvalidAmountsWithoutPersistingRows()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedRequiredParentsAsync(fixture.Context);

        await AssertInvalidAmountLeavesNoRowAsync(
            Guid.NewGuid(),
            1_000_000_000_000_000m,
            useAsyncSave: false);
        await AssertInvalidAmountLeavesNoRowAsync(Guid.NewGuid(), 1.00001m, useAsyncSave: false);
        await AssertInvalidAmountLeavesNoRowAsync(
            Guid.NewGuid(),
            1_000_000_000_000_000m,
            useAsyncSave: true);
        await AssertInvalidAmountLeavesNoRowAsync(Guid.NewGuid(), 1.00001m, useAsyncSave: true);
    }

    // Titular, conexão e cartão reais no banco permitem provar round-trip e ausência de linhas após falha local.
    private static async Task SeedRequiredParentsAsync(FinoraDbContext context)
    {
        context.Users.Add(new ApplicationUser(UserId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            "decimal-test-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().Add(CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            "decimal-test-card",
            "BRL"));
        await context.SaveChangesAsync();
    }

    // A alteração acontece depois do tracking; o caminho síncrono/assíncrono deve rejeitar e outro contexto prova ausência.
    private static async Task AssertInvalidAmountLeavesNoRowAsync(Guid id, decimal invalidAmount, bool useAsyncSave)
    {
        await using (var writeContext = CreateContext())
        {
            var transaction = CreateTransaction(id, $"invalid-{id:N}", 1.2345m, "BRL");
            writeContext.Set<FinancialTransaction>().Add(transaction);
            writeContext.Entry(transaction).Property(nameof(FinancialTransaction.Amount)).CurrentValue = invalidAmount;

            if (useAsyncSave)
            {
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => writeContext.SaveChangesAsync());
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => writeContext.SaveChanges());
            }
        }

        await using var verificationContext = CreateContext();
        Assert.AreEqual(0, await verificationContext.Set<FinancialTransaction>()
            .CountAsync(transaction => transaction.Id == id));
    }

    // Os fatos de origem são sintéticos e conservadores; a persistência não agrega nem converte os valores.
    private static FinancialTransaction CreateTransaction(Guid id, string externalId, decimal amount, string currency)
    {
        var facts = new TransactionFacts(
            externalId,
            "decimal-test-card",
            "round-trip individual de valor monetário",
            amount,
            currency,
            "2026-11-17",
            "DEBIT",
            "POSTED",
            "TARIFA");
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);

        return FinancialTransaction.Create(
            id,
            UserId,
            CardAccountId,
            normalization,
            "decimal-round-trip-v1",
            ReferenceDate);
    }

    // A configuração é lida pela variável de testes dedicada, já validada para loopback e database allowlisted.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, settings.ConnectionString);
        return new FinoraDbContext(optionsBuilder.Options);
    }
}
