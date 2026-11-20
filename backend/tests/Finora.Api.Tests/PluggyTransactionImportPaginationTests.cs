using System.Net;
using System.Text;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;

namespace Finora.Api.Tests;

// Exercita a coleta inteira por transporte sintético, sem chamar Pluggy, banco pessoal ou serviço remoto.
[TestClass]
public sealed class PluggyTransactionImportPaginationTests
{
    private const string ItemId = "persisted-item";
    private const string FirstAccount = "credit-card-one";
    private const string SecondAccount = "credit-card-two";
    private static readonly DateOnly DateFrom = new(2025, 11, 20);

    // Todas as contas elegíveis e páginas de movimentos são lidas; next sem dateFrom é enviado sem reescrita.
    [TestMethod]
    public async Task ReadsMultipleAccountPagesAndLiteralContinuationWithoutDateFrom()
    {
        const string next = "?accountId=credit-card-one&after=opaque%2Fcursor";
        var requests = new List<string>();
        var handler = CreateHandler(requests, (path, query) =>
        {
            if (path == "/accounts")
            {
                var page = int.Parse(QueryValue(query, "page")!);
                return page == 1
                    ? AccountsPage(1, 2, Account(FirstAccount), Account("debit-account", "DEBIT", "CHECKING"))
                    : AccountsPage(2, 2, Account(SecondAccount));
            }

            if (path == "/v2/transactions" && query.Contains("accountId=" + FirstAccount, StringComparison.Ordinal))
            {
                return query.Contains("after=", StringComparison.Ordinal)
                    ? TransactionsPage("[]", "null")
                    : TransactionsPage("[" + Transaction("future-installment", FirstAccount, "2026-12-10") + "]", Json(next));
            }

            if (path == "/v2/transactions" && query.Contains("accountId=" + SecondAccount, StringComparison.Ordinal))
            {
                return TransactionsPage("[]", "null");
            }

            throw new AssertFailedException("Request sintético inesperado: " + path + query);
        });
        using var api = CreateApi(handler);
        var reader = new PluggyTransactionImportReader(api, TimeProvider.System);

        var result = await reader.ReadAsync(ItemId, DateFrom, CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, result.Snapshot!.Accounts.Count);
        Assert.AreEqual(1, result.Snapshot.Transactions.Count);
        Assert.AreEqual("future-installment", result.Snapshot.Transactions[0].ExternalId);
        Assert.IsTrue(result.Snapshot.PaginationCompleted);
        var firstPage = requests.Single(request => request.StartsWith("/v2/transactions?accountId=" + FirstAccount, StringComparison.Ordinal)
            && !request.Contains("after=", StringComparison.Ordinal));
        StringAssert.Contains(firstPage, "dateFrom=2025-11-20");
        Assert.IsFalse(firstPage.Contains("dateTo", StringComparison.Ordinal));
        Assert.IsTrue(requests.Contains("/v2/transactions" + next));
    }

    // Uma janela sem movimentos conclui a paginação sem fabricar dados ou erro de ausência de gasto.
    [TestMethod]
    public async Task EmptyPagesProduceCompleteEmptySnapshot()
    {
        var requests = new List<string>();
        var handler = CreateHandler(requests, (path, _) => path switch
        {
            "/accounts" => AccountsPage(1, 1, Account(FirstAccount)),
            "/v2/transactions" => TransactionsPage("[]", "null"),
            _ => throw new AssertFailedException("Request sintético inesperado: " + path)
        });
        using var api = CreateApi(handler);
        var result = await new PluggyTransactionImportReader(api, TimeProvider.System)
            .ReadAsync(ItemId, DateFrom, CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Snapshot!.Transactions.Count);
        Assert.IsTrue(result.Snapshot.PaginationCompleted);
    }

    // Cursor repetido e conta alheia encerram a coleta sem devolver o snapshot parcial.
    [TestMethod]
    public async Task RepeatedCursorAndForeignAccountFailClosed()
    {
        const string repeated = "?accountId=credit-card-one&dateFrom=2025-11-20&after=cycle";
        var cursorRequests = new List<string>();
        var cursorHandler = CreateHandler(cursorRequests, (path, query) => path switch
        {
            "/accounts" => AccountsPage(1, 1, Account(FirstAccount)),
            "/v2/transactions" => query.Contains("after=", StringComparison.Ordinal)
                ? TransactionsPage("[]", Json(repeated))
                : TransactionsPage("[]", Json(repeated)),
            _ => throw new AssertFailedException("Request sintético inesperado: " + path)
        });
        using var cursorApi = CreateApi(cursorHandler);
        var cursorResult = await new PluggyTransactionImportReader(cursorApi, TimeProvider.System)
            .ReadAsync(ItemId, DateFrom, CancellationToken.None);

        Assert.IsFalse(cursorResult.Succeeded);
        Assert.IsNull(cursorResult.Snapshot);
        Assert.AreEqual(TransactionImportFailureCode.InvalidResponse, cursorResult.FailureCode);
        Assert.AreEqual(2, cursorRequests.Count(request => request.StartsWith("/v2/transactions", StringComparison.Ordinal)));

        var foreignHandler = CreateHandler([], (path, _) => path switch
        {
            "/accounts" => AccountsPage(1, 1, Account(FirstAccount)),
            "/v2/transactions" => TransactionsPage("[" + Transaction("foreign", SecondAccount, "2026-01-01") + "]", "null"),
            _ => throw new AssertFailedException("Request sintético inesperado: " + path)
        });
        using var foreignApi = CreateApi(foreignHandler);
        var foreignResult = await new PluggyTransactionImportReader(foreignApi, TimeProvider.System)
            .ReadAsync(ItemId, DateFrom, CancellationToken.None);

        Assert.IsFalse(foreignResult.Succeeded);
        Assert.IsNull(foreignResult.Snapshot);
        Assert.AreEqual(TransactionImportFailureCode.InvalidResponse, foreignResult.FailureCode);
    }

