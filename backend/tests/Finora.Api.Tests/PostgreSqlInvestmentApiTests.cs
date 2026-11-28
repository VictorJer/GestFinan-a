using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida contrato HTTP, autenticação e titularidade em host real com dados PostgreSQL sintéticos descartáveis.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlInvestmentApiTests
{
    // O fluxo HTTP completo concilia CRUD do titular e impede que outro token veja ou altere a posição.
    [TestMethod]
    public async Task PortfolioCrudIsProtectedAndScopedToAuthenticatedOwner()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var ownerToken = await fixture.TokenAsync();
        var otherId = await fixture.SeedUserAsync("other-investment@example.invalid");
        // O login local recusa ambiguamente mais de uma conta; o teste cria sessão assinada sintética para a segunda.
        var otherSessionId = AuthSession.GenerateId();
        var sessionCreatedAt = DateTimeOffset.FromUnixTimeSeconds(fixture.Clock.GetUtcNow().ToUnixTimeSeconds());
        fixture.Context.Set<AuthSession>().Add(AuthSession.Create(
            otherSessionId, otherId, sessionCreatedAt, sessionCreatedAt.AddSeconds(1800)));
        await fixture.Context.SaveChangesAsync();
        var otherToken = PostgreSqlBearerTests.Sign(
            fixture.Key,
            PostgreSqlBearerTests.Payload(fixture, otherId, otherSessionId));

        // Falta ou rejeição de sessão produz challenge Bearer antes de qualquer leitura/escrita de carteira.
        using var anonymousRead = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments");
        await PostgreSqlBearerTests.AssertChallengeAsync(anonymousRead);
        using var anonymousWrite = await SendAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/investments",
            json: ValidInvestmentJson("não autorizado"));
        await PostgreSqlBearerTests.AssertChallengeAsync(anonymousWrite);
        Assert.AreEqual(0, await fixture.Context.Set<Investment>().CountAsync());

        // A criação retorna somente os campos públicos da posição e não publica UserId ou dados de sessão.
        using var created = await SendAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/investments",
            ownerToken,
            ValidInvestmentJson("Reserva sintética", InvestmentClassCatalog.FixedIncome, 100.25m, 90.25m));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(
            new[] { "id", "name", "class", "appliedAmount", "currentAmount" },
            createdBody.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        var investmentId = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.AreNotEqual(Guid.Empty, investmentId);
        Assert.AreEqual("Reserva sintética", createdBody.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("100.25", createdBody.RootElement.GetProperty("appliedAmount").GetString());
        Assert.AreEqual("90.25", createdBody.RootElement.GetProperty("currentAmount").GetString());

        // A consulta de outro titular retorna carteira vazia e zeros, mesmo com uma posição já persistida.
        using var otherPortfolio = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments", otherToken);
        Assert.AreEqual(HttpStatusCode.OK, otherPortfolio.StatusCode);
        using var otherBody = JsonDocument.Parse(await otherPortfolio.Content.ReadAsStringAsync());
        Assert.AreEqual(0, otherBody.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual("0.00", otherBody.RootElement.GetProperty("summary").GetProperty("totalCurrentAmount").GetString());

        // A leitura própria publica lista e resumo conciliados na mesma resposta após persistência real.
        using var ownerPortfolio = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments", ownerToken);
        Assert.AreEqual(HttpStatusCode.OK, ownerPortfolio.StatusCode);
        using var ownerBody = JsonDocument.Parse(await ownerPortfolio.Content.ReadAsStringAsync());
        Assert.AreEqual(1, ownerBody.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual("100.25", ownerBody.RootElement.GetProperty("summary").GetProperty("totalAppliedAmount").GetString());
        Assert.AreEqual("90.25", ownerBody.RootElement.GetProperty("summary").GetProperty("totalCurrentAmount").GetString());
        Assert.AreEqual("-10.00", ownerBody.RootElement.GetProperty("summary").GetProperty("difference").GetString());
        Assert.AreEqual(3, ownerBody.RootElement.GetProperty("summary").GetProperty("currentAmountsByClass").GetArrayLength());

        // IDs alheios e inexistentes compartilham 404 sanitizado e não refletem nome nem valores privados.
        var missingId = Guid.NewGuid();
        using var foreignUpdate = await SendAsync(
            fixture.Client,
            HttpMethod.Put,
            $"/api/investments/{investmentId:D}",
            otherToken,
            ValidInvestmentJson("tentativa alheia", InvestmentClassCatalog.VariableIncome, 1m, 1m));
        using var missingUpdate = await SendAsync(
            fixture.Client,
            HttpMethod.Put,
            $"/api/investments/{missingId:D}",
            otherToken,
            ValidInvestmentJson("tentativa ausente", InvestmentClassCatalog.VariableIncome, 1m, 1m));
        await AssertSameNotFoundAsync(foreignUpdate, missingUpdate, "Reserva sintética");

        using var foreignDelete = await SendAsync(
            fixture.Client, HttpMethod.Delete, $"/api/investments/{investmentId:D}", otherToken);
        using var missingDelete = await SendAsync(
            fixture.Client, HttpMethod.Delete, $"/api/investments/{missingId:D}", otherToken);
        await AssertSameNotFoundAsync(foreignDelete, missingDelete, "Reserva sintética");
        Assert.AreEqual(1, await fixture.Context.Set<Investment>().CountAsync());

        // O titular edita, relê os valores conciliados e conclui exclusão com 204 sem corpo.
        using var updated = await SendAsync(
            fixture.Client,
            HttpMethod.Put,
            $"/api/investments/{investmentId:D}",
            ownerToken,
            ValidInvestmentJson("Reserva revisada", InvestmentClassCatalog.VariableIncome, 120m, 150m));
        Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode);
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.AreEqual(InvestmentClassCatalog.VariableIncome, updatedBody.RootElement.GetProperty("class").GetString());

        using var updatedPortfolio = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments", ownerToken);
        using var updatedPortfolioBody = JsonDocument.Parse(await updatedPortfolio.Content.ReadAsStringAsync());
        Assert.AreEqual("120.00", updatedPortfolioBody.RootElement.GetProperty("summary").GetProperty("totalAppliedAmount").GetString());
        Assert.AreEqual("150.00", updatedPortfolioBody.RootElement.GetProperty("summary").GetProperty("totalCurrentAmount").GetString());
        Assert.AreEqual("30.00", updatedPortfolioBody.RootElement.GetProperty("summary").GetProperty("difference").GetString());

        using var deleted = await SendAsync(
            fixture.Client, HttpMethod.Delete, $"/api/investments/{investmentId:D}", ownerToken);
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.AreEqual(0, (await deleted.Content.ReadAsByteArrayAsync()).Length);
        using var emptyPortfolio = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments", ownerToken);
        using var emptyBody = JsonDocument.Parse(await emptyPortfolio.Content.ReadAsStringAsync());
        Assert.AreEqual(0, emptyBody.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual("0.00", emptyBody.RootElement.GetProperty("summary").GetProperty("totalAppliedAmount").GetString());
        Assert.AreEqual("0.00", emptyBody.RootElement.GetProperty("summary").GetProperty("totalCurrentAmount").GetString());
        Assert.AreEqual(0, await fixture.Context.Set<Investment>().CountAsync());
        Assert.AreNotEqual(ownerId, otherId);
    }

    // Parser HTTP rejeita campo ausente/extra/duplicado, tipo incorreto, classe desconhecida e valores não representáveis.
    [TestMethod]
    public async Task CreateRejectsPayloadOutsideStrictInvestmentContract()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var invalidBodies = new[]
        {
            "{}",
            "null",
            "{",
            "{\"name\":null,\"class\":\"fixed_income\",\"appliedAmount\":\"1.00\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"1.00\",\"currentAmount\":\"1.00\",\"userId\":\"other\"}",
            "{\"name\":\"X\",\"name\":\"Y\",\"class\":\"fixed_income\",\"appliedAmount\":\"1.00\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"unknown\",\"appliedAmount\":\"1.00\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"-1.00\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"1.001\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"1\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"01.00\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"1e2\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"1,00\",\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":1.00,\"currentAmount\":\"1.00\"}",
            "{\"name\":\"X\",\"class\":\"fixed_income\",\"appliedAmount\":\"100000000000000000.00\",\"currentAmount\":\"1.00\"}"
        };

        foreach (var json in invalidBodies)
        {
            using var response = await SendAsync(fixture.Client, HttpMethod.Post, "/api/investments", token, json);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");
        }

        using var wrongContentType = new HttpRequestMessage(HttpMethod.Post, "/api/investments")
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
        };
        wrongContentType.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var wrongTypeResponse = await fixture.Client.SendAsync(wrongContentType);
        await AssertProblemAsync(wrongTypeResponse, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");
        Assert.AreEqual(0, await fixture.Context.Set<Investment>().CountAsync());
    }

    // A posição máxima de numeric(19,2) mantém cada centavo após POST, leitura e resumo HTTP.
    [TestMethod]
    public async Task MaximumNumeric19Scale2RoundTripsAsCanonicalStrings()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        const string maximum = "99999999999999999.99";
        var payload = JsonSerializer.Serialize(new
        {
            name = "Limite decimal",
            @class = InvestmentClassCatalog.FixedIncome,
            appliedAmount = maximum,
            currentAmount = maximum
        });

        using var created = await SendAsync(fixture.Client, HttpMethod.Post, "/api/investments", token, payload);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.AreEqual(maximum, createdBody.RootElement.GetProperty("appliedAmount").GetString());
        Assert.AreEqual(maximum, createdBody.RootElement.GetProperty("currentAmount").GetString());

        using var response = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments", token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = body.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.AreEqual(maximum, item.GetProperty("appliedAmount").GetString());
        Assert.AreEqual(maximum, item.GetProperty("currentAmount").GetString());
        var summary = body.RootElement.GetProperty("summary");
        Assert.AreEqual(maximum, summary.GetProperty("totalAppliedAmount").GetString());
        Assert.AreEqual(maximum, summary.GetProperty("totalCurrentAmount").GetString());
        Assert.AreEqual("0.00", summary.GetProperty("difference").GetString());
        Assert.AreEqual(maximum, summary.GetProperty("currentAmountsByClass").EnumerateArray()
            .Single(value => value.GetProperty("class").GetString() == InvestmentClassCatalog.FixedIncome)
            .GetProperty("currentAmount").GetString());
    }

    // O middleware protegido aplica 429 também à leitura, sem permitir que a carteira ignore o limite por sessão.
    [TestMethod]
    public async Task PortfolioReadUsesProtectedRateLimit()
    {
        var configuration = new Dictionary<string, string?>
        {
            ["RateLimiting:Protected:PermitLimit"] = "1",
            ["RateLimiting:Protected:WindowSeconds"] = "60"
        };
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(apiConfiguration: configuration);
        await fixture.Context.Database.MigrateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();

        using var first = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments", token);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        using var limited = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments", token);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    // Ausência da tabela nova no schema autenticado falha fechada com 503 sanitizado e sem provisionamento implícito.
    [TestMethod]
    public async Task MissingInvestmentTableReturnsSanitizedUnavailable()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();

        using var response = await SendAsync(fixture.Client, HttpMethod.Get, "/api/investments", token);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE");
    }

    // Cada request usa cabeçalhos isolados e inclui corpo somente para operações que o contrato permite.
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? token = null,
        string? json = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    // Serializa valores sintéticos como strings de duas casas e nunca como números JSON binários.
    private static string ValidInvestmentJson(
        string name,
        string @class = InvestmentClassCatalog.FixedIncome,
        decimal appliedAmount = 1m,
        decimal currentAmount = 1m) => JsonSerializer.Serialize(new
        {
            name,
            @class,
            appliedAmount = appliedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            currentAmount = currentAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        });

    // 404 alheio/inexistente compartilham status e código, sem refletir nome, valor ou titular encontrado.
    private static async Task AssertSameNotFoundAsync(
        HttpResponseMessage foreign,
        HttpResponseMessage missing,
        string privateName)
    {
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        var foreignBody = await foreign.Content.ReadAsStringAsync();
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.IsFalse(foreignBody.Contains(privateName, StringComparison.Ordinal));
        Assert.IsFalse(missingBody.Contains(privateName, StringComparison.Ordinal));
    }

    // Asserções de erro validam mídia e código público estável, não detalhes internos do pipeline.
    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, body.RootElement.GetProperty("code").GetString());
    }
}
