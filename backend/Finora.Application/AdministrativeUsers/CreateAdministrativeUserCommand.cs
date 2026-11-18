using MediatR;

namespace Finora.Application.AdministrativeUsers;

/// <summary>Solicita provisionamento administrativo sem colocar credenciais em representação serializável.</summary>
public sealed class CreateAdministrativeUserCommand : IRequest<AdministrativeUserOperationResult>
{
    /// <summary>Recebe credenciais transitórias para encaminhá-las ao contrato sem alterá-las.</summary>
    public CreateAdministrativeUserCommand(string email, string password)
    {
        Email = email ?? throw new ArgumentNullException(nameof(email));
        Password = password ?? throw new ArgumentNullException(nameof(password));
    }

    internal string Email { get; }

    internal string Password { get; }

    /// <summary>Redige e-mail e senha mesmo se um logger chamar a representação textual do request.</summary>
    public override string ToString() =>
        $"{nameof(CreateAdministrativeUserCommand)} {{ Email = [redacted], Password = [redacted] }}";
}

/// <summary>Encaminha o pedido de criação sem capturar exceções nem alterar o segredo.</summary>
public sealed class CreateAdministrativeUserHandler(IAdministrativeUserAccess access)
    : IRequestHandler<CreateAdministrativeUserCommand, AdministrativeUserOperationResult>
{
    /// <summary>Delega os dados transitórios e preserva o token de cancelamento.</summary>
    public Task<AdministrativeUserOperationResult> Handle(
        CreateAdministrativeUserCommand request,
        CancellationToken cancellationToken) =>
        access.CreateAsync(request.Email, request.Password, cancellationToken);
}
