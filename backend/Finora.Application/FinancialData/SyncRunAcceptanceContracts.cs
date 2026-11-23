using Finora.Domain.FinancialData;

namespace Finora.Application.FinancialData;

// O resultado distingue repetição aceita de conflito sem retornar dados da execução vinculada a outra identidade.
public enum SyncRunAcceptanceStatus
{
    Accepted,
    Replayed,
    NotRequired,
    ReauthorizationRequired,
    ConnectionUnavailable,
    IdempotencyConflict,
    ActiveExecutionConflict,
    MinimumIntervalActive,
    InvalidRequest
}

// Somente uma execução confirmada pelo store pode carregar referência e estado para a camada HTTP.
public sealed record SyncRunAcceptanceResult(
    SyncRunAcceptanceStatus Status,
    Guid? SyncRunId = null,
    SyncRunState? RunState = null,
    DateOnly? RequestedFrom = null,
    DateOnly? RequestedToExclusive = null,
    DateTimeOffset? NextAllowedAtUtc = null)
{
    // Um replay só é aceito quando a chave identifica a mesma intenção e sua execução persistida.
    public bool IsAccepted => Status is SyncRunAcceptanceStatus.Accepted or SyncRunAcceptanceStatus.Replayed;
}

/// <summary>Fronteira atômica de persistência para validar titularidade e convergir duplicatas pela chave global.</summary>
public interface ISyncRunAcceptanceStore
{
    Task<SyncRunAcceptanceResult> AcceptOrGetAsync(
        SyncRun candidate,
        CancellationToken cancellationToken);
}

/// <summary>Indica que o banco não confirmou se a transação de aceitação foi aplicada ou revertida.</summary>
public sealed class SyncRunAcceptanceIndeterminateException : Exception
{
    // Preserva a causa técnica sem disponibilizar sua mensagem na resposta HTTP.
    public SyncRunAcceptanceIndeterminateException(Exception innerException)
        : base("A confirmação da transação de aceitação não foi observada.", innerException)
    {
    }
}

/// <summary>Aplica a regra comum de convergência para criação confirmada e replay de uma intenção persistida.</summary>
public static class SyncRunAcceptancePolicy
{
    // A execução existente só é retornada quando identidade e fingerprint continuam iguais.
    public static SyncRunAcceptanceResult ResolveExisting(SyncRun existing, SyncRun candidate)
    {
        if (existing.UserId != candidate.UserId
            || existing.ConnectionId != candidate.ConnectionId
            || !string.Equals(existing.RequestFingerprint, candidate.RequestFingerprint, StringComparison.Ordinal))
        {
            return new SyncRunAcceptanceResult(SyncRunAcceptanceStatus.IdempotencyConflict);
        }

        return ToResult(existing, SyncRunAcceptanceStatus.Replayed);
    }

    // O resultado inclui somente referência, estado e janela próprios do titular da execução.
    public static SyncRunAcceptanceResult ToResult(SyncRun run, SyncRunAcceptanceStatus status) => new(
        status,
        run.Id,
        run.State,
        run.RequestedFrom,
        run.RequestedToExclusive);

    // Somente o conflito por nova chave na mesma conexão compartilha referência segura à execução ativa.
    public static SyncRunAcceptanceResult ToActiveExecutionConflict(SyncRun activeRun) =>
        ToResult(activeRun, SyncRunAcceptanceStatus.ActiveExecutionConflict);
}

/// <summary>Gera fingerprints sem persistir nem registrar a chave opaca enviada pelo cliente.</summary>
public static class SyncRunIdempotency
{
    public const int MaximumKeyLength = 128;

    // Só aceita ASCII visível em tamanho limitado; espaços e controles não são normalizados nem confundidos.
    public static bool TryHashKey(string? value, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumKeyLength
            || value.Any(character => character is < '!' or > '~'))
        {
            return false;
        }

        hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(value)));
        return true;
    }

    /// <summary>Preserves the exact v1 fingerprint used by existing manual clients.</summary>
    public static string CreateRequestFingerprint(Guid connectionId) =>
        CreateRequestFingerprint(connectionId, SyncRunRequestMode.Manual);

    /// <summary>Creates a mode-specific fingerprint so one idempotency key cannot change request intent.</summary>
    public static string CreateRequestFingerprint(Guid connectionId, SyncRunRequestMode mode)
    {
        // A forma manual permanece byte a byte igual à v1 já persistida; entrada recebe namespace próprio.
        var canonicalIntent = mode switch
        {
            SyncRunRequestMode.Manual => $"sync-execution-request:v1\nconnectionId={connectionId:D}",
            SyncRunRequestMode.OnEntry => $"sync-execution-on-entry-request:v1\nconnectionId={connectionId:D}",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(canonicalIntent)));
    }

    // O fingerprint de entrada identifica a política sem adicionar coluna ou alterar registros manuais históricos.
    public static bool IsOnEntryRequest(Guid connectionId, string? requestFingerprint) =>
        string.Equals(
            requestFingerprint,
            CreateRequestFingerprint(connectionId, SyncRunRequestMode.OnEntry),
            StringComparison.Ordinal);
}
