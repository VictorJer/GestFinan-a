using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// A integração valida a persistência dos fatos temporais e interpretações atuais sem criar novas regras financeiras.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialDateMetadataRoundTripTests
{
    private static readonly Guid UserId = Guid.Parse("11541f07-c46c-42d1-b606-376dc575eb84");
    private static readonly Guid ConnectionId = Guid.Parse("ae39816a-88d4-437a-8d55-543464b2055e");
    private static readonly Guid CardAccountId = Guid.Parse("14b3fab3-1737-4a8e-b353-7927ad4d2b1c");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);
    private const string NormalizationVersion = "date-metadata-round-trip-v1";

    // Calendário, instante de fronteira, dados mensais e motivos independentes devem sobreviver a uma nova leitura.
    [TestMethod]
    public async Task PostgreSqlRoundTripsOriginalDatesMonthlyMetadataAndIndependentReasons()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedRequiredParentsAsync(fixture.Context);

        var scenarios = new[]
        {
            CreateScenario(
                Facts("calendar-date", "2026-11-30"),
                new DateOnly(2026, 11, 30),
                TransactionNature.Fee,
                null,
                TransactionSituation.Effective,
                null,
                null),
            CreateScenario(
                Facts("month-boundary-instant", "2026-11-01T02:30:00Z", purchaseDate: "2026-10-31T23:00:00-03:00"),
                new DateOnly(2026, 10, 31),
                TransactionNature.Fee,
                null,
                TransactionSituation.Effective,
                null,
                null),
            CreateScenario(
                Facts(
                    "monthly-installment",
                    "2026-11-30",
                    status: "PENDING",
                    installmentNumber: 2,
                    totalInstallments: 5,
                    purchaseDate: "2026-10",
                    billId: "bill-monthly-2026-12",
                    billPostDate: "2026-12-02",
                    billForecastDate: "2026-12",
                    paymentType: "INSTALLMENT"),
                new DateOnly(2026, 11, 30),
                TransactionNature.Fee,
                null,
                TransactionSituation.FutureInstallment,
                TransactionExclusionReason.FutureInstallment,
                null),
            CreateScenario(
                Facts(
                    "ambiguous-date-inconsistent-installment",
                    "2026-11-01T02:30:00",
                    operationType: "PAGAMENTO",
                    status: "PENDING",
                    installmentNumber: 4,
                    totalInstallments: 3,
                    purchaseDate: "purchase-date-original",
                    billId: "bill-inconsistent",
                    billPostDate: "2026-11-01",
                    billForecastDate: "2026-12",
                    paymentType: "INSTALLMENT"),
                null,
                TransactionNature.Unknown,
                TransactionExclusionReason.UnknownOperationType,
                TransactionSituation.Unknown,
                TransactionExclusionReason.InvalidInstallment,
                TransactionExclusionReason.MissingDateOffset),
            CreateScenario(
                Facts("pending-without-operation", "2026-11-17", operationType: null, status: "PENDING"),
                new DateOnly(2026, 11, 17),
                TransactionNature.Unknown,
                TransactionExclusionReason.MissingOperationType,
                TransactionSituation.Pending,
                TransactionExclusionReason.NotEffective,
                null)
        };

        // Normalização anterior à escrita permanece conservadora; nenhum cenário ativa elegibilidade positiva.
        var transactions = scenarios.Select(scenario =>
        {
            var normalization = TransactionNormalizer.Normalize(scenario.Facts, ReferenceDate);
            Assert.IsFalse(normalization.IsEligibleForRealExpenses, scenario.Facts.ExternalId);
            return FinancialTransaction.Create(
                scenario.Id,
                UserId,
                CardAccountId,
                normalization,
                NormalizationVersion,
                ReferenceDate);
        }).ToArray();

        await using (var writeContext = CreateContext())
        {
            writeContext.Set<FinancialTransaction>().AddRange(transactions);
            await writeContext.SaveChangesAsync();
        }

        // Contexto independente confere fatos originais, dia técnico, metadados mensais, referência e motivos armazenados.
        await using var readContext = CreateContext();
        foreach (var scenario in scenarios)
        {
            var stored = await readContext.Set<FinancialTransaction>()
                .SingleAsync(transaction => transaction.Id == scenario.Id);

            AssertSourceFactsRoundTrip(scenario.Facts, stored);
            Assert.AreEqual(scenario.ExpectedTransactionDate, stored.TransactionDate, scenario.Facts.ExternalId);
            Assert.AreEqual(scenario.ExpectedNature, stored.Nature, scenario.Facts.ExternalId);
            Assert.AreEqual(scenario.ExpectedNatureReason, stored.NatureReason, scenario.Facts.ExternalId);
            Assert.AreEqual(scenario.ExpectedSituation, stored.Situation, scenario.Facts.ExternalId);
            Assert.AreEqual(scenario.ExpectedSituationReason, stored.SituationReason, scenario.Facts.ExternalId);
            Assert.AreEqual(scenario.ExpectedDateReason, stored.DateReason, scenario.Facts.ExternalId);
            Assert.AreEqual(ReferenceDate, stored.NormalizationReferenceDate);
            Assert.AreEqual(NormalizationVersion, stored.NormalizationRuleVersion);
        }
    }

    // Titular, conexão e cartão existentes são os únicos pais necessários para persistir as fixtures temporais.
    private static async Task SeedRequiredParentsAsync(FinoraDbContext context)
    {
        context.Users.Add(new ApplicationUser(UserId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            "date-metadata-test-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().Add(CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            "date-metadata-test-card",
            "BRL"));
        await context.SaveChangesAsync();
    }

    // Campos opcionais explicitam ausência e incoerência sem fabricar metadados a partir de datas próximas.
    private static TransactionFacts Facts(
        string externalId,
        string originalDate,
        string? operationType = "TARIFA",
        string? status = "POSTED",
        int? installmentNumber = null,
        int? totalInstallments = null,
        string? purchaseDate = null,
        string? billId = null,
        string? billPostDate = null,
        string? billForecastDate = null,
        string? paymentType = null) => new(
            externalId,
            "date-metadata-test-card",
            $"fato temporal sintético {externalId}",
            10.2500m,
            "BRL",
            originalDate,
            "DEBIT",
            status,
            operationType,
            installmentNumber,
            totalInstallments,
            purchaseDate,
            billId,
            billPostDate,
            billForecastDate,
            paymentType);

    // Cada cenário declara a interpretação esperada sem alterar o resultado do normalizador existente.
    private static Scenario CreateScenario(
        TransactionFacts facts,
        DateOnly? expectedTransactionDate,
        TransactionNature expectedNature,
        TransactionExclusionReason? expectedNatureReason,
        TransactionSituation expectedSituation,
        TransactionExclusionReason? expectedSituationReason,
        TransactionExclusionReason? expectedDateReason) => new(
            Guid.NewGuid(),
            facts,
            expectedTransactionDate,
            expectedNature,
            expectedNatureReason,
            expectedSituation,
            expectedSituationReason,
            expectedDateReason);

    // Compara todos os campos recebidos da origem, inclusive nulos e valores mensais que não dirigem classificação.
    private static void AssertSourceFactsRoundTrip(TransactionFacts source, FinancialTransaction stored)
    {
        Assert.AreEqual(source.ExternalId, stored.ExternalId);
        Assert.AreEqual(source.AccountId, stored.ExternalAccountId);
        Assert.AreEqual(source.Description, stored.Description);
        Assert.AreEqual(source.Amount, stored.Amount);
        Assert.AreEqual(source.CurrencyCode, stored.CurrencyCode);
        Assert.AreEqual(source.OriginalDate, stored.OriginalDate);
        Assert.AreEqual(source.Type, stored.Type);
        Assert.AreEqual(source.Status, stored.Status);
        Assert.AreEqual(source.OperationType, stored.OperationType);
        Assert.AreEqual(source.InstallmentNumber, stored.InstallmentNumber);
        Assert.AreEqual(source.TotalInstallments, stored.TotalInstallments);
        Assert.AreEqual(source.PurchaseDate, stored.PurchaseDate);
        Assert.AreEqual(source.BillId, stored.BillId);
        Assert.AreEqual(source.BillPostDate, stored.BillPostDate);
        Assert.AreEqual(source.BillForecastDate, stored.BillForecastDate);
        Assert.AreEqual(source.PaymentType, stored.PaymentType);
    }

    // Reutiliza a configuração de teste dedicada que rejeita host remoto e database fora da allowlist.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, settings.ConnectionString);
        return new FinoraDbContext(optionsBuilder.Options);
    }

    // Agrupa a origem e as expectativas para comparar a leitura sem recalcular regras fora do domínio.
    private sealed record Scenario(
        Guid Id,
        TransactionFacts Facts,
        DateOnly? ExpectedTransactionDate,
        TransactionNature ExpectedNature,
        TransactionExclusionReason? ExpectedNatureReason,
        TransactionSituation ExpectedSituation,
        TransactionExclusionReason? ExpectedSituationReason,
        TransactionExclusionReason? ExpectedDateReason);
}
