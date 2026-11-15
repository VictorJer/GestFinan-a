using Finora.Domain.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Concilia exemplos sintéticos sem persistir, converter moeda ou agregar valores individuais.
[TestClass]
public sealed class TransactionNormalizerTests
{
    private static readonly DateOnly Reference = new(2026, 11, 15);
    private static readonly TimeZoneInfo TimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(TransactionDateNormalizer.BusinessTimeZoneId);

    // Somente compra fictícia BRL, efetivada e com regra explicitamente habilitada é elegível.
    [TestMethod]
    public void ExplainsEligibilityAcrossControlledSyntheticSet()
    {
        var purchase = Normalize(Facts("purchase-1", amount: 123.4500m), confirmed: true);
        var foreignPurchase = Normalize(Facts("purchase-usd", currency: "USD", amount: 10m), confirmed: true);
        var payment = Normalize(Facts("payment", operation: "PAGAMENTO_FATURA", type: "CREDIT", amount: -20m));
        var pending = Normalize(Facts("pending", status: "PENDING"), confirmed: true);
        var future = Normalize(Facts("future", status: "PENDING", forecast: "2026-12",
            paymentType: "INSTALLMENT", installment: 1, total: 3), confirmed: true);
        var refund = Normalize(Facts("refund", operation: "ESTORNO", type: "CREDIT", amount: -5m));
        var unknown = Normalize(Facts("unknown", operation: "NEW_OPERATION"));

        Assert.IsTrue(purchase.IsValid);
        Assert.IsTrue(purchase.IsEligibleForRealExpenses);
        Assert.AreEqual(123.4500m, purchase.Original.Amount);
        Assert.AreEqual(TransactionExclusionReason.UnsupportedCurrency, foreignPurchase.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.NotPurchase, payment.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.NotEffective, pending.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.FutureInstallment, future.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.UnlinkedRefund, refund.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.UnknownOperationType, unknown.ExclusionReason);
        Assert.IsFalse(foreignPurchase.IsEligibleForRealExpenses);
        Assert.IsFalse(payment.IsEligibleForRealExpenses);
    }

    // O gate positivo desligado conserva elegibilidade desconhecida até a evidência real ser aceita.
    [TestMethod]
    public void DoesNotEnableProposedPurchaseRuleByDefault()
    {
        var result = Normalize(Facts("unverified-purchase"));

        Assert.AreEqual(TransactionNature.Unknown, result.Nature);
        Assert.IsFalse(result.IsEligibleForRealExpenses);
        Assert.AreEqual(TransactionExclusionReason.MissingOperationType, result.ExclusionReason);
    }

    // IDs distintos não são fundidos por descrição/data/valor e a precisão decimal permanece intacta.
    [TestMethod]
    public void KeepsCoincidentTransactionsAsSeparateEligibleFacts()
    {
        var first = Normalize(Facts("distinct-id-1", amount: 9.8765m), confirmed: true);
        var second = Normalize(Facts("distinct-id-2", amount: 9.8765m), confirmed: true);

        Assert.AreNotEqual(first.Original.ExternalId, second.Original.ExternalId);
        Assert.AreEqual(first.Original.Description, second.Original.Description);
        Assert.AreEqual(first.Original.Amount, second.Original.Amount);
        Assert.IsTrue(first.IsEligibleForRealExpenses);
        Assert.IsTrue(second.IsEligibleForRealExpenses);
    }

    // Campo-base ausente invalida o fato sem substituir valor, identidade ou moeda.
    [TestMethod]
    public void MissingRequiredDataIsInvalidRatherThanEligible()
    {
        var invalid = Normalize(Facts("invalid") with { Description = " " }, confirmed: true);

        Assert.IsFalse(invalid.IsValid);
        Assert.IsFalse(invalid.IsEligibleForRealExpenses);
        Assert.AreEqual(TransactionExclusionReason.MissingRequiredField, invalid.ExclusionReason);
    }

    // Fixtures descrevem somente os campos usados pelos classificadores e são totalmente fictícias.
    private static TransactionNormalizationResult Normalize(TransactionFacts facts, bool confirmed = false) =>
        TransactionNormalizer.Normalize(facts, Reference, confirmed, TimeZone);

    private static TransactionFacts Facts(
        string id,
        string currency = "BRL",
        decimal amount = 4.25m,
        string? operation = null,
        string type = "DEBIT",
        string status = "POSTED",
        int? installment = null,
        int? total = null,
        string? forecast = null,
        string? paymentType = null) => new(
        id, "fixture-account", "fixture description", amount, currency, "2026-11-01", type, status, operation,
        installment, total, null, null, null, forecast, paymentType);
}
