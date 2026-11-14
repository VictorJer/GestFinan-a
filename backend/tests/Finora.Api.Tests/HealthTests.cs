using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita o contrato HTTP público, em vez de chamar o Controller diretamente.
[TestClass]
public sealed class HealthTests
{
    // Todos os ambientes mantêm o mesmo diagnóstico mínimo sem fornecer token ou configurações financeiras.
    [TestMethod]
    [DataRow("Development")]
    [DataRow("Production")]
    [DataRow("Staging")]
    public async Task HealthReturnsOnlyProcessStatus(string environment)
    {
        await using var factory = new ApiFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual("{\"status\":\"ok\"}", body);
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual(1, json.RootElement.EnumerateObject().Count());
        Assert.AreEqual("ok", json.RootElement.GetProperty("status").GetString());
    }
}
