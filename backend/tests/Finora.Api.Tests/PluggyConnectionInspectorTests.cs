using System.Net;
using System.Text;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida item/consentimento e seleção paginada com envelopes artificiais, sem credenciais reais.
[TestClass]
public sealed class PluggyConnectionInspectorTests
{
    // A data de expiração vencida bloqueia a leitura mesmo com status e execução previamente exitosos.
    [TestMethod]
    public async Task ExpiredConsentBlocksPreviouslySuccessfulItem()
    {
        using var rig = CreateRig(Item("UPDATED", "SUCCESS", DateTimeOffset.UtcNow.AddMinutes(-1)));

        var result = await rig.Inspector.InspectAsync(CancellationToken.None);

        Assert.IsFalse(result.IsReady);
        Assert.AreEqual(PluggyLocalReadOutcome.ConnectionBlocked, result.FailureOutcome);
        Assert.AreEqual(PluggyLocalReadFailureCategory.ConsentExpired, result.FailureCategory);
        Assert.AreEqual(PluggyConsentExpiryCheck.Expired, result.ConsentExpiryCheck);
        Assert.AreEqual(2, rig.RequestUris.Count);
    }

    // Sem validade reportada, o status não prova consentimento perpétuo, mas a leitura pode continuar.
    [TestMethod]
    public async Task MissingConsentExpiryIsSignaledAndUniqueCardCanBeSelected()
    {
        using var rig = CreateRig(
            Item("UPDATED", "SUCCESS", null),
            AccountsPage(1, 1, Account("card-1", "CREDIT", "CREDIT_CARD")));

        var result = await rig.Inspector.InspectAsync(CancellationToken.None);

        Assert.IsTrue(result.IsReady);
        Assert.AreEqual("card-1", result.AccountId);
        Assert.AreEqual(PluggyConsentExpiryCheck.Unavailable, result.ConsentExpiryCheck);
    }

    // Estados desconhecidos ou erro do provedor não são inferidos como conexão apta.
    [TestMethod]
    public async Task UnknownOrErroredItemStateFailsClosed()
    {
        using var unknown = CreateRig(Item("WAITING_USER_INPUT", "SUCCESS", null));
        var unknownResult = await unknown.Inspector.InspectAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.UnsupportedState, unknownResult.FailureCategory);
        Assert.AreEqual(2, unknown.RequestUris.Count);

