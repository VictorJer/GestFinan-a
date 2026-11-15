using System.Net;
using System.Text;
using System.Text.Json;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida uma única página cursor-based e o resumo verdadeiro sem guardar ou imprimir transações.
[TestClass]
public sealed class PluggyLocalReaderTests
{
    // A resposta válida conta registros, indica continuação e não segue nem retorna o cursor opaco.
    [TestMethod]
    public async Task ValidPageReportsCountAndContinuationWithoutFollowingCursor()
    {
        const string cursor = "?accountId=card-test&after=opaque-cursor-sentinel";
        using var rig = CreateReader(TransactionsPage(
            "{\"id\":\"transaction-test\",\"accountId\":\"card-test\"}",
            JsonSerializer.Serialize(cursor)));

        var result = await rig.Reader.ReadAsync(CancellationToken.None);

        Assert.AreEqual(PluggyLocalReadOutcome.TransactionsRead, result.Outcome);
        Assert.AreEqual(1, result.TransactionCount);
        Assert.IsTrue(result.HasMoreTransactions);
        Assert.AreEqual(4, rig.RequestUris.Count);
        StringAssert.Contains(rig.RequestUris[3].AbsolutePath, "/v2/transactions");
        StringAssert.Contains(rig.RequestUris[3].Query, "accountId=card-test");
        Assert.IsFalse(rig.RequestUris[3].Query.Contains("after=", StringComparison.Ordinal));
    }

    // `next: null` encerra a amostra atual, enquanto a página vazia permanece inconclusiva.
    [TestMethod]
    public async Task NullContinuationIsValidAndEmptyPageIsInconclusive()
    {
        using var lastPage = CreateReader(TransactionsPage(
            "{\"id\":\"transaction-test\",\"accountId\":\"card-test\"}", "null"));
        var lastResult = await lastPage.Reader.ReadAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadOutcome.TransactionsRead, lastResult.Outcome);
        Assert.IsFalse(lastResult.HasMoreTransactions);

        using var empty = CreateReader(TransactionsPage(string.Empty, "null"));
        var emptyResult = await empty.Reader.ReadAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadOutcome.NoTransactions, emptyResult.Outcome);
        Assert.AreEqual(0, emptyResult.TransactionCount);
    }

    // Envelope ausente, cursor incompatível e JSON inválido são falhas de contrato, não sucesso vazio.
    [TestMethod]
    public async Task InvalidEnvelopeCursorOrJsonFailsClosed()
    {
        using var missingNext = CreateReader(
            "{\"results\":[{\"id\":\"transaction-test\",\"accountId\":\"card-test\"}]} ");
        var missingNextResult = await missingNext.Reader.ReadAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidResponse, missingNextResult.FailureCategory);

        using var invalidNext = CreateReader(TransactionsPage(
            "{\"id\":\"transaction-test\",\"accountId\":\"card-test\"}", "123"));
        var invalidNextResult = await invalidNext.Reader.ReadAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidResponse, invalidNextResult.FailureCategory);

        using var invalidJson = CreateReader("{not-json");
        var invalidJsonResult = await invalidJson.Reader.ReadAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidResponse, invalidJsonResult.FailureCategory);
    }

    // Lançamentos de outra conta ou sem identidade opaca não podem comprovar leitura do cartão.
    [TestMethod]
    public async Task TransactionsMustBelongToSelectedAccountAndHaveIds()
    {
        using var wrongAccount = CreateReader(TransactionsPage(
            "{\"id\":\"transaction-test\",\"accountId\":\"other-card\"}", "null"));
        var wrongAccountResult = await wrongAccount.Reader.ReadAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidResponse, wrongAccountResult.FailureCategory);

        using var missingId = CreateReader(TransactionsPage(
            "{\"accountId\":\"card-test\"}", "null"));
        var missingIdResult = await missingId.Reader.ReadAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidResponse, missingIdResult.FailureCategory);
    }

    // A cadeia simulada mantém o mesmo cliente autenticado entre item, contas e transações.
    private static ReaderRig CreateReader(string transactionsResponse)
    {
        var responses = new Queue<string>([
            "{\"apiKey\":\"test-api-key\"}",
            "{\"status\":\"UPDATED\",\"executionStatus\":\"SUCCESS\",\"consentExpiresAt\":null,\"error\":null}",
            "{\"page\":1,\"total\":1,\"totalPages\":1,\"results\":[{\"id\":\"card-test\",\"type\":\"CREDIT\",\"subtype\":\"CREDIT_CARD\",\"itemId\":\"test-item\"}]}",
            transactionsResponse
        ]);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json")
        });
        var client = new PluggyApiClient(
            new HttpClient(handler) { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("client-id", "client-secret", "test-item", null));
        var inspector = new PluggyConnectionInspector(client);
        return new ReaderRig(new PluggyLocalReader(inspector, client), client, handler.RequestUris);
    }

    // Serializa somente campos de teste e não inclui conteúdo de compras.
    private static string TransactionsPage(string transaction, string next) =>
        $"{{\"results\":[{transaction}],\"next\":{next}}}";

    // Mantém respostas e URLs simuladas em memória e nunca abre conexão de rede.
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    // Descarta o cliente scoped e a API key simulada após cada execução.
    private sealed class ReaderRig(
        PluggyLocalReader reader,
        PluggyApiClient client,
        List<Uri> requestUris) : IDisposable
    {
        public PluggyLocalReader Reader { get; } = reader;
        public List<Uri> RequestUris { get; } = requestUris;
        public void Dispose() => client.Dispose();
    }
}
