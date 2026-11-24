using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finora.Application.FinancialData;
using Finora.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Não depende de PostgreSQL: configuração ausente e entradas inválidas são resolvidas antes de abrir conexão.
[TestClass]
[DoNotParallelize]
public sealed class AuthenticationAvailabilityTests
{
    // A rota de prova pertence somente ao assembly de testes; somente login e logout são expostos em auth.
    [TestMethod]
    public async Task NormalApiSurfaceContainsHealthLoginAndProtectedLogout()
    {
        await using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        CollectionAssert.AreEquivalent(new[]
            {
                "/health",
                "/api/auth/login",
                "/api/auth/logout",
                "/api/cards",
                "/api/transactions",
                "/api/transactions/{id}/annotation",
                "/api/categories",
                "/api/connections/current/sync",
                "/api/connections/current/sync/on-entry",
                "/api/connections/current/sync-state",
                "/api/sync-runs/{syncRunId}"
            },
            document.RootElement.GetProperty("paths").EnumerateObject().Select(property => property.Name).ToArray());
        var syncParameters = document.RootElement.GetProperty("paths")
            .GetProperty("/api/connections/current/sync").GetProperty("post").GetProperty("parameters");
        var idempotencyHeader = syncParameters.EnumerateArray().Single(parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.AreEqual("header", idempotencyHeader.GetProperty("in").GetString());
        Assert.IsTrue(idempotencyHeader.GetProperty("required").GetBoolean());
        var cardsResponses = document.RootElement.GetProperty("paths").GetProperty("/api/cards")
            .GetProperty("get").GetProperty("responses");
        // O catálogo só publica listagem/criação e explicita respostas sanitizadas no documento diagnóstico.
        var categories = document.RootElement.GetProperty("paths").GetProperty("/api/categories");
        CollectionAssert.AreEquivalent(new[] { "200", "401", "429", "500", "503" },
            categories.GetProperty("get").GetProperty("responses").EnumerateObject()
                .Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "201", "400", "401", "409", "429", "500", "503" },
            categories.GetProperty("post").GetProperty("responses").EnumerateObject()
                .Select(property => property.Name).ToArray());
        var getOperation = categories.GetProperty("get");
        StringAssert.Contains(getOperation.GetProperty("description").GetString()!, "virtual");
        var getSchema = getOperation.GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.AreEqual("array", getSchema.GetProperty("type").GetString());
        var categoryItem = ResolveSchema(document.RootElement, getSchema.GetProperty("items"));
        CollectionAssert.AreEquivalent(new[] { "id", "name", "kind" },
            categoryItem.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        var createdCategory = ResolveSchema(document.RootElement,
            categories.GetProperty("post").GetProperty("responses").GetProperty("201")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        CollectionAssert.AreEquivalent(new[] { "id", "name", "kind" },
            createdCategory.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        var requestCategory = ResolveSchema(document.RootElement,
            categories.GetProperty("post").GetProperty("requestBody")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        // O body nullable do Controller pode ser emitido como oneOf(null, DTO); inspeciona o DTO não nulo.
        var requestPayloadSchema = requestCategory.TryGetProperty("oneOf", out var requestVariants)
            ? ResolveSchema(document.RootElement, requestVariants.EnumerateArray()
                .Single(variant => variant.TryGetProperty("$ref", out _)))
            : requestCategory;
        CollectionAssert.AreEqual(new[] { "name" },
            requestPayloadSchema.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "200", "400", "401", "404", "409", "429", "500", "503" },
            cardsResponses.EnumerateObject().Select(property => property.Name).ToArray());
        // OpenAPI descreve filtros opcionais, erros e formato paginado sem depender de banco configurado.
        var transactions = document.RootElement.GetProperty("paths").GetProperty("/api/transactions").GetProperty("get");
        CollectionAssert.AreEquivalent(new[] { "200", "400", "401", "404", "409", "429", "500", "503" },
            transactions.GetProperty("responses").EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "cardId", "categoryId", "situation", "from", "toExclusive", "search", "page", "pageSize" },
            transactions.GetProperty("parameters").EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString()!)
                .ToArray());
        foreach (var parameter in transactions.GetProperty("parameters").EnumerateArray())
        {
            Assert.AreEqual("query", parameter.GetProperty("in").GetString());
            Assert.IsFalse(parameter.TryGetProperty("required", out var required) && required.GetBoolean());
            Assert.IsFalse(string.IsNullOrWhiteSpace(parameter.GetProperty("description").GetString()));
        }
        StringAssert.Contains(transactions.GetProperty("description").GetString()!, "TransactionDate");
        StringAssert.Contains(transactions.GetProperty("description").GetString()!, "fim exclusivo");
        var transactionResponseSchema = ResolveSchema(
            document.RootElement,
            transactions.GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        CollectionAssert.AreEquivalent(
            new[] { "items", "page", "pageSize", "filteredCount", "coverage" },
            transactionResponseSchema.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        var annotationPatch = document.RootElement.GetProperty("paths")
            .GetProperty("/api/transactions/{id}/annotation").GetProperty("patch");
        CollectionAssert.AreEquivalent(new[] { "200", "400", "401", "404", "429", "500", "503" },
            annotationPatch.GetProperty("responses").EnumerateObject()
                .Select(property => property.Name).ToArray());
        StringAssert.Contains(annotationPatch.GetProperty("description").GetString()!, "Bearer");
        StringAssert.Contains(annotationPatch.GetProperty("description").GetString()!, "404 indistinguível");
        var movementParameter = annotationPatch.GetProperty("parameters").EnumerateArray().Single();
        Assert.AreEqual("id", movementParameter.GetProperty("name").GetString());
        Assert.AreEqual("path", movementParameter.GetProperty("in").GetString());
        Assert.IsTrue(movementParameter.GetProperty("required").GetBoolean());

        var annotationBody = annotationPatch.GetProperty("requestBody");
        Assert.IsTrue(annotationBody.GetProperty("required").GetBoolean());
        StringAssert.Contains(annotationBody.GetProperty("description").GetString()!, "pelo menos uma");
        var annotationRequestSchema = ResolveSchema(document.RootElement,
            annotationBody.GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        CollectionAssert.AreEquivalent(new[] { "categoryId", "note" },
            annotationRequestSchema.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name).ToArray());
        Assert.IsFalse(annotationRequestSchema.GetProperty("properties").EnumerateObject()
            .Any(property => property.Name is "userId" or "description" or "amount"));
        Assert.AreEqual(500, annotationRequestSchema.GetProperty("properties")
            .GetProperty("note").GetProperty("maxLength").GetInt32());

        var annotationResponseSchema = ResolveSchema(document.RootElement,
            annotationPatch.GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        CollectionAssert.AreEquivalent(new[] { "categoryId", "note" },
            annotationResponseSchema.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name).ToArray());
        var syncRunStatus = document.RootElement.GetProperty("paths")
            .GetProperty("/api/sync-runs/{syncRunId}").GetProperty("get");
        CollectionAssert.AreEquivalent(new[] { "200", "401", "404" },
            syncRunStatus.GetProperty("responses").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.IsFalse(typeof(Program).Assembly.GetTypes().Any(type => type.Name == nameof(AuthenticationProbeController)));
        using var probe = await client.GetAsync("/_tests/authentication-probe");
        Assert.AreEqual(HttpStatusCode.NotFound, probe.StatusCode);
        using var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, logout.StatusCode);
        Assert.IsFalse(logout.Headers.Contains("WWW-Authenticate"));
        Assert.IsTrue(logout.Headers.CacheControl?.NoStore);
        foreach (var path in new[] { "/api/auth/register", "/api/auth/reset-password", "/api/auth/refresh" })
        {
            using var response = await client.PostAsJsonAsync(path, new { });
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // Resolve referências locais para validar que o OpenAPI descreve o formato de página, não só o status HTTP.
    private static JsonElement ResolveSchema(JsonElement document, JsonElement schema)
    {
        while (schema.TryGetProperty("$ref", out var reference))
        {
            foreach (var part in reference.GetString()![2..].Split('/'))
            {
                document = document.GetProperty(part);
            }

            schema = document;
        }

        return schema;
    }

    // A política fallback protege rotas HTTP sem atributo explícito e mantém os endpoints anônimos existentes.
    [TestMethod]
    public async Task FallbackRequiresBearerWithoutExecutingProbeAndKeepsHealthAndOpenApiAnonymous()
    {
        await using var factory = new AuthenticationApiFactory(Configuration());
        using var client = factory.CreateClient();

        // A operação de prova é documentada pela fallback; health continua anônimo no mesmo host.
        using var openApi = await client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, openApi.StatusCode);
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var fallbackOperation = paths.GetProperty("/_tests/fallback-authentication-probe").GetProperty("get");
        var fallbackSecurity = fallbackOperation.GetProperty("security");
        Assert.AreEqual(1, fallbackSecurity.GetArrayLength());
        var fallbackRequirement = fallbackSecurity[0].EnumerateObject().Single();
        Assert.AreEqual("Bearer", fallbackRequirement.Name);
        Assert.AreEqual(0, fallbackRequirement.Value.GetArrayLength());
        Assert.IsTrue(document.RootElement.GetProperty("components").GetProperty("securitySchemes")
            .TryGetProperty(fallbackRequirement.Name, out _));
        Assert.IsFalse(paths.GetProperty("/health").GetProperty("get").TryGetProperty("security", out _));

        using var probe = await client.GetAsync("/_tests/fallback-authentication-probe");
        await PostgreSqlBearerTests.AssertChallengeAsync(probe);
        Assert.AreEqual(0, factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);

        using var health = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
    }

    // A unidade JWT/banco precisa estar completa; headers Bearer incidentais não afetam liveness/documentação.
    [TestMethod]
    [DataRow("missing")]
    [DataRow("partial")]
    [DataRow("missing-database")]
    [DataRow("invalid-database")]
    [DataRow("invalid-key")]
    public async Task MissingOrInvalidConfigurationKeepsDiagnosticsAndFailsAuthenticationClosed(string scenario)
    {
        var configuration = Configuration();
        switch (scenario)
        {
            case "missing": configuration.Clear(); break;
            case "partial": configuration["Authentication:Jwt:Audience"] = null; break;
            case "missing-database": configuration["ConnectionStrings:Finora"] = null; break;
            case "invalid-database": configuration["ConnectionStrings:Finora"] = "invalid-connection-sentinel"; break;
            case "invalid-key": configuration["Authentication:Jwt:SigningKeyBase64"] = "invalid-key-sentinel"; break;
        }
        await using var factory = new AuthenticationApiFactory(configuration);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token-sentinel");
        foreach (var path in new[] { "/health", "/openapi/v1.json", "/swagger/index.html" })
        {
            using var response = await client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"Diagnostic path was challenged: {path}");
            if (path == "/health") { Assert.AreEqual("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync()); }
        }
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "email-sentinel@example.invalid", password = "password-sentinel" });
        await AssertProblemAsync(login, HttpStatusCode.ServiceUnavailable);
        Assert.IsTrue(login.Headers.CacheControl?.NoStore);
        using var protectedResponse = await client.GetAsync("/_tests/authentication-probe");
        await AssertProblemAsync(protectedResponse, HttpStatusCode.ServiceUnavailable);
        using var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
        await AssertProblemAsync(logoutResponse, HttpStatusCode.ServiceUnavailable);
        Assert.IsTrue(logoutResponse.Headers.CacheControl?.NoStore);
        Assert.AreEqual(0, factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
        using var scope = factory.Services.CreateScope();
        Assert.IsNull(scope.ServiceProvider.GetService<FinoraDbContext>());
        Assert.IsFalse(factory.Logs.Text.Contains("sentinel", StringComparison.Ordinal));
    }

    // Mesmo uma configuração sintaticamente válida não conecta para health/diagnóstico; credencial válida falha como 503 real.
    [TestMethod]
    public async Task ConfiguredHostDoesNotContactUnavailableDatabaseForDiagnostics()
    {
        await using var factory = new AuthenticationApiFactory(Configuration());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token-sentinel");
        using var health = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        // A composição válida registra handlers de leitura e aceitação; resolvê-los não abre o banco.
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        Assert.IsNotNull(services.GetRequiredService<IRequestHandler<AcceptSyncRunCommand, SyncRunAcceptanceResult>>());
        Assert.IsNotNull(services.GetRequiredService<ISyncRunAcceptanceStore>());
        Assert.IsNotNull(services.GetRequiredService<IRequestHandler<GetCurrentCardsQuery, CurrentCardsQueryResult>>());
        Assert.IsNotNull(services.GetRequiredService<ICurrentCardsReader>());
        Assert.IsNotNull(services.GetRequiredService<IRequestHandler<GetTransactionsQuery, GetTransactionsQueryResult>>());
        Assert.IsNotNull(services.GetRequiredService<IRequestHandler<UpdateTransactionAnnotationCommand, UpdateTransactionAnnotationResult>>());
        Assert.IsNotNull(services.GetRequiredService<ITransactionCardOwnershipReader>());
        Assert.IsNotNull(services.GetRequiredService<ITransactionMovementReader>());
        Assert.AreEqual(System.Data.ConnectionState.Closed, services.GetRequiredService<FinoraDbContext>().Database.GetDbConnection().State);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "email-sentinel@example.invalid", password = "password-sentinel" });
        await AssertProblemAsync(login, HttpStatusCode.ServiceUnavailable);
        Assert.IsFalse(factory.Logs.Text.Contains("sentinel", StringComparison.Ordinal));
    }

    // Limites de campos e corpo são anteriores à persistência, inclusive JSON malformado/sem tipo e streams desconhecidos.
    [TestMethod]
    public async Task InvalidInputIsSanitizedAndEveryLoginResponseDisablesCaching()
    {
        await using var factory = new AuthenticationApiFactory(Configuration());
        using var client = factory.CreateClient();
        foreach (var body in new[] { "{}", "null", "{", "{\"email\":123,\"password\":\"password-sentinel\"}",
                     JsonSerializer.Serialize(new { email = new string('a', 257), password = "password-sentinel" }),
                     JsonSerializer.Serialize(new { email = "email-sentinel", password = new string('p', 1025) }) })
        {
            using var response = await client.PostAsync("/api/auth/login", new StringContent(body, Encoding.UTF8, "application/json"));
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
            Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        }
        using var oversized = await client.PostAsync("/api/auth/login", new StringContent(new string('x', 16385), Encoding.UTF8, "application/json"));
        await AssertProblemAsync(oversized, HttpStatusCode.RequestEntityTooLarge);
        using var unknownLength = await client.PostAsync("/api/auth/login", new UnknownLengthJsonContent(new string('x', 16385)));
        await AssertProblemAsync(unknownLength, HttpStatusCode.RequestEntityTooLarge);
        using var wrongType = await client.PostAsync("/api/auth/login", new StringContent("password-sentinel"));
        await AssertProblemAsync(wrongType, HttpStatusCode.BadRequest);
        Assert.IsFalse(factory.Logs.Text.Contains("sentinel", StringComparison.Ordinal));
    }

    // Porta local sem serviço não representa uma base real; é usada só para provar indisponibilidade sem outro destino.
    internal static Dictionary<string, string?> Configuration(string? connectionString = null, string? key = null) => new()
    {
        ["ConnectionStrings:Finora"] = connectionString ?? "Host=127.0.0.1;Port=1;Database=finora_integration_test;Username=user-sentinel;Password=connection-sentinel;Timeout=1;Pooling=false",
        ["Authentication:Jwt:Issuer"] = "issuer-sentinel",
        ["Authentication:Jwt:Audience"] = "audience-sentinel",
        ["Authentication:Jwt:SigningKeyBase64"] = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    };

    // Confere apenas contrato fixo e rastreio; nenhuma asserção imprime sentinelas de senha/conexão em caso de falha.
    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
        var authContract = response.RequestMessage?.RequestUri?.AbsolutePath.StartsWith("/api/auth/", StringComparison.Ordinal) == true;
        var expectedCode = status switch
        {
            HttpStatusCode.BadRequest => authContract ? "AUTH_INVALID_INPUT" : "REQUEST_INVALID_INPUT",
            HttpStatusCode.RequestEntityTooLarge => "AUTH_BODY_TOO_LARGE",
            HttpStatusCode.Unauthorized => "AUTH_INVALID_CREDENTIALS",
            HttpStatusCode.NotFound => "RESOURCE_NOT_FOUND",
            HttpStatusCode.Conflict => "RESOURCE_CONFLICT",
            HttpStatusCode.ServiceUnavailable => "AUTH_UNAVAILABLE",
            HttpStatusCode.InternalServerError => authContract ? "AUTH_UNEXPECTED_FAILURE" : "INTERNAL_ERROR",
            _ => "AUTH_UNEXPECTED_FAILURE"
        };
        Assert.AreEqual(expectedCode, problem.RootElement.GetProperty("code").GetString());
        if (response.RequestMessage?.RequestUri?.AbsolutePath == "/api/auth/login")
        {
            Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        }
        Assert.IsFalse(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.IsFalse(body.Contains("sentinel", StringComparison.Ordinal));
        Assert.IsFalse(problem.RootElement.TryGetProperty("errors", out _));
    }

    // Força leitura limitada do stream em vez de depender somente do cabeçalho Content-Length.
    private sealed class UnknownLengthJsonContent : HttpContent
    {
        private readonly string _value;
        public UnknownLengthJsonContent(string value)
        {
            _value = value;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(Encoding.UTF8.GetBytes(_value)).AsTask();
        }
    }
}
