using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Garante 500/503 sanitizados, tratamento do cancelamento e respeito a respostas já iniciadas.
[TestClass]
[DoNotParallelize]
public sealed class ApiExceptionBoundaryTests
{
    // Exceção inesperada e indisponibilidade tipada geram códigos distintos sem refletir sentinelas em corpo/logs.
    [TestMethod]
    public async Task UnexpectedAndKnownUnavailableFailuresAreSanitized()
    {
        await using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        using var unexpected = await client.GetAsync("/_tests/exception-probe/unexpected");
        await AssertProblemAsync(unexpected, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        using var unavailable = await client.GetAsync("/_tests/exception-probe/unavailable");
        await AssertProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE");

        AssertNoSentinels(await unexpected.Content.ReadAsStringAsync());
        AssertNoSentinels(await unavailable.Content.ReadAsStringAsync());
        AssertNoSentinels(factory.Logs.Text);
    }

    // O cliente cancela a chamada e a resposta iniciada não pode ser substituída pelo middleware.
    [TestMethod]
    public async Task CancellationAbortsAndStartedResponseIsNotRewritten()
    {
        await using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetAsync("/_tests/exception-probe/cancel", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => { using var ignored = await pending; });

        try
        {
            using var started = await client.GetAsync("/_tests/exception-probe/started");
            Assert.AreEqual(HttpStatusCode.OK, started.StatusCode);
            Assert.AreEqual("partial-response", await started.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException) { }

        AssertNoSentinels(factory.Logs.Text);
    }

    // Confere envelope e rastreabilidade sem incluir exceção, stack trace ou dados externos.
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        AssertNoSentinels(body);
    }

    // Os valores de teste não podem aparecer nem na resposta nem nos logs capturados pelo host real.
    private static void AssertNoSentinels(string text)
    {
        foreach (var sentinel in new[] { "password-sentinel", "token-sentinel", "SQL-sentinel", "provider-sentinel" })
        {
            Assert.IsFalse(text.Contains(sentinel, StringComparison.OrdinalIgnoreCase));
        }
    }
}
