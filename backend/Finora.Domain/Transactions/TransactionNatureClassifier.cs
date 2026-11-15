namespace Finora.Domain.Transactions;

/// <summary>Classifica operações conhecidas sem inferir natureza por descrição ou sinal isolado.</summary>
public static class TransactionNatureClassifier
{
    // Operações não reconhecidas ou incompatíveis com tipo/sinal permanecem desconhecidas.
    public static TransactionNatureResult Classify(
        TransactionFacts transaction,
        bool positivePurchaseRuleConfirmed = false)
    {
        if (string.IsNullOrWhiteSpace(transaction.OperationType))
        {
            if (positivePurchaseRuleConfirmed
                && string.Equals(transaction.Type, "DEBIT", StringComparison.Ordinal)
                && transaction.Amount > 0)
            {
                return new TransactionNatureResult(TransactionNature.Purchase);
            }

            return Unknown(TransactionExclusionReason.MissingOperationType);
        }

        var (nature, expectedType, expectedSign) = transaction.OperationType switch
        {
            // O titular confirmou compra sob PAGAMENTO; sem sinal discriminatório, não inferimos a natureza.
            "PAGAMENTO" => (TransactionNature.Unknown, string.Empty, 0),
            "PAGAMENTO_FATURA" =>
                (TransactionNature.BillPayment, "CREDIT", -1),
            "ESTORNO" => (TransactionNature.Refund, "CREDIT", -1),
            "TARIFA" => (TransactionNature.Fee, "DEBIT", 1),
            "CASHBACK" or "OPERACOES_CREDITO_CONTRATADAS_CARTAO" =>
                (TransactionNature.Adjustment, "CREDIT", -1),
            _ => (TransactionNature.Unknown, string.Empty, 0)
        };

        if (nature == TransactionNature.Unknown)
        {
            return Unknown(TransactionExclusionReason.UnknownOperationType);
        }

        if (!string.Equals(transaction.Type, expectedType, StringComparison.Ordinal)
            || Math.Sign(transaction.Amount) != expectedSign)
        {
            return Unknown(TransactionExclusionReason.ContradictoryTypeOrSign);
        }

        return new TransactionNatureResult(nature);
    }

    // Mantém o motivo de exclusão sem reutilizar conteúdo livre vindo da instituição.
    private static TransactionNatureResult Unknown(TransactionExclusionReason reason) =>
        new(TransactionNature.Unknown, reason);
}
