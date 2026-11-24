using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita a rota real com sessão e persistência PostgreSQL descartável, sem chamar provedor financeiro.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlCardsApiTests
{
    private static readonly Guid ConnectionId = Guid.Parse("f70c5b3a-038a-4786-ad56-483840d80680");
    private static readonly Guid SecondConnectionId = Guid.Parse("b285cbcf-2177-48fc-83cb-55e6cd902954");
    private static readonly Guid FirstCardId = Guid.Parse("a5a35003-cd81-4f07-9563-5710a034b78a");
    private static readonly Guid SecondCardId = Guid.Parse("e3f88d8b-1714-4fe6-b36e-d8d0cc37783f");

    // Challenge anônimo preserva Bearer/Problem Details; sessão válida lê lista vazia e aplica o limite protegido.
    [TestMethod]
    public async Task CardsRouteRequiresBearerReturnsEmptyListAndUsesProtectedRateLimit()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(
            apiConfiguration: RateLimitConfiguration());
        var userId = await fixture.SeedUserAsync();
        await AddConnectionAsync(fixture.Context, userId, ConnectionId, "cards-item");

        using var anonymous = await GetCardsAsync(fixture.Client);
        await PostgreSqlBearerTests.AssertChallengeAsync(anonymous);

        var token = await fixture.TokenAsync();
        using var empty = await GetCardsAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);
        using (var body = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()))
        {
            Assert.AreEqual(JsonValueKind.Array, body.RootElement.ValueKind);
            Assert.AreEqual(0, body.RootElement.GetArrayLength());
        }

        using var limited = await GetCardsAsync(fixture.Client, token);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    // Conexão ausente é um erro de recurso próprio com o envelope fixo, não uma lista de cartões vazia.
    [TestMethod]
    public async Task MissingOwnedConnectionReturnsSanitizedNotFound()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();

        using var response = await GetCardsAsync(fixture.Client, token);

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // Duas conexões próprias produzem conflito tipado em vez de escolher uma delas silenciosamente.
    [TestMethod]
    public async Task AmbiguousOwnedConnectionsReturnSanitizedConflict()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var userId = await fixture.SeedUserAsync();
        await AddConnectionAsync(fixture.Context, userId, ConnectionId, "cards-item-one");
        await AddConnectionAsync(fixture.Context, userId, SecondConnectionId, "cards-item-two");
        var token = await fixture.TokenAsync();

        using var response = await GetCardsAsync(fixture.Client, token);

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.Conflict);
    }

    // A serialização contém somente ID interno, campos disponíveis e moeda; dados de origem e limites não escapam.
    [TestMethod]
    public async Task ResponseContainsOnlyAllowlistedCardFieldsAndKeepsOptionalValuesNull()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var userId = await fixture.SeedUserAsync();
        await AddConnectionAsync(fixture.Context, userId, ConnectionId, "external-item-sentinel");
        fixture.Context.Set<CardAccount>().AddRange(
            CardAccount.Create(FirstCardId, userId, ConnectionId, "external-card-sentinel", "BRL", "Cartão principal", "1234", 900m, 1200m),
            CardAccount.Create(SecondCardId, userId, ConnectionId, "external-card-second", "USD"));
        await fixture.Context.SaveChangesAsync();
        var token = await fixture.TokenAsync();

        using var response = await GetCardsAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        Assert.AreEqual(2, body.RootElement.GetArrayLength());
        var first = body.RootElement.EnumerateArray().Single(card =>
            card.GetProperty("id").GetGuid() == FirstCardId);
        CollectionAssert.AreEquivalent(
            new[] { "id", "name", "lastFour", "currencyCode" },
            first.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("Cartão principal", first.GetProperty("name").GetString());
        Assert.AreEqual("1234", first.GetProperty("lastFour").GetString());
        Assert.AreEqual("BRL", first.GetProperty("currencyCode").GetString());
        var second = body.RootElement.EnumerateArray().Single(card =>
            card.GetProperty("id").GetGuid() == SecondCardId);
        Assert.AreEqual(JsonValueKind.Null, second.GetProperty("name").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, second.GetProperty("lastFour").ValueKind);
        Assert.IsFalse(json.Contains("external-item-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("external-card-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("availableCreditLimit", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("totalCreditLimit", StringComparison.Ordinal));
    }

    // Configuração mínima força 429 na segunda leitura autenticada, sem alterar o default de produção.
    private static Dictionary<string, string?> RateLimitConfiguration() => new()
    {
        ["RateLimiting:Protected:PermitLimit"] = "1",
        ["RateLimiting:Protected:WindowSeconds"] = "60"
    };

    // Cria vínculo sintético sem carregar identificadores da origem na resposta da API.
    private static async Task AddConnectionAsync(
        FinoraDbContext context,
        Guid userId,
        Guid connectionId,
        string externalItemId)
    {
        context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(connectionId, userId, "pluggy", externalItemId));
        await context.SaveChangesAsync();
    }

    // Cada chamada recebe seu próprio Bearer; a variante anônima exercita o challenge HTTP real.
    private static async Task<HttpResponseMessage> GetCardsAsync(HttpClient client, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/cards");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    // A resposta 429 mantém somente o código fixo de Problem Details e traceId.
    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(json);
        Assert.AreEqual((int)expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
        Assert.AreEqual(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.IsFalse(json.Contains("sentinel", StringComparison.Ordinal));
    }
}
