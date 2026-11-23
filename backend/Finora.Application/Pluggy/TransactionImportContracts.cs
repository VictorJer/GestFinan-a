namespace Finora.Application.Pluggy;

using Finora.Application.FinancialData;

/// <summary>Projeção mínima da conexão autorizada para uma coleta interna, sem expor credenciais.</summary>
public sealed record TransactionImportConnection(
    Guid Id,
    Guid UserId,
    string Provider,
    string ExternalItemId,
    DateTimeOffset? LastSuccessfulImportAtUtc);

/// <summary>Resolve uma conexão somente quando ela pertence ao titular autenticado que a solicitou.</summary>
public interface ITransactionImportConnectionReader
{
    Task<TransactionImportConnection?> FindOwnedAsync(
        Guid connectionId,
        Guid userId,
        CancellationToken cancellationToken);
}

/// <summary>Conta de origem permitida no snapshot; não inclui campos de identificação pessoal desnecessários.</summary>
public sealed record TransactionImportAccount(
    string ExternalId,
    string Type,
    string Subtype,
    string CurrencyCode);

/// <summary>Snapshot transitório das páginas validadas, mantido fora de resultados operacionais e logs.</summary>
public sealed record TransactionImportSnapshot(
    IReadOnlyList<TransactionImportAccount> Accounts,
    IReadOnlyList<PluggyTransactionData> Transactions,
    DateOnly RequestedDateFrom,
    bool PaginationCompleted,
    int? PagesRead = null,
    int? ReceivedCount = null,
    DateOnly? ObservedFrom = null,
    DateOnly? ObservedTo = null);

/// <summary>Códigos internos fixos para falhas de coleta, sem propagar mensagens ou respostas do provedor.</summary>
public enum TransactionImportFailureCode
{
    ConnectionUnavailable,
    ProviderUnavailable,
    AuthenticationRequired,
    RateLimited,
    InvalidResponse,
    ProviderTimeout,
    Cancelled,
    CollectionLimitExceeded,
    PersistenceFailed,
    StaleSnapshot,
    CommitUncertain,
    IdentityConflict,
    LeaseLost,
    AttemptDeadlineExceeded
}

/// <summary>Contrato de leitura externo que recebe apenas a identidade do item e a janela já fixada.</summary>
public interface ITransactionImportReader
{
    Task<TransactionImportReadResult> ReadAsync(
        string externalItemId,
        DateOnly dateFrom,
        CancellationToken cancellationToken);
}

/// <summary>Invoca o importador existente com a execução aceita, sua janela e o claim da tentativa persistida.</summary>
public interface ISyncRunAttemptExecutor
{
    Task<TransactionImportResult> ExecuteAttemptAsync(
        SyncRunLeaseGrant execution,
        CancellationToken cancellationToken);
}

/// <summary>Promove um snapshot já completo em transação curta e devolve somente contagem/código fixos.</summary>
public interface ITransactionImportPromoter
{
    Task<TransactionImportPromotionResult> PromoteAsync(
        TransactionImportConnection connection,
        TransactionImportSnapshot snapshot,
        DateOnly normalizationReferenceDate,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        CancellationToken cancellationToken);

    // Operação interna distinta permite trocar item somente após provar continuidade da identidade do cartão.
    Task<TransactionImportPromotionResult> PromoteReconnectionAsync(
        TransactionImportConnection connection,
        string replacementExternalItemId,
        TransactionImportSnapshot snapshot,
        DateOnly normalizationReferenceDate,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        CancellationToken cancellationToken);

    // Registra uma falha técnica sem tocar no histórico nem renovar o último sucesso.
    Task<TransactionImportPromotionResult> RecordFailureAsync(
        TransactionImportConnection connection,
        DateOnly requestedDateFrom,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        TransactionImportFailureCode failureCode,
        int? pagesRead,
        int? receivedCount,
        CancellationToken cancellationToken);

    Task<TransactionImportPromotionResult> PromoteAttemptAsync(
        SyncRunLeaseGrant execution,
        TransactionImportConnection connection,
        TransactionImportSnapshot snapshot,
        DateOnly normalizationReferenceDate,
        DateTimeOffset finishedAtUtc,
        CancellationToken cancellationToken);

}

/// <summary>Resultado interno da gravação; mensagens e exceções de persistência não atravessam a fronteira.</summary>
public sealed record TransactionImportPromotionResult(
    bool Succeeded,
    TransactionImportFailureCode? FailureCode,
    int? PersistedCount = null);

/// <summary>Resultado técnico privado da leitura, separado do resumo sanitizado entregue ao chamador.</summary>
public sealed record TransactionImportReadResult(
    TransactionImportSnapshot? Snapshot,
    TransactionImportFailureCode? FailureCode,
    int? PagesRead = null,
    int? ReceivedCount = null,
    DateTimeOffset? RetryAfterAtUtc = null)
{
    public bool Succeeded => Snapshot is not null && FailureCode is null;

    // Mantém falhas sem conteúdo externo e impede snapshots parciais de parecerem completos.
    public static TransactionImportReadResult Failed(
        TransactionImportFailureCode failureCode,
        int? pagesRead = null,
        int? receivedCount = null,
        DateTimeOffset? retryAfterAtUtc = null) =>
        new(null, failureCode, pagesRead, receivedCount, retryAfterAtUtc);
}

/// <summary>Resumo sem IDs, valores, mensagens do provedor ou conteúdo das transações.</summary>
public sealed record TransactionImportResult(
    bool Succeeded,
    TransactionImportFailureCode? FailureCode,
    int? AccountCount = null,
    int? TransactionCount = null,
    int? PersistedCount = null,
    DateOnly? RequestedDateFrom = null,
    bool? PaginationCompleted = null,
    int? PagesRead = null,
    int? ReceivedCount = null,
    DateTimeOffset? RetryAfterAtUtc = null);
