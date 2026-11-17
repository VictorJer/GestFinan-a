namespace Finora.Domain.Transactions;

/// <summary>Resultado explicável por lançamento, sem agregar valores nem calcular dashboard.</summary>
public sealed record TransactionNormalizationResult(
    TransactionFacts Original,
    TransactionNature Nature,
    TransactionSituation Situation,
    TransactionDateResult Date,
    bool IsValid,
    bool IsEligibleForRealExpenses,
    TransactionExclusionReason? ExclusionReason)
{
    // Motivos por dimensão permanecem separados para armazenamento e diagnóstico conservadores.
    public TransactionExclusionReason? NatureReason { get; init; }

    public TransactionExclusionReason? SituationReason { get; init; }

    public TransactionExclusionReason? DateReason { get; init; }
}

/// <summary>Combina natureza, situação, data, validade e moeda sem efeitos de persistência.</summary>
public static class TransactionNormalizer
{
    // A regra positiva fica desligada até ser sustentada pelo gate de evidência da conexão real.
    public static TransactionNormalizationResult Normalize(
        TransactionFacts transaction,
        DateOnly referenceDate,
        bool positivePurchaseRuleConfirmed = false,
        TimeZoneInfo? timeZone = null)
    {
        var nature = TransactionNatureClassifier.Classify(transaction, positivePurchaseRuleConfirmed);
        var situation = TransactionSituationClassifier.Classify(transaction, referenceDate);
        var date = TransactionDateNormalizer.Normalize(transaction.OriginalDate, timeZone);

        var requiredFieldsValid = !string.IsNullOrWhiteSpace(transaction.ExternalId)
            && !string.IsNullOrWhiteSpace(transaction.AccountId)
            && !string.IsNullOrWhiteSpace(transaction.Description)
            && !string.IsNullOrWhiteSpace(transaction.CurrencyCode)
            && date.TransactionDate is not null;
        if (!requiredFieldsValid)
        {
            return Result(false, date.ExclusionReason ?? TransactionExclusionReason.MissingRequiredField);
        }

        if (nature.Nature == TransactionNature.Refund)
        {
            return Result(true, TransactionExclusionReason.UnlinkedRefund);
        }

        if (nature.Nature == TransactionNature.Unknown)
        {
            return Result(true, nature.ExclusionReason ?? TransactionExclusionReason.UnknownOperationType);
        }

        if (nature.Nature != TransactionNature.Purchase)
        {
            return Result(true, TransactionExclusionReason.NotPurchase);
        }

        if (situation.Situation == TransactionSituation.Unknown)
        {
            return Result(true, situation.ExclusionReason ?? TransactionExclusionReason.UnknownStatus);
        }

        if (situation.Situation != TransactionSituation.Effective)
        {
            return Result(true, situation.ExclusionReason ?? TransactionExclusionReason.NotEffective);
        }

        if (!string.Equals(transaction.CurrencyCode, "BRL", StringComparison.Ordinal))
        {
            return Result(true, TransactionExclusionReason.UnsupportedCurrency);
        }

        if (!positivePurchaseRuleConfirmed)
        {
            return Result(true, TransactionExclusionReason.UnknownOperationType);
        }

        return Result(true, null, eligible: true);

        // A função local preserva a mesma transação e não calcula agregados ou conversões cambiais.
        TransactionNormalizationResult Result(
            bool valid,
            TransactionExclusionReason? reason,
            bool eligible = false) => new(
                transaction,
                nature.Nature,
                situation.Situation,
                date,
                valid,
                eligible,
                reason)
            {
                NatureReason = nature.ExclusionReason,
                SituationReason = situation.ExclusionReason,
                DateReason = date.ExclusionReason
            };
    }
}
