using System.Net;
using System.Text.Json;
using Finora.Api.Authentication;
using Finora.Infrastructure.Persistence;
using Finora.Infrastructure.Pluggy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica o documento e os recursos reais servidos pelo middleware local.
[TestClass]
public sealed class ExplorationTests
{
    // O documento lista somente rotas implementadas e mantém a operação de health com contrato explícito.
    [TestMethod]
    public async Task DevelopmentPublishesHealthContract()
    {
        await using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();

        // A ausência de JWT/banco também exclui handlers externos; health e OpenAPI continuam sendo diagnóstico local.
        Assert.IsFalse(factory.Services.GetRequiredService<AuthenticationAvailability>().IsAvailable);
        Assert.IsNull(factory.Services.GetService<FinoraDbContext>());
        Assert.IsNull(factory.Services.GetService<PluggyApiClient>());
        using var health = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        Assert.AreEqual("{\"status\":\"ok\"}", await health.Content.ReadAsStringAsync());

        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        StringAssert.StartsWith(root.GetProperty("openapi").GetString()!, "3.");
        // O mecanismo é reutilizável, mas a política deve ser aplicada por operação, nunca globalmente.
        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.AreEqual("http", bearer.GetProperty("type").GetString());
        Assert.AreEqual("bearer", bearer.GetProperty("scheme").GetString());
        Assert.AreEqual("JWT", bearer.GetProperty("bearerFormat").GetString());
        Assert.IsFalse(root.TryGetProperty("security", out _));
        var paths = root.GetProperty("paths");
        CollectionAssert.AreEquivalent(
            new[]
            {
                "/health", "/api/auth/login", "/api/auth/logout", "/api/cards", "/api/transactions",
                "/api/dashboard", // O resumo FNR-018 é uma operação protegida própria, sem paginação.
                "/api/categories", // O catálogo autenticado FNR-016 agora integra a superfície OpenAPI.
                "/api/transactions/{id}/annotation", // A edição protegida FNR-017 também integra o contrato publicado.
                "/api/transactions/recurrence-candidates", // A recorrência FNR-038 publica consulta, reavaliação e decisão protegidas.
                "/api/transactions/recurrence-candidates/evaluate",
                "/api/transactions/recurrence-candidates/{candidateId}/decision",
                "/api/investments", // A carteira manual FNR-029 publica consulta e cadastro.
                "/api/investments/{id}", // Edição e exclusão continuam protegidas pelo mesmo contrato.
                "/api/connections/current/sync", "/api/connections/current/sync/on-entry",
                "/api/connections/current/sync-state", "/api/sync-runs/{syncRunId}"
            },
            paths.EnumerateObject().Select(p => p.Name).ToArray());
        // A matriz exercita método e rota; cada proteção precisa resolver o esquema sem criar alternativas duplicadas.
        var protectedOperations = new (string Path, string Method)[]
        {
            ("/api/auth/logout", "post"),
            ("/api/cards", "get"),
            ("/api/transactions", "get"),
            ("/api/dashboard", "get"),
            ("/api/transactions/{id}/annotation", "patch"),
            ("/api/categories", "get"),
            ("/api/categories", "post"),
            ("/api/transactions/recurrence-candidates", "get"),
            ("/api/transactions/recurrence-candidates/evaluate", "post"),
            ("/api/transactions/recurrence-candidates/{candidateId}/decision", "put"),
            ("/api/investments", "get"),
            ("/api/investments", "post"),
            ("/api/investments/{id}", "put"),
            ("/api/investments/{id}", "delete"),
            ("/api/connections/current/sync", "post"),
            ("/api/connections/current/sync/on-entry", "post"),
            ("/api/connections/current/sync-state", "get"),
            ("/api/sync-runs/{syncRunId}", "get")
        };
        var publicOperations = new (string Path, string Method)[]
        {
            ("/health", "get"),
            ("/api/auth/login", "post")
        };
        var operationsWithSecurity = paths.EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(method => method.Value.TryGetProperty("security", out _))
                .Select(method => $"{path.Name}:{method.Name}"))
            .ToArray();
        CollectionAssert.AreEquivalent(
            protectedOperations.Select(item => $"{item.Path}:{item.Method}").ToArray(),
            operationsWithSecurity);
        foreach (var (path, method) in protectedOperations)
        {
            var requirements = paths.GetProperty(path).GetProperty(method).GetProperty("security");
            Assert.AreEqual(1, requirements.GetArrayLength(), $"Duplicate security requirements: {method} {path}");
            var schemeRequirement = requirements[0].EnumerateObject().Single();
            Assert.AreEqual("Bearer", schemeRequirement.Name);
            Assert.AreEqual(0, schemeRequirement.Value.GetArrayLength());
            Assert.IsTrue(root.GetProperty("components").GetProperty("securitySchemes")
                .TryGetProperty(schemeRequirement.Name, out _));
        }
        foreach (var (path, method) in publicOperations)
        {
            Assert.IsFalse(paths.GetProperty(path).GetProperty(method).TryGetProperty("security", out _));
        }
        // Login e logout documentam somente os resultados implementados; reset/cadastro/refresh seguem ausentes.
        var login = paths.GetProperty("/api/auth/login").GetProperty("post");
        CollectionAssert.AreEquivalent(new[] { "200", "400", "401", "413", "503", "500" },
            login.GetProperty("responses").EnumerateObject().Select(property => property.Name).ToArray());
        var logout = paths.GetProperty("/api/auth/logout").GetProperty("post");
        CollectionAssert.AreEquivalent(new[] { "204", "401", "503", "500" },
            logout.GetProperty("responses").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.IsFalse(logout.TryGetProperty("requestBody", out _));
        var operation = paths.GetProperty("/health").GetProperty("get");
        Assert.IsFalse(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
        var schema = operation.GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");

        // Resolve a referência local para conferir o DTO, não apenas a presença de uma operação.
        if (schema.TryGetProperty("$ref", out var reference))
        {
            foreach (var part in reference.GetString()![2..].Split('/'))
            {
                root = root.GetProperty(part);
            }
            schema = root;
        }
        var properties = schema.GetProperty("properties");
        Assert.AreEqual(1, properties.EnumerateObject().Count());
        Assert.AreEqual("string", properties.GetProperty("status").GetProperty("type").GetString());
    }

    // HTML, configuração do documento e JavaScript são servidos pela aplicação, sem CDN.
    [TestMethod]
    public async Task DevelopmentServesSwaggerAndLocalAssets()
    {
        await using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "swagger-ui-bundle.js");
        StringAssert.Contains(html, "src=\"index.js\"");
        StringAssert.Contains(html, "./swagger-ui.css");
        Assert.IsFalse(html.Contains("src=\"https://", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("href=\"https://", StringComparison.OrdinalIgnoreCase));

        // O inicializador aponta ao documento da aplicação e não embute cabeçalhos, token ou valor de autorização.
        using var initializer = await client.GetAsync("/swagger/index.js");
        Assert.AreEqual(HttpStatusCode.OK, initializer.StatusCode);
        var initializerContent = await initializer.Content.ReadAsStringAsync();
        StringAssert.Contains(initializerContent, "/openapi/v1.json");
        Assert.IsFalse(initializerContent.Contains("accessToken", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(initializerContent.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(initializerContent.Contains("Bearer ", StringComparison.OrdinalIgnoreCase));

        using var asset = await client.GetAsync("/swagger/swagger-ui-bundle.js");
        Assert.AreEqual(HttpStatusCode.OK, asset.StatusCode);
        StringAssert.Contains(await asset.Content.ReadAsStringAsync(), "SwaggerUIBundle");
    }
}
