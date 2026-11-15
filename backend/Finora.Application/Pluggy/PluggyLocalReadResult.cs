namespace Finora.Application.Pluggy;

/// <summary>Representa apenas o resultado sanitizado que a composição local pode apresentar.</summary>
public sealed record PluggyLocalReadResult(
    PluggyLocalReadOutcome Outcome,
    PluggyLocalReadStage Stage,
    int TransactionCount = 0,
    bool HasMoreTransactions = false,
    PluggyLocalReadFailureCategory? FailureCategory = null,
    IReadOnlyList<string>? MissingConfiguration = null,
    PluggyConsentExpiryCheck ConsentExpiryCheck = PluggyConsentExpiryCheck.NotChecked);

/// <summary>Separa leitura comprovada, inconclusiva e bloqueios operacionais sem expor payloads.</summary>
public enum PluggyLocalReadOutcome
{
    TransactionsRead,
    NoTransactions,
    InvalidConfiguration,
    ConnectionBlocked,
    Failed
}

/// <summary>Identifica a etapa segura para diagnóstico, sem incluir URL, conta ou conteúdo financeiro.</summary>
public enum PluggyLocalReadStage
{
    Configuration,
    Authentication,
    Item,
    Accounts,
    Transactions
}

/// <summary>Classifica falhas externas em categorias estáveis, sem mensagens brutas do provedor.</summary>
public enum PluggyLocalReadFailureCategory
{
    Unauthorized,
    Forbidden,
    RateLimited,
    ProviderUnavailable,
    Timeout,
    Transport,
    InvalidResponse,
    UnsupportedState,
    InvalidAccountSelection,
    ConsentExpired
}

/// <summary>Indica se a validade do consentimento foi confirmada ou não estava disponível.</summary>
public enum PluggyConsentExpiryCheck
{
    NotChecked,
    Valid,
    Expired,
    Unavailable
}
