using System.Net;
using System.Text.Json;
using Finora.Api.Controllers;
using Finora.Application.Authentication;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Captura logs/scopes/exceções em Trace no pipeline real; os únicos dados são sentinelas e credencial de teste descartável.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlAuthenticationPrivacyTests
{
    // Sucesso publica JWT somente no corpo autorizado; falhas/representações/logs não expõem nenhum material sensível.
    [TestMethod]
    public async Task RealLoginBearerAndOperationalErrorsDoNotLeakSensitiveSentinels()
    {
        var fault = new PrivacyFault();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationSessionReader>(provider => new PrivacyReader(
                new AuthenticationSessionReader(provider.GetRequiredService<FinoraDbContext>(),
                    provider.GetRequiredService<TimeProvider>()), fault)));
        const string email = "email-sentinel@example.invalid";
        const string password = "Password-sentinel!123";
        var user = await fixture.SeedUserAsync(email, password);
        var hash = (await fixture.ReadUserAsync(user)).PasswordHash!;
        using var login = await fixture.LoginAsync(email, password);
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        using var success = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = success.RootElement.GetProperty("accessToken").GetString()!;
        using var accepted = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        using var wrong = await fixture.LoginAsync(email, "wrong-password-sentinel");
        await AuthenticationAvailabilityTests.AssertProblemAsync(wrong, HttpStatusCode.Unauthorized);
        using var malformed = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, "malformed-jwt-sentinel");
        await PostgreSqlBearerTests.AssertChallengeAsync(malformed);

        var sentinels = new[] { email, email.ToUpperInvariant(), password, "wrong-password-sentinel", token,
            "Bearer " + token, hash, fixture.ConnectionString, fixture.Key, "malformed-jwt-sentinel", "exception-sentinel" };
        fault.ExceptionText = "exception-sentinel " + string.Join(' ', sentinels);
        using var unexpected = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        await AuthenticationAvailabilityTests.AssertProblemAsync(unexpected, HttpStatusCode.InternalServerError);
        fault.ExceptionText = null;
        await fixture.Context.Database.ExecuteSqlRawAsync("DROP TABLE \"AuthSessions\"");
        using var unavailable = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        await AuthenticationAvailabilityTests.AssertProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable);
        var errors = await wrong.Content.ReadAsStringAsync() + await malformed.Content.ReadAsStringAsync()
            + await unexpected.Content.ReadAsStringAsync() + await unavailable.Content.ReadAsStringAsync();
        foreach (var value in sentinels)
        {
            Assert.IsFalse(fixture.Factory.Logs.Text.Contains(value, StringComparison.Ordinal), "Log não pode conter material sensível.");
            Assert.IsFalse(errors.Contains(value, StringComparison.Ordinal), "Erro não pode refletir material sensível.");
        }
        Assert.AreEqual(1, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
        Assert.IsFalse(new LoginRequest { Email = email, Password = password }.ToString().Contains("sentinel", StringComparison.Ordinal));
        Assert.IsFalse(new LoginResponse(token, "Bearer", 1800, fixture.Clock.GetUtcNow().AddMinutes(30)).ToString().Contains(token, StringComparison.Ordinal));
    }

    // A falha arbitrária é injetada somente depois de login/consulta reais; não representa erro real do provider.
    private sealed class PrivacyFault
    {
        internal string? ExceptionText { get; set; }
    }

    private sealed class PrivacyReader(IAuthenticationSessionReader inner, PrivacyFault fault) : IAuthenticationSessionReader
    {
        public Task<bool> IsActiveAsync(AuthenticationSessionReference session, CancellationToken cancellationToken)
        {
            if (fault.ExceptionText is { } text) { throw new InvalidOperationException(text); }
            return inner.IsActiveAsync(session, cancellationToken);
        }
    }
}
