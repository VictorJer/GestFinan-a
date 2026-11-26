using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Pluggy;

namespace Finora.Api.Tests;

// Confirma que o adaptador preserva fatos e não promove sinais ambíguos a compras.
[TestClass]
public sealed class PluggyFinancialTransactionMapperTests
{
    private static readonly Guid UserId = Guid.Parse("74472d39-4281-4c73-a7df-9a8f4ffeb498");
    private static readonly Guid CardId = Guid.Parse("bc96108b-4f54-45db-af4e-a9d8f426f5a0");
    private static readonly DateOnly Reference = new(2026, 11, 20);

    // Operações desconhecidas, pagamento de fatura e estorno conservam natureza independente de valor positivo.
    [TestMethod]
    public void AmbiguousPaymentBillPaymentAndRefundKeepConservativeNature()
    {
        var ambiguous = Map(Transaction("ambiguous", "PAGAMENTO", "DEBIT", 12.34m));
        var billPayment = Map(Transaction("bill", "PAGAMENTO_FATURA", "CREDIT", -12.34m));
        var refund = Map(Transaction("refund", "ESTORNO", "CREDIT", -12.34m));

        Assert.AreEqual(TransactionNature.Unknown, ambiguous.Nature);
        Assert.AreEqual(TransactionExclusionReason.UnknownOperationType, ambiguous.NatureReason);
        Assert.AreEqual(TransactionNature.BillPayment, billPayment.Nature);
        Assert.AreEqual(TransactionNature.Refund, refund.Nature);
        Assert.IsFalse(ambiguous.Nature == TransactionNature.Purchase);
    }

    // Parcelamento futuro e data sem offset preservam status/fonte sem inventar data técnica.
    [TestMethod]
    public void FutureInstallmentAndAmbiguousDateRemainFaithfulToSource()
    {
        var future = Map(Transaction(
            "future", "PAGAMENTO", "DEBIT", 10m,
            status: "PENDING", originalDate: "2026-12-10", installmentNumber: 2,
            totalInstallments: 10, billForecastDate: "2026-12-15", paymentType: "INSTALLMENT"));
        var ambiguousDate = Map(Transaction(
            "ambiguous-date", "TARIFA", "DEBIT", 1m, originalDate: "2026-11-18T12:30:00"));

        Assert.AreEqual("2026-12-10", future.OriginalDate);
        Assert.AreEqual(TransactionSituation.FutureInstallment, future.Situation);
        Assert.AreEqual("2026-11-18T12:30:00", ambiguousDate.OriginalDate);
        Assert.IsNull(ambiguousDate.TransactionDate);
        Assert.AreEqual(TransactionExclusionReason.MissingDateOffset, ambiguousDate.DateReason);
    }

    // Moeda e precisão são preservadas individualmente; nenhum total, conversão ou competência derivada é criado.
    [TestMethod]
    public void PreservesForeignCurrencyAndExactDecimalWithoutAggregating()
    {
        const decimal amount = 12.3456m;
        var foreign = Map(Transaction("foreign", "TARIFA", "DEBIT", amount, currencyCode: "USD"));

        Assert.AreEqual(amount, foreign.Amount);
        Assert.AreEqual("USD", foreign.CurrencyCode);
        Assert.AreEqual(TransactionNature.Fee, foreign.Nature);
        Assert.AreEqual("2026-11-18", foreign.OriginalDate);
        Assert.AreEqual(new DateOnly(2026, 11, 18), foreign.TransactionDate);
    }

    // Metadados Pluggy permanecem no snapshot da origem, e nenhum parcelamento isolado cria candidato recorrente.
    [TestMethod]
    public void PreservesInstallmentMetadataIndependentlyOfRecurrenceCandidate()
    {
        var present = Map(Transaction(
            "installment-present", "COMPRA", "DEBIT", 25m,
            installmentNumber: 1, totalInstallments: 3,
            billForecastDate: "2026-11", paymentType: "INSTALLMENT"));
        var absent = Map(Transaction("installment-absent", "COMPRA", "DEBIT", 25m));
        var incoherent = Map(Transaction(
            "installment-incoherent", "COMPRA", "DEBIT", 25m,
            installmentNumber: 4, totalInstallments: 3,
            billForecastDate: "2026-11", paymentType: "INSTALLMENT"));

        Assert.AreEqual(1, present.InstallmentNumber);
        Assert.AreEqual(3, present.TotalInstallments);
        Assert.AreEqual("2026-11", present.BillForecastDate);
        Assert.AreEqual("INSTALLMENT", present.PaymentType);
        Assert.IsNull(absent.InstallmentNumber);
        Assert.IsNull(absent.TotalInstallments);
        Assert.IsNull(absent.BillForecastDate);
        Assert.IsNull(absent.PaymentType);
        Assert.AreEqual(4, incoherent.InstallmentNumber);
        Assert.AreEqual(3, incoherent.TotalInstallments);
        Assert.AreEqual("INSTALLMENT", incoherent.PaymentType);
        Assert.AreEqual(TransactionSituation.Unknown, incoherent.Situation);
        Assert.AreEqual(TransactionExclusionReason.InvalidInstallment, incoherent.SituationReason);
        Assert.AreEqual(0, TransactionRecurrenceEvaluator.Evaluate(new[] { present }).Count);
    }

    // Usa IDs internos explícitos e fatos externos sintéticos para exercitar o mesmo caminho de mapeamento.
    private static FinancialTransaction Map(PluggyTransactionData source) =>
        PluggyFinancialTransactionMapper.ToFinancialTransaction(source, UserId, CardId, Reference);

    private static PluggyTransactionData Transaction(
        string id,
        string operation,
        string type,
        decimal amount,
        string status = "POSTED",
        string originalDate = "2026-11-18",
        string currencyCode = "BRL",
        int? installmentNumber = null,
        int? totalInstallments = null,
        string? billForecastDate = null,
        string? paymentType = null) => new(
            id,
            "external-account",
            "synthetic description",
            amount,
            currencyCode,
            originalDate,
            type,
            status,
            operation,
            installmentNumber,
            totalInstallments,
            BillForecastDate: billForecastDate,
            PaymentType: paymentType);
}
