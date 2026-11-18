using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finora.Api.Authentication;
using Finora.Application.Authentication;
using Finora.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// As chaves/identificadores são sempre sintéticos e não dependem de User Secrets ou configuração operacional.
[TestClass]
public sealed class JwtIssuanceTests
{
    // Nenhum estado parcial/malformado se torna segredo padrão ou exceção contendo o valor recebido.
    [TestMethod]
    public void InvalidConfigurationIsUnavailableWithoutReflectingValues()
    {
        foreach (var key in new string?[] { null, "", "malformed-secret-sentinel", Convert.ToBase64String(new byte[31]) })
        {
            Assert.IsNull(JwtSettings.Read(Configuration(key)));
        }
        Assert.IsNull(JwtSettings.Read(Configuration(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), issuer: "")));
        Assert.IsNull(JwtSettings.Read(Configuration(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), audience: "")));
        Assert.IsNull(JwtSettings.Read(new ConfigurationBuilder().Build()));
    }

    // Inspeção de payload e assinatura real verifica claims mínimas, precisão e duração sem normalizações ocultas.
    [TestMethod]
    public async Task IssuedTokenHasOnlyApprovedClaimsAndValidHs256Signature()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var settings = JwtSettings.Read(Configuration(key))!;
        var issuer = new JwtAccessTokenIssuer(settings);
        var userId = Guid.NewGuid();
        var sessionId = AuthSession.GenerateId();
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var result = issuer.Issue(userId, sessionId, now, now.AddSeconds(1800));
        var token = new JsonWebToken(result.AccessToken);
        Assert.AreEqual("HS256", token.Alg);
        Assert.AreEqual("JWT", token.Typ);
        Assert.AreEqual(userId.ToString("D"), token.Subject);
        Assert.AreEqual(sessionId, token.Id);
        Assert.AreEqual(now.UtcDateTime, token.IssuedAt);
        Assert.AreEqual(now.AddSeconds(1800).UtcDateTime, token.ValidTo);
        Assert.AreEqual(now.AddSeconds(1800), result.ExpiresAtUtc);
        using var payload = JsonDocument.Parse(DecodePayload(result.AccessToken));
        CollectionAssert.AreEquivalent(new[] { "sub", "jti", "iss", "aud", "iat", "exp" },
            payload.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(result.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "issuer-sentinel", ValidAudience = "audience-sentinel",
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(key)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }, ClockSkew = TimeSpan.Zero
        });
        Assert.IsTrue(validation.IsValid);
        Assert.IsFalse(settings.ToString().Contains(key, StringComparison.Ordinal));
        Assert.IsFalse(settings.ToString().Contains("sentinel", StringComparison.Ordinal));
    }

    // Rejeita discrepâncias entre sessão e emissão sem registrar identificadores ou segredos na exceção.
    [TestMethod]
    public void InvalidEmissionContractFailsWithFixedMessage()
    {
        var issuer = new JwtAccessTokenIssuer(JwtSettings.Read(Configuration(Convert.ToBase64String(new byte[32])))!);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var error = Assert.ThrowsExactly<AuthenticationUnavailableException>(() =>
            issuer.Issue(Guid.NewGuid(), "sensitive-session-sentinel", now, now.AddSeconds(1800)));
        Assert.IsFalse(error.ToString().Contains("sensitive-session-sentinel", StringComparison.Ordinal));
        Assert.ThrowsExactly<AuthenticationUnavailableException>(() => issuer.Issue(Guid.Empty, AuthSession.GenerateId(), now, now.AddMinutes(30)));
        Assert.ThrowsExactly<AuthenticationUnavailableException>(() => issuer.Issue(Guid.NewGuid(), AuthSession.GenerateId(), now.AddTicks(1), now.AddMinutes(30)));
        Assert.ThrowsExactly<AuthenticationUnavailableException>(() => issuer.Issue(Guid.NewGuid(), AuthSession.GenerateId(), now, now.AddSeconds(1801)));
    }

    // Monta configuração isolada que não carrega arquivos locais ou variáveis do ambiente.
    internal static IConfiguration Configuration(string? key, string issuer = "issuer-sentinel", string audience = "audience-sentinel") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Jwt:Issuer"] = issuer,
            ["Authentication:Jwt:Audience"] = audience,
            ["Authentication:Jwt:SigningKeyBase64"] = key
        }).Build();

    // Decodificação de inspeção não participa da validação criptográfica nem permite autenticar um token.
    private static string DecodePayload(string token) => Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));
}
