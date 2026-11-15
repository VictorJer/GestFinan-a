using System.Text.Json;
using Finora.Application.Pluggy;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Orquestra a validação da conexão e a leitura de uma página de transações do cartão.</summary>
internal sealed class PluggyLocalReader(
    PluggyConnectionInspector connectionInspector,
    PluggyApiClient client) : IPluggyLocalReader
{
    // Só comprova leitura com ao menos um registro válido vinculado ao cartão selecionado.
    public async Task<PluggyLocalReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var connection = await connectionInspector.InspectAsync(cancellationToken);
        if (!connection.IsReady || connection.AccountId is null)
        {
            return new PluggyLocalReadResult(
                connection.FailureOutcome ?? PluggyLocalReadOutcome.Failed,
                connection.Stage,
                FailureCategory: connection.FailureCategory,
                MissingConfiguration: connection.MissingConfiguration,
                ConsentExpiryCheck: connection.ConsentExpiryCheck);
        }

        var response = await client.GetTransactionsPageAsync(connection.AccountId, cancellationToken);
        if (response.Payload is null)
        {
            return Failed(response.FailureCategory, connection.ConsentExpiryCheck);
        }

        var page = response.Payload;
        if (page.Results is null || !TryGetContinuation(page.Next, out var hasMore))
        {
            return Failed(PluggyLocalReadFailureCategory.InvalidResponse, connection.ConsentExpiryCheck);
        }

        foreach (var transaction in page.Results)
        {
            if (string.IsNullOrWhiteSpace(transaction.Id)
                || !string.Equals(transaction.AccountId, connection.AccountId, StringComparison.Ordinal))
            {
                return Failed(PluggyLocalReadFailureCategory.InvalidResponse, connection.ConsentExpiryCheck);
            }
        }

        var count = page.Results.Count;
        return new PluggyLocalReadResult(
            count > 0 ? PluggyLocalReadOutcome.TransactionsRead : PluggyLocalReadOutcome.NoTransactions,
            PluggyLocalReadStage.Transactions,
            count,
            hasMore,
            ConsentExpiryCheck: connection.ConsentExpiryCheck);
    }

    // Exposes only whether the opaque next cursor exists; its value is never followed or rendered.
    private static bool TryGetContinuation(JsonElement next, out bool hasMore)
    {
        hasMore = false;
        if (next.ValueKind == JsonValueKind.Undefined)
        {
            return false;
        }

        if (next.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (next.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var cursor = next.GetString();
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        hasMore = true;
        return true;
    }

    // Evita corpos, IDs e exceções externas no contrato apresentado pelo comando local.
    private static PluggyLocalReadResult Failed(
        PluggyLocalReadFailureCategory? category,
        PluggyConsentExpiryCheck consentExpiryCheck) => new(
            PluggyLocalReadOutcome.Failed,
            PluggyLocalReadStage.Transactions,
            FailureCategory: category ?? PluggyLocalReadFailureCategory.InvalidResponse,
            ConsentExpiryCheck: consentExpiryCheck);
}
