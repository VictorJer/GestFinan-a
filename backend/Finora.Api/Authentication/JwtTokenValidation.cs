using System.Text.Json;
using System.Globalization;
using System.Security.Claims;
using Finora.Application.Authentication;
using Finora.Infrastructure.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Finora.Api.Authentication;

// Criptografia e semântica obrigatória são validadas antes de qualquer consulta ao estado persistido.
public static class JwtTokenValidation
{
    // O relógio injetado é compartilhado com emissão/consulta; não há janela adicional após expiração.
    public static TokenValidationParameters Parameters(JwtSettings settings, TimeProvider clock) => new()
    {
        ValidateIssuerSigningKey = true, RequireSignedTokens = true,
        IssuerSigningKey = settings.SigningKey, ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        ValidateIssuer = true, ValidIssuer = settings.Issuer,
        ValidateAudience = true, ValidAudience = settings.Audience, IgnoreTrailingSlashWhenValidatingAudience = false,
        ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.Zero,
        ValidTypes = new[] { "JWT" }, NameClaimType = "sub",
        IncludeTokenOnFailedValidation = false, LogValidationExceptions = false,
        LifetimeValidator = (notBefore, expires, _, _) => expires.HasValue && expires.Value > clock.GetUtcNow().UtcDateTime
            && (!notBefore.HasValue || notBefore.Value <= clock.GetUtcNow().UtcDateTime)
    };

    // Inspeciona JSON original para não aceitar claims duplicadas que um parser possa sobrescrever silenciosamente.
    public static AuthenticationSessionReference? ReadSession(JsonWebToken token, TimeProvider clock)
    {
        try
        {
            // Reabre o JSON assinado para detectar ambiguidades ocultadas pela materialização da biblioteca.
            var parts = token.EncodedToken.Split('.');
            if (parts.Length != 3) { return null; }
            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
            if (!HasUniqueProperties(header.RootElement) || !HasUniqueProperties(payload.RootElement)) { return null; }
            var root = payload.RootElement;
            var required = new[] { "sub", "jti", "iss", "aud", "iat", "exp" };
            if (required.Any(name => !root.TryGetProperty(name, out _))) { return null; }
            if (root.GetProperty("sub").ValueKind != JsonValueKind.String
                || root.GetProperty("jti").ValueKind != JsonValueKind.String
                || root.GetProperty("iss").ValueKind != JsonValueKind.String
                || root.GetProperty("aud").ValueKind != JsonValueKind.String) { return null; }
            // Identidade e jti precisam estar em representação canônica antes de servir como chave de consulta.
            var subject = root.GetProperty("sub").GetString();
            var id = root.GetProperty("jti").GetString();
            if (!Guid.TryParseExact(subject, "D", out var userId) || userId == Guid.Empty
                || userId.ToString("D") != subject || !AuthSession.IsValidId(id)) { return null; }
            var issued = root.GetProperty("iat");
            var expiration = root.GetProperty("exp");
            if (issued.ValueKind != JsonValueKind.Number || expiration.ValueKind != JsonValueKind.Number
                || !issued.TryGetInt64(out var iat) || !expiration.TryGetInt64(out var exp)) { return null; }
            // Converte somente datas inteiras e exige a mesma duração nominal persistida para a sessão.
            var created = DateTimeOffset.FromUnixTimeSeconds(iat);
            var expires = DateTimeOffset.FromUnixTimeSeconds(exp);
            if (expires - created != TimeSpan.FromSeconds(1800) || created > clock.GetUtcNow() || expires <= clock.GetUtcNow()) { return null; }
            return new AuthenticationSessionReference(userId, id!, created, expires);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // A action de logout aceita somente a identidade já autenticada e recusa claims ausentes/duplicadas.
    public static AuthenticationSessionReference? ReadSessionClaims(ClaimsPrincipal principal, TimeProvider clock)
    {
        var subject = ReadSingleClaim(principal, "sub");
        var sessionId = ReadSingleClaim(principal, "jti");
        var issuedValue = ReadSingleClaim(principal, "iat");
        var expirationValue = ReadSingleClaim(principal, "exp");
        if (!Guid.TryParseExact(subject, "D", out var userId) || userId == Guid.Empty
            || !string.Equals(userId.ToString("D"), subject, StringComparison.Ordinal)
            || !AuthSession.IsValidId(sessionId)
            || !long.TryParse(issuedValue, NumberStyles.None, CultureInfo.InvariantCulture, out var issued)
            || !long.TryParse(expirationValue, NumberStyles.None, CultureInfo.InvariantCulture, out var expiration))
        {
            return null;
        }

        try
        {
            var createdAtUtc = DateTimeOffset.FromUnixTimeSeconds(issued);
            var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiration);
            if (expiresAtUtc - createdAtUtc != TimeSpan.FromSeconds(1800)
                || createdAtUtc > clock.GetUtcNow() || expiresAtUtc <= clock.GetUtcNow())
            {
                return null;
            }

            return new AuthenticationSessionReference(userId, sessionId!, createdAtUtc, expiresAtUtc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // Claims obrigatórias precisam aparecer uma única vez para impedir seleção ambígua de identidade/sessão.
    private static string? ReadSingleClaim(ClaimsPrincipal principal, string claimType)
    {
        var claims = principal.FindAll(claimType).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }

    // Rejeita objetos ambíguos, incluindo cabeçalhos repetidos, sem refletir o token nas mensagens.
    private static bool HasUniqueProperties(JsonElement element) => element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count()
            == element.EnumerateObject().Count();
}
