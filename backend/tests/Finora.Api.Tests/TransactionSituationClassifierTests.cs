using Finora.Domain.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica status e metadados de parcela independentes da natureza com fixtures inteiramente sintéticas.
[TestClass]
public sealed class TransactionSituationClassifierTests
{
    private static readonly DateOnly Reference = new(2026, 11, 15);

    // POSTED e PENDING mantêm situações distintas, e apenas a evidência completa produz parcela futura.
    [TestMethod]
    public void ClassifiesPostedPendingAndEvidenceBasedFutureInstallment()
    {
        var posted = TransactionSituationClassifier.Classify(Facts(status: "POSTED"), Reference);
        var pending = TransactionSituationClassifier.Classify(Facts(status: "PENDING"), Reference);
        var future = TransactionSituationClassifier.Classify(Facts(
            status: "PENDING", installment: 2, total: 3, forecast: "2026-12", paymentType: "INSTALLMENT"), Reference);

        Assert.AreEqual(TransactionSituation.Effective, posted.Situation);
        Assert.AreEqual(TransactionSituation.Pending, pending.Situation);
        Assert.AreEqual(TransactionSituation.FutureInstallment, future.Situation);
    }

    // Status novo, total ausente, numeração inválida ou data isolada nunca criam situação futura.
    [TestMethod]
    public void KeepsUnknownAndIncoherentInstallmentsOutOfEffectiveSet()
    {
        var unknownStatus = TransactionSituationClassifier.Classify(Facts(status: "NEW_STATUS"), Reference);
        var missingTotal = TransactionSituationClassifier.Classify(Facts(
            status: "PENDING", installment: 2, forecast: "2026-12", paymentType: "INSTALLMENT"), Reference);
        var invalidNumber = TransactionSituationClassifier.Classify(Facts(
            status: "PENDING", installment: 4, total: 3, forecast: "2026-12", paymentType: "INSTALLMENT"), Reference);
        var futurePurchaseDateOnly = TransactionSituationClassifier.Classify(Facts(
            status: "PENDING", purchaseDate: "2026-12-10"), Reference);
        var postedWithFutureForecast = TransactionSituationClassifier.Classify(Facts(
            status: "POSTED", installment: 1, total: 3, forecast: "2026-12", paymentType: "INSTALLMENT"), Reference);

        Assert.AreEqual(TransactionSituation.Unknown, unknownStatus.Situation);
        Assert.AreEqual(TransactionSituation.Pending, missingTotal.Situation);
        Assert.AreEqual(TransactionSituation.Unknown, invalidNumber.Situation);
        Assert.AreEqual(TransactionSituation.Pending, futurePurchaseDateOnly.Situation);
        Assert.AreEqual(TransactionSituation.Unknown, postedWithFutureForecast.Situation);
        Assert.AreEqual(TransactionExclusionReason.InvalidInstallment, invalidNumber.ExclusionReason);
    }

    // Factories só simplificam fixtures; não introduzem regras implícitas sobre data ou natureza.
    private static TransactionFacts Facts(
        string status,
        int? installment = null,
        int? total = null,
        string? forecast = null,
        string? paymentType = null,
        string? purchaseDate = null) => new(
        "fixture-id", "fixture-account", "fixture", 10m, "BRL", "2026-10-01", "DEBIT", status, "OUTROS",
        installment, total, purchaseDate, null, null, forecast, paymentType);
}
