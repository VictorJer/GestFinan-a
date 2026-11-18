using System.Security.Cryptography;

namespace Finora.Infrastructure.Identity;

/// <summary>Estado mínimo de autorização persistida; não armazena token ou dados pessoais adicionais.</summary>
public sealed class AuthSession
{
    // Construtor reservado à materialização EF, sem gerar nova identidade durante leitura.
    private AuthSession() { }

    public string Id { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    // Permite fornecer o mesmo identificador à emissão JWT antes de confirmar a transação da sessão.
    public static AuthSession Create(string id, Guid userId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        var session = new AuthSession
        {
            Id = id,
            UserId = userId,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
        session.ValidateForPersistence();
        return session;
    }

    // O gerador usa 256 bits de aleatoriedade do sistema, sem derivar identidade de usuário ou timestamp.
    public static string GenerateId() => Encode(RandomNumberGenerator.GetBytes(32));

    // Confere também os bits finais canônicos, não somente o alfabeto e o comprimento visíveis.
    public static bool IsValidId(string? id)
    {
        if (id is null || id.Length != 43 || id.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }
        var bytes = Convert.FromBase64String(id.Replace('-', '+').Replace('_', '/') + "=");
        return bytes.Length == 32 && string.Equals(Encode(bytes), id, StringComparison.Ordinal);
    }

    // A barreira de gravação revalida alterações feitas pelo tracking, além da fábrica inicial.
    public void ValidateForPersistence()
    {
        if (!IsValidId(Id) || UserId == Guid.Empty || CreatedAtUtc.Offset != TimeSpan.Zero
            || ExpiresAtUtc.Offset != TimeSpan.Zero || ExpiresAtUtc <= CreatedAtUtc
            || (RevokedAtUtc is { } revoked && (revoked.Offset != TimeSpan.Zero || revoked < CreatedAtUtc)))
        {
            throw new InvalidOperationException("Invalid authentication session.");
        }
    }

    // Base64url sem padding mantém representação opaca e compatível com o jti.
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Evita publicar identificadores incidentais em logs de entidades.
    public override string ToString() => "AuthSession { [redacted] }";
}
