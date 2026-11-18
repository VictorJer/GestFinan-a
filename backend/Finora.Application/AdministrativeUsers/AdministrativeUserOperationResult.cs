namespace Finora.Application.AdministrativeUsers;

/// <summary>Contém somente o estado enumerado de uma operação administrativa de usuário.</summary>
public sealed record AdministrativeUserOperationResult(AdministrativeUserOperationState State);

/// <summary>Classifica conclusões e falhas conhecidas sem carregar descrições do Identity ou exceções.</summary>
public enum AdministrativeUserOperationState
{
    Created,
    AlreadyExists,
    PasswordReset,
    NotFound,
    InvalidEmail,
    PasswordPolicyViolation,
    Conflict,
    OperationalFailure
}
