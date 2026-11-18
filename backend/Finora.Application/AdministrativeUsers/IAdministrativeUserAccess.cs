namespace Finora.Application.AdministrativeUsers;

/// <summary>Contrato de aplicação para pré-consulta e mutações administrativas implementadas pela Infrastructure.</summary>
public interface IAdministrativeUserAccess
{
    /// <summary>Consulta a elegibilidade sem manter recursos de banco durante a interação do operador.</summary>
    Task<AdministrativeUserPreflightResult> PreflightAsync(
        AdministrativeUserAction action,
        string email,
        CancellationToken cancellationToken);

    /// <summary>Solicita a criação do titular sem expor detalhes de Identity no resultado.</summary>
    Task<AdministrativeUserOperationResult> CreateAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    /// <summary>Redefine a senha somente se o usuário ainda corresponder à pré-consulta recebida.</summary>
    Task<AdministrativeUserOperationResult> ResetPasswordAsync(
        string email,
        Guid expectedUserId,
        string newPassword,
        CancellationToken cancellationToken);
}
