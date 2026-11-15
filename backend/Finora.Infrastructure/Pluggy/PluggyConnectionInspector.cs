using System.Text.Json;
using Finora.Application.Pluggy;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Valida o item e seleciona exclusivamente uma conta de cartão pertencente a ele.</summary>
internal sealed class PluggyConnectionInspector(PluggyApiClient client)
{
    // A inspeção autentica primeiro, valida validade/estado e percorre todas as páginas de contas.
    internal async Task<PluggyConnectionInspection> InspectAsync(CancellationToken cancellationToken)
    {
        var authentication = await client.AuthenticateAsync(cancellationToken);
        if (!authentication.Succeeded)
        {
            var missingConfiguration = authentication.MissingConfiguration;
            return new PluggyConnectionInspection(
                false,
                null,
                missingConfiguration is null
                    ? PluggyLocalReadOutcome.Failed
                    : PluggyLocalReadOutcome.InvalidConfiguration,
                missingConfiguration is null
                    ? PluggyLocalReadStage.Authentication
                    : PluggyLocalReadStage.Configuration,
                authentication.FailureCategory,
                missingConfiguration,
                PluggyConsentExpiryCheck.NotChecked);
        }

        var itemId = client.ConfiguredItemId!;
        var itemResponse = await client.GetItemAsync(itemId, cancellationToken);
        if (itemResponse.Payload is null)
        {
            return Failed(PluggyLocalReadStage.Item, itemResponse.FailureCategory);
        }

        var consentCheck = ValidateItem(itemResponse.Payload, DateTimeOffset.UtcNow);
        if (consentCheck.FailureCategory is not null)
        {
            return new PluggyConnectionInspection(
                false,
                null,
                PluggyLocalReadOutcome.ConnectionBlocked,
                PluggyLocalReadStage.Item,
                consentCheck.FailureCategory,
                null,
                consentCheck.ConsentExpiryCheck);
        }

        var accountsResult = await GetAllAccountsAsync(itemId, cancellationToken);
        if (accountsResult.FailureCategory is not null || accountsResult.Accounts is null)
        {
            return Failed(PluggyLocalReadStage.Accounts, accountsResult.FailureCategory);
        }

        var selection = SelectCard(accountsResult.Accounts, client.ConfiguredAccountId);
        if (selection.FailureCategory is not null)
        {
            return new PluggyConnectionInspection(
                false,
                null,
                PluggyLocalReadOutcome.InvalidConfiguration,
                PluggyLocalReadStage.Accounts,
                selection.FailureCategory,
                null,
                consentCheck.ConsentExpiryCheck);
        }

        return new PluggyConnectionInspection(
            true,
            selection.AccountId,
            null,
            PluggyLocalReadStage.Accounts,
            null,
            null,
            consentCheck.ConsentExpiryCheck);
    }

    // Lê todas as páginas declaradas e rejeita envelopes ou vínculos inconsistentes.
    private async Task<PluggyAccountsResult> GetAllAccountsAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        var accounts = new List<PluggyAccountResponse>();
        int? totalPages = null;
        for (var pageNumber = 1; totalPages is null || pageNumber <= totalPages; pageNumber++)
        {
            var response = await client.GetAccountsPageAsync(itemId, pageNumber, cancellationToken);
            var page = response.Payload;
            if (page is null)
            {
                return new PluggyAccountsResult(null, response.FailureCategory
                    ?? PluggyLocalReadFailureCategory.InvalidResponse);
            }

            if (page.Page != pageNumber || page.Total < 0 || page.TotalPages < 1
                || page.Results is null || (totalPages is not null && page.TotalPages != totalPages))
            {
                return new PluggyAccountsResult(null, PluggyLocalReadFailureCategory.InvalidResponse);
            }

            totalPages ??= page.TotalPages;
            foreach (var account in page.Results)
            {
                if (string.IsNullOrWhiteSpace(account.Id)
                    || !string.Equals(account.ItemId, itemId, StringComparison.Ordinal))
                {
                    return new PluggyAccountsResult(null, PluggyLocalReadFailureCategory.InvalidResponse);
                }

                accounts.Add(account);
            }
        }

