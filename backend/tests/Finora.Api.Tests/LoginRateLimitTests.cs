using System.Net;
using System.Text;
using System.Text.Json;
using Finora.Api.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica a proteção de login antes do parsing/caso de uso, usando apenas limites e dependências locais sintéticas.
[TestClass]
[DoNotParallelize]
public sealed class LoginRateLimitTests
{
    // A solicitação excedente retorna 429 antes do corpo oversized e não muda de partição via X-Forwarded-For.
    [TestMethod]
    public async Task ExcessLoginIsRejectedBeforeBodyEvaluationAndIgnoresUntrustedForwardedFor()
    {
        var configuration = AuthenticationAvailabilityTests.Configuration();
        configuration["RateLimiting:Login:PermitLimit"] = "1";
        await using var factory = new AuthenticationApiFactory(configuration);
        using var client = factory.CreateClient();

        using var first = LoginRequest("{}");
        first.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.10");
        using var evaluated = await client.SendAsync(first);
        Assert.AreEqual(HttpStatusCode.BadRequest, evaluated.StatusCode);
        Assert.IsTrue(evaluated.Headers.CacheControl?.NoStore);

        using var excess = LoginRequest(new string('x', 16 * 1024 + 1));
        excess.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.20");
        using var limited = await client.SendAsync(excess);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsTrue(limited.Headers.CacheControl?.NoStore);
        Assert.AreEqual("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.AreEqual("RATE_LIMITED", problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(problem.RootElement.TryGetProperty("accessToken", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("errors", out _));
    }

    // Sem override, o limite aprovado admite dez solicitações por janela e rejeita a seguinte.
    [TestMethod]
    public async Task DefaultLoginLimitAllowsTenRequestsPerWindow()
    {
        await using var factory = new AuthenticationApiFactory(AuthenticationAvailabilityTests.Configuration());
        using var client = factory.CreateClient();

        for (var index = 0; index < 10; index++)
        {
            using var request = LoginRequest("{}");
            using var response = await client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"Request {index + 1} was unexpectedly limited.");
        }

        using var excess = LoginRequest("{}");
        using var limited = await client.SendAsync(excess);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsTrue(limited.Headers.CacheControl?.NoStore);
    }

    // Limites ausentes usam defaults; quando configurados, ambos precisam ser inteiros positivos.
    [TestMethod]
    [DataRow("RateLimiting:Login:PermitLimit", "0")]
    [DataRow("RateLimiting:Login:PermitLimit", "-1")]
    [DataRow("RateLimiting:Login:PermitLimit", "invalid")]
    [DataRow("RateLimiting:Login:WindowSeconds", "0")]
    [DataRow("RateLimiting:Login:WindowSeconds", "invalid")]
    [DataRow("RateLimiting:Protected:PermitLimit", "0")]
    [DataRow("RateLimiting:Protected:WindowSeconds", "invalid")]
    public void InvalidLoginLimitConfigurationFailsClosed(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = value
        }).Build();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ServiceCollection().AddFinoraRateLimiting(configuration));

        Assert.IsTrue(exception.Message.StartsWith(key, StringComparison.Ordinal));
    }

    // O corpo é criado apenas para testar o caminho HTTP; a rejeição deve ocorrer antes de sua leitura.
    private static HttpRequestMessage LoginRequest(string body) => new(HttpMethod.Post, "/api/auth/login")
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
