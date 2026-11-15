using Finora.Application.Pluggy;
using Finora.Domain.Transactions;

namespace Finora.Infrastructure.Pluggy;

// Faz a tradução explícita do contrato neutro para fatos puros do domínio.
internal static class PluggyTransactionMapper
{
    // Preserva valores e opcionais; a regra de natureza permanece fora da Infrastructure.
    internal static TransactionFacts ToFacts(PluggyTransactionData transaction) => new(
        transaction.ExternalId,
        transaction.AccountId,
        transaction.Description,
        transaction.Amount,
        transaction.CurrencyCode,
        transaction.OriginalDate,
        transaction.Type,
        transaction.Status,
        transaction.OperationType,
        transaction.InstallmentNumber,
        transaction.TotalInstallments,
        transaction.PurchaseDate,
        transaction.BillId,
        transaction.BillPostDate,
        transaction.BillForecastDate,
        transaction.PaymentType);
}
