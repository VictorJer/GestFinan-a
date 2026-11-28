using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida a matemática pura com vetores sintéticos, sem API, carteira persistida ou dados externos.
[TestClass]
public sealed class InvestmentSimulationCalculatorTests
{
    // Um vetor fechado independente confere taxa anual efetiva, aporte no fim do mês e totais publicados.
    [TestMethod]
    public void CalculatesEffectiveAnnualRateAndEndOfMonthContributionsAgainstIndependentVector()
    {
        var result = InvestmentSimulationCalculator.Calculate(
            initialAmount: 1_000.00m,
            monthlyContribution: 100.00m,
            annualRatePercent: 12.00m,
            years: 1);

        Assert.AreEqual(13, result.Points.Count);
        Assert.AreEqual(new InvestmentSimulationPoint(0, 1_000.00m, 1_000.00m), result.Points[0]);
        Assert.AreEqual(1_100.00m, result.Points[1].TotalContributed);
        Assert.IsTrue(Math.Abs(result.Points[1].ProjectedValue - 1_109.48879293458305m) <= 0.0000001m);
        Assert.IsTrue(Math.Abs(result.ProjectedValue - 2_384.64979083532m) <= 0.00001m);
        Assert.AreEqual(2_200.00m, result.TotalContributed);
        Assert.IsTrue(Math.Abs(result.ProjectedReturn - 184.64979083532m) <= 0.00001m);
    }

    // Taxa zero mantém igualdade exata: saldo final é inicial mais doze aportes, sem aproximação da raiz mensal.
    [TestMethod]
    public void ZeroRateProducesExactLinearSeries()
    {
        var result = InvestmentSimulationCalculator.Calculate(
            initialAmount: 123.45m,
            monthlyContribution: 67.89m,
            annualRatePercent: 0m,
            years: 1);

        Assert.AreEqual(13, result.Points.Count);
        Assert.AreEqual(123.45m, result.Points[0].ProjectedValue);
        Assert.AreEqual(191.34m, result.Points[1].ProjectedValue);
        Assert.AreEqual(938.13m, result.ProjectedValue);
        Assert.AreEqual(938.13m, result.TotalContributed);
        Assert.AreEqual(0m, result.ProjectedReturn);
    }

    // Os limites inclusivos mínimos e máximos permanecem calculáveis até 360 meses sem overflow ou truncamento.
    [TestMethod]
    public void AcceptsInclusiveInputBoundsAndMaximumTerm()
    {
        var minimum = InvestmentSimulationCalculator.Calculate(0m, 0m, 0m, 1);
        var maximum = InvestmentSimulationCalculator.Calculate(
            InvestmentSimulationCalculator.MaximumInitialAmount,
            InvestmentSimulationCalculator.MaximumMonthlyContribution,
            InvestmentSimulationCalculator.MaximumAnnualRatePercent,
            InvestmentSimulationCalculator.MaximumYears);

        Assert.AreEqual(0m, minimum.ProjectedValue);
        Assert.AreEqual(13, minimum.Points.Count);
        Assert.AreEqual(361, maximum.Points.Count);
        Assert.AreEqual(10_000_000.00m, maximum.Points[0].ProjectedValue);
        Assert.AreEqual(370_000_000.00m, maximum.TotalContributed);
        Assert.IsTrue(maximum.ProjectedValue > maximum.TotalContributed);
        Assert.IsTrue(maximum.Points.All(point => point.ProjectedValue >= 0m));
    }

    // As entradas inválidas são rejeitadas antes do cálculo, incluindo excesso de escala e prazo fora da faixa.
    [TestMethod]
    public void RejectsAmountsRatesAndTermsOutsideRepresentableContract()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(-0.01m, 0m, 0m, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(10_000_000.01m, 0m, 0m, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(0m, -0.01m, 0m, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(0m, 1_000_000.01m, 0m, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(0m, 0m, -0.01m, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(0m, 0m, 50.01m, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(1.001m, 0m, 0m, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(0m, 0m, 0.001m, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(0m, 0m, 0m, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            InvestmentSimulationCalculator.Calculate(0m, 0m, 0m, 31));
    }

    // O arredondamento de saída não retroage na série e o retorno em centavos reconcilia com os totais exibidos.
    [TestMethod]
    public void PublishedCentsReconcileWithoutIntermediateRounding()
    {
        var result = InvestmentSimulationCalculator.Calculate(
            initialAmount: 1_000.00m,
            monthlyContribution: 100.00m,
            annualRatePercent: 12.00m,
            years: 1);

        var publishedValue = decimal.Round(result.ProjectedValue, 2, MidpointRounding.AwayFromZero);
        var publishedContributions = decimal.Round(result.TotalContributed, 2, MidpointRounding.AwayFromZero);
        var publishedReturn = decimal.Round(result.ProjectedReturn, 2, MidpointRounding.AwayFromZero);

        Assert.AreEqual(2_384.65m, publishedValue);
        Assert.AreEqual(2_200.00m, publishedContributions);
        Assert.AreEqual(publishedValue - publishedContributions, publishedReturn);
        Assert.IsTrue(result.Points[12].ProjectedValue > result.Points[11].ProjectedValue);
    }
}
