using Finora.Domain.Transactions;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica a matriz financeira proposta com fixtures sintéticas, sem classificar descrições.
[TestClass]
public sealed class TransactionNatureClassifierTests
{
    // Operações conhecidas só recebem natureza quando direção e sinal são coerentes.
    [TestMethod]
    [DataRow("PAGAMENTO_FATURA", "CREDIT", -15.50, TransactionNature.BillPayment)]
    [DataRow("ESTORNO", "CREDIT", -15.50, TransactionNature.Refund)]
    [DataRow("TARIFA", "DEBIT", 2.25, TransactionNature.Fee)]
    [DataRow("CASHBACK", "CREDIT", -1.00, TransactionNature.Adjustment)]
    [DataRow("OPERACOES_CREDITO_CONTRATADAS_CARTAO", "CREDIT", -5.00, TransactionNature.Adjustment)]
    public void ClassifiesDocumentedSpecialOperations(
        string operationType,
        string type,
        double amount,
        TransactionNature expected)
    {
        var result = TransactionNatureClassifier.Classify(Facts(operationType, type, (decimal)amount));

        Assert.AreEqual(expected, result.Nature);
        Assert.IsNull(result.ExclusionReason);
    }

    // Ausência, enum novo e combinação contraditória não são convertidos em compra por fallback.
    [TestMethod]
    public void KeepsMissingUnknownAndContradictoryOperationsUnknown()
    {
        var missing = TransactionNatureClassifier.Classify(Facts(null, "DEBIT", 4m));
        var unknown = TransactionNatureClassifier.Classify(Facts("NEW_OPERATION", "DEBIT", 4m));
        var ambiguousPayment = TransactionNatureClassifier.Classify(Facts("PAGAMENTO", "CREDIT", -4m));
        var contradiction = TransactionNatureClassifier.Classify(Facts("TARIFA", "CREDIT", -4m));
        var signConflict = TransactionNatureClassifier.Classify(Facts("ESTORNO", "CREDIT", 4m));

        Assert.AreEqual(TransactionExclusionReason.MissingOperationType, missing.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.UnknownOperationType, unknown.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.UnknownOperationType, ambiguousPayment.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.ContradictoryTypeOrSign, contradiction.ExclusionReason);
        Assert.AreEqual(TransactionExclusionReason.ContradictoryTypeOrSign, signConflict.ExclusionReason);
        Assert.AreEqual(TransactionNature.Unknown, missing.Nature);
        Assert.AreEqual(TransactionNature.Unknown, unknown.Nature);
        Assert.AreEqual(TransactionNature.Unknown, ambiguousPayment.Nature);
    }

    // Fixture fictícia representa a confirmação do titular sem converter o caso em compra automática.
    [TestMethod]
    public void ConfirmedPurchaseLikePaymentRemainsUnknownAndPendingWithoutStructuredMetadata()
    {
        var facts = Facts("PAGAMENTO", "DEBIT", 42.75m) with { Status = "PENDING" };

        var nature = TransactionNatureClassifier.Classify(facts);
        var situation = TransactionSituationClassifier.Classify(facts, new DateOnly(2026, 11, 15));

        Assert.AreEqual(TransactionNature.Unknown, nature.Nature);
        Assert.AreEqual(TransactionExclusionReason.UnknownOperationType, nature.ExclusionReason);
        Assert.AreEqual(TransactionSituation.Pending, situation.Situation);
        Assert.AreEqual(TransactionExclusionReason.NotEffective, situation.ExclusionReason);
    }

    // A tradução explícita preserva dinheiro, identidade opaca e metadados sem dependência de JSON.
    [TestMethod]
    public void InfrastructureMapperPreservesNeutralContractFacts()
    {
        var source = new PluggyTransactionData(
            "fixture-id", "fixture-account", "fixture description", 123.4500m, "USD", "2026-01-02",
            "DEBIT", "POSTED", "TARIFA", 2, 4, "2025-11-02", "fixture-bill", "2026-01-03", null,
            "INSTALLMENT");

        var facts = PluggyTransactionMapper.ToFacts(source);

        Assert.AreEqual(source.ExternalId, facts.ExternalId);
        Assert.AreEqual(source.Amount, facts.Amount);
        Assert.AreEqual(source.CurrencyCode, facts.CurrencyCode);
        Assert.AreEqual(source.BillPostDate, facts.BillPostDate);
        Assert.AreEqual(source.PaymentType, facts.PaymentType);
    }

    // Descrição e conteúdo não participam da inferência de natureza.
    private static TransactionFacts Facts(string? operationType, string? type, decimal amount) => new(
        "fixture-id", "fixture-account", "descrição fictícia irrelevante", amount, "BRL", "2026-01-01",
        type, "POSTED", operationType);
}
