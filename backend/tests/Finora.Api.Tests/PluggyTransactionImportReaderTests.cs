using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;

namespace Finora.Api.Tests;

// Usa transporte em memória para validar a resolução do item persistido sem credenciais operacionais.
[TestClass]
public sealed class PluggyTransactionImportReaderTests
{
    // Um item indisponível falha com código interno e não exige ItemId global de configuração.
    [TestMethod]
    public async Task UnavailablePersistedItemFailsWithoutConfiguredItemId()
    {
        var requests = new List<Uri>();
        var handler = new ImportReaderHandler(request =>
        {
            requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath == "/auth")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"apiKey\":\"synthetic-api-key\"}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("private provider response", Encoding.UTF8, "text/plain")
            };
        });
        var api = new PluggyApiClient(
            new HttpClient(handler) { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("synthetic-client", "synthetic-secret", null, null));
        var reader = new PluggyTransactionImportReader(api, TimeProvider.System);

        var result = await reader.ReadAsync("persisted-item-id", new DateOnly(2025, 11, 20), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.InvalidResponse, result.FailureCode);
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual("/items/persisted-item-id", requests[1].AbsolutePath);
        Assert.IsFalse(result.ToString()!.Contains("private provider response", StringComparison.Ordinal));
        api.Dispose();
    }

    // Credencial inválida é traduzida a código fechado sem ecoar o corpo de erro do provedor.
    [TestMethod]
    public async Task UnauthorizedAuthenticationReturnsAllowlistedFailure()
    {
        var api = new PluggyApiClient(
            new HttpClient(new ImportReaderHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("private auth response sentinel", Encoding.UTF8, "text/plain")
            })) { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("synthetic-client", "synthetic-secret", null, null));

        var result = await new PluggyTransactionImportReader(api, TimeProvider.System)
            .ReadAsync("persisted-item-id", new DateOnly(2025, 11, 20), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.AuthenticationRequired, result.FailureCode);
        Assert.IsFalse(result.ToString()!.Contains("private auth response sentinel", StringComparison.Ordinal));
        api.Dispose();
    }

    // Cancelamento solicitado pelo chamador permanece distinto de timeout e não publica snapshot parcial.
    [TestMethod]
    public async Task CallerCancellationReturnsSanitizedCancelledFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var api = new PluggyApiClient(
            new HttpClient(new ImportReaderHandler(_ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }))
            { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("synthetic-client", "synthetic-secret", null, null));

        var result = await new PluggyTransactionImportReader(api, TimeProvider.System)
            .ReadAsync("persisted-item-id", new DateOnly(2025, 11, 20), cancellation.Token);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.Cancelled, result.FailureCode);
        Assert.IsNull(result.Snapshot);
        api.Dispose();
    }

    // Timeout do transporte é diferente de cancelamento do titular e não deixa o snapshot parcial escapar.
    [TestMethod]
    public async Task RequestTimeoutReturnsSanitizedTimeoutFailure()
    {
        var api = new PluggyApiClient(
            new HttpClient(new ImportReaderTimeoutHandler()) { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("synthetic-client", "synthetic-secret", null, null),
            TimeSpan.FromMilliseconds(20));

        var result = await new PluggyTransactionImportReader(api, TimeProvider.System)
            .ReadAsync("persisted-item-id", new DateOnly(2025, 11, 20), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.ProviderTimeout, result.FailureCode);
        Assert.IsNull(result.Snapshot);
        api.Dispose();
    }

    // Um Retry-After 429 vira apenas instante UTC interno; o corpo externo e o texto do header não são propagados.
    [TestMethod]
    public async Task RateLimitPreservesRetryAfterTimestampWithoutProviderPayload()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var api = new PluggyApiClient(
            new HttpClient(new ImportReaderHandler(_ =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("private rate-limit body", Encoding.UTF8, "text/plain")
                };
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(20));
                return response;
            })) { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("synthetic-client", "synthetic-secret", null, null),
            timeProvider: new FixedTimeProvider(nowUtc));

        var result = await new PluggyTransactionImportReader(api, TimeProvider.System)
            .ReadAsync("persisted-item-id", new DateOnly(2025, 11, 20), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.RateLimited, result.FailureCode);
        Assert.AreEqual(nowUtc.AddMinutes(20), result.RetryAfterAtUtc);
        Assert.IsFalse(result.ToString()!.Contains("private rate-limit body", StringComparison.Ordinal));
        api.Dispose();
    }

    // Handler de teste registra somente o destino e nunca consulta a origem real.
    private sealed class ImportReaderHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    // Mantém a requisição suspensa até o timeout local do cliente cancelar o token de transporte.
    private sealed class ImportReaderTimeoutHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    // Relógio fixo torna a conversão de delta Retry-After determinística sem aguardar tempo real.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
