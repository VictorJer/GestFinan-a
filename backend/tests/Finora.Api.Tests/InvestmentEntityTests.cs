using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica as invariantes puras da carteira sem EF, HTTP, banco ou dados reais.
[TestClass]
public sealed class InvestmentEntityTests
{
    private static readonly Guid InvestmentId = Guid.Parse("9265f006-82f8-4ae8-9cf7-8c054a647911");
    private static readonly Guid UserId = Guid.Parse("8da377a1-98ed-4c57-825f-20a81f9233a0");

    // Criação preserva os campos, remove somente espaços externos do nome e oferece três códigos estáveis.
    [TestMethod]
    public void CreatesInvestmentWithSupportedClassAndNormalizedName()
    {
        var investment = Investment.Create(
            InvestmentId,
            UserId,
            "  Reserva de emergência  ",
            InvestmentClassCatalog.FixedIncome,
            1250.50m,
            1300.75m);

        Assert.AreEqual(InvestmentId, investment.Id);
        Assert.AreEqual(UserId, investment.UserId);
        Assert.AreEqual("Reserva de emergência", investment.Name);
        Assert.AreEqual(InvestmentClassCatalog.FixedIncome, investment.Class);
        Assert.AreEqual(1250.50m, investment.AppliedAmount);
        Assert.AreEqual(1300.75m, investment.CurrentAmount);
        CollectionAssert.AreEqual(
            new[]
            {
                InvestmentClassCatalog.FixedIncome,
                InvestmentClassCatalog.VariableIncome,
                InvestmentClassCatalog.RealEstateFunds
            },
            InvestmentClassCatalog.Options.Select(option => option.Code).ToArray());
    }

    // Zero e o limite numeric(19,2) são válidos; zeros excedentes não provocam arredondamento de valor.
    [TestMethod]
    public void AcceptsZeroMaximumAndTrailingZeroPrecision()
    {
        const decimal maximum = 99_999_999_999_999_999.99m;
        var zero = CreateInvestment(appliedAmount: 0m, currentAmount: 0.00m);
        var boundary = CreateInvestment(appliedAmount: maximum, currentAmount: 1.2300m);

        Assert.AreEqual(0m, zero.AppliedAmount);
        Assert.AreEqual(0.00m, zero.CurrentAmount);
        Assert.AreEqual(maximum, boundary.AppliedAmount);
        Assert.AreEqual(1.2300m, boundary.CurrentAmount);
        boundary.ValidateForPersistence();
    }

    // IDs, nomes, classes e valores inválidos são recusados, incluindo fração que seria arredondada e overflow.
    [TestMethod]
    public void RejectsInvalidIdentityNameClassAndMoney()
    {
        const decimal maximum = 99_999_999_999_999_999.99m;

        Assert.Throws<ArgumentException>(() => Investment.Create(
            Guid.Empty, UserId, "Investimento", InvestmentClassCatalog.FixedIncome, 1m, 1m));
        Assert.Throws<ArgumentException>(() => Investment.Create(
            InvestmentId, Guid.Empty, "Investimento", InvestmentClassCatalog.FixedIncome, 1m, 1m));
        Assert.Throws<ArgumentException>(() => Investment.Create(
            InvestmentId, UserId, " \u00a0 ", InvestmentClassCatalog.FixedIncome, 1m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Investment.Create(
            InvestmentId, UserId, new string('n', Investment.MaximumNameLength + 1), InvestmentClassCatalog.FixedIncome, 1m, 1m));
        Assert.Throws<ArgumentException>(() => Investment.Create(
            InvestmentId, UserId, "Investimento", "unknown_class", 1m, 1m));
        Assert.Throws<ArgumentException>(() => Investment.Create(
            InvestmentId, UserId, "Investimento", "Fixed_Income", 1m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Investment.Create(
            InvestmentId, UserId, "Investimento", InvestmentClassCatalog.FixedIncome, -0.01m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Investment.Create(
            InvestmentId, UserId, "Investimento", InvestmentClassCatalog.FixedIncome, 1m, 1.234m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Investment.Create(
            InvestmentId, UserId, "Investimento", InvestmentClassCatalog.FixedIncome, maximum + 0.01m, 1m));
    }

    // Edição valida o conjunto todo antes de modificar campos, mantendo o estado antigo quando uma entrada falha.
    [TestMethod]
    public void UpdateIsAtomicWhenNewFieldsAreInvalid()
    {
        var investment = CreateInvestment();

        Assert.Throws<ArgumentOutOfRangeException>(() => investment.Update(
            "Novo nome", InvestmentClassCatalog.VariableIncome, 5m, decimal.MaxValue));

        Assert.AreEqual("Investimento sintético", investment.Name);
        Assert.AreEqual(InvestmentClassCatalog.FixedIncome, investment.Class);
        Assert.AreEqual(10.00m, investment.AppliedAmount);
        Assert.AreEqual(12.00m, investment.CurrentAmount);
    }

    // Usa somente valores sintéticos para concentrar os casos de teste em nome, classe e precisão.
    private static Investment CreateInvestment(
        decimal appliedAmount = 10.00m,
        decimal currentAmount = 12.00m) =>
        Investment.Create(
            InvestmentId,
            UserId,
            "Investimento sintético",
            InvestmentClassCatalog.FixedIncome,
            appliedAmount,
            currentAmount);
}
