using System.Text;

namespace Finora.Domain.FinancialData;

/// <summary>Centraliza a precisão individual aceita pelo contrato relacional do núcleo.</summary>
internal static class FinancialValueRules
{
    private const decimal MaximumNumeric19Scale4 = 999_999_999_999_999.9999m;
    private const int MaximumExternalIdUtf8Bytes = 1_024;
    private const int MaximumProviderCodeLength = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Valida antes que o provider PostgreSQL possa arredondar escala ou exceder numeric(19,4).
    internal static decimal EnsureNumeric19Scale4(decimal value, string parameterName)
    {
        var rounded = decimal.Round(value, 4, MidpointRounding.ToZero);
        if (value < -MaximumNumeric19Scale4
            || value > MaximumNumeric19Scale4
            || rounded != value)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "O valor precisa caber em numeric(19,4) sem arredondamento.");
        }

        return value;
    }

    // IDs externos permanecem opacos e são limitados por bytes UTF-8, sem trim ou truncamento.
    internal static void EnsureOpaqueExternalId(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("O identificador externo é obrigatório.", parameterName);
        }

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("O identificador externo precisa ser texto Unicode válido.", parameterName, exception);
        }

        if (byteCount > MaximumExternalIdUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"O identificador externo excede {MaximumExternalIdUtf8Bytes} bytes UTF-8.");
        }
    }

    // O código interno do provedor tem limite de schema, sem normalização silenciosa.
    internal static void EnsureProviderCode(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O código do provedor é obrigatório.", parameterName);
        }

        if (value.Length > MaximumProviderCodeLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"O código do provedor excede {MaximumProviderCodeLength} caracteres.");
        }
    }
}