    // Uma falha intermediária ou mais de cem páginas não retorna páginas parciais como snapshot completo.
    [TestMethod]
    public async Task IntermediateFailureAndPageBudgetDoNotReturnPartialSnapshot()
    {
        var failureHandler = CreateHandler([], (path, query) => path switch
        {
            "/accounts" => AccountsPage(1, 1, Account(FirstAccount)),
            "/v2/transactions" when query.Contains("after=", StringComparison.Ordinal)
                => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "/v2/transactions" => TransactionsPage("[]", Json("?accountId=credit-card-one&dateFrom=2025-11-20&after=next")),
            _ => throw new AssertFailedException("Request sintético inesperado: " + path)
        });
        using var failureApi = CreateApi(failureHandler);
        var failed = await new PluggyTransactionImportReader(failureApi, TimeProvider.System)
            .ReadAsync(ItemId, DateFrom, CancellationToken.None);
        Assert.IsFalse(failed.Succeeded);
        Assert.IsNull(failed.Snapshot);
        Assert.AreEqual(TransactionImportFailureCode.ProviderUnavailable, failed.FailureCode);
        Assert.AreEqual(2, failed.PagesRead);
        Assert.AreEqual(0, failed.ReceivedCount);

        var pageCount = 0;
        var budgetHandler = CreateHandler([], (path, _) =>
        {
            if (path == "/accounts") return AccountsPage(1, 1, Account(FirstAccount));
            pageCount++;
            return TransactionsPage("[]", Json($"?accountId={FirstAccount}&dateFrom=2025-11-20&after=cursor-{pageCount}"));
        });
        using var budgetApi = CreateApi(budgetHandler);
        var budget = await new PluggyTransactionImportReader(budgetApi, TimeProvider.System)
            .ReadAsync(ItemId, DateFrom, CancellationToken.None);
        Assert.IsFalse(budget.Succeeded);
        Assert.IsNull(budget.Snapshot);
        Assert.AreEqual(TransactionImportFailureCode.CollectionLimitExceeded, budget.FailureCode);
        Assert.AreEqual(100, pageCount);
    }

    // Compõe autenticação/item sintéticos antes de despachar cada request de contas e movimentos.
    private static ImportScriptHandler CreateHandler(
        List<string> requests,
        Func<string, string, HttpResponseMessage> respond) => new(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;
        requests.Add(path + query);
        if (path == "/auth")
        {
            return JsonResponse("{\"apiKey\":\"synthetic-api-key\"}");
        }

        if (path == "/items/" + ItemId)
        {
            return JsonResponse("{\"status\":\"UPDATED\",\"executionStatus\":\"SUCCESS\",\"consentExpiresAt\":\"2030-01-01T00:00:00Z\",\"error\":null}");
        }

        return respond(path, query);
    });

    // Cria cliente Pluggy sem ItemId global para provar que a identidade vem da conexão persistida.
    private static PluggyApiClient CreateApi(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = PluggyApiClient.BaseAddress },
        new PluggyOptions("synthetic-client", "synthetic-secret", null, null));

    // Retorna resposta JSON de conta paginada com moeda de origem.
    private static HttpResponseMessage AccountsPage(int page, int totalPages, params string[] accounts) =>
        JsonResponse($"{{\"page\":{page},\"total\":{accounts.Length},\"totalPages\":{totalPages},\"results\":[{string.Join(',', accounts)}]}}");

    // Monta somente os campos mínimos permitidos no contrato de cartão.
    private static string Account(string id, string type = "CREDIT", string subtype = "CREDIT_CARD") =>
        $"{{\"id\":\"{id}\",\"itemId\":\"{ItemId}\",\"type\":\"{type}\",\"subtype\":\"{subtype}\",\"currencyCode\":\"BRL\"}}";

    // Mantém movimento sintético com identidade, conta, valor, moeda e data obrigatórios.
    private static string Transaction(string id, string accountId, string date) =>
        $"{{\"id\":\"{id}\",\"accountId\":\"{accountId}\",\"description\":\"synthetic\",\"amount\":1.25,\"currencyCode\":\"BRL\",\"date\":\"{date}\",\"type\":\"DEBIT\",\"status\":\"PENDING\",\"operationType\":\"PAGAMENTO\"}}";

    // Mantém next como texto JSON para testar o transporte literal do cursor.
    private static HttpResponseMessage TransactionsPage(string results, string next) =>
        JsonResponse($"{{\"results\":{results},\"next\":{next}}}");

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string? QueryValue(string query, string key) => query.TrimStart('?').Split('&')
        .Select(part => part.Split('=', 2))
        .Where(parts => parts.Length == 2 && parts[0] == key)
        .Select(parts => Uri.UnescapeDataString(parts[1]))
        .SingleOrDefault();

    // Handler restrito a fixtures, sem encaminhamento de requests para rede externa.
    private sealed class ImportScriptHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
