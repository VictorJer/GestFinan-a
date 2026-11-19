using MediatR;

namespace Finora.Application.Authentication;

/// <summary>Solicita revogação somente da sessão confiável derivada das claims já validadas.</summary>
public sealed class LogoutCommand : IRequest<LogoutResult>
{
    // A referência técnica não fica exposta em propriedades públicas serializáveis ou logs genéricos.
    public LogoutCommand(AuthenticationSessionReference session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    internal AuthenticationSessionReference Session { get; }

    // Identificadores da sessão são omitidos de qualquer representação textual incidental.
    public override string ToString() => "LogoutCommand { Session = [redacted] }";
}

/// <summary>Delega a revogação ao contrato de persistência e preserva o cancelamento do chamador.</summary>
public sealed class LogoutHandler(IAuthenticationSessionRevoker revoker)
    : IRequestHandler<LogoutCommand, LogoutResult>
{
    // A Application não interpreta o estado HTTP nem captura falhas da infraestrutura.
    public Task<LogoutResult> Handle(LogoutCommand request, CancellationToken cancellationToken) =>
        revoker.RevokeAsync(request.Session, cancellationToken);
}
