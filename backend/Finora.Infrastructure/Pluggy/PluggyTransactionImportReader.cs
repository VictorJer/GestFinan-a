using System.Text.Json;
using Finora.Application.Pluggy;
using Finora.Domain.Transactions;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Coleta todas as páginas permitidas do item persistido e publica somente snapshot completo em memória.</summary>
internal sealed class PluggyTransactionImportReader(
    PluggyApiClient client,
    TimeProvider timeProvider) : ITransactionImportReader
{
    private static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(120);
    private const int MaximumPagesPerAccount = 100;
    private const int MaximumTransactions = 50_000;
    private const int MaximumAccountPages = 100;

    public async Task<TransactionImportReadResult> ReadAsync(
        string externalItemId,
        DateOnly dateFrom,
        CancellationToken cancellationToken)
    {
        // Recusa identidades inválidas antes da autenticação e limita a execução inteira com um token vinculado.
        if (string.IsNullOrWhiteSpace(externalItemId))
        {
            return TransactionImportReadResult.Failed(TransactionImportFailureCode.ProviderUnavailable);
        }

        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = timeProvider.CreateTimer(
            static state => ((CancellationTokenSource)state!).Cancel(),
            totalTimeout,
            TotalBudget,
            Timeout.InfiniteTimeSpan);

        try
        {
            var authentication = await client.AuthenticateAsync(totalTimeout.Token, requireConfiguredItem: false);
            if (!authentication.Succeeded)
            {
                return TransactionImportReadResult.Failed(
                    MapFailure(authentication.FailureCategory),
                    retryAfterAtUtc: authentication.RetryAfterAtUtc);
            }

            // A consulta do item usa a identidade da conexão persistida, nunca o ItemId de configuração diagnóstica.
            var itemResult = await client.GetItemAsync(externalItemId, totalTimeout.Token);
            if (itemResult.Payload is null)
            {
                return TransactionImportReadResult.Failed(
                    MapFailure(itemResult.FailureCategory),
                    retryAfterAtUtc: itemResult.RetryAfterAtUtc);
            }

            var itemValidation = PluggyConnectionInspector.ValidateItem(itemResult.Payload, timeProvider.GetUtcNow());
            if (itemValidation.FailureCategory is not null)
            {
                return TransactionImportReadResult.Failed(MapFailure(itemValidation.FailureCategory));
            }

            var accounts = await ReadCreditCardAccountsAsync(externalItemId, totalTimeout.Token);
            if (accounts.FailureCode is not null || accounts.Accounts is null)
            {
                return TransactionImportReadResult.Failed(accounts.FailureCode
                    ?? TransactionImportFailureCode.InvalidResponse,
                    accounts.PagesRead,
                    retryAfterAtUtc: accounts.RetryAfterAtUtc);
            }

            var transactions = new List<PluggyTransactionData>();
            var transactionsByIdentity = new Dictionary<(string AccountId, string ExternalId), PluggyTransactionData>();
            var transactionPagesRead = 0;
            var receivedCount = 0;
            foreach (var account in accounts.Accounts)
            {
                var read = await ReadTransactionsAsync(
                    account.ExternalId, dateFrom, transactions, transactionsByIdentity, totalTimeout.Token);
                if (read.FailureCode is not null)
                {
                    // Só contagens já observadas atravessam a fronteira; registros parciais continuam em memória local.
                    return TransactionImportReadResult.Failed(
                        read.FailureCode.Value,
                        accounts.PagesRead + transactionPagesRead + read.PagesRead,
                        receivedCount + read.ReceivedCount,
                        read.RetryAfterAtUtc);
                }

                transactionPagesRead += read.PagesRead;
                receivedCount += read.ReceivedCount;
            }

            return new TransactionImportReadResult(
                new TransactionImportSnapshot(
                    accounts.Accounts,
                    transactions,
                    dateFrom,
                    true,
                    accounts.PagesRead + transactionPagesRead,
                    receivedCount,
                    GetObservedFrom(transactions),
                    GetObservedTo(transactions)),
                null);
        }
        catch (OperationCanceledException)
        {
            return TransactionImportReadResult.Failed(cancellationToken.IsCancellationRequested
                ? TransactionImportFailureCode.Cancelled
                : TransactionImportFailureCode.ProviderTimeout);
        }
    }

    // Percorre todas as páginas numeradas e retém somente cartões vinculados ao item consultado.
    private async Task<AccountsReadResult> ReadCreditCardAccountsAsync(
        string externalItemId,
        CancellationToken cancellationToken)
    {
        var accounts = new List<TransactionImportAccount>();
        int? expectedPages = null;
        for (var pageNumber = 1; expectedPages is null || pageNumber <= expectedPages; pageNumber++)
        {
            if (pageNumber > MaximumAccountPages)
            {
                return new AccountsReadResult(null, TransactionImportFailureCode.CollectionLimitExceeded, pageNumber - 1);
            }

            var response = await client.GetAccountsPageAsync(externalItemId, pageNumber, cancellationToken);
            var page = response.Payload;
            if (page is null)
            {
                return new AccountsReadResult(
                    null, MapFailure(response.FailureCategory), pageNumber - 1, response.RetryAfterAtUtc);
            }

            if (page.Page != pageNumber || page.Total < 0 || page.TotalPages < 1 || page.Results is null
                || (expectedPages is not null && expectedPages != page.TotalPages))
            {
                return new AccountsReadResult(null, TransactionImportFailureCode.InvalidResponse, pageNumber);
            }

            expectedPages ??= page.TotalPages;
            foreach (var account in page.Results)
            {
                if (string.IsNullOrWhiteSpace(account.Id)
                    || !string.Equals(account.ItemId, externalItemId, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(account.Type)
                    || string.IsNullOrWhiteSpace(account.Subtype))
                {
                    return new AccountsReadResult(null, TransactionImportFailureCode.InvalidResponse, pageNumber);
                }

                if (string.Equals(account.Type, "CREDIT", StringComparison.Ordinal)
                    && string.Equals(account.Subtype, "CREDIT_CARD", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(account.CurrencyCode))
                    {
                        return new AccountsReadResult(null, TransactionImportFailureCode.InvalidResponse, pageNumber);
                    }

                    accounts.Add(new TransactionImportAccount(
                        account.Id,
                        account.Type,
                        account.Subtype,
                        account.CurrencyCode ?? string.Empty));
                }
            }
        }

        if (accounts.Select(account => account.ExternalId).Distinct(StringComparer.Ordinal).Count() != accounts.Count)
        {
            return new AccountsReadResult(null, TransactionImportFailureCode.InvalidResponse, expectedPages ?? 0);
        }

        return new AccountsReadResult(accounts, null, expectedPages ?? 0);
    }

    // Coleta cada cartão até o cursor final e rejeita cursores repetidos, contas alheias e limites excedidos.
    private async Task<TransactionPagesReadResult> ReadTransactionsAsync(
        string accountId,
        DateOnly dateFrom,
        List<PluggyTransactionData> collected,
        Dictionary<(string AccountId, string ExternalId), PluggyTransactionData> transactionsByIdentity,
        CancellationToken cancellationToken)
    {
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pagesRead = 0;
        var receivedCount = 0;
        string? next = null;
        for (var pageNumber = 0; ; pageNumber++)
        {
            if (pageNumber >= MaximumPagesPerAccount)
            {
                return new TransactionPagesReadResult(
                    TransactionImportFailureCode.CollectionLimitExceeded, pageNumber, receivedCount);
            }

            var response = await client.GetAnalysisTransactionsPageAsync(
                accountId, dateFrom, next, cancellationToken);
            var page = response.Payload;
            if (page is null || page.Results is null || !TryGetNext(page.Next, out next))
            {
                return new TransactionPagesReadResult(
                    MapFailure(response.FailureCategory ?? PluggyLocalReadFailureCategory.InvalidResponse),
                    pageNumber,
                    receivedCount,
                    response.RetryAfterAtUtc);
            }

            pagesRead++;
            if (receivedCount + page.Results.Count > MaximumTransactions)
            {
                return new TransactionPagesReadResult(
                    TransactionImportFailureCode.CollectionLimitExceeded, pagesRead, receivedCount);
            }
            receivedCount += page.Results.Count;

            foreach (var transaction in page.Results)
            {
                if (transaction is null || !transaction.TryToContract(out var contract) || contract is null
                    || !string.Equals(contract.AccountId, accountId, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(contract.CurrencyCode)
                    || !IsPersistableAmount(contract.Amount))
                {
                    return new TransactionPagesReadResult(TransactionImportFailureCode.InvalidResponse, pagesRead, receivedCount);
                }

                var identity = (contract.AccountId, contract.ExternalId);
                if (transactionsByIdentity.TryGetValue(identity, out var existing))
                {
                    if (existing != contract)
                    {
                        return new TransactionPagesReadResult(
                            TransactionImportFailureCode.InvalidResponse, pagesRead, receivedCount);
                    }

                    continue;
                }

                if (collected.Count >= MaximumTransactions)
                {
                    return new TransactionPagesReadResult(
                        TransactionImportFailureCode.CollectionLimitExceeded, pagesRead, receivedCount);
                }

                collected.Add(contract);
                transactionsByIdentity.Add(identity, contract);
            }

            if (next is null)
            {
                return new TransactionPagesReadResult(null, pagesRead, receivedCount);
            }

            if (!seenCursors.Add(next))
            {
                return new TransactionPagesReadResult(TransactionImportFailureCode.InvalidResponse, pagesRead, receivedCount);
            }
        }
    }

    // Accepta somente cursor nulo ou texto não vazio; o cliente aplica validação de origem, conta e filtros.
    private static bool TryGetNext(JsonElement value, out string? next)
    {
        next = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        next = value.GetString();
        return !string.IsNullOrWhiteSpace(next);
    }

    // Converte somente categorias fixas; conteúdo externo nunca compõe falhas do caso de uso.
    private static TransactionImportFailureCode MapFailure(PluggyLocalReadFailureCategory? failure) => failure switch
    {
        PluggyLocalReadFailureCategory.Unauthorized or PluggyLocalReadFailureCategory.Forbidden
            => TransactionImportFailureCode.AuthenticationRequired,
        PluggyLocalReadFailureCategory.RateLimited => TransactionImportFailureCode.RateLimited,
        PluggyLocalReadFailureCategory.Timeout => TransactionImportFailureCode.ProviderTimeout,
        PluggyLocalReadFailureCategory.InvalidResponse => TransactionImportFailureCode.InvalidResponse,
        PluggyLocalReadFailureCategory.ConsentExpired or PluggyLocalReadFailureCategory.UnsupportedState
            => TransactionImportFailureCode.ProviderUnavailable,
        _ => TransactionImportFailureCode.ProviderUnavailable
    };

    // Rejeita valores fora do numeric(19,4) antes de o provider poder arredondar ou exceder a coluna.
    private static bool IsPersistableAmount(decimal amount)
    {
        const decimal maximum = 999_999_999_999_999.9999m;
        return amount >= -maximum
            && amount <= maximum
            && decimal.Round(amount, 4, MidpointRounding.ToZero) == amount;
    }

    // Os extremos vêm somente das datas técnicas que a regra existente conseguiu normalizar.
    private static DateOnly? GetObservedFrom(IReadOnlyList<PluggyTransactionData> transactions)
    {
        var dates = transactions.Select(value => TransactionDateNormalizer.Normalize(value.OriginalDate).TransactionDate)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return dates.Length == 0 ? null : dates.Min();
    }

    private static DateOnly? GetObservedTo(IReadOnlyList<PluggyTransactionData> transactions)
    {
        var dates = transactions.Select(value => TransactionDateNormalizer.Normalize(value.OriginalDate).TransactionDate)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return dates.Length == 0 ? null : dates.Max();
    }

    private sealed record AccountsReadResult(
        IReadOnlyList<TransactionImportAccount>? Accounts,
        TransactionImportFailureCode? FailureCode,
        int PagesRead,
        DateTimeOffset? RetryAfterAtUtc = null);

    private sealed record TransactionPagesReadResult(
        TransactionImportFailureCode? FailureCode,
        int PagesRead,
        int ReceivedCount,
        DateTimeOffset? RetryAfterAtUtc = null);
}