        if (accounts.Select(account => account.Id).Distinct(StringComparer.Ordinal).Count() != accounts.Count)
        {
            return new PluggyAccountsResult(null, PluggyLocalReadFailureCategory.InvalidResponse);
        }

        return new PluggyAccountsResult(accounts, null);
    }

    // Cartão é definido pelo contrato CREDIT/CREDIT_CARD, nunca por nome ou aparência da conta.
    private static PluggyCardSelection SelectCard(
        IReadOnlyList<PluggyAccountResponse> accounts,
        string? configuredAccountId)
    {
        if (!string.IsNullOrWhiteSpace(configuredAccountId))
        {
            var configured = accounts.SingleOrDefault(account =>
                string.Equals(account.Id, configuredAccountId, StringComparison.Ordinal));
            return IsCreditCard(configured)
                ? new PluggyCardSelection(configured!.Id, null)
                : new PluggyCardSelection(null, PluggyLocalReadFailureCategory.InvalidAccountSelection);
        }

        var cards = accounts.Where(IsCreditCard).ToArray();
        return cards.Length == 1
            ? new PluggyCardSelection(cards[0].Id, null)
            : new PluggyCardSelection(null, PluggyLocalReadFailureCategory.InvalidAccountSelection);
    }

    // Só aceita o estado documentado como atualizado com execução bem-sucedida; o resto falha fechado.
    private static PluggyItemValidation ValidateItem(PluggyItemResponse item, DateTimeOffset utcNow)
    {
        var hasError = item.Error is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
        if (hasError
            || !string.Equals(item.Status, "UPDATED", StringComparison.Ordinal)
            || !string.Equals(item.ExecutionStatus, "SUCCESS", StringComparison.Ordinal))
        {
            return new PluggyItemValidation(
                PluggyConsentExpiryCheck.NotChecked,
                PluggyLocalReadFailureCategory.UnsupportedState);
        }

        if (item.ConsentExpiresAt is null)
        {
            return new PluggyItemValidation(PluggyConsentExpiryCheck.Unavailable, null);
        }

        return item.ConsentExpiresAt <= utcNow
            ? new PluggyItemValidation(
                PluggyConsentExpiryCheck.Expired,
                PluggyLocalReadFailureCategory.ConsentExpired)
            : new PluggyItemValidation(PluggyConsentExpiryCheck.Valid, null);
    }

    // Converte falhas de transporte HTTP/contrato para resultados sem manter conteúdo bruto.
    private static PluggyConnectionInspection Failed(
        PluggyLocalReadStage stage,
        PluggyLocalReadFailureCategory? category) => new(
            false,
            null,
            PluggyLocalReadOutcome.Failed,
            stage,
            category ?? PluggyLocalReadFailureCategory.InvalidResponse,
            null,
            PluggyConsentExpiryCheck.NotChecked);

    private static bool IsCreditCard(PluggyAccountResponse? account) =>
        account is not null
        && string.Equals(account.Type, "CREDIT", StringComparison.Ordinal)
        && string.Equals(account.Subtype, "CREDIT_CARD", StringComparison.Ordinal);
}

// Resultados internos mantêm IDs fora da saída e carregam somente o cartão para a próxima etapa.
internal sealed record PluggyConnectionInspection(
    bool IsReady,
    string? AccountId,
    PluggyLocalReadOutcome? FailureOutcome,
    PluggyLocalReadStage Stage,
    PluggyLocalReadFailureCategory? FailureCategory,
    IReadOnlyList<string>? MissingConfiguration,
    PluggyConsentExpiryCheck ConsentExpiryCheck);

// Tipos internos separam validação de item, seleção de cartão e paginação sem expor DTOs do provedor.
internal sealed record PluggyItemValidation(
    PluggyConsentExpiryCheck ConsentExpiryCheck,
    PluggyLocalReadFailureCategory? FailureCategory);

internal sealed record PluggyAccountsResult(
    IReadOnlyList<PluggyAccountResponse>? Accounts,
    PluggyLocalReadFailureCategory? FailureCategory);

internal sealed record PluggyCardSelection(
    string? AccountId,
    PluggyLocalReadFailureCategory? FailureCategory);
