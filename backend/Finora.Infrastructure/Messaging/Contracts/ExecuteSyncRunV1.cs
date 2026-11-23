namespace Finora.Infrastructure.Messaging.Contracts;

/// <summary>Comando mínimo e versionado para localizar uma execução persistida sem transportar dados financeiros.</summary>
public sealed record ExecuteSyncRunV1(
    int ContractVersion,
    Guid SyncRunId,
    DateTimeOffset EmittedAtUtc);

/// <summary>Cria e valida o envelope V1 antes que ele atravesse a fronteira de mensageria.</summary>
public static class ExecuteSyncRunV1Contract
{
    public const int CurrentVersion = 1;

    // A fábrica fixa a versão e normaliza o instante sem alterar o momento originalmente registrado.
    public static ExecuteSyncRunV1 Create(Guid syncRunId, DateTimeOffset emittedAt)
    {
        if (syncRunId == Guid.Empty)
        {
            throw new ArgumentException("A execução precisa ter um identificador válido.", nameof(syncRunId));
        }

        var message = new ExecuteSyncRunV1(CurrentVersion, syncRunId, emittedAt.ToUniversalTime());
        if (!IsValid(message))
        {
            throw new ArgumentException("O envelope da execução não atende ao contrato V1.", nameof(emittedAt));
        }

        return message;
    }

    // O consumidor pode rejeitar versões futuras ou campos técnicos inválidos antes de carregar a SyncRun.
    public static bool IsValid(ExecuteSyncRunV1? message) =>
        message is not null
        && message.ContractVersion == CurrentVersion
        && message.SyncRunId != Guid.Empty
        && message.EmittedAtUtc.Offset == TimeSpan.Zero;
}
