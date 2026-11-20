using System.Net;
using Finora.Api.Cors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita a allowlist CORS na composição HTTP real sem depender de PostgreSQL ou de um domínio implantado.
[TestClass]
[DoNotParallelize]
public sealed class CorsContractTests
{
    private const string AllowedOrigin = "https://finora.example";

    // Preflight permitido encerra antes da autenticação e autoriza somente o método/cabeçalho contratados.
    [TestMethod]
    public async Task AllowedPreflightReturnsOnlyConfiguredCorsPermissionsWithoutBearer()
    {
        await using var factory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();
        using var request = Preflight(AllowedOrigin, "POST", "content-type,authorization");

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.IsTrue(response.Headers.GetValues("Access-Control-Allow-Methods").Single().Contains("POST", StringComparison.Ordinal));
        var allowedHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").Single();
        Assert.IsTrue(allowedHeaders.Split(',').Any(header => header.Trim().Equals("content-type", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(allowedHeaders.Split(',').Any(header => header.Trim().Equals("authorization", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.IsFalse(response.Headers.Contains("WWW-Authenticate"));
    }

    // Origem não listada recebe resposta sem permissão CORS em vez de eco ou wildcard.
    [TestMethod]
    public async Task UnlistedOriginAndEmptyAllowlistDoNotGrantCorsAccess()
    {
        await using var configuredFactory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var configuredClient = configuredFactory.CreateClient();
        using var denied = await configuredClient.SendAsync(Preflight("https://attacker.example", "POST", "content-type"));
        Assert.AreEqual(HttpStatusCode.NoContent, denied.StatusCode);
        Assert.IsFalse(denied.Headers.Contains("Access-Control-Allow-Origin"));

        await using var closedFactory = new AuthenticationApiFactory();
        using var closedClient = closedFactory.CreateClient();
        using var closed = await closedClient.SendAsync(Preflight(AllowedOrigin, "POST", "content-type"));
        Assert.AreEqual(HttpStatusCode.NoContent, closed.StatusCode);
        Assert.IsFalse(closed.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // O preflight não autoriza cabeçalhos fora da allowlist mesmo quando a origem é válida.
    [TestMethod]
    public async Task PreflightWithUnlistedHeaderDoesNotGrantCorsAccess()
    {
        await using var factory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Preflight(AllowedOrigin, "POST", "x-unapproved-header"));

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.IsFalse(response.Headers.GetValues("Access-Control-Allow-Headers")
            .Any(value => value.Contains("x-unapproved-header", StringComparison.OrdinalIgnoreCase)));
    }

    // Métodos fora do contrato não aparecem como autorizados no preflight, mesmo para uma origem allowlisted.
    [TestMethod]
    public async Task PreflightWithUnlistedMethodDoesNotGrantCorsAccess()
    {
        await using var factory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Preflight(AllowedOrigin, "DELETE", "content-type"));

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.IsFalse(response.Headers.GetValues("Access-Control-Allow-Methods")
            .Any(value => value.Split(',').Any(method => method.Trim().Equals("DELETE", StringComparison.OrdinalIgnoreCase))));
    }

    // Origin nunca substitui Bearer: cliente sem Origin continua desafiado pela autorização fallback.
    [TestMethod]
    public async Task RequestWithoutOriginStillRequiresBearer()
    {
        await using var factory = new AuthenticationApiFactory(ConfigurationWithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/_tests/fallback-authentication-probe");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsTrue(response.Headers.WwwAuthenticate.Any(value => value.Scheme == "Bearer"));
        Assert.AreEqual(0, factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // A resposta HTTP real mantém CORS para a origem aprovada sem remover challenge ou executar a action.
    [TestMethod]
    public async Task AllowedOriginDoesNotBypassAuthenticationOnProtectedRequest()
    {
        await using var factory = new AuthenticationApiFactory(ConfigurationWithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_tests/fallback-authentication-probe");
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.IsTrue(response.Headers.WwwAuthenticate.Any(value => value.Scheme == "Bearer"));
        Assert.AreEqual(0, factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // CORS continua presente em respostas de falha sanitizadas produzidas depois de entrar no pipeline.
    [TestMethod]
    public async Task AllowedOriginReceivesCorsHeadersOnUnauthorizedRateLimitedAndUnexpectedErrors()
    {
        await using var factory = new AuthenticationApiFactory(ConfigurationWithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();

        using var unauthorizedRequest = new HttpRequestMessage(HttpMethod.Get, "/_tests/fallback-authentication-probe");
        unauthorizedRequest.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        using var unauthorized = await client.SendAsync(unauthorizedRequest);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        AssertCorsOrigin(unauthorized);
        Assert.IsTrue(unauthorized.Headers.WwwAuthenticate.Any(value => value.Scheme == "Bearer"));

        using var limitedRequest = new HttpRequestMessage(HttpMethod.Get, "/_tests/exception-probe/rate-limited");
        limitedRequest.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        using var limited = await client.SendAsync(limitedRequest);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertCorsOrigin(limited);

        using var failureRequest = new HttpRequestMessage(HttpMethod.Get, "/_tests/exception-probe/unexpected");
        failureRequest.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        using var failure = await client.SendAsync(failureRequest);
        Assert.AreEqual(HttpStatusCode.InternalServerError, failure.StatusCode);
        AssertCorsOrigin(failure);
    }

    // O endpoint de liveness mantém resposta anônima e recebe CORS somente para a origem configurada.
    [TestMethod]
    public async Task HealthKeepsItsAnonymousContractForConfiguredOrigin()
    {
        await using var factory = new AuthenticationApiFactory(ConfigurationWithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        AssertCorsOrigin(response);
        Assert.AreEqual("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
    }

    // A configuração é rejeitada ao compor a política; curingas, paths e esquemas alheios nunca viram allowlist.
    [TestMethod]
    [DataRow("*")]
    [DataRow("https://app.example.test/path")]
    [DataRow("ftp://app.example.test")]
    public void InvalidOriginConfigurationFailsClosed(string origin)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = origin
        }).Build();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ServiceCollection().AddFinoraCors(configuration));

        Assert.AreEqual("Cors:AllowedOrigins contém origem inválida.", exception.Message);
    }

    // Sem URL real, a configuração vazia não concede origem; exemplos documentais usam domínio reservado.
    [TestMethod]
    public async Task EnvironmentWithoutConfiguredClientUrlKeepsCorsClosed()
    {
        await using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // A configuração dos testes é isolada do ambiente e usa apenas um hostname sintético.
    private static Dictionary<string, string?> WithOrigins(params string[] origins) => origins
        .Select((origin, index) => new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{index}", origin))
        .ToDictionary(pair => pair.Key, pair => pair.Value);

    // Reutiliza somente a configuração sintética de autenticação com porta loopback e acrescenta origens de teste.
    private static Dictionary<string, string?> ConfigurationWithOrigins(params string[] origins)
    {
        var configuration = AuthenticationAvailabilityTests.Configuration();
        foreach (var entry in WithOrigins(origins)) { configuration[entry.Key] = entry.Value; }
        return configuration;
    }

    // Valida exclusivamente a permissão de origem e não aceita credenciais CORS.
    private static void AssertCorsOrigin(HttpResponseMessage response)
    {
        Assert.AreEqual(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    // Monta preflight sem credenciais, simulando apenas os metadados enviados pelo navegador.
    private static HttpRequestMessage Preflight(string origin, string method, string headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", method);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", headers);
        return request;
    }
}
