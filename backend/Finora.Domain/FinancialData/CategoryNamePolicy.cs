using System.Globalization;
using System.Text;

namespace Finora.Domain.FinancialData;

/// <summary>Nome exibido e chave ordinal compartilhados pelo catálogo e pelas categorias pessoais.</summary>
public sealed record CategoryName(string DisplayName, string Key);

/// <summary>Política v1 de nomes: espaço Unicode único e chave sem marcas diacríticas, em maiúsculas invariantes.</summary>
public static class CategoryNamePolicy
{
    public const int MaximumLength = 60;

    // Verifica ambos os resultados separadamente: uma futura expansão da chave não pode contornar o limite do banco.
    public static bool FitsLimits(string displayName, string key) =>
        displayName.Length is > 0 and <= MaximumLength && key.Length is > 0 and <= MaximumLength;

    // Recusa controles antes da compactação para que quebras de linha não virem separadores válidos.
    public static bool TryNormalize(string? input, out CategoryName? name)
    {
        name = null;
        if (string.IsNullOrEmpty(input) || input.Any(char.IsControl))
        {
            return false;
        }

        var display = new StringBuilder();
        var pendingSpace = false;

        // Preserva letras do nome apresentado e colapsa toda sequência de espaços Unicode em um espaço simples.
        foreach (var rune in input.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = display.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                display.Append(' ');
                pendingSpace = false;
            }

            display.Append(rune.ToString());
            if (display.Length > MaximumLength)
            {
                return false;
            }
        }

        var presented = display.ToString();
        if (presented.Length == 0)
        {
            return false;
        }

        // Decompõe acentos, remove somente marcas combinantes e recompõe antes do casing invariável.
        var keyBuilder = new StringBuilder();
        foreach (var rune in presented.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
            {
                keyBuilder.Append(rune.ToString());
            }
        }

        var key = keyBuilder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        if (!FitsLimits(presented, key))
        {
            return false;
        }

        name = new CategoryName(presented, key);
        return true;
    }
}
