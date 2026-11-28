using System.Globalization;

namespace Finora.Api;

// Centraliza a gramática decimal do contrato HTTP sem alterar decimal/numeric(19,2) na Application/Database.
internal static class InvestmentHttpMoney
{
    internal const string ItemAmountPattern = "^(?:0|[1-9][0-9]{0,16})\\.[0-9]{2}$";
    internal const string SummaryAmountPattern = "^(?:0|[1-9][0-9]*)\\.[0-9]{2}$";
    internal const string SignedSummaryAmountPattern = "^-?(?:0|[1-9][0-9]*)\\.[0-9]{2}$";

    private const decimal MaximumNumeric19Scale2 = 99_999_999_999_999_999.99m;

    // O round-trip pela formatação fixa rejeita agrupamento, expoente, escala variável e zeros à esquerda.
    internal static bool TryParseItemAmount(string? text, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrEmpty(text)
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0m
            || parsed > MaximumNumeric19Scale2
            || !string.Equals(text, Format(parsed), StringComparison.Ordinal))
        {
            return false;
        }

        amount = parsed;
        return true;
    }

    // A escala HTTP é sempre duas casas e a cultura é invariável, independentemente da localidade do servidor.
    internal static string Format(decimal amount) => amount == 0m
        ? "0.00"
        : amount.ToString("0.00", CultureInfo.InvariantCulture);
}
