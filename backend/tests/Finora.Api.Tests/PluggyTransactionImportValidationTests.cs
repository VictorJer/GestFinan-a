using System.Net;
using System.Text;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;

namespace Finora.Api.Tests;

// Verifica que o leitor rejeita conjuntos inválidos antes de entregar qualquer snapshot ao caso de uso.
[TestClass]
public sealed class PluggyTransactionImportValidationTests
{
    private const string ItemId = "persisted-item";
    private const string AccountId = "credit-card";
    private static readonly DateOnly DateFrom = new(2025, 11, 20);

    // Campos-base ausentes não recebem valores substitutos e impedem publicação do snapshot.
    [TestMethod]
    [DataRow("{\"id\":\"tx\",\"accountId\":\"credit-card\",\"amount\":1.25,\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\"}")]
    [DataRow("{\"id\":\"tx\",\"accountId\":\"credit-card\",\"description\":\"synthetic\",\"amount\":1.25,\"date\":\"2026-01-01\"}")]
    [DataRow("{\"id\":\"tx\",\"accountId\":\"credit-card\",\"description\":\"synthetic\",\"amount\":null,\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\"}")]
    public async Task MissingBaseTransactionFieldsRejectWholeSnapshot(string transaction)
    {
        using var api = CreateApi(new ValidationHandler(path => path switch
        {
            "/accounts" => AccountsPage(),
            "/v2/transactions" => TransactionsPage("[" + transaction + "]", "null"),
            _ => throw new AssertFailedException("Request sintético inesperado: " + path)
        }));

        var result = await new PluggyTransactionImportReader(api, TimeProvider.System)
            .ReadAsync(ItemId, DateFrom, CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Snapshot);
        Assert.AreEqual(TransactionImportFailureCode.InvalidResponse, result.FailureCode);
    }

    // Valor sem escala exata ou fora de numeric(19,4) é rejeitado antes de qualquer persistência.
    [TestMethod]
    [DataRow("1.23456")]
    [DataRow("1000000000000000")]
    public async Task UnsupportedAmountPrecisionRejectsWholeSnapshot(string amount)
    {
        var transaction = Transaction("tx", AccountId, amount);
        using var api = CreateApi(new ValidationHandler(path => path switch
        {
            "/accounts" => AccountsPage(),
            "/v2/transactions" => TransactionsPage("[" + transaction + "]", "null"),
            _ => throw new AssertFailedException("Request sintético inesperado: " + path)
        }));

        var result = await new PluggyTransactionImportReader(api, TimeProvider.System)
            .ReadAsync(ItemId, DateFrom, CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Snapshot);
        Assert.AreEqual(TransactionImportFailureCode.InvalidResponse, result.FailureCode);
    }

    // O mesmo ID contextual com payload financeiro contraditório não é resolvido por ordem de chegada.
    [TestMethod]
    public async Task ContradictoryDuplicateAcrossPagesRejectsWholeSnapshot()
    {
        using var api = CreateApi(new ValidationHandler((path, query) => path switch
        {
            "/accounts" => AccountsPage(),
            "/v2/transactions" when query.Contains("after=", StringComparison.Ordinal)
                => TransactionsPage("[" + Transaction("same-id", AccountId, "2.50") + "]", "null"),
            "/v2/transactions" => TransactionsPage("[" + Transaction("same-id", AccountId, "1.25") + "]",
                "\"?accountId=credit-card&dateFrom=2025-11-20&after=page-two\""),
            _ => throw new AssertFailedException("Request sintético inesperado: " + path)
        }));

        var result = await new PluggyTransactionImportReader(api, TimeProvider.System)
            .ReadAsync(ItemId, DateFrom, CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Snapshot);
        Assert.AreEqual(TransactionImportFailureCode.InvalidResponse, result.FailureCode);
    }

    // Configura somente autenticação e item sintéticos, sem exigir ItemId global nem consultar rede externa.
    private static PluggyApiClient CreateApi(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = PluggyApiClient.BaseAddress },
        new PluggyOptions("synthetic-client", "synthetic-secret", null, null));

    private static HttpResponseMessage AccountsPage() => JsonResponse(
        $"{{\"page\":1,\"total\":1,\"totalPages\":1,\"results\":[{{\"id\":\"{AccountId}\",\"itemId\":\"{ItemId}\",\"type\":\"CREDIT\",\"subtype\":\"CREDIT_CARD\",\"currencyCode\":\"BRL\"}}]}}");

    // Preserva o valor literal para testar escala e faixa sem conversão anterior na fixture.
    private static string Transaction(string id, string accountId, string amount) =>
        $"{{\"id\":\"{id}\",\"accountId\":\"{accountId}\",\"description\":\"synthetic\",\"amount\":{amount},\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\"}}";

    private static HttpResponseMessage TransactionsPage(string transactions, string next) =>
        JsonResponse($"{{\"results\":{transactions},\"next\":{next}}}");

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    // Insere envelopes comuns e delega apenas respostas sintéticas específicas do teste.
    private sealed class ValidationHandler(Func<string, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/auth")
            {
                return Task.FromResult(JsonResponse("{\"apiKey\":\"synthetic-key\"}"));
            }

            if (path == "/items/" + ItemId)
            {
                return Task.FromResult(JsonResponse("{\"status\":\"UPDATED\",\"executionStatus\":\"SUCCESS\",\"consentExpiresAt\":\"2030-01-01T00:00:00Z\",\"error\":null}"));
            }

            return Task.FromResult(respond(path, request.RequestUri.Query));
        }

        public ValidationHandler(Func<string, HttpResponseMessage> respond) : this((path, _) => respond(path)) { }
    }
}
