namespace Finora.Application.Pluggy;

/// <summary>Contrato neutro mínimo para interpretar uma transação sem expor o DTO externo.</summary>
public sealed record PluggyTransactionData(
    string ExternalId,
    string AccountId,
    string Description,
    decimal Amount,
    string CurrencyCode,
    string OriginalDate,
    string? Type,
    string? Status,
    string? OperationType,
    int? InstallmentNumber = null,
    int? TotalInstallments = null,
    string? PurchaseDate = null,
    string? BillId = null,
    string? BillPostDate = null,
    string? BillForecastDate = null,
    string? PaymentType = null);
