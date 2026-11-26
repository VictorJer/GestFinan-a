using System.Globalization;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita a política aprovada somente com movimentos fictícios, sem persistência ou provedor real.
[TestClass]
public sealed class TransactionRecurrenceEvaluatorTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 11, 25);
    private static readonly TimeZoneInfo BusinessTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(TransactionDateNormalizer.BusinessTimeZoneId);
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CardAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // Três meses consecutivos na fronteira de valor e fim de mês formam um único candidato v1.
    [TestMethod]
    public void EmitsCandidateForThreeDistinctConsecutiveMonthsAtInclusiveBoundaries()
    {
        var movements = new[]
        {
            Movement("monthly-jan", new DateOnly(2026, 1, 31), 99.00m),
            Movement("monthly-feb", new DateOnly(2026, 2, 28), 100.00m),
            Movement("monthly-mar", new DateOnly(2026, 3, 31), 101.00m)
        };

        var result = TransactionRecurrenceEvaluator.Evaluate(movements);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(TransactionRecurrenceEvaluator.PolicyVersion, result[0].PolicyVersion);
        CollectionAssert.AreEqual(movements.Select(movement => movement.Id).ToArray(),
            result[0].OccurrenceTransactionIds.ToArray());
    }

    // Igualdade de valor isolada não compensa descrições ou datas incompatíveis.
    [TestMethod]
    public void DoesNotEmitCandidateForEqualAmountsWithDifferentDescriptions()
    {
        var movements = new[]
        {
            Movement("equal-jan", new DateOnly(2026, 1, 12), 59.90m, description: "Livraria Horizonte"),
            Movement("equal-feb", new DateOnly(2026, 2, 12), 59.90m, description: "Farmácia das Palmeiras"),
            Movement("equal-mar", new DateOnly(2026, 3, 12), 59.90m, description: "Cinema Aurora")
        };

        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(movements).Count);
    }

    // Uma lacuna de mês impede a sequência, mesmo que existam três movimentos semelhantes no histórico.
    [TestMethod]
    public void DoesNotBridgeMissingCalendarMonth()
    {
        var movements = new[]
        {
            Movement("gap-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("gap-mar", new DateOnly(2026, 3, 5), 25m),
            Movement("gap-apr", new DateOnly(2026, 4, 5), 25m)
        };

        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(movements).Count);
    }

    // Cartão/conta e moeda são dimensões de igualdade exata e nunca são combinadas ou convertidas.
    [TestMethod]
    public void DoesNotCombineDifferentCardAccountsOrCurrencies()
    {
        var differentCard = new[]
        {
            Movement("card-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("card-feb", new DateOnly(2026, 2, 5), 25m,
                cardAccountId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            Movement("card-mar", new DateOnly(2026, 3, 5), 25m)
        };
        var differentCurrency = new[]
        {
            Movement("currency-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("currency-feb", new DateOnly(2026, 2, 5), 25m, currency: "USD"),
            Movement("currency-mar", new DateOnly(2026, 3, 5), 25m)
        };

        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(differentCard).Count);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(differentCurrency).Count);
    }

    // A tolerância de valor é inclusiva em 1% e recusa desvios superiores à política aprovada.
    [TestMethod]
    public void AppliesInclusiveOnePercentAmountBoundary()
    {
        var withinLimit = new[]
        {
            Movement("value-in-jan", new DateOnly(2026, 1, 5), 99m),
            Movement("value-in-feb", new DateOnly(2026, 2, 5), 100m),
            Movement("value-in-mar", new DateOnly(2026, 3, 5), 101m)
        };
        var outsideLimit = new[]
        {
            Movement("value-out-jan", new DateOnly(2026, 1, 5), 99m),
            Movement("value-out-feb", new DateOnly(2026, 2, 5), 100m),
            Movement("value-out-mar", new DateOnly(2026, 3, 5), 101.01m)
        };

        Assert.AreEqual(1, TransactionRecurrenceEvaluator.Evaluate(withinLimit).Count);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(outsideLimit).Count);
    }

    // A janela usa a mediana dos dias civis e tolera até três dias, mas não um desvio de quatro.
    [TestMethod]
    public void AppliesInclusiveThreeDayBoundary()
    {
        var withinLimit = new[]
        {
            Movement("date-in-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("date-in-feb", new DateOnly(2026, 2, 6), 25m),
            Movement("date-in-mar", new DateOnly(2026, 3, 9), 25m)
        };
        var outsideLimit = new[]
        {
            Movement("date-out-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("date-out-feb", new DateOnly(2026, 2, 6), 25m),
            Movement("date-out-mar", new DateOnly(2026, 3, 10), 25m)
        };

        Assert.AreEqual(1, TransactionRecurrenceEvaluator.Evaluate(withinLimit).Count);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(outsideLimit).Count);
    }

    // A normalização NFC trata caixa, pontuação e espaços sem remover números ou acentos.
    [TestMethod]
    public void NormalizesDescriptionSafelyAndPreservesSignificantCharacters()
    {
        var normalizedMatch = new[]
        {
            Movement("description-jan", new DateOnly(2026, 1, 5), 25m,
                description: "Nuvem-Ficti\u0301cia 8472"),
            Movement("description-feb", new DateOnly(2026, 2, 5), 25m,
                description: "NUVEM FICTÍCIA 8472"),
            Movement("description-mar", new DateOnly(2026, 3, 5), 25m,
                description: "Nuvem*Fictícia 8472")
        };
        var changedNumber = new[]
        {
            normalizedMatch[0],
            normalizedMatch[1],
            Movement("description-number", new DateOnly(2026, 3, 5), 25m,
                description: "Nuvem Fictícia 8473")
        };

        Assert.AreEqual(1, TransactionRecurrenceEvaluator.Evaluate(normalizedMatch).Count);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(changedNumber).Count);
    }

    // Magnitudes negativas podem corresponder entre si, mas sinais mistos e valores zero não são elegíveis.
    [TestMethod]
    public void RequiresSameNonzeroSignWithoutInferringNature()
    {
        var sameNegativeSign = new[]
        {
            Movement("negative-jan", new DateOnly(2026, 1, 5), -99m),
            Movement("negative-feb", new DateOnly(2026, 2, 5), -100m),
            Movement("negative-mar", new DateOnly(2026, 3, 5), -101m)
        };
        var mixedSign = new[]
        {
            Movement("mixed-jan", new DateOnly(2026, 1, 5), -99m),
            Movement("mixed-feb", new DateOnly(2026, 2, 5), 100m),
            Movement("mixed-mar", new DateOnly(2026, 3, 5), 101m)
        };
        var zeroValues = new[]
        {
            Movement("zero-jan", new DateOnly(2026, 1, 5), 0m),
            Movement("zero-feb", new DateOnly(2026, 2, 5), 0m),
            Movement("zero-mar", new DateOnly(2026, 3, 5), 0m)
        };

        Assert.AreEqual(1, TransactionRecurrenceEvaluator.Evaluate(sameNegativeSign).Count);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(mixedSign).Count);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(zeroValues).Count);
    }

    // A identidade inclui cartão e identificador externo; cópias idênticas no mesmo cartão contam uma vez.
    [TestMethod]
    public void DeduplicatesWithinCardButKeepsExternalIdsScopedToTheirCard()
    {
        var secondCardId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var movements = new[]
        {
            Movement("shared-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("shared-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("shared-feb", new DateOnly(2026, 2, 5), 25m),
            Movement("shared-mar", new DateOnly(2026, 3, 5), 25m),
            Movement("shared-jan", new DateOnly(2026, 1, 5), 25m, cardAccountId: secondCardId),
            Movement("shared-feb", new DateOnly(2026, 2, 5), 25m, cardAccountId: secondCardId),
            Movement("shared-mar", new DateOnly(2026, 3, 5), 25m, cardAccountId: secondCardId)
        };

        var candidates = TransactionRecurrenceEvaluator.Evaluate(movements);

        Assert.AreEqual(2, candidates.Count);
        Assert.AreEqual(1, candidates.Count(candidate => candidate.CardAccountId == CardAccountId));
        Assert.AreEqual(1, candidates.Count(candidate => candidate.CardAccountId == secondCardId));
        Assert.IsTrue(candidates.All(candidate => candidate.OccurrenceTransactionIds.Count == 3));
    }

    // Uma data técnica ausente não é substituída pela data original ou por metadados de fatura.
    [TestMethod]
    public void DoesNotCountOccurrenceWithoutTechnicalTransactionDate()
    {
        var movements = new[]
        {
            Movement("missing-date-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("missing-date-feb", null, 25m),
            Movement("missing-date-mar", new DateOnly(2026, 3, 5), 25m)
        };

        Assert.IsNull(movements[1].TransactionDate);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(movements).Count);
    }

    // Situação, status e natureza permanecem intactos, e apenas lançamentos efetivos podem contar.
    [TestMethod]
    public void ExcludesPendingAndUnknownWithoutChangingOriginalInterpretations()
    {
        var movements = new[]
        {
            Movement("effective-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("pending-feb", new DateOnly(2026, 2, 5), 25m, status: "PENDING"),
            Movement("unknown-mar", new DateOnly(2026, 3, 5), 25m, status: "NEW_STATUS")
        };
        var originalNatures = movements.Select(movement => movement.Nature).ToArray();
        var originalStatuses = movements.Select(movement => movement.Status).ToArray();
        var originalSituations = movements.Select(movement => movement.Situation).ToArray();

        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(movements).Count);

        CollectionAssert.AreEqual(originalNatures, movements.Select(movement => movement.Nature).ToArray());
        CollectionAssert.AreEqual(originalStatuses, movements.Select(movement => movement.Status).ToArray());
        CollectionAssert.AreEqual(originalSituations, movements.Select(movement => movement.Situation).ToArray());
        Assert.AreEqual(TransactionSituation.Effective, movements[0].Situation);
        Assert.AreEqual(TransactionSituation.Pending, movements[1].Situation);
        Assert.AreEqual(TransactionSituation.Unknown, movements[2].Situation);
    }

    // A projeção parcelada futura não é uma observação, mesmo quando compartilha os demais sinais.
    [TestMethod]
    public void ExcludesExplicitFutureInstallmentFromObservedSequence()
    {
        var futureInstallment = Movement(
            "future-installment-feb",
            new DateOnly(2026, 2, 5),
            25m,
            status: "PENDING",
            installmentNumber: 1,
            totalInstallments: 3,
            billForecastDate: "2026-12",
            paymentType: "INSTALLMENT");
        var movements = new[]
        {
            Movement("future-jan", new DateOnly(2026, 1, 5), 25m),
            futureInstallment,
            Movement("future-mar", new DateOnly(2026, 3, 5), 25m)
        };

        Assert.AreEqual(TransactionSituation.FutureInstallment, futureInstallment.Situation);
        Assert.AreEqual("PENDING", futureInstallment.Status);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(movements).Count);
    }

    // A continuidade mensal atravessa dezembro/janeiro e respeita o calendário de cada ano.
    [TestMethod]
    public void RecognizesConsecutiveMonthsAcrossYearBoundary()
    {
        var movements = new[]
        {
            Movement("year-dec", new DateOnly(2025, 12, 31), 25m),
            Movement("year-jan", new DateOnly(2026, 1, 31), 25m),
            Movement("year-feb", new DateOnly(2026, 2, 28), 25m)
        };

        Assert.AreEqual(1, TransactionRecurrenceEvaluator.Evaluate(movements).Count);
    }

    // Permutar a ordem de leitura não altera candidatos, versão da política ou ocorrências selecionadas.
    [TestMethod]
    public void EvaluationIsStableAcrossInputOrder()
    {
        var movements = new[]
        {
            Movement("stable-jan", new DateOnly(2026, 1, 5), 25m),
            Movement("stable-feb", new DateOnly(2026, 2, 5), 25m),
            Movement("stable-mar", new DateOnly(2026, 3, 5), 25m)
        };

        var forward = TransactionRecurrenceEvaluator.Evaluate(movements).Single();
        var reverse = TransactionRecurrenceEvaluator.Evaluate(movements.Reverse()).Single();

        Assert.AreEqual(forward.UserId, reverse.UserId);
        Assert.AreEqual(forward.CardAccountId, reverse.CardAccountId);
        Assert.AreEqual(forward.PolicyVersion, reverse.PolicyVersion);
        CollectionAssert.AreEqual(forward.OccurrenceTransactionIds.ToArray(),
            reverse.OccurrenceTransactionIds.ToArray());
    }

    // Cria snapshots válidos a partir de fatos sintéticos, sem acessar repositórios ou serviços externos.
    private static FinancialTransaction Movement(
        string externalId,
        DateOnly? date,
        decimal amount,
        string description = "Nuvem Fictícia 8472",
        string currency = "BRL",
        Guid? userId = null,
        Guid? cardAccountId = null,
        string status = "POSTED",
        int? installmentNumber = null,
        int? totalInstallments = null,
        string? billForecastDate = null,
        string? paymentType = null)
    {
        var facts = new TransactionFacts(
            externalId,
            "fixture-account",
            description,
            amount,
            currency,
            date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not-a-date",
            "DEBIT",
            status,
            "COMPRA",
            installmentNumber,
            totalInstallments,
            BillForecastDate: billForecastDate,
            PaymentType: paymentType);
        var normalization = TransactionNormalizer.Normalize(
            facts,
            ReferenceDate,
            positivePurchaseRuleConfirmed: true,
            timeZone: BusinessTimeZone);

        return FinancialTransaction.Create(
            Guid.NewGuid(),
            userId ?? UserId,
            cardAccountId ?? CardAccountId,
            normalization,
            "fixture-normalization-v1",
            ReferenceDate);
    }
}
