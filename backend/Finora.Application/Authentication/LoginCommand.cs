using MediatR;

namespace Finora.Application.Authentication;

/// <summary>Transporta credenciais transitórias sem expô-las na representação textual ou serialização pública.</summary>
public sealed class LoginCommand : IRequest<LoginResult>
{
    // A normalização pertence ao adaptador Identity; a senha não pode sofrer transformação no transporte.
    public LoginCommand(string email, string password)
    {
        Email = email ?? throw new ArgumentNullException(nameof(email));
        Password = password ?? throw new ArgumentNullException(nameof(password));
    }

    internal string Email { get; }
    internal string Password { get; }

    // Evita que logging genérico de requests registre credenciais ou dados pessoais.
    public override string ToString() => "LoginCommand { Email = [redacted], Password = [redacted] }";
}

/// <summary>Delega autenticação sem acoplar o caso de uso a Identity, persistência ou formato JWT.</summary>
public sealed class LoginHandler(IAuthenticationLogin login) : IRequestHandler<LoginCommand, LoginResult>
{
    // Preserva o cancelamento HTTP e deixa falhas inesperadas para a borda responsável pela resposta sanitizada.
    public Task<LoginResult> Handle(LoginCommand request, CancellationToken cancellationToken) =>
        login.LoginAsync(request.Email, request.Password, cancellationToken);
}
