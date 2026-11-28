using System.Data;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica a barreira comum de SaveChanges antes de abrir conexão com PostgreSQL.
[TestClass]
public sealed class FinoraDbContextSaveValidationTests
{
    private static readonly Guid UserId = Guid.Parse("3c472e18-2c28-4bf1-90dc-f60c0e97ac12");
    private static readonly Guid ConnectionId = Guid.Parse("a63e12ba-9921-436c-96f1-2967dc4ab374");
    private static readonly Guid CardAccountId = Guid.Parse("a42cf987-2b5c-4768-9b61-1fbb8d5b6096");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);

    // O caminho síncrono revalida o valor após tracking e rejeita escala que PostgreSQL poderia arredondar.
    [TestMethod]
    public void SaveChangesRejectsModifiedTransactionAmountBeforeOpeningConnection()
    {
        using var context = CreateContext();
        var transaction = CreateTransaction();
        context.Add(transaction);
        context.Entry(transaction).Property(nameof(FinancialTransaction.Amount)).CurrentValue = 1.00001m;

        Assert.Throws<ArgumentOutOfRangeException>(() => context.SaveChanges());
        Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    // O caminho assíncrono aplica a mesma regra para limites de crédito alterados depois da criação.
    [TestMethod]
    public async Task SaveChangesAsyncRejectsModifiedCreditLimitBeforeOpeningConnection()
    {
        await using var context = CreateContext();
        var account = CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            "external-card",
            "USD",
            totalCreditLimit: 10m);
        context.Add(account);
        context.Entry(account).Property(nameof(CardAccount.TotalCreditLimit)).CurrentValue =
            1_000_000_000_000_000m;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.SaveChangesAsync());
        Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    // O limite técnico dos IDs externos é contado em bytes UTF-8 também quando o valor muda no tracking.
    [TestMethod]
    public async Task SaveChangesAsyncRejectsOversizedMultibyteExternalIdBeforeOpeningConnection()
    {
        await using var context = CreateContext();
        var transaction = CreateTransaction();
        context.Add(transaction);
        context.Entry(transaction).Property(nameof(FinancialTransaction.ExternalId)).CurrentValue =
            new string('é', 513);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.SaveChangesAsync());
        Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    // Timestamps com sufixo UTC não podem ser convertidos silenciosamente pelo provider durante a gravação.
    [TestMethod]
    public async Task SaveChangesAsyncRejectsNonUtcSyncRunTimestampBeforeOpeningConnection()
    {
        await using var context = CreateContext();
        var run = SyncRun.Create(
            Guid.Parse("7df73e47-8f6e-441e-8900-1f2e71e0a4a6"),
            UserId,
            ConnectionId,
            new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 11, 1));
        context.Add(run);
        context.Entry(run).Property(nameof(SyncRun.CreatedAtUtc)).CurrentValue =
            new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.FromHours(-3));

        await Assert.ThrowsAsync<ArgumentException>(() => context.SaveChangesAsync());
        Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    // A carteira também revalida alterações após tracking antes de o PostgreSQL arredondar a escala.
    [TestMethod]
    public async Task SaveChangesAsyncRejectsModifiedInvestmentAmountBeforeOpeningConnection()
    {
        await using var context = CreateContext();
        var investment = Investment.Create(
            Guid.NewGuid(),
            UserId,
            "Investimento sintético",
            InvestmentClassCatalog.FixedIncome,
            1m,
            1m);
        context.Add(investment);
        context.Entry(investment).Property(nameof(Investment.CurrentAmount)).CurrentValue = 1.234m;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.SaveChangesAsync());
        Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    // Cria contextos com configuração real de Npgsql e identidade, mas a connection string nunca é usada.
    private static FinoraDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(
            optionsBuilder,
            "Host=127.0.0.1;Database=finora_integration_test;Username=finora_test");
        return new FinoraDbContext(optionsBuilder.Options);
    }

    // A fixture reproduz fatos sintéticos aceitos pelo normalizador conservador sem ativar compra elegível.
    private static FinancialTransaction CreateTransaction()
    {
        var facts = new TransactionFacts(
            "external-transaction",
            "external-account",
            "descrição fictícia",
            1m,
            "BRL",
            "2026-11-17",
            "DEBIT",
            "POSTED",
            "TARIFA");
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);

        return FinancialTransaction.Create(
            Guid.Parse("8de2784b-e0fb-436d-956b-14c748a057ed"),
            UserId,
            CardAccountId,
            normalization,
            "transaction-normalizer-v1",
            ReferenceDate);
    }
}
