using System.Collections.ObjectModel;
using System.Globalization;

namespace Finora.Domain.FinancialData;

/// <summary>Ponto mensal da projeção, ainda sem arredondamento para apresentação.</summary>
public sealed record InvestmentSimulationPoint(
    int Month,
    decimal TotalContributed,
    decimal ProjectedValue);

/// <summary>Resultado puro do simulador, com a série completa do mês zero ao prazo informado.</summary>
public sealed record InvestmentSimulationResult(
    IReadOnlyList<InvestmentSimulationPoint> Points,
    decimal TotalContributed,
    decimal ProjectedValue,
    decimal ProjectedReturn);

/// <summary>Calcula projeções nominais sem persistência, I/O ou arredondamento entre períodos.</summary>
public static class InvestmentSimulationCalculator
{
    public const decimal MaximumInitialAmount = 10_000_000.00m;
    public const decimal MaximumMonthlyContribution = 1_000_000.00m;
    public const decimal MaximumAnnualRatePercent = 50.00m;
    public const int MaximumYears = 30;

    /// <summary>Calcula a série com taxa anual efetiva e aporte aplicado ao final de cada mês.</summary>
    public static InvestmentSimulationResult Calculate(
        decimal initialAmount,
        decimal monthlyContribution,
        decimal annualRatePercent,
        int years)
    {
        // A validação antecede a conversão da taxa e qualquer operação financeira.
        EnsureInputs(initialAmount, monthlyContribution, annualRatePercent, years);

        var monthlyRate = ConvertAnnualRateToMonthly(annualRatePercent);
        var monthCount = checked(years * 12);
        var points = new List<InvestmentSimulationPoint>(monthCount + 1)
        {
            new(0, initialAmount, initialAmount)
        };
        var projectedValue = initialAmount;
        var totalContributed = initialAmount;

        try
        {
            // O saldo interno mantém precisão decimal entre meses; primeiro incidem juros e depois entra o aporte.
            for (var month = 1; month <= monthCount; month++)
            {
                projectedValue = checked(projectedValue * (1m + monthlyRate));
                projectedValue = checked(projectedValue + monthlyContribution);
                totalContributed = checked(initialAmount + checked(monthlyContribution * month));
                points.Add(new InvestmentSimulationPoint(month, totalContributed, projectedValue));
            }

            // Falhas de representação são rejeitadas, sem truncar a série ou publicar valores aparentemente válidos.
            var projectedReturn = checked(projectedValue - totalContributed);
            return new InvestmentSimulationResult(
                new ReadOnlyCollection<InvestmentSimulationPoint>(points),
                totalContributed,
                projectedValue,
                projectedReturn);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "A projeção excedeu os limites numéricos representáveis.",
                exception);
        }
    }

    // Limites e precisão de centavos espelham o contrato do simulador sem depender da borda HTTP.
    private static void EnsureInputs(
        decimal initialAmount,
        decimal monthlyContribution,
        decimal annualRatePercent,
        int years)
    {
        if (initialAmount < 0m
            || initialAmount > MaximumInitialAmount
            || decimal.Round(initialAmount, 2, MidpointRounding.ToZero) != initialAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(initialAmount));
        }

        if (monthlyContribution < 0m
            || monthlyContribution > MaximumMonthlyContribution
            || decimal.Round(monthlyContribution, 2, MidpointRounding.ToZero) != monthlyContribution)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyContribution));
        }

        if (annualRatePercent < 0m
            || annualRatePercent > MaximumAnnualRatePercent
            || decimal.Round(annualRatePercent, 2, MidpointRounding.ToZero) != annualRatePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(annualRatePercent));
        }

        if (years is < 1 or > MaximumYears)
        {
            throw new ArgumentOutOfRangeException(nameof(years));
        }
    }

    // A raiz anual é aproximada uma vez em double e convertida por texto invariável para evitar coerção implícita.
    private static decimal ConvertAnnualRateToMonthly(decimal annualRatePercent)
    {
        if (annualRatePercent == 0m)
        {
            return 0m;
        }

        var annualFactor = 1d + (double)(annualRatePercent / 100m);
        var monthlyRateValue = Math.Pow(annualFactor, 1d / 12d) - 1d;
        if (!double.IsFinite(monthlyRateValue)
            || monthlyRateValue < 0d
            || !decimal.TryParse(
                monthlyRateValue.ToString("R", CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var monthlyRate))
        {
            throw new InvalidOperationException("A taxa mensal não pode ser representada com segurança.");
        }

        return monthlyRate;
    }
}
