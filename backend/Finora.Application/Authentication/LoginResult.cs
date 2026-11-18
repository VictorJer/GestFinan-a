namespace Finora.Application.Authentication;

/// <summary>Distingue falha de credencial de indisponibilidade sem carregar exceção, e-mail ou detalhes Identity.</summary>
public enum LoginState { Success, InvalidCredentials, Unavailable }

/// <summary>Contém o token apenas em sucesso confirmado; sua representação textual nunca o revela.</summary>
public sealed class LoginResult
{
    // As fábricas impedem resultados de erro com token ou sucesso sem material emitido.
    private LoginResult(LoginState state, PreparedAccessToken? token)
    {
        State = state;
        Token = token;
    }

    public LoginState State { get; }
    public PreparedAccessToken? Token { get; }

    // O contrato não permite distinguir conta ausente, senha errada ou bloqueio por esse resultado.
    public static LoginResult InvalidCredentials() => new(LoginState.InvalidCredentials, null);
    public static LoginResult Unavailable() => new(LoginState.Unavailable, null);
    public static LoginResult Success(PreparedAccessToken token) =>
        new(LoginState.Success, token ?? throw new ArgumentNullException(nameof(token)));

    // Mantém somente o estado não sensível, mesmo quando há um token associado.
    public override string ToString() => $"LoginResult {{ State = {State}, Token = [redacted] }}";
}

/// <summary>Material emitido transitório; não implica que o commit da sessão tenha sido confirmado.</summary>
public sealed class PreparedAccessToken
{
    // Valida o contrato temporal nominal sem interpretar ou registrar o conteúdo do token.
    public PreparedAccessToken(string accessToken, DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        AccessToken = accessToken;
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
    }

    public string AccessToken { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public int ExpiresIn => 1800;
    public string TokenType => "Bearer";

    // Não permitir que interpolação ou logs de resultados exponham o JWT completo.
    public override string ToString() => "PreparedAccessToken { AccessToken = [redacted] }";
}

/// <summary>Sinaliza indisponibilidade conhecida sem transportar a exceção original até a resposta HTTP.</summary>
public sealed class AuthenticationUnavailableException : Exception
{
    // Mensagem fixa evita vazamento de conexão, configuração, credenciais ou detalhes do provider.
    public AuthenticationUnavailableException() : base("Authentication is unavailable.") { }
}
