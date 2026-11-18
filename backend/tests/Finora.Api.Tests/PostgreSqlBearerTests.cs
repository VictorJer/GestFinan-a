using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Finora.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Exercita o pipeline JwtBearer real e comprova a ausência de execução protegida em cada rejeição.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlBearerTests
{
    // O instante válido anterior ao SQL não autoriza a ação se a sessão expira enquanto a leitura está bloqueada.
    [TestMethod]
    public async Task ExpirationWhileSessionLookupWaitsForDatabaseStillRejectsProtectedAction()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var expiration = new DateTimeOffset(new JsonWebToken(token).ValidTo, TimeSpan.Zero);
        fixture.Clock.Set(expiration.AddSeconds(-1));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var held = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = held;
        command.CommandText = "LOCK TABLE \"AuthSessions\" IN ACCESS EXCLUSIVE MODE";
        await command.ExecuteNonQueryAsync();
        var pending = PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        await fixture.WaitForSessionReaderAsync();
        fixture.Clock.Set(expiration);
        await held.RollbackAsync();
        using var response = await pending;
        await AssertChallengeAsync(response);
        Assert.AreEqual(0, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // Banco/schema ausente torna visível qualquer consulta indevida antes das rejeições criptográficas/semânticas.
    [TestMethod]
    public async Task InvalidTokensAreRejectedBeforeDatabaseAndNeverExecuteProtectedAction()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var user = await fixture.SeedUserAsync();
        var valid = await fixture.TokenAsync();
        var original = Payload(fixture, user, new JsonWebToken(valid).Id);
        var cases = new List<string> { "malformed-token-sentinel", Tamper(valid),
            Unsigned(JsonSerializer.Serialize(original)), Sign(fixture.Key, original, SecurityAlgorithms.HmacSha512),
            Sign(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)), original) };
        // Os mesmos valores duplicados também são ambíguos, não somente duplicatas conflitantes.
        foreach (var name in new[] { "sub", "jti", "iss", "aud", "iat", "exp" })
        {
            var missingClaim = new Dictionary<string, object>(original);
            missingClaim.Remove(name);
            cases.Add(Sign(fixture.Key, missingClaim));
            var json = JsonSerializer.Serialize(original);
            var property = JsonSerializer.Serialize(name) + ":" + JsonSerializer.Serialize(original[name]);
            cases.Add(SignRaw(fixture.Key, json.Replace(property, property + "," + property, StringComparison.Ordinal)));
        }
        foreach (var change in new (string Name, object Value)[]
        {
            ("iss", "wrong-issuer-sentinel"), ("aud", "wrong-audience-sentinel"), ("aud", "audience-sentinel/"),
            ("aud", new[] { "audience-sentinel" }), ("sub", Guid.Empty.ToString("D")),
            ("sub", user.ToString("N")), ("sub", "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA"),
            ("jti", "bad-session-sentinel"), ("iat", "not-a-number-sentinel"), ("exp", "not-a-number-sentinel")
        })
        {
            var payload = new Dictionary<string, object>(original) { [change.Name] = change.Value };
            cases.Add(Sign(fixture.Key, payload));
        }
        var expired = new Dictionary<string, object>(original)
        {
            ["iat"] = fixture.Clock.GetUtcNow().ToUnixTimeSeconds() - 1800,
            ["exp"] = fixture.Clock.GetUtcNow().ToUnixTimeSeconds()
        };
        cases.Add(Sign(fixture.Key, expired));
        var future = new Dictionary<string, object>(original)
        {
            ["iat"] = fixture.Clock.GetUtcNow().ToUnixTimeSeconds() + 1,
            ["exp"] = fixture.Clock.GetUtcNow().ToUnixTimeSeconds() + 1801
        };
        cases.Add(Sign(fixture.Key, future));
        await fixture.Context.Database.ExecuteSqlRawAsync("DROP TABLE \"AuthSessions\"");
        using var missing = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client);
        await AssertChallengeAsync(missing);
        foreach (var token in cases)
        {
            using var response = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
            await AssertChallengeAsync(response);
        }
        Assert.AreEqual(0, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // Reiniciar host com configuração estável preserva sessão real; rotação coordenada rejeita a assinatura anterior.
    [TestMethod]
    public async Task RealSessionSurvivesHostRestartAndKeyRotationOrExactExpirationRejectsAccess()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await using var restarted = fixture.NewFactory();
        using var client = restarted.CreateClient();
        using var afterRestart = await PostgreSqlAuthenticationFixture.ProtectedAsync(client, token);
        Assert.AreEqual(HttpStatusCode.OK, afterRestart.StatusCode);
        await using var rotated = fixture.NewFactory(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        using var rotatedClient = rotated.CreateClient();
        using var afterRotation = await PostgreSqlAuthenticationFixture.ProtectedAsync(rotatedClient, token);
        await AssertChallengeAsync(afterRotation);
        Assert.AreEqual(0, rotated.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
        var expiration = new DateTimeOffset(new JsonWebToken(token).ValidTo, TimeSpan.Zero);
        fixture.Clock.Set(expiration.AddTicks(-1));
        using var beforeExpiration = await PostgreSqlAuthenticationFixture.ProtectedAsync(client, token);
        Assert.AreEqual(HttpStatusCode.OK, beforeExpiration.StatusCode);
        fixture.Clock.Set(expiration);
        using var atExpiration = await PostgreSqlAuthenticationFixture.ProtectedAsync(client, token);
        await AssertChallengeAsync(atExpiration);
        Assert.AreEqual(2, restarted.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // Sessões ausentes, de outro titular ou com prazos incompatíveis não estabelecem identidade confiável.
    [TestMethod]
    public async Task MissingMismatchedOrExpiredSessionIsRejected()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var owner = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var jwt = new JsonWebToken(token);
        var other = await fixture.SeedUserAsync("other@example.invalid");
        foreach (var payload in new[] { Payload(fixture, owner, AuthSession.GenerateId()), Payload(fixture, other, jwt.Id) })
        {
            using var response = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, Sign(fixture.Key, payload));
            await AssertChallengeAsync(response);
        }
        var created = fixture.Clock.GetUtcNow();
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AuthSessions\" SET \"CreatedAtUtc\"={created.AddSeconds(-1)} WHERE \"Id\"={jwt.Id}");
        using var mismatched = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        await AssertChallengeAsync(mismatched);
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AuthSessions\" SET \"CreatedAtUtc\"={created}, \"ExpiresAtUtc\"={created.AddSeconds(1)} WHERE \"Id\"={jwt.Id}");
        fixture.Clock.Set(created.AddSeconds(2));
        using var expired = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        await AssertChallengeAsync(expired);
        await fixture.Context.Database.ExecuteSqlRawAsync("DELETE FROM \"AuthSessions\"");
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AspNetUsers\" WHERE \"Id\"={owner}");
        using var deleted = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        await AssertChallengeAsync(deleted);
        Assert.AreEqual(0, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // Estado muda entre requisições com o mesmo JWT; não há cache positivo que preserve autorização revogada.
    [TestMethod]
    public async Task NextCallReadsRevocationFromDatabaseAndDoesNotExecuteAgain()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var owner = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        using var first = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        using var identity = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.AreEqual(owner.ToString("D"), identity.RootElement.GetProperty("subject").GetString());
        var jti = new JsonWebToken(token).Id;
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AuthSessions\" SET \"RevokedAtUtc\"={fixture.Clock.GetUtcNow()} WHERE \"Id\"={jti}");
        using var revoked = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        await AssertChallengeAsync(revoked);
        Assert.AreEqual(1, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // Lockout impede novas emissões, mas não substitui revogação da sessão anterior ainda ativa.
    [TestMethod]
    public async Task LoginLockoutDoesNotRevokeExistingSession()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        for (var index = 0; index < 5; index++)
        {
            using var rejected = await fixture.LoginAsync(password: "wrong-sentinel");
            Assert.AreEqual(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        using var access = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.OK, access.StatusCode);
    }

    // Gera fixtures assinadas pela própria biblioteca; não implementa criptografia alternativa na aplicação.
    internal static Dictionary<string, object> Payload(PostgreSqlAuthenticationFixture fixture, Guid userId, string sessionId) => new()
    {
        ["sub"] = userId.ToString("D"), ["jti"] = sessionId, ["iss"] = "issuer-sentinel", ["aud"] = "audience-sentinel",
        ["iat"] = fixture.Clock.GetUtcNow().ToUnixTimeSeconds(), ["exp"] = fixture.Clock.GetUtcNow().AddMinutes(30).ToUnixTimeSeconds()
    };

    internal static string Sign(string key, Dictionary<string, object> payload, string algorithm = SecurityAlgorithms.HmacSha256) =>
        SignRaw(key, JsonSerializer.Serialize(payload), algorithm);

    // Payload bruto preserva duplicatas deliberadas para provar rejeição antes de qualquer consulta ao banco.
    private static string SignRaw(string key, string payload, string algorithm = SecurityAlgorithms.HmacSha256) =>
        new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(payload,
            new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(key)), algorithm));

    // Token sem assinatura é uma entrada adversarial sintética, não uma opção de emissão operacional.
    private static string Unsigned(string payload) => Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}")
        + "." + Base64UrlEncoder.Encode(payload) + ".";

    // Altera um byte representado da assinatura, evitando depender de bits de padding do último caractere.
    internal static string Tamper(string token)
    {
        var parts = token.Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        return string.Join('.', parts);
    }

    // O challenge não expõe a causa da rejeição nem descrições internas do IdentityModel.
    internal static async Task AssertChallengeAsync(HttpResponseMessage response)
    {
        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.AreEqual("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.IsNull(response.Headers.WwwAuthenticate.Single().Parameter);
    }
}
