using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Os probes usam autenticação e sessão reais, mas recursos sintéticos exclusivos do host de testes.
[TestClass]
[DoNotParallelize]
public sealed class OwnershipProbeTests
{
    // Recurso alheio e ID ausente recebem o mesmo 404; filtros não substituem o owner validado.
    [TestMethod]
    public async Task OwnershipAndListFiltersUseOnlyAuthenticatedOwner()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var otherOwnerId = Guid.NewGuid();
        var ownerResourceId = Guid.NewGuid();
        var otherResourceId = Guid.NewGuid();
        var missingResourceId = Guid.NewGuid();
        var store = fixture.Factory.Services.GetRequiredService<OwnershipProbeStore>();
        store.Seed(ownerId, otherOwnerId, ownerResourceId, otherResourceId);
        var token = await fixture.TokenAsync();

        using var other = await SendAsync(fixture.Client, token, $"/_tests/ownership-probe/{otherResourceId:D}");
        using var missing = await SendAsync(fixture.Client, token, $"/_tests/ownership-probe/{missingResourceId:D}");
        var otherProblem = await ReadProblemAsync(other, HttpStatusCode.NotFound);
        var missingProblem = await ReadProblemAsync(missing, HttpStatusCode.NotFound);
        Assert.AreEqual(otherProblem.Code, missingProblem.Code);
        Assert.AreEqual(otherProblem.Title, missingProblem.Title);
        Assert.AreEqual("RESOURCE_NOT_FOUND", otherProblem.Code);
        Assert.IsFalse((await other.Content.ReadAsStringAsync()).Contains(otherResourceId.ToString("D"), StringComparison.Ordinal));
        Assert.IsFalse((await missing.Content.ReadAsStringAsync()).Contains(missingResourceId.ToString("D"), StringComparison.Ordinal));

        using var filtered = await SendAsync(fixture.Client, token,
            $"/_tests/ownership-probe?ownerId={otherOwnerId:D}");
        Assert.AreEqual(HttpStatusCode.OK, filtered.StatusCode);
        using var list = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        var ids = list.RootElement.GetProperty("ids").EnumerateArray().Select(value => value.GetGuid()).ToArray();
        CollectionAssert.AreEqual(new[] { ownerResourceId }, ids);
    }

    // Um 403 por permissão conhecida é igual para IDs reais e ausentes e ocorre sem consultar recursos.
    [TestMethod]
    public async Task ForbiddenPermissionDoesNotConfirmResourceExistence()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var otherOwnerId = Guid.NewGuid();
        var existingResourceId = Guid.NewGuid();
        var missingResourceId = Guid.NewGuid();
        var store = fixture.Factory.Services.GetRequiredService<OwnershipProbeStore>();
        store.Seed(ownerId, otherOwnerId, Guid.NewGuid(), existingResourceId);
        var token = await fixture.TokenAsync();
        var lookupsBefore = store.Lookups;

        using var existing = await SendAsync(fixture.Client, token,
            $"/_tests/ownership-probe/restricted/{existingResourceId:D}");
        using var missing = await SendAsync(fixture.Client, token,
            $"/_tests/ownership-probe/restricted/{missingResourceId:D}");
        var existingProblem = await ReadProblemAsync(existing, HttpStatusCode.Forbidden);
        var missingProblem = await ReadProblemAsync(missing, HttpStatusCode.Forbidden);

        Assert.AreEqual(existingProblem.Code, missingProblem.Code);
        Assert.AreEqual(existingProblem.Title, missingProblem.Title);
        Assert.AreEqual("PERMISSION_REQUIRED", existingProblem.Code);
        Assert.AreEqual(lookupsBefore, store.Lookups);
    }

    // Cada chamada leva o Bearer somente em memória e evita compartilhar headers mutáveis do HttpClient.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    // Compara somente contrato público estável; traceId é deliberadamente único por requisição.
    private static async Task<(string Code, string Title)> ReadProblemAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.AreEqual((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        return (root.GetProperty("code").GetString()!, root.GetProperty("title").GetString()!);
    }
}
