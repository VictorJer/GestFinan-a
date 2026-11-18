using MediatR;

namespace Finora.Application.AdministrativeUsers;

/// <summary>Solicita reset administrativo amarrado ao identificador visto na pré-consulta.</summary>
public sealed class ResetAdministrativeUserPasswordCommand
    : IRequest<AdministrativeUserOperationResult>
{
    /// <summary>Recebe a senha transitória e o identificador esperado sem criar uma representação reveladora.</summary>
    public ResetAdministrativeUserPasswordCommand(
        string email,
        Guid expectedUserId,
        string newPassword)
    {
        Email = email ?? throw new ArgumentNullException(nameof(email));
        ExpectedUserId = expectedUserId;
        NewPassword = newPassword ?? throw new ArgumentNullException(nameof(newPassword));
    }

    internal string Email { get; }

    internal Guid ExpectedUserId { get; }

    internal string NewPassword { get; }

    /// <summary>Redige e-mail, identificador e senha em qualquer log textual acidental.</summary>
    public override string ToString() =>
        $"{nameof(ResetAdministrativeUserPasswordCommand)} {{ Email = [redacted], UserId = [redacted], Password = [redacted] }}";
}

/// <summary>Encaminha o reset sem acessar Identity ou persistência na camada Application.</summary>
public sealed class ResetAdministrativeUserPasswordHandler(IAdministrativeUserAccess access)
    : IRequestHandler<ResetAdministrativeUserPasswordCommand, AdministrativeUserOperationResult>
{
    /// <summary>Delega a revalidação esperada e preserva o cancelamento do chamador.</summary>
    public Task<AdministrativeUserOperationResult> Handle(
        ResetAdministrativeUserPasswordCommand request,
        CancellationToken cancellationToken) =>
        access.ResetPasswordAsync(
            request.Email,
            request.ExpectedUserId,
            request.NewPassword,
            cancellationToken);
}
