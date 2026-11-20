using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Inspeciona o envelope HTTP real sem publicar rotas de negócio nem depender de provedores externos.
[TestClass]
[DoNotParallelize]
public sealed class ProblemDetailsContractTests
{
    // MVC, challenge, forbid, roteamento e conflito usam status e formato sanitizados comuns.
    [TestMethod]
    public async Task HttpErrorsUseSanitizedProblemDetailsAndPreserveBearerChallenge()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/_tests/problem-probe/validation")
        {
            Content = JsonContent.Create(new { value = "", extra = "validation-sentinel" })
        };
        invalidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var invalid = await fixture.Client.SendAsync(invalidRequest);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");
        Assert.IsFalse((await invalid.Content.ReadAsStringAsync()).Contains("validation-sentinel", StringComparison.Ordinal));

        using var challenge = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client);
        await PostgreSqlBearerTests.AssertChallengeAsync(challenge);
        await AssertProblemAsync(challenge, HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");

        using var forbiddenRequest = new HttpRequestMessage(HttpMethod.Get, "/_tests/forbidden-probe");
        forbiddenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var forbidden = await fixture.Client.SendAsync(forbiddenRequest);
        await AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, "PERMISSION_REQUIRED");
        Assert.AreEqual(0, forbidden.Headers.WwwAuthenticate.Count);

        using var missing = await fixture.Client.GetAsync("/_tests/missing-problem-probe");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        using var conflict = await SendAsync(fixture.Client, token, "/_tests/problem-probe/conflict");
        await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "RESOURCE_CONFLICT");
    }

    // O teste valida somente os campos estáveis e rejeita conteúdo de entrada ou detalhes extras de MVC.
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        var root = problem.RootElement;
        Assert.AreEqual((int)status, root.GetProperty("status").GetInt32());
        Assert.AreEqual(code, root.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.IsFalse(body.Contains("sentinel", StringComparison.Ordinal));
        Assert.IsFalse(root.TryGetProperty("errors", out _));
    }

    // A rota de conflito segue o fallback autenticado e não compartilha Authorization mutável do cliente.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
