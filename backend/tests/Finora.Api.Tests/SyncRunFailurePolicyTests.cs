using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confirma classificação fechada, backoff aprovado e preservação de Retry-After sem qualquer dependência externa.
[TestClass]
public sealed class SyncRunFailurePolicyTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    // Backoff é indexado pela tentativa concluída e Retry-After futuro sempre prevalece sobre o prazo mínimo.
    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(2, 5)]
    public void UsesApprovedBackoffAndNeverRetriesBeforeProviderDeadline(int attemptNumber, int backoffMinutes)
    {
        var nextAttempt = SyncRunFailurePolicy.GetNextAttemptAtUtc(attemptNumber, NowUtc, null);
        Assert.AreEqual(NowUtc.AddMinutes(backoffMinutes), nextAttempt);

        var retryAfter = NowUtc.AddMinutes(backoffMinutes + 3);
        nextAttempt = SyncRunFailurePolicy.GetNextAttemptAtUtc(attemptNumber, NowUtc, retryAfter);
        Assert.AreEqual(retryAfter, nextAttempt);
    }

    // O limite de quinze minutos bloqueia a repetição sem alterar o instante exigido pela origem.
    [TestMethod]
    public void RetryAfterAboveLimitIsBlockedInsteadOfTruncated()
    {
        var requiredAtUtc = NowUtc.AddMinutes(23);

        Assert.IsTrue(SyncRunFailurePolicy.ExceedsAutomaticRetryLimit(requiredAtUtc, NowUtc));
        Assert.AreEqual(requiredAtUtc,
            SyncRunFailurePolicy.GetNextAttemptAtUtc(1, NowUtc, requiredAtUtc));
        Assert.IsFalse(SyncRunFailurePolicy.ExceedsAutomaticRetryLimit(NowUtc.AddMinutes(15), NowUtc));
        Assert.IsFalse(SyncRunFailurePolicy.ExceedsAutomaticRetryLimit(NowUtc.AddMinutes(-1), NowUtc));
    }

    // Somente códigos fixos entram no modelo; autorização/contrato são terminais, falhas transitórias podem repetir.
    [TestMethod]
    public void ClassifiesRetryableAndTerminalFailuresWithoutExternalText()
    {
        Assert.IsTrue(SyncRunFailurePolicy.IsRetryable(TransactionImportFailureCode.ProviderUnavailable));
        Assert.IsTrue(SyncRunFailurePolicy.IsRetryable(TransactionImportFailureCode.ProviderTimeout));
        Assert.IsTrue(SyncRunFailurePolicy.IsRetryable(TransactionImportFailureCode.RateLimited));
        Assert.IsTrue(SyncRunFailurePolicy.IsRetryable(TransactionImportFailureCode.PersistenceFailed));
        Assert.IsFalse(SyncRunFailurePolicy.IsRetryable(TransactionImportFailureCode.AuthenticationRequired));
        Assert.IsFalse(SyncRunFailurePolicy.IsRetryable(TransactionImportFailureCode.InvalidResponse));
        Assert.AreEqual(SyncRunFailureCodes.AuthenticationRequired,
            SyncRunFailurePolicy.ToPersistedCode(TransactionImportFailureCode.AuthenticationRequired));
        Assert.AreEqual(SyncRunFailureCodes.Unknown,
            SyncRunFailurePolicy.ToPersistedCode(TransactionImportFailureCode.IdentityConflict));
    }

    // Uma falha observada no deadline não pode agendar retry usando posse ou orçamento já expirados.
    [TestMethod]
    public void RetryHandoffIsRejectedAtOrAfterAttemptDeadline()
    {
        var createdAtUtc = NowUtc.AddMinutes(-1);
        var run = SyncRun.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            createdAtUtc,
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 1, 1),
            nextAttemptAtUtc: createdAtUtc);
        Assert.IsTrue(run.TryBeginAttempt(NowUtc));

        var atDeadline = NowUtc.Add(SyncRun.MaximumAttemptDuration);

        Assert.IsFalse(run.CanScheduleRetry(
            atDeadline,
            atDeadline.AddMinutes(1),
            SyncRunFailureCodes.ProviderTimeout));
        Assert.IsFalse(run.CanScheduleRetry(
            atDeadline.AddTicks(1),
            atDeadline.AddMinutes(1),
            SyncRunFailureCodes.ProviderTimeout));
    }
}
