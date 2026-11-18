using MediatR;

namespace Finora.Application.AdministrativeUsers;

/// <summary>Solicita a verificação de disponibilidade para criação ou redefinição administrativa.</summary>
public sealed class CheckAdministrativeUserPreflightQuery
    : IRequest<AdministrativeUserPreflightResult>
{
    /// <summary>Cria a consulta sem tornar e-mail ou credenciais parte de uma representação textual.</summary>
    public CheckAdministrativeUserPreflightQuery(AdministrativeUserAction action, string email)
    {
        Action = action;
        Email = email ?? throw new ArgumentNullException(nameof(email));
    }

    internal AdministrativeUserAction Action { get; }

    internal string Email { get; }

    /// <summary>Redige o e-mail para que logs acidentais não revelem PII.</summary>
    public override string ToString() =>
        $"{nameof(CheckAdministrativeUserPreflightQuery)} {{ Action = {Action}, Email = [redacted] }}";
}

/// <summary>Encaminha a consulta ao contrato de aplicação e preserva o cancelamento do chamador.</summary>
public sealed class CheckAdministrativeUserPreflightHandler(IAdministrativeUserAccess access)
    : IRequestHandler<CheckAdministrativeUserPreflightQuery, AdministrativeUserPreflightResult>
{
    /// <summary>Delega a consulta sem iniciar transação ou capturar falhas de persistência.</summary>
    public Task<AdministrativeUserPreflightResult> Handle(
        CheckAdministrativeUserPreflightQuery request,
        CancellationToken cancellationToken) =>
        access.PreflightAsync(request.Action, request.Email, cancellationToken);
}
