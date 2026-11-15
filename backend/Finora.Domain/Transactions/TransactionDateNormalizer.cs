using System.Globalization;

namespace Finora.Domain.Transactions;

/// <summary>Preserva a data original e deriva competência sem deslocar datas de calendário.</summary>
public static class TransactionDateNormalizer
{
    public const string BusinessTimeZoneId = "America/Sao_Paulo";

    // Datas yyyy-MM-dd permanecem calendários; instantes com fuso explícito usam America/Sao_Paulo.
    public static TransactionDateResult Normalize(string? originalDate, TimeZoneInfo? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(originalDate))
        {
            return new TransactionDateResult(originalDate, null, TransactionExclusionReason.MissingRequiredField);
        }

        if (DateOnly.TryParseExact(
                originalDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var calendarDate))
        {
            return new TransactionDateResult(originalDate, calendarDate);
        }

        if (!HasExplicitOffset(originalDate))
        {
            return new TransactionDateResult(originalDate, null, TransactionExclusionReason.MissingDateOffset);
        }

        if (!DateTimeOffset.TryParse(originalDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
        {
            return new TransactionDateResult(originalDate, null, TransactionExclusionReason.InvalidDate);
        }

        try
        {
            var zone = timeZone ?? TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
            var localInstant = TimeZoneInfo.ConvertTime(instant, zone);
            return new TransactionDateResult(originalDate, DateOnly.FromDateTime(localInstant.DateTime));
        }
        catch (TimeZoneNotFoundException)
        {
            return new TransactionDateResult(originalDate, null, TransactionExclusionReason.TimeZoneUnavailable);
        }
        catch (InvalidTimeZoneException)
        {
            return new TransactionDateResult(originalDate, null, TransactionExclusionReason.TimeZoneUnavailable);
        }
    }

    // A competência de referência segue o mesmo fuso dos instantes, mas não altera a janela dateFrom em UTC.
    public static DateOnly GetReferenceDate(DateTimeOffset reference, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reference, zone).DateTime);
    }

    // Só timestamps com Z ou offset após o separador de hora são instantes; horário local é ambíguo.
    private static bool HasExplicitOffset(string value)
    {
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var timeSeparator = value.IndexOf('T');
        var offsetSign = value.LastIndexOfAny(['+', '-']);
        return timeSeparator >= 0 && offsetSign > timeSeparator;
    }
}

/// <summary>Representa simultaneamente o valor original e a competência derivada, sem descartar a origem.</summary>
public sealed record TransactionDateResult(
    string? OriginalDate,
    DateOnly? TransactionDate,
    TransactionExclusionReason? ExclusionReason = null);
