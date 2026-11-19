namespace Finora.Application.Authentication;

/// <summary>Valida credenciais e confirma a sessão antes de retornar um token utilizável.</summary>
public interface IAuthenticationLogin
{
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken);
}

/// <summary>Prepara em memória o token; não confirma nem persiste a sessão.</summary>
public interface IAccessTokenIssuer
{
    PreparedAccessToken Issue(Guid userId, string sessionId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc);
}

/// <summary>Consulta o estado persistente em cada chamada, após validação criptográfica.</summary>
public interface IAuthenticationSessionReader
{
    Task<bool> IsActiveAsync(AuthenticationSessionReference session, CancellationToken cancellationToken);
}

/// <summary>Revoga a sessão identificada pela autenticação validada, sem receber um token JWT.</summary>
public interface IAuthenticationSessionRevoker
{
    Task<LogoutResult> RevokeAsync(AuthenticationSessionReference session, CancellationToken cancellationToken);
}

/// <summary>Identifica a sessão sem transportar token completo ou dados pessoais adicionais.</summary>
public sealed record AuthenticationSessionReference(
    Guid UserId, string SessionId, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{
    // Também os identificadores técnicos são omitidos em representações textuais incidentais.
    public override string ToString() => "AuthenticationSessionReference { [redacted] }";
}
