using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica o documento e os recursos reais servidos pelo middleware local.
[TestClass]
public sealed class ExplorationTests
{
    // O documento deve refletir somente o endpoint existente e seu contrato JSON de sucesso.
    [TestMethod]
    public async Task DevelopmentPublishesHealthContract()
    {
        await using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        StringAssert.StartsWith(root.GetProperty("openapi").GetString()!, "3.");
        var paths = root.GetProperty("paths");
        CollectionAssert.AreEquivalent(
            new[] { "/health", "/api/auth/login", "/api/auth/logout", "/api/connections/current/sync", "/api/sync-runs/{syncRunId}" },
            paths.EnumerateObject().Select(p => p.Name).ToArray());
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

        // Nesta versão a configuração é emitida no inicializador local, separado do HTML.
        using var initializer = await client.GetAsync("/swagger/index.js");
        Assert.AreEqual(HttpStatusCode.OK, initializer.StatusCode);
        StringAssert.Contains(await initializer.Content.ReadAsStringAsync(), "/openapi/v1.json");

        using var asset = await client.GetAsync("/swagger/swagger-ui-bundle.js");
        Assert.AreEqual(HttpStatusCode.OK, asset.StatusCode);
        StringAssert.Contains(await asset.Content.ReadAsStringAsync(), "SwaggerUIBundle");
    }
}
