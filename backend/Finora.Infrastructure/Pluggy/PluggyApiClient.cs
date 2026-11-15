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
    internal static readonly Uri BaseAddress = new("https://api.pluggy.ai/", UriKind.Absolute);
    private string? _apiKey;
    internal Uri? ConfiguredBaseAddress => httpClient.BaseAddress;
    internal string? ConfiguredItemId => options.ItemId;
    internal string? ConfiguredAccountId => options.AccountId;

    // O tempo configurável internamente permite testar timeouts sem alterar o limite de produção.
    internal PluggyApiClient(HttpClient httpClient, PluggyOptions options, TimeSpan? requestTimeout = null)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.requestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    // Impede chamadas quando faltam opções obrigatórias e nunca inclui os valores no resultado.
    internal async Task<PluggyAuthenticationResult> AuthenticateAsync(CancellationToken cancellationToken)
    {
        var missing = options.GetMissingRequiredKeys();
        if (missing.Count > 0)
        {
            return new PluggyAuthenticationResult(false, MissingConfiguration: missing);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout);
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
                return new PluggyAuthenticationResult(false, MapFailure(response.StatusCode));
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
        timeout.CancelAfter(requestTimeout);
        try
        {
            using var request = CreateAuthenticatedRequest(HttpMethod.Get, relativePath);
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new PluggyApiResponse<T>(null, MapFailure(response.StatusCode));
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
    PluggyLocalReadFailureCategory? FailureCategory = null) where T : class;

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
    string? ItemId);

// O DTO da transação retém somente IDs opacos para validar pertencimento e contar registros.
internal sealed record PluggyTransactionsPage(
    IReadOnlyList<PluggyTransactionResponse>? Results,
    JsonElement Next);

internal sealed record PluggyTransactionResponse(string? Id, string? AccountId);
