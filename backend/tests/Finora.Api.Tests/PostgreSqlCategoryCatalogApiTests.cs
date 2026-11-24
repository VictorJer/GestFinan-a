using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Integra as rotas reais, sessão persistida e catálogo em PostgreSQL isolado; nenhuma conexão financeira é criada.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlCategoryCatalogApiTests
{
    // A listagem mostra opções próprias, ordem fixa e ausência virtual sem tocar movimentos ou sincronizações.
    [TestMethod]
    public async Task CreatingAndListingPersonalOptionsAreScopedAndLeaveFinancialHistoryUntouched()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var owner = await fixture.SeedUserAsync();
        var ownerToken = await fixture.TokenAsync();
        var other = await fixture.SeedUserAsync("other-category@example.invalid");

        using var created = await SendAsync(fixture.Client, HttpMethod.Post, ownerToken, "{\"name\":\"  Projetos  \"}");
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        Assert.IsNull(created.Headers.Location);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(new[] { "id", "name", "kind" },
            createdBody.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        var personalId = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.AreNotEqual(Guid.Empty, personalId);
        Assert.AreEqual("Projetos", createdBody.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("personal", createdBody.RootElement.GetProperty("kind").GetString());

        using var listing = await SendAsync(fixture.Client, HttpMethod.Get, ownerToken);
        Assert.AreEqual(HttpStatusCode.OK, listing.StatusCode);
        using var items = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
        Assert.AreEqual(9, items.RootElement.GetArrayLength());
        for (var i = 0; i < 7; i++)
        {
            Assert.AreEqual(SystemCategoryCatalog.Categories[i].Id, items.RootElement[i].GetProperty("id").GetGuid());
            Assert.AreEqual("system", items.RootElement[i].GetProperty("kind").GetString());
        }
        Assert.AreEqual(personalId, items.RootElement[7].GetProperty("id").GetGuid());
        Assert.AreEqual(JsonValueKind.Null, items.RootElement[8].GetProperty("id").ValueKind);
        Assert.AreEqual("unassigned", items.RootElement[8].GetProperty("kind").GetString());

        // A política de login administrativo não emite segundo token; o adapter prova os dois escopos diretamente.
        var store = new CategoryCatalogStore(fixture.Context);
        var otherCreated = await new CreatePersonalCategoryHandler(store).Handle(
            new CreatePersonalCategoryCommand(other, "Projetos"), default);
        Assert.AreEqual(CreateCategoryStatus.Created, otherCreated.Status);
        var otherOptions = await new GetCategoriesHandler(store).Handle(new GetCategoriesQuery(other), default);
        var ownerOptions = await new GetCategoriesHandler(store).Handle(new GetCategoriesQuery(owner), default);
        Assert.IsFalse(otherOptions.Any(option => option.Id == personalId));
        Assert.IsFalse(ownerOptions.Any(option => option.Id == otherCreated.Category!.Id));
        Assert.HasCount(9, otherOptions);

        Assert.AreEqual(7, await fixture.Context.Set<Category>().CountAsync(category => category.IsSystem));
        Assert.AreEqual(2, await fixture.Context.Set<Category>().CountAsync(category => !category.IsSystem));
        Assert.AreEqual(0, await fixture.Context.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Set<SyncRun>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Set<TransactionAnnotation>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Set<FinancialConnection>().CountAsync());
        Assert.IsTrue(items.RootElement.EnumerateArray().Where(item => item.GetProperty("id").ValueKind != JsonValueKind.Null)
            .All(item => item.GetProperty("id").GetGuid() != Guid.Empty));
        Assert.AreNotEqual(owner, other);
    }

    // JSON adicional/malformado, nomes inválidos ou equivalentes e sessão revogada nunca criam categorias.
    [TestMethod]
    public async Task CreationRejectsExtraFieldsReservedDuplicateAndRevokedSession()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        using var absent = await SendAsync(fixture.Client, HttpMethod.Post, token);
        Assert.AreEqual(HttpStatusCode.BadRequest, absent.StatusCode);
        foreach (var json in new[] { "{}", "null", "{", "{\"name\":null}", "{\"name\":\"A\\nB\"}",
                     "{\"name\":\"X\",\"userId\":\"outro\"}", "{\"name\":\"X\",\"id\":null}",
                     "{\"name\":\"X\",\"kind\":\"system\"}",
                     "{\"name\":\"X\",\"extra\":true}" })
        {
            using var invalid = await SendAsync(fixture.Client, HttpMethod.Post, token, json);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode, $"Corpo inválido aceito: {json}");
        }

        foreach (var name in new[] { " saude ", "SeM\u00a0categoria" })
        {
            using var conflict = await SendAsync(fixture.Client, HttpMethod.Post, token,
                JsonSerializer.Serialize(new { name }));
            Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        }
        using var accepted = await SendAsync(fixture.Client, HttpMethod.Post, token, "{\"name\":\"Projetos\"}");
        Assert.AreEqual(HttpStatusCode.Created, accepted.StatusCode);
        using var duplicate = await SendAsync(fixture.Client, HttpMethod.Post, token, "{\"name\":\"  PROJETOS  \"}");
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AuthSessions\" SET \"RevokedAtUtc\"={fixture.Clock.GetUtcNow()} WHERE \"Id\"={new JsonWebToken(token).Id}");
        using var rejected = await SendAsync(fixture.Client, HttpMethod.Post, token, "{\"name\":\"Outro\"}");
        await PostgreSqlBearerTests.AssertChallengeAsync(rejected);
        using var rejectedList = await SendAsync(fixture.Client, HttpMethod.Get, token);
        await PostgreSqlBearerTests.AssertChallengeAsync(rejectedList);
        Assert.AreEqual(1, await fixture.Context.Set<Category>().CountAsync(category => !category.IsSystem));
    }

    // Um host autenticado sem migration do catálogo responde 503 na leitura e não inventa opções.
    [TestMethod]
    public async Task UnprovisionedCatalogReturnsSanitizedUnavailableWithoutCreatingRows()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, token);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "DEPENDENCY_UNAVAILABLE");
        Assert.IsFalse(body.Contains("Category", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, await fixture.Context.Set<Category>().CountAsync());
    }

    // O limite protegido inclui POST e retorna o mesmo envelope problem+json sem processar uma segunda criação.
    [TestMethod]
    public async Task CreationUsesProtectedRateLimitWithSanitized429()
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
        using var first = await SendAsync(fixture.Client, HttpMethod.Post, token, "{\"name\":\"Uma\"}");
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        using var limited = await SendAsync(fixture.Client, HttpMethod.Post, token, "{\"name\":\"Duas\"}");
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        Assert.AreEqual(1, await fixture.Context.Set<Category>().CountAsync(category => !category.IsSystem));
    }

    // Falha inesperada do adapter é sanitizada pelo pipeline e nunca ecoa mensagem interna no body/logs.
    [TestMethod]
    public async Task CreationSanitizesUnexpectedStoreFailureAs500()
    {
        var failure = new InvalidOperationException("category-store-secret-sentinel");
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
        {
            services.RemoveAll<ICategoryCatalogStore>();
            services.AddScoped<ICategoryCatalogStore>(_ => new FaultingCategoryStore(failure));
        });
        await fixture.Context.Database.MigrateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, token, "{\"name\":\"Projetos\"}");
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("sentinel", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Factory.Logs.Text.Contains("sentinel", StringComparison.Ordinal));
    }

    // Banco indisponível durante a persistência não vira duplicidade e usa 503 sanitizado.
    [TestMethod]
    public async Task CreationMapsUnavailableDatabaseToSanitized503()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        // Renomear quebra somente a consulta do adapter e preserva as FKs para não confundir a falha injetada.
        await fixture.Context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Category\" RENAME TO \"CategoryUnavailable\"");
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, token, "{\"name\":\"Projetos\"}");
        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE");
    }

    // GET participa do mesmo limite por sessão e não contorna o middleware protegido.
    [TestMethod]
    public async Task ListingUsesProtectedRateLimitWithSanitized429()
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
        using var first = await SendAsync(fixture.Client, HttpMethod.Get, token);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        using var limited = await SendAsync(fixture.Client, HttpMethod.Get, token);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    // Headers permanecem por chamada para evitar vazamento de autorização entre usuários e operações.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string? token,
        string? json = null)
    {
        using var request = new HttpRequestMessage(method, "/api/categories");
        if (token is not null) { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); }
        if (json is not null) { request.Content = new StringContent(json, Encoding.UTF8, "application/json"); }
        return await client.SendAsync(request);
    }

    // Falhas públicas são validadas pela mídia, status e código estável, sem acoplar o contrato ao texto interno.
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, body.RootElement.GetProperty("code").GetString());
    }

    // Adapter sintético injeta falha inesperada sem conectar a qualquer serviço externo.
    private sealed class FaultingCategoryStore(Exception failure) : ICategoryCatalogStore
    {
        public Task<Guid?> CreatePersonalAsync(Guid userId, CategoryName name, CancellationToken cancellationToken) =>
            Task.FromException<Guid?>(failure);

        public Task<IReadOnlyList<Category>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<Category>>(failure);
    }
}
