using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finora.Application.Pluggy;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Encapsula a autenticação Pluggy e mantém a API key somente na memória desta execução.</summary>
internal sealed class PluggyApiClient : IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient httpClient;
    private readonly PluggyOptions options;
    private readonly TimeSpan requestTimeout;
    private readonly TimeProvider timeProvider;
    internal static readonly Uri BaseAddress = new("https://api.pluggy.ai/", UriKind.Absolute);
    private string? _apiKey;
    internal Uri? ConfiguredBaseAddress => httpClient.BaseAddress;
    internal string? ConfiguredItemId => options.ItemId;
    internal string? ConfiguredAccountId => options.AccountId;

    // Timeout e relógio são injetáveis para testar cancelamento sem esperar pelo tempo real.
    internal PluggyApiClient(
        HttpClient httpClient,
        PluggyOptions options,
        TimeSpan? requestTimeout = null,
        TimeProvider? timeProvider = null)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    // Impede chamadas quando faltam opções obrigatórias e nunca inclui os valores no resultado.
    internal async Task<PluggyAuthenticationResult> AuthenticateAsync(
        CancellationToken cancellationToken,
        bool requireConfiguredItem = true)
    {
        var missing = options.GetMissingRequiredKeys(requireConfiguredItem);
        if (missing.Count > 0)
        {
            return new PluggyAuthenticationResult(false, MissingConfiguration: missing);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = timeProvider.CreateTimer(
            static state => ((CancellationTokenSource)state!).Cancel(),
            timeout,
            requestTimeout,
            Timeout.InfiniteTimeSpan);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                "auth",
                new AuthenticationRequest(options.ClientId!, options.ClientSecret!),
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PluggyAuthenticationResult(false, PluggyLocalReadFailureCategory.Timeout);
        }
        catch (HttpRequestException)
        {
            return new PluggyAuthenticationResult(false, PluggyLocalReadFailureCategory.Transport);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var failureCategory = MapFailure(response.StatusCode);
                return new PluggyAuthenticationResult(
                    false,
                    failureCategory,
                    RetryAfterAtUtc: failureCategory == PluggyLocalReadFailureCategory.RateLimited
                        ? ReadRetryAfterAtUtc(response)
                        : null);
            }

            try
            {
                var payload = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(timeout.Token);
                if (string.IsNullOrWhiteSpace(payload?.ApiKey))
                {
                    return new PluggyAuthenticationResult(
                        false, PluggyLocalReadFailureCategory.InvalidResponse);
                }

                _apiKey = payload.ApiKey;
                return new PluggyAuthenticationResult(true);
            }
            catch (JsonException)
            {
                return new PluggyAuthenticationResult(
                    false, PluggyLocalReadFailureCategory.InvalidResponse);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new PluggyAuthenticationResult(false, PluggyLocalReadFailureCategory.Timeout);
            }
        }
    }

    // Cria requests relativos ao host fixo e aplica o token apenas ao transporte autenticado.
    internal HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string relativePath)
    {
        if (_apiKey is null)
        {
            throw new InvalidOperationException("Pluggy authentication has not completed.");
        }

        if (!Uri.TryCreate(relativePath, UriKind.Relative, out var relativeUri)
            || !Uri.TryCreate(BaseAddress, relativeUri, out var target)
            || target.Scheme != BaseAddress.Scheme
            || target.Authority != BaseAddress.Authority)
        {
            throw new ArgumentException("Pluggy requests must use the fixed API origin.", nameof(relativePath));
        }

        var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.Add("X-API-KEY", _apiKey);
        return request;
    }

    // Lê apenas envelopes tipados e converte erros HTTP sem reter corpo ou mensagem externa.
    internal async Task<PluggyApiResponse<T>> GetJsonAsync<T>(
        string relativePath,
        CancellationToken cancellationToken) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = timeProvider.CreateTimer(
            static state => ((CancellationTokenSource)state!).Cancel(),
            timeout,
            requestTimeout,
            Timeout.InfiniteTimeSpan);
        try
        {
            using var request = CreateAuthenticatedRequest(HttpMethod.Get, relativePath);
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var failureCategory = MapFailure(response.StatusCode);
                return new PluggyApiResponse<T>(
                    null,
                    failureCategory,
                    failureCategory == PluggyLocalReadFailureCategory.RateLimited
                        ? ReadRetryAfterAtUtc(response)
                        : null);
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(timeout.Token);
            return payload is null
                ? new PluggyApiResponse<T>(null, PluggyLocalReadFailureCategory.InvalidResponse)
                : new PluggyApiResponse<T>(payload);
        }
        catch (JsonException)
        {
            return new PluggyApiResponse<T>(null, PluggyLocalReadFailureCategory.InvalidResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PluggyApiResponse<T>(null, PluggyLocalReadFailureCategory.Timeout);
        }
        catch (HttpRequestException)
        {
            return new PluggyApiResponse<T>(null, PluggyLocalReadFailureCategory.Transport);
        }
    }

    // Obtém o estado do item somente pelos campos de decisão, sem copiar respostas externas completas.
    internal Task<PluggyApiResponse<PluggyItemResponse>> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken) =>
        GetJsonAsync<PluggyItemResponse>($"items/{Uri.EscapeDataString(itemId)}", cancellationToken);

    // Percorre uma página numerada de contas por chamada para não ignorar cartões em páginas posteriores.
    internal Task<PluggyApiResponse<PluggyAccountsPage>> GetAccountsPageAsync(
        string itemId,
        int page,
        CancellationToken cancellationToken)
    {
        var encodedItemId = Uri.EscapeDataString(itemId);
        var path = $"accounts?itemId={encodedItemId}&page={page}&pageSize=500";
        return GetJsonAsync<PluggyAccountsPage>(path, cancellationToken);
    }

    // Solicita somente a primeira página cursor-based e mantém o cursor externo fora dos resultados.
    internal Task<PluggyApiResponse<PluggyTransactionsPage>> GetTransactionsPageAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        var path = $"v2/transactions?accountId={Uri.EscapeDataString(accountId)}";
        return GetJsonAsync<PluggyTransactionsPage>(path, cancellationToken);
    }

    // A análise fixa host e endpoint, acrescentando a query opaca somente após validar seus filtros.
    internal Task<PluggyApiResponse<PluggyAnalysisTransactionsPage>> GetAnalysisTransactionsPageAsync(
        string accountId,
        DateOnly dateFrom,
        string? next,
        CancellationToken cancellationToken)
    {
        if (!TryBuildAnalysisPath(accountId, dateFrom, next, out var path))
        {
            return Task.FromResult(new PluggyApiResponse<PluggyAnalysisTransactionsPage>(
                null, PluggyLocalReadFailureCategory.InvalidResponse));
        }

        return GetJsonAsync<PluggyAnalysisTransactionsPage>(path, cancellationToken);
    }

    // Valida a conta e filtros conhecidos, permitindo que next omita dateFrom sem reconstruir a query opaca.
    internal static bool TryBuildAnalysisPath(
        string accountId,
        DateOnly dateFrom,
        string? next,
        out string path)
    {
        path = string.Empty;
        var account = Uri.EscapeDataString(accountId);
        var date = dateFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (next is null)
        {
            path = $"v2/transactions?accountId={account}&dateFrom={date}";
            return true;
        }

        if (!next.StartsWith('?')
            || next.Length < 2
            || next.Contains('#')
            || next.Contains('\\')
            || next.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var segment in next[1..].Split('&', StringSplitOptions.None))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            string key;
            string value;
            try
            {
                key = Uri.UnescapeDataString(segment[..separator].Replace('+', ' '));
                value = Uri.UnescapeDataString(segment[(separator + 1)..].Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (key is not ("accountId" or "dateFrom" or "after"))
            {
                return false;
            }

            if (!values.TryGetValue(key, out var entries))
            {
                entries = [];
                values.Add(key, entries);
            }

            entries.Add(value);
        }

        if (!HasSingle(values, "accountId", accountId)
            || (values.ContainsKey("dateFrom") && !HasSingle(values, "dateFrom", date))
            || !values.TryGetValue("after", out var cursors)
            || cursors.Count != 1
            || string.IsNullOrWhiteSpace(cursors[0]))
        {
            return false;
        }

        path = "v2/transactions" + next;
        return true;

        // Parâmetros repetidos ou diferentes da consulta original não são normalizados silenciosamente.
        static bool HasSingle(Dictionary<string, List<string>> query, string key, string expected) =>
            query.TryGetValue(key, out var entries)
            && entries.Count == 1
            && string.Equals(entries[0], expected, StringComparison.Ordinal);
    }

    // Mantém redirects desativados para impedir que credenciais acompanhem respostas 3xx externas.
    internal static HttpMessageHandler CreatePrimaryHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    };

    // Traduz apenas categorias estáveis e não propaga corpos/mensagens do provedor.
    private static PluggyLocalReadFailureCategory MapFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => PluggyLocalReadFailureCategory.Unauthorized,
        HttpStatusCode.Forbidden => PluggyLocalReadFailureCategory.Forbidden,
        (HttpStatusCode)429 => PluggyLocalReadFailureCategory.RateLimited,
        >= HttpStatusCode.InternalServerError => PluggyLocalReadFailureCategory.ProviderUnavailable,
        _ => PluggyLocalReadFailureCategory.InvalidResponse
    };

    // Persiste somente o instante calculado do Retry-After, sem expor headers ou payloads externos ao caso de uso.
    private DateTimeOffset? ReadRetryAfterAtUtc(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Date is { } absoluteDate)
        {
            return absoluteDate.ToUniversalTime();
        }

        if (retryAfter?.Delta is not { } delay || delay < TimeSpan.Zero)
        {
            return null;
        }

        try
        {
            return timeProvider.GetUtcNow().ToUniversalTime().Add(delay);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Um delta fora da faixa DateTimeOffset bloqueia retry automático sem inventar um prazo menor.
            return DateTimeOffset.MaxValue;
        }
    }

    // Descarta o token em memória junto ao cliente scoped ao comando.
    public void Dispose()
    {
        _apiKey = null;
        httpClient.Dispose();
    }

    // Modelos privados impedem que o corpo bruto faça parte do contrato público da Infrastructure.
    private sealed record AuthenticationRequest(string ClientId, string ClientSecret);
    private sealed record AuthenticationResponse(string? ApiKey);
}

// Modelos mínimos aceitam somente dados necessários à seleção, sem propagar campos financeiros.
internal sealed record PluggyApiResponse<T>(
    T? Payload,
    PluggyLocalReadFailureCategory? FailureCategory = null,
    DateTimeOffset? RetryAfterAtUtc = null) where T : class;

internal sealed record PluggyItemResponse(
    string? Status,
    string? ExecutionStatus,
    DateTimeOffset? ConsentExpiresAt,
    JsonElement? Error);

internal sealed record PluggyAccountsPage(
    int Page,
    int Total,
    int TotalPages,
    IReadOnlyList<PluggyAccountResponse>? Results);

internal sealed record PluggyAccountResponse(
    string? Id,
    string? Type,
    string? Subtype,
    string? ItemId,
    string? CurrencyCode);

// O DTO da transação retém somente IDs opacos para validar pertencimento e contar registros.
internal sealed record PluggyTransactionsPage(
    IReadOnlyList<PluggyTransactionResponse>? Results,
    JsonElement Next);

internal sealed record PluggyTransactionResponse(string? Id, string? AccountId);
