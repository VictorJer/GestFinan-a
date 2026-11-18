namespace Finora.Application.AdministrativeUsers;

/// <summary>Representa um estado seguro da consulta prévia, sem entidade ou dados de credencial.</summary>
public sealed class AdministrativeUserPreflightResult
{
    /// <summary>Cria um resultado tipado e opcionalmente transporta o identificador interno para revalidação.</summary>
    public AdministrativeUserPreflightResult(
        AdministrativeUserPreflightState state,
        Guid? candidateUserId = null)
    {
        State = state;
        CandidateUserId = candidateUserId;
    }

    /// <summary>Expõe somente o estado que a camada de apresentação pode mapear para uma resposta fixa.</summary>
    public AdministrativeUserPreflightState State { get; }

    /// <summary>Transporta o titular esperado entre camadas; nunca deve ser incluído em saída ou logs.</summary>
    public Guid? CandidateUserId { get; }

    /// <summary>Redige o identificador para que logs acidentais não revelem dados internos.</summary>
    public override string ToString() =>
        $"{nameof(AdministrativeUserPreflightResult)} {{ State = {State} }}";
}

/// <summary>Classifica os resultados permitidos pela pré-consulta administrativa.</summary>
public enum AdministrativeUserPreflightState
{
    Ready,
    AlreadyExists,
    NotFound,
    InvalidEmail,
    Conflict
}