        using var errored = CreateRig(Item("UPDATED", "SUCCESS", null, hasError: true));
        var erroredResult = await errored.Inspector.InspectAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.UnsupportedState, erroredResult.FailureCategory);
        Assert.AreEqual(2, errored.RequestUris.Count);
    }

    // Enumera todas as páginas declaradas antes de escolher o único cartão pelo tipo contratual.
    [TestMethod]
    public async Task CardOnLaterPageIsFoundAfterBankAccountOnFirstPage()
    {
        using var rig = CreateRig(
            Item("UPDATED", "SUCCESS", DateTimeOffset.UtcNow.AddHours(1)),
            AccountsPage(1, 2, Account("bank-1", "BANK", "CHECKING_ACCOUNT")),
            AccountsPage(2, 2, Account("card-2", "CREDIT", "CREDIT_CARD")));

        var result = await rig.Inspector.InspectAsync(CancellationToken.None);

        Assert.IsTrue(result.IsReady);
        Assert.AreEqual("card-2", result.AccountId);
        Assert.AreEqual(4, rig.RequestUris.Count);
        StringAssert.Contains(rig.RequestUris[2].Query, "page=1");
        StringAssert.Contains(rig.RequestUris[3].Query, "page=2");
    }

    // Ausência ou multiplicidade de cartões não permite uma escolha silenciosa.
    [TestMethod]
    public async Task MissingOrAmbiguousCardIsRejected()
    {
        using var missing = CreateRig(
            Item("UPDATED", "SUCCESS", null),
            AccountsPage(1, 1, Account("bank-1", "BANK", "CHECKING_ACCOUNT")));
        var missingResult = await missing.Inspector.InspectAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidAccountSelection, missingResult.FailureCategory);

        using var ambiguous = CreateRig(
            Item("UPDATED", "SUCCESS", null),
            AccountsPage(1, 1,
                Account("card-1", "CREDIT", "CREDIT_CARD"),
                Account("card-2", "CREDIT", "CREDIT_CARD")));
        var ambiguousResult = await ambiguous.Inspector.InspectAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidAccountSelection, ambiguousResult.FailureCategory);
    }

    // Um AccountId configurado só é aceito se corresponder a uma conta CREDIT/CREDIT_CARD do item.
    [TestMethod]
    public async Task ConfiguredAccountMustBelongToItemAndBeCreditCard()
    {
        using var foreign = CreateRig(
            Item("UPDATED", "SUCCESS", null),
            AccountsPage(1, 1, Account("card-1", "CREDIT", "CREDIT_CARD")),
            configuredAccountId: "foreign-card");
        var foreignResult = await foreign.Inspector.InspectAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidAccountSelection, foreignResult.FailureCategory);

        using var bank = CreateRig(
            Item("UPDATED", "SUCCESS", null),
            AccountsPage(1, 1, Account("bank-1", "BANK", "CHECKING_ACCOUNT")),
            configuredAccountId: "bank-1");
        var bankResult = await bank.Inspector.InspectAsync(CancellationToken.None);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidAccountSelection, bankResult.FailureCategory);
    }

    // Uma conta retornada com itemId diferente viola o envelope e não entra na seleção.
    [TestMethod]
    public async Task AccountFromDifferentItemIsRejected()
    {
        using var rig = CreateRig(
            Item("UPDATED", "SUCCESS", null),
            AccountsPage(1, 1, Account("card-1", "CREDIT", "CREDIT_CARD", "other-item")));

        var result = await rig.Inspector.InspectAsync(CancellationToken.None);

        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidResponse, result.FailureCategory);
    }

    // Cria uma cadeia de respostas JSON fictícias com a ordem de requests prevista pelo contrato.
    private static TestRig CreateRig(
        string? itemResponse,
        string? firstAccountPage = null,
        string? secondAccountPage = null,
        string? configuredAccountId = null)
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(JsonResponse("{\"apiKey\":\"test-api-key\"}"));
        if (itemResponse is not null) responses.Enqueue(JsonResponse(itemResponse));
        if (firstAccountPage is not null) responses.Enqueue(JsonResponse(firstAccountPage));
        if (secondAccountPage is not null) responses.Enqueue(JsonResponse(secondAccountPage));

        var handler = new RecordingHandler(_ => responses.Dequeue());
        var client = new PluggyApiClient(
            new HttpClient(handler) { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("client-id", "client-secret", "test-item", configuredAccountId));
        return new TestRig(new PluggyConnectionInspector(client), client, handler.RequestUris);
    }

    // Monta o menor item necessário para validar estado e consentimento, sem dados pessoais.
    private static string Item(
        string status,
        string executionStatus,
        DateTimeOffset? consentExpiresAt,
        bool hasError = false)
    {
        var expiry = consentExpiresAt is null ? "null" : $"\"{consentExpiresAt:O}\"";
        var error = hasError ? "{\"code\":\"SENTINEL\"}" : "null";
        return $"{{\"status\":\"{status}\",\"executionStatus\":\"{executionStatus}\",\"consentExpiresAt\":{expiry},\"error\":{error}}}";
    }

    // Monta envelopes paginados e somente os campos de classificação e vínculo da conta.
    private static string AccountsPage(int page, int totalPages, params string[] accounts) =>
        $"{{\"page\":{page},\"total\":{accounts.Length},\"totalPages\":{totalPages},\"results\":[{string.Join(',', accounts)}]}}";

    private static string Account(
        string id,
        string type,
        string subtype,
        string itemId = "test-item") =>
        $"{{\"id\":\"{id}\",\"type\":\"{type}\",\"subtype\":\"{subtype}\",\"itemId\":\"{itemId}\"}}";

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    // Mantém respostas e destinos apenas na memória do teste.
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

    // Descarta o cliente em cada caso para não prolongar a vida da API key simulada.
    private sealed class TestRig(
        PluggyConnectionInspector inspector,
        PluggyApiClient client,
        List<Uri> requestUris) : IDisposable
    {
        public PluggyConnectionInspector Inspector { get; } = inspector;
        public List<Uri> RequestUris { get; } = requestUris;
        public void Dispose() => client.Dispose();
    }
}
