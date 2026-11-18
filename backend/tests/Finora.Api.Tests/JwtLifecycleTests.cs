using System.Security.Cryptography;
using Finora.Api.Authentication;
using Finora.Infrastructure.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Fronteiras temporais/rotação usam relógio e chaves sintéticos, sem alterar configuração operacional.
[TestClass]
public sealed class JwtLifecycleTests
{
    // Um novo emissor/validador com a mesma chave valida o token, e troca de chave o rejeita imediatamente.
    [TestMethod]
    public async Task StableKeySurvivesRecompositionAndRotationRejectsPreviousSignature()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var clock = new AuthenticationTestClock(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var token = new JwtAccessTokenIssuer(JwtSettings.Read(JwtIssuanceTests.Configuration(key))!)
            .Issue(Guid.NewGuid(), AuthSession.GenerateId(), clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(30)).AccessToken;
        var restartedSettings = JwtSettings.Read(JwtIssuanceTests.Configuration(key))!;
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, JwtTokenValidation.Parameters(restartedSettings, clock));
        Assert.IsTrue(validation.IsValid);
        Assert.IsNotNull(JwtTokenValidation.ReadSession(new JsonWebToken(token), clock));
        var rotated = JwtSettings.Read(JwtIssuanceTests.Configuration(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))))!;
        Assert.IsFalse((await new JsonWebTokenHandler().ValidateTokenAsync(token, JwtTokenValidation.Parameters(rotated, clock))).IsValid);
    }

    // Igualdade com expiração já nega acesso e emissão futura não é aceita mesmo quando a assinatura é correta.
    [TestMethod]
    public async Task ExactExpirationAndFutureIssuanceAreRejectedWithoutSleep()
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var clock = new AuthenticationTestClock(now);
        var settings = JwtSettings.Read(JwtIssuanceTests.Configuration(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))))!;
        var issuer = new JwtAccessTokenIssuer(settings);
        var token = issuer.Issue(Guid.NewGuid(), AuthSession.GenerateId(), now, now.AddMinutes(30)).AccessToken;
        clock.Set(now.AddMinutes(30).AddTicks(-1));
        Assert.IsTrue((await new JsonWebTokenHandler().ValidateTokenAsync(token, JwtTokenValidation.Parameters(settings, clock))).IsValid);
        clock.Set(now.AddMinutes(30));
        Assert.IsFalse((await new JsonWebTokenHandler().ValidateTokenAsync(token, JwtTokenValidation.Parameters(settings, clock))).IsValid);
        Assert.IsNull(JwtTokenValidation.ReadSession(new JsonWebToken(token), clock));
        clock.Set(now.AddSeconds(-1));
        Assert.IsNull(JwtTokenValidation.ReadSession(new JsonWebToken(token), clock));
    }
}
