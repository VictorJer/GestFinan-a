namespace Finora.Domain.FinancialData;

/// <summary>Define os códigos persistentes permitidos e os rótulos apresentados para classes da carteira.</summary>
public static class InvestmentClassCatalog
{
    public const string FixedIncome = "fixed_income";
    public const string VariableIncome = "variable_income";
    public const string RealEstateFunds = "real_estate_funds";

    // A lista imutável mantém a ordem e os rótulos da escolha confirmada para a carteira manual.
    public static IReadOnlyList<(string Code, string Label)> Options { get; } = Array.AsReadOnly(
    [
        (FixedIncome, "Renda fixa"),
        (VariableIncome, "Renda variável"),
        (RealEstateFunds, "Fundos imobiliários")
    ]);

    // Códigos são comparados ordinalmente para não aceitar variações ou normalização silenciosa de entrada.
    public static bool IsSupported(string? code) =>
        code is FixedIncome or VariableIncome or RealEstateFunds;
}
