using Finora.Application.Authentication;
using Finora.Infrastructure.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Finora.Api.Authentication;

/// <summary>Usa assinatura da biblioteca IdentityModel, sem persistir ou publicar o token antes do commit.</summary>
public sealed class JwtAccessTokenIssuer(JwtSettings settings) : IAccessTokenIssuer
{
    // Criação e expiração são compartilhadas com a sessão e devem representar exatamente 30 minutos UTC.
    public PreparedAccessToken Issue(Guid userId, string sessionId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        if (userId == Guid.Empty || !AuthSession.IsValidId(sessionId)
            || createdAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero
            || createdAtUtc.Ticks % TimeSpan.TicksPerSecond != 0 || expiresAtUtc != createdAtUtc.AddSeconds(1800))
        {
            throw new AuthenticationUnavailableException();
        }

        // Desativa tempos implícitos para não acrescentar nbf ou substituir o instante canônico da sessão.
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = createdAtUtc.UtcDateTime,
            Expires = expiresAtUtc.UtcDateTime,
            TokenType = "JWT",
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString("D"),
                [JwtRegisteredClaimNames.Jti] = sessionId
            },
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256)
        };
        return new PreparedAccessToken(handler.CreateToken(descriptor), expiresAtUtc);
    }
}
