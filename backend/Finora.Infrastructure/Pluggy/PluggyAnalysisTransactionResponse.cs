using System.Text.Json.Serialization;
using Finora.Application.Pluggy;

namespace Finora.Infrastructure.Pluggy;

// Isola a resposta de análise e ignora campos externos que não são necessários ao domínio.
internal sealed record PluggyAnalysisTransactionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; init; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; init; }

    [JsonPropertyName("date")]
    public string? Date { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("operationType")]
    public string? OperationType { get; init; }

    [JsonPropertyName("creditCardMetadata")]
    public PluggyCreditCardMetadataResponse? CreditCardMetadata { get; init; }

    // A conversão falha fechada quando faltam dados-base, sem inventar zero ou texto substituto.
    internal bool TryToContract(out PluggyTransactionData? transaction)
    {
        transaction = null;
        if (string.IsNullOrWhiteSpace(Id)
            || string.IsNullOrWhiteSpace(AccountId)
            || string.IsNullOrWhiteSpace(Description)
            || Amount is null
            || string.IsNullOrWhiteSpace(CurrencyCode)
            || string.IsNullOrWhiteSpace(Date))
        {
            return false;
        }

        transaction = new PluggyTransactionData(
            Id,
            AccountId,
            Description,
            Amount.Value,
            CurrencyCode,
            Date,
            Type,
            Status,
            OperationType,
            CreditCardMetadata?.InstallmentNumber,
            CreditCardMetadata?.TotalInstallments,
            CreditCardMetadata?.PurchaseDate,
            CreditCardMetadata?.BillId,
            CreditCardMetadata?.BillPostDate,
            CreditCardMetadata?.BillForecastDate,
            CreditCardMetadata?.PaymentType);
        return true;
    }
}

// Somente metadados necessários à validação são retidos; cardNumber e detalhes do merchant são omitidos.
internal sealed record PluggyCreditCardMetadataResponse
{
    [JsonPropertyName("installmentNumber")]
    public int? InstallmentNumber { get; init; }

    [JsonPropertyName("totalInstallments")]
    public int? TotalInstallments { get; init; }

    [JsonPropertyName("purchaseDate")]
    public string? PurchaseDate { get; init; }

    [JsonPropertyName("billId")]
    public string? BillId { get; init; }

    [JsonPropertyName("billPostDate")]
    public string? BillPostDate { get; init; }

    [JsonPropertyName("billForecastDate")]
    public string? BillForecastDate { get; init; }

    [JsonPropertyName("paymentType")]
    public string? PaymentType { get; init; }
}

// O envelope tipado deixa o cursor opaco separado das transações e de seus campos financeiros.
internal sealed record PluggyAnalysisTransactionsPage(
    IReadOnlyList<PluggyAnalysisTransactionResponse>? Results,
    System.Text.Json.JsonElement Next);
