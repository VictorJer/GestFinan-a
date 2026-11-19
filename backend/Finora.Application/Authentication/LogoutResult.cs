namespace Finora.Application.Authentication;

/// <summary>Classifica o logout sem transportar detalhes de persistência ou dados da sessão.</summary>
public enum LogoutState
{
    Confirmed,
    InvalidSession,
    Unavailable
}

/// <summary>Resultado tipado da tentativa de revogação da sessão autenticada.</summary>
public sealed record LogoutResult(LogoutState State)
{
    // O estado é suficiente para a borda HTTP; nenhuma referência ou exceção acompanha o resultado.
    public override string ToString() => $"LogoutResult {{ State = {State} }}";
}
