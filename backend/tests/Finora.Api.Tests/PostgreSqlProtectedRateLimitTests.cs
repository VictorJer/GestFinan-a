using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Finora.Api.Authentication;
using Finora.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confirma no host real que a política protegida usa titular validado e não atravessa instâncias.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlProtectedRateLimitTests
{
    private const string AllowedOrigin = "https://finora.example";

    // 429 não executa a action; outro titular e outra instância mantêm partições independentes.
    [TestMethod]
    public async Task ProtectedLimitPartitionsByOwnerAndInstanceWithoutRunningRejectedAction()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(apiConfiguration: Configuration());
        await fixture.SeedUserAsync();
        await fixture.SeedUserAsync("second@example.invalid");
        var firstToken = await CreateSessionTokenAsync(fixture, await FindUserIdAsync(fixture, "owner@example.invalid"));
        var secondToken = await CreateSessionTokenAsync(fixture, await FindUserIdAsync(fixture, "second@example.invalid"));
        var actionCounter = fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>();

        using var firstOwnerRequest = await ProtectedAsync(fixture.Client, firstToken);
        Assert.AreEqual(HttpStatusCode.OK, firstOwnerRequest.StatusCode);
        using var firstOwnerExcess = await ProtectedAsync(fixture.Client, firstToken, includeOrigin: true);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, firstOwnerExcess.StatusCode);
        await AssertRateLimitedAsync(firstOwnerExcess);
        Assert.AreEqual(AllowedOrigin, firstOwnerExcess.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.AreEqual(1, actionCounter.Calls);

        using var otherOwnerRequest = await ProtectedAsync(fixture.Client, secondToken);
        Assert.AreEqual(HttpStatusCode.OK, otherOwnerRequest.StatusCode);
        Assert.AreEqual(2, actionCounter.Calls);

        // Uma sessão válida em outro host não compartilha memória/contagem com a primeira instância.
        await using var secondInstance = fixture.NewFactory();
        using var secondClient = secondInstance.CreateClient();
        using var secondInstanceRequest = await ProtectedAsync(secondClient, firstToken);
        Assert.AreEqual(HttpStatusCode.OK, secondInstanceRequest.StatusCode);
        Assert.AreEqual(1, secondInstance.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);

        // A indisponibilidade de validação de sessão e um Bearer inválido continuam antes do limitador.
        using var invalidToken = await ProtectedAsync(fixture.Client, "invalid-token-sentinel");
        await PostgreSqlBearerTests.AssertChallengeAsync(invalidToken);
        Assert.AreEqual(2, actionCounter.Calls);

        var unavailableConfiguration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Finora"] = "Host=127.0.0.1;Port=1;Database=finora_integration_test;Username=unavailable;Password=unavailable;Timeout=1;Pooling=false"
        };
        await using var unavailableHost = fixture.NewFactory(apiConfiguration: unavailableConfiguration);
        using var unavailableClient = unavailableHost.CreateClient();
        using var unavailable = await ProtectedAsync(unavailableClient, firstToken);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.IsFalse(unavailable.Headers.Contains("WWW-Authenticate"));

        // Health e preflight não consomem a política autenticada mesmo após o titular exceder seu limite.
        for (var index = 0; index < 2; index++)
        {
            using var health = await fixture.Client.GetAsync("/health");
            Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        }
        using var preflight = Preflight();
        using var preflightResponse = await fixture.Client.SendAsync(preflight);
        Assert.AreEqual(HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.AreEqual(AllowedOrigin, preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    // Usa um único permit/minuto para tornar partições e rejeições determinísticas sem aguardar relógio real.
    private static Dictionary<string, string?> Configuration() => new()
    {
        ["RateLimiting:Protected:PermitLimit"] = "1",
        ["RateLimiting:Protected:WindowSeconds"] = "60",
        ["Cors:AllowedOrigins:0"] = AllowedOrigin
    };

    // A emissão sintética evita o fluxo de login, que deliberadamente recusa banco com múltiplos titulares.
    private static async Task<string> CreateSessionTokenAsync(PostgreSqlAuthenticationFixture fixture, Guid userId)
    {
        var createdAt = fixture.Clock.GetUtcNow();
        var session = AuthSession.Create(AuthSession.GenerateId(), userId, createdAt, createdAt.AddSeconds(1800));
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.Add(session);
        await fixture.Context.SaveChangesAsync();
        var settings = fixture.Factory.Services.GetRequiredService<JwtSettings>();
        return new JwtAccessTokenIssuer(settings).Issue(userId, session.Id, session.CreatedAtUtc, session.ExpiresAtUtc).AccessToken;
    }

    // Busca somente IDs sintéticos para associar tokens independentes aos dois titulares de fixture.
    private static async Task<Guid> FindUserIdAsync(PostgreSqlAuthenticationFixture fixture, string email)
    {
        fixture.Context.ChangeTracker.Clear();
        return await fixture.Context.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
    }

    // Cabeçalhos são anexados por requisição para não compartilhar estado entre clientes.
    private static Task<HttpResponseMessage> ProtectedAsync(HttpClient client, string token, bool includeOrigin = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/_tests/fallback-authentication-probe");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (includeOrigin) { request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin); }
        return client.SendAsync(request);
    }

    // Valida o código estável de 429 sem expor claims, identificadores ou dados da sessão.
    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("RATE_LIMITED", problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(problem.RootElement.TryGetProperty("subject", out _));
    }

    // Preflight permitido continua anônimo e não depende da política de chamadas autenticadas.
    private static HttpRequestMessage Preflight()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type");
        return request;
    }
}
