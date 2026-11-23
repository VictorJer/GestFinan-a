using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;

namespace Finora.Application.FinancialData;

// Classifica falhas de negócio sem transportar mensagens externas ou confundir tentativa financeira com entrega.
public static class SyncRunFailurePolicy
{
    public static string ToPersistedCode(TransactionImportFailureCode failureCode) => failureCode switch
    {
        TransactionImportFailureCode.ProviderUnavailable => SyncRunFailureCodes.ProviderUnavailable,
        TransactionImportFailureCode.RateLimited => SyncRunFailureCodes.RateLimited,
        TransactionImportFailureCode.AuthenticationRequired => SyncRunFailureCodes.AuthenticationRequired,
        TransactionImportFailureCode.InvalidResponse or TransactionImportFailureCode.CollectionLimitExceeded
            => SyncRunFailureCodes.InvalidResponse,
        TransactionImportFailureCode.ProviderTimeout => SyncRunFailureCodes.ProviderTimeout,
        TransactionImportFailureCode.PersistenceFailed => SyncRunFailureCodes.PersistenceFailed,
        TransactionImportFailureCode.Cancelled => SyncRunFailureCodes.Cancelled,
        _ => SyncRunFailureCodes.Unknown
    };

    // Só falhas transitórias conhecidas repetem; autorização, contrato, identidade e resultado incerto exigem resolução.
    public static bool IsRetryable(TransactionImportFailureCode failureCode) => failureCode is
        TransactionImportFailureCode.ProviderUnavailable
        or TransactionImportFailureCode.ProviderTimeout
        or TransactionImportFailureCode.RateLimited
        or TransactionImportFailureCode.PersistenceFailed
        or TransactionImportFailureCode.Cancelled;

    // A segunda tentativa usa um minuto; a terceira, cinco; nenhuma tentativa consome orçamento durante o backoff.
    public static TimeSpan? GetBackoff(int completedAttemptNumber) => completedAttemptNumber switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        _ => null
    };

    // Retry-After passado é ignorado; um prazo futuro acima do limite bloqueia retry automático sem ser truncado.
    public static bool ExceedsAutomaticRetryLimit(DateTimeOffset? retryAfterAtUtc, DateTimeOffset nowUtc) =>
        retryAfterAtUtc is { } requiredAtUtc
        && requiredAtUtc > nowUtc.Add(SyncRun.MaximumAutomaticRetryDelay);

    // Escolhe o maior entre backoff aprovado e o prazo informado pela origem para nunca repetir cedo demais.
    public static DateTimeOffset GetNextAttemptAtUtc(
        int completedAttemptNumber,
        DateTimeOffset nowUtc,
        DateTimeOffset? retryAfterAtUtc)
    {
        var backoff = GetBackoff(completedAttemptNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(completedAttemptNumber));
        var policyDueAtUtc = nowUtc.Add(backoff);
        return retryAfterAtUtc is { } requiredAtUtc && requiredAtUtc > policyDueAtUtc
            ? requiredAtUtc
            : policyDueAtUtc;
    }
}
