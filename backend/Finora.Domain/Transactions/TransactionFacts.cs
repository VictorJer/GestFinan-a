namespace Finora.Domain.Transactions;

/// <summary>Fatos de origem preservados para regras puras, sem dependência de JSON ou Pluggy.</summary>
public sealed record TransactionFacts(
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

// Natureza é independente de status, competência e elegibilidade financeira.
public enum TransactionNature
{
    Purchase,
    Refund,
    BillPayment,
    Fee,
    Adjustment,
    Unknown
}

// Motivos são códigos internos estáveis e não incorporam texto externo.
public enum TransactionExclusionReason
{
    MissingOperationType,
    UnknownOperationType,
    ContradictoryTypeOrSign,
    MissingRequiredField,
    UnsupportedCurrency,
    NotPurchase,
    NotEffective,
    UnknownStatus,
    InvalidInstallment,
    FutureInstallment,
    InvalidDate,
    MissingDateOffset,
    TimeZoneUnavailable,
    UnlinkedRefund
}

/// <summary>Classificação de natureza com motivo fixo quando a evidência não sustenta uma regra.</summary>
public sealed record TransactionNatureResult(
    TransactionNature Nature,
    TransactionExclusionReason? ExclusionReason = null);
