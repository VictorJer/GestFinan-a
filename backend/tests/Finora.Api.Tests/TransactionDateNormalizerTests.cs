using Finora.Domain.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida datas de calendário e instantes com fuso em torno das viradas de mês.
[TestClass]
public sealed class TransactionDateNormalizerTests
{
    // O dia de calendário original não sofre conversão para UTC ou fuso local.
    [TestMethod]
    public void PreservesCalendarDateAndLeapDay()
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(TransactionDateNormalizer.BusinessTimeZoneId);
        var monthEnd = TransactionDateNormalizer.Normalize("2026-03-31", timezone);
        var leapDay = TransactionDateNormalizer.Normalize("2024-02-29", timezone);

        Assert.AreEqual("2026-03-31", monthEnd.OriginalDate);
        Assert.AreEqual(new DateOnly(2026, 3, 31), monthEnd.TransactionDate);
        Assert.AreEqual(new DateOnly(2024, 2, 29), leapDay.TransactionDate);
    }

    // Instantes UTC e com offset são convertidos por America/Sao_Paulo, inclusive na virada do mês.
    [TestMethod]
    public void ConvertsUtcAndOffsetInstantsAtMonthBoundary()
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(TransactionDateNormalizer.BusinessTimeZoneId);
        var utcInstant = TransactionDateNormalizer.Normalize("2026-11-01T02:30:00Z", timezone);
        var offsetInstant = TransactionDateNormalizer.Normalize("2026-11-01T01:00:00+02:00", timezone);

        Assert.AreEqual(new DateOnly(2026, 10, 31), utcInstant.TransactionDate);
        Assert.AreEqual(new DateOnly(2026, 10, 31), offsetInstant.TransactionDate);
        Assert.AreEqual("2026-11-01T02:30:00Z", utcInstant.OriginalDate);
    }

    // Horário sem offset e timestamp inválido permanecem inelegíveis em vez de assumir fuso local.
    [TestMethod]
    public void RejectsAmbiguousOrInvalidInstants()
    {
        var ambiguous = TransactionDateNormalizer.Normalize("2026-11-01T02:30:00");
        var invalid = TransactionDateNormalizer.Normalize("2026-99-99T00:00:00Z");

        Assert.AreEqual(TransactionExclusionReason.MissingDateOffset, ambiguous.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.InvalidDate, invalid.ExclusionReason);
    }

    // Confirma que o fuso IANA proposto está disponível no ambiente usado para esta validação.
    [TestMethod]
    public void ProposedBusinessTimezoneIsAvailable()
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(TransactionDateNormalizer.BusinessTimeZoneId);

        Assert.AreEqual(TransactionDateNormalizer.BusinessTimeZoneId, timezone.Id);
    }
}
