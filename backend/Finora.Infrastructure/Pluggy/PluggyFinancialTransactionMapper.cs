using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Converte somente fatos autorizados da origem em snapshot financeiro conservador.</summary>
internal static class PluggyFinancialTransactionMapper
{
    private const string NormalizationVersion = "transaction-normalizer-v1";

    // Gera entidade com identidade interna nova e compra positiva desativada; não classifica por descrição/sinal.
    internal static FinancialTransaction ToFinancialTransaction(
        PluggyTransactionData source,
        Guid userId,
        Guid cardAccountId,
        DateOnly referenceDate)
    {
        var facts = PluggyTransactionMapper.ToFacts(source);
        var normalization = TransactionNormalizer.Normalize(facts, referenceDate, positivePurchaseRuleConfirmed: false);
        return FinancialTransaction.Create(
            Guid.NewGuid(),
            userId,
            cardAccountId,
            normalization,
            NormalizationVersion,
            referenceDate);
    }
}
