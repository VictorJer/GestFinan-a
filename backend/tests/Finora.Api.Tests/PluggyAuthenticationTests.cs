using System.Net;
using System.Reflection;
using System.Text;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita autenticação com transporte em memória e opções fictícias, sem acessar a conta real.
[TestClass]
public sealed class PluggyAuthenticationTests
{
    // Configuração incompleta retorna apenas nomes de chaves e não inicia nenhuma chamada HTTP.
    [TestMethod]
    public async Task MissingConfigurationDoesNotCallProvider()
    {
        var handler = new RecordingHandler(_ => throw new AssertFailedException("HTTP inesperado"));
        using var httpClient = CreateHttpClient(handler);
        using var client = new PluggyApiClient(httpClient, new PluggyOptions(null, null, null, null));

        var result = await client.AuthenticateAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.AreEqual(
            new[] { "Pluggy:ClientId", "Pluggy:ClientSecret", "Pluggy:ItemId" },
            result.MissingConfiguration!.ToArray());
        Assert.AreEqual(0, handler.RequestCount);
    }

    // A resposta fica encapsulada no cliente; a key só é aplicada a requests enquanto o escopo existe.
    [TestMethod]
    public async Task AuthenticationKeepsApiKeyInMemoryAndDisposalClearsIt()
    {
        const string apiKey = "sentinel-api-key";
        var handler = new RecordingHandler(async request =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual(new Uri("https://api.pluggy.ai/auth"), request.RequestUri);
            var body = await request.Content!.ReadAsStringAsync();
            StringAssert.Contains(body, "sentinel-client-id");
            StringAssert.Contains(body, "sentinel-client-secret");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"apiKey\":\"{apiKey}\"}}", Encoding.UTF8, "application/json")
            };
        });
        var client = new PluggyApiClient(CreateHttpClient(handler), new PluggyOptions(
            "sentinel-client-id", "sentinel-client-secret", "sentinel-item-id", null));

        var result = await client.AuthenticateAsync(CancellationToken.None);
        using var request = client.CreateAuthenticatedRequest(HttpMethod.Get, "items/item-placeholder");
        var keyIsPresentOnTransportRequest = request.Headers.TryGetValues("X-API-KEY", out var values)
            && values.Single() == apiKey;
        var externalDestinationRejected = false;
        try
        {
            using var externalRequest = client.CreateAuthenticatedRequest(
                HttpMethod.Get, "//attacker.invalid/collect");
        }
        catch (ArgumentException)
        {
            externalDestinationRejected = true;
        }
        client.Dispose();

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(keyIsPresentOnTransportRequest);
        Assert.IsTrue(externalDestinationRejected);
        Assert.IsNull(typeof(PluggyApiClient)
            .GetField("_apiKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client));
        Assert.IsFalse(typeof(PluggyApiClient).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Any(property => property.Name.Contains("Key", StringComparison.OrdinalIgnoreCase)));
    }

    // O cliente real mantém o host documentado em HTTPS e não segue redirects.
    [TestMethod]
    public void DestinationIsFixedAndPrimaryHandlerRejectsRedirects()
    {
        Assert.AreEqual(new Uri("https://api.pluggy.ai/"), PluggyApiClient.BaseAddress);
        using var handler = (HttpClientHandler)PluggyApiClient.CreatePrimaryHandler();
        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.IsFalse(handler.UseCookies);
    }

    // As opções são lidas por chave hierárquica, sem aceitar credenciais de linha de comando.
    [TestMethod]
    public void ExternalConfigurationUsesOnlyPluggySection()
    {
        var values = new Dictionary<string, string?>
        {
            ["Pluggy:ClientId"] = "configured-client",
            ["Pluggy:ClientSecret"] = "configured-secret",
            ["Pluggy:ItemId"] = "configured-item"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var options = PluggyOptions.FromConfiguration(configuration);

        Assert.AreEqual("configured-client", options.ClientId);
        Assert.AreEqual("configured-secret", options.ClientSecret);
        Assert.AreEqual("configured-item", options.ItemId);
        Assert.IsNull(options.AccountId);
    }

    // A composição registra um cliente scoped de execução com destino fixo e opções externalizadas.
    [TestMethod]
    public void RegistrationBuildsTypedClientWithoutValidatingMissingSecrets()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddPluggyApiClient(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        using var client = scope.ServiceProvider.GetRequiredService<PluggyApiClient>();

        Assert.AreEqual(PluggyApiClient.BaseAddress, client.ConfiguredBaseAddress);
    }

    // Cada teste substitui o transporte e nunca acessa serviços externos.
    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = PluggyApiClient.BaseAddress
    };

    // O handler captura somente chamadas fictícias para validar o contrato sem rede.
    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return responseFactory(request);
        }
    }
}
