using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Impede que uma configuração fora de Development publique documentação ou seus assets.
[TestClass]
public sealed class ExplorationIsolationTests
{
    // Sem seguir redirecionamentos, cada endereço deve ser realmente inexistente no ambiente testado.
    [TestMethod]
    [DataRow("Production", "/swagger")]
    [DataRow("Production", "/swagger/index.html")]
    [DataRow("Production", "/swagger/index.js")]
    [DataRow("Production", "/swagger/swagger-ui-bundle.js")]
    [DataRow("Production", "/swagger/swagger-ui.css")]
    [DataRow("Production", "/openapi/v1.json")]
    [DataRow("Staging", "/swagger")]
    [DataRow("Staging", "/swagger/index.html")]
    [DataRow("Staging", "/swagger/index.js")]
    [DataRow("Staging", "/swagger/swagger-ui-bundle.js")]
    [DataRow("Staging", "/openapi/v1.json")]
    public async Task DocumentationIsUnavailableOutsideDevelopment(string environment, string path)
    {
        await using var factory = new ApiFactory(environment);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(string.Empty, await response.Content.ReadAsStringAsync());

        // Desativar a documentação não deve desativar nem ampliar o diagnóstico público.
        using var health = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        Assert.AreEqual("{\"status\":\"ok\"}", await health.Content.ReadAsStringAsync());
    }
}
