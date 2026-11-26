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
    private static readonly string[] SyncIntentPaths =
    [
        "/api/connections/current/sync",
        "/api/connections/current/sync/on-entry"
    ];

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

    // A política base preserva os métodos e cabeçalhos anteriores sem herdar a exceção de Idempotency-Key.
    [TestMethod]
    public async Task BasePolicyPreservesPreviouslyAllowedMethodsAndHeaders()
    {
        await using var factory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();

        foreach (var method in new[] { "GET", "POST", "PATCH" })
        {
            using var response = await client.SendAsync(Preflight(
                AllowedOrigin, method, "authorization,content-type", "/_tests/fallback-authentication-probe"));

            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.AreEqual(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.IsTrue(response.Headers.GetValues("Access-Control-Allow-Methods").Single()
                .Split(',').Any(value => value.Trim().Equals(method, StringComparison.OrdinalIgnoreCase)));
            CollectionAssert.AreEquivalent(
                new[] { "authorization", "content-type" }, ReadAllowedHeaders(response));
            Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Credentials"));
        }
    }

    // Os dois POSTs contratados recebem a chave no preflight sem Bearer e sem ampliar outras permissões.
    [TestMethod]
    public async Task SyncIntentPreflightsGrantIdempotencyKeyOnlyForTheExactPostRoutes()
    {
        await using var factory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();

        foreach (var path in SyncIntentPaths)
        {
            using var response = await client.SendAsync(
                Preflight(AllowedOrigin, "POST", "authorization,idempotency-key", path));

            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.AreEqual(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.IsTrue(response.Headers.GetValues("Access-Control-Allow-Methods").Single()
                .Split(',').Any(method => method.Trim().Equals("POST", StringComparison.OrdinalIgnoreCase)));
            var allowedHeaders = ReadAllowedHeaders(response);
            CollectionAssert.AreEquivalent(
                new[] { "authorization", "content-type", "idempotency-key" }, allowedHeaders);
            Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Credentials"));
            Assert.IsFalse(response.Headers.Contains("WWW-Authenticate"));
        }
    }

    // Origem não listada recebe resposta sem permissão CORS em vez de eco ou wildcard.
    [TestMethod]
    public async Task UnlistedOriginAndEmptyAllowlistDoNotGrantCorsAccess()
    {
        await using var configuredFactory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var configuredClient = configuredFactory.CreateClient();
        using var denied = await configuredClient.SendAsync(Preflight(
            "https://attacker.example", "POST", "authorization,idempotency-key", SyncIntentPaths[0]));
        Assert.AreEqual(HttpStatusCode.NoContent, denied.StatusCode);
        Assert.IsFalse(denied.Headers.Contains("Access-Control-Allow-Origin"));

        await using var closedFactory = new AuthenticationApiFactory();
        using var closedClient = closedFactory.CreateClient();
        using var closed = await closedClient.SendAsync(Preflight(
            AllowedOrigin, "POST", "authorization,idempotency-key", SyncIntentPaths[1]));
        Assert.AreEqual(HttpStatusCode.NoContent, closed.StatusCode);
        Assert.IsFalse(closed.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // A exceção admite somente cabeçalhos contratados e não transforma a política em AllowAnyHeader.
    [TestMethod]
    public async Task SyncIntentPreflightDoesNotGrantAnAdditionalUnknownHeader()
    {
        await using var factory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Preflight(
            AllowedOrigin, "POST", "authorization,idempotency-key,x-unapproved-header", SyncIntentPaths[1]));

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        CollectionAssert.AreEquivalent(
            new[] { "authorization", "content-type", "idempotency-key" }, ReadAllowedHeaders(response));
        Assert.IsFalse(ReadAllowedHeaders(response).Contains("x-unapproved-header", StringComparer.OrdinalIgnoreCase));
    }

    // Métodos não listados continuam negados mesmo quando o caminho coincide com uma intenção de sincronização.
    [TestMethod]
    public async Task PreflightWithUnlistedMethodDoesNotGrantCorsAccess()
    {
        await using var factory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Preflight(
            AllowedOrigin, "DELETE", "authorization,idempotency-key", SyncIntentPaths[0]));

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.IsFalse(response.Headers.GetValues("Access-Control-Allow-Methods")
            .Any(value => value.Split(',').Any(method => method.Trim().Equals("DELETE", StringComparison.OrdinalIgnoreCase))));
        Assert.IsFalse(ReadAllowedHeaders(response).Contains("idempotency-key", StringComparer.OrdinalIgnoreCase));
    }

    // Rotas vizinhas e métodos permitidos pela base não herdam a exceção específica de Idempotency-Key.
    [TestMethod]
    public async Task IdempotencyHeaderIsNotGrantedForSimilarRoutesOrOtherAllowedMethods()
    {
        await using var factory = new AuthenticationApiFactory(WithOrigins(AllowedOrigin));
        using var client = factory.CreateClient();

        foreach (var path in new[]
                 {
                     "/api/connections/current/sync-state",
                     "/api/connections/current/sync/on-entry-extra",
                     "/api/connections/current/sync/"
                 })
        {
            using var response = await client.SendAsync(
                Preflight(AllowedOrigin, "POST", "authorization,idempotency-key", path));

            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.IsFalse(ReadAllowedHeaders(response).Contains("idempotency-key", StringComparer.OrdinalIgnoreCase));
        }

        using var patchResponse = await client.SendAsync(
            Preflight(AllowedOrigin, "PATCH", "authorization,idempotency-key", SyncIntentPaths[0]));

        Assert.AreEqual(HttpStatusCode.NoContent, patchResponse.StatusCode);
        Assert.IsTrue(patchResponse.Headers.GetValues("Access-Control-Allow-Methods").Single()
            .Split(',').Any(method => method.Trim().Equals("PATCH", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(ReadAllowedHeaders(patchResponse).Contains("idempotency-key", StringComparer.OrdinalIgnoreCase));
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

    // Monta preflight sem credenciais e permite variar a rota para provar o despacho exato da política.
    private static HttpRequestMessage Preflight(
        string origin,
        string method,
        string headers,
        string path = "/api/auth/login")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", method);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", headers);
        return request;
    }

    // Normaliza os cabeçalhos concedidos para comparar o conjunto, sem depender de ordem/capitalização.
    private static string[] ReadAllowedHeaders(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Headers", out var values)
            ? values.SelectMany(value => value.Split(','))
                .Select(header => header.Trim().ToLowerInvariant())
                .Where(header => header.Length > 0)
                .ToArray()
            : [];
}
