using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida que o handler só confirme falhas após persistir retry ou encerramento na execução aceita.
[TestClass]
public sealed class ExecuteSyncRunFailureHandlingTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 23, 12, 0, 30, TimeSpan.Zero);

    // Falha transitória da primeira tentativa grava backoff/contagens e libera a mensagem após handoff persistente.
    [TestMethod]
    public async Task TransientFailureSchedulesFirstBackoffOnTheSameRun()
    {
        var retry = new RecordingLeaseStore(CreateGrant(1));
        var handler = CreateHandler(retry, new TransactionImportResult(
            false,
            TransactionImportFailureCode.ProviderTimeout,
            PagesRead: 3,
            ReceivedCount: 9));

        var result = await handler.Handle(new ExecuteSyncRunCommand(retry.Grant.SyncRunId), CancellationToken.None);

        Assert.AreEqual(ExecuteSyncRunStatus.Executed, result.Status);
        Assert.AreEqual(NowUtc.AddMinutes(1), retry.NextAttemptAtUtc);
        Assert.AreEqual(SyncRunFailureCodes.ProviderTimeout, retry.ScheduledFailureCode);
        Assert.AreEqual(3, retry.PagesRead);
        Assert.AreEqual(9, retry.ReceivedCount);
        Assert.AreEqual(1, retry.ScheduleCallCount);
        Assert.AreEqual(0, retry.TerminalCallCount);
    }

    // Retry-After além de quinze minutos fecha a mesma execução e persiste o instante sem repetição ou truncamento.
    [TestMethod]
    public async Task LongRetryAfterCompletesFailureWithTheOriginalRequiredInstant()
    {
        var retry = new RecordingLeaseStore(CreateGrant(1));
        var requiredAt = NowUtc.AddMinutes(23);
        var handler = CreateHandler(retry, new TransactionImportResult(
            false,
            TransactionImportFailureCode.RateLimited,
            PagesRead: 1,
            ReceivedCount: 4,
            RetryAfterAtUtc: requiredAt));

        var result = await handler.Handle(new ExecuteSyncRunCommand(retry.Grant.SyncRunId), CancellationToken.None);

        Assert.AreEqual(ExecuteSyncRunStatus.Executed, result.Status);
        Assert.AreEqual(requiredAt, retry.TerminalNextAttemptAtUtc);
        Assert.AreEqual(SyncRunFailureCodes.RateLimited, retry.TerminalFailureCode);
        Assert.AreEqual(1, retry.TerminalCallCount);
        Assert.AreEqual(0, retry.ScheduleCallCount);
    }

    // Falha de autorização é terminal imediatamente e nunca consome backoff automático.
    [TestMethod]
    public async Task AuthorizationFailureIsTerminalWithoutAutomaticRetry()
    {
        var retry = new RecordingLeaseStore(CreateGrant(1));
        var handler = CreateHandler(retry, new TransactionImportResult(
            false, TransactionImportFailureCode.AuthenticationRequired));

        var result = await handler.Handle(new ExecuteSyncRunCommand(retry.Grant.SyncRunId), CancellationToken.None);

        Assert.AreEqual(ExecuteSyncRunStatus.Executed, result.Status);
        Assert.AreEqual(SyncRunFailureCodes.AuthenticationRequired, retry.TerminalFailureCode);
        Assert.AreEqual(1, retry.TerminalCallCount);
        Assert.AreEqual(0, retry.ScheduleCallCount);
    }

    // Tentativa três esgota o orçamento persistido e termina sem criar uma quarta tentativa.
    [TestMethod]
    public async Task ExhaustedAttemptBudgetCompletesFailureOnTheSameRun()
    {
        var retry = new RecordingLeaseStore(CreateGrant(SyncRun.ApprovedAttemptLimit));
        var handler = CreateHandler(retry, new TransactionImportResult(
            false, TransactionImportFailureCode.ProviderUnavailable));

        var result = await handler.Handle(new ExecuteSyncRunCommand(retry.Grant.SyncRunId), CancellationToken.None);

        Assert.AreEqual(ExecuteSyncRunStatus.Executed, result.Status);
        Assert.AreEqual(1, retry.TerminalCallCount);
        Assert.AreEqual(0, retry.ScheduleCallCount);
    }

    // Falha sem prova de commit não é confirmada nem transformada em retry cego.
    [TestMethod]
    public async Task UncertainCommitDoesNotPersistAutomaticHandoffOrReportSuccess()
    {
        var retry = new RecordingLeaseStore(CreateGrant(1));
        var handler = CreateHandler(retry, new TransactionImportResult(
            false, TransactionImportFailureCode.CommitUncertain));

        var result = await handler.Handle(new ExecuteSyncRunCommand(retry.Grant.SyncRunId), CancellationToken.None);

        Assert.AreEqual(ExecuteSyncRunStatus.HandoffFailed, result.Status);
        Assert.AreEqual(0, retry.TerminalCallCount);
        Assert.AreEqual(0, retry.ScheduleCallCount);
    }

    // Falha ao confirmar o retry durável propaga erro e não devolve status que o consumer possa reconhecer como ackável.
    [TestMethod]
    public async Task RetryPersistenceFailureDoesNotReturnAnAckableResult()
    {
        var retry = new RecordingLeaseStore(CreateGrant(1)) { FailSchedule = true };
        var handler = CreateHandler(retry, new TransactionImportResult(
            false, TransactionImportFailureCode.ProviderTimeout));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            handler.Handle(new ExecuteSyncRunCommand(retry.Grant.SyncRunId), CancellationToken.None));

        Assert.AreEqual(1, retry.ScheduleCallCount);
        Assert.AreEqual(0, retry.TerminalCallCount);
    }

    // O handler usa relógio e executor sintéticos, mas executa a política e os contratos Application de produção.
    private static ExecuteSyncRunHandler CreateHandler(
        RecordingLeaseStore store,
        TransactionImportResult result) => new(
            store,
            new InlineLeaseGuard(),
            new FixedAttemptExecutor(result),
            new FixedTimeProvider(NowUtc));

    // Claim completo garante que janela, tentativa e deadline permanecem coerentes durante cada cenário.
    private static SyncRunLeaseGrant CreateGrant(int attemptNumber) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        attemptNumber,
        FencingToken: attemptNumber,
        AcquiredAtUtc: NowUtc.AddSeconds(-30),
        ExpiresAtUtc: NowUtc.AddSeconds(90),
        RequestedFrom: new DateOnly(2025, 1, 1),
        RequestedToExclusive: new DateOnly(2026, 1, 1),
        AttemptStartedAtUtc: NowUtc.AddSeconds(-30),
        AttemptDeadlineAtUtc: NowUtc.AddMinutes(9));

    // Relógio fixo mantém os prazos esperados determinísticos.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Guard em linha remove apenas espera de renovação; a política continua recebendo o mesmo claim persistido.
    private sealed class InlineLeaseGuard : ISyncRunLeaseExecutionGuard
    {
        public Task<T> ExecuteWithLeaseAsync<T>(
            SyncRunLeaseGrant grant,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }

    // Executor fornece somente o resultado do cenário e não chama banco, Pluggy nem infraestrutura de transporte.
    private sealed class FixedAttemptExecutor(TransactionImportResult result) : ISyncRunAttemptExecutor
    {
        public Task<TransactionImportResult> ExecuteAttemptAsync(
            SyncRunLeaseGrant execution,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    // Store falso captura os valores de handoff/terminal sem simular uma persistência que não ocorreu.
    private sealed class RecordingLeaseStore(SyncRunLeaseGrant grant) : ISyncRunLeaseStore
    {
        public SyncRunLeaseGrant Grant { get; } = grant;
        public DateTimeOffset? NextAttemptAtUtc { get; private set; }
        public DateTimeOffset? TerminalNextAttemptAtUtc { get; private set; }
        public string? ScheduledFailureCode { get; private set; }
        public string? TerminalFailureCode { get; private set; }
        public int? PagesRead { get; private set; }
        public int? ReceivedCount { get; private set; }
        public int ScheduleCallCount { get; private set; }
        public int TerminalCallCount { get; private set; }
        public bool FailSchedule { get; init; }

        public Task<SyncRunLeaseAcquisitionResult> AcquireAsync(
            Guid syncRunId,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => Task.FromResult(
                syncRunId == Grant.SyncRunId
                    ? new SyncRunLeaseAcquisitionResult(SyncRunLeaseAcquisitionStatus.Acquired, Grant)
                    : new SyncRunLeaseAcquisitionResult(SyncRunLeaseAcquisitionStatus.RunUnavailable));

        public Task<SyncRunLeaseGrant?> RenewAsync(
            SyncRunLeaseGrant execution,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => Task.FromResult<SyncRunLeaseGrant?>(execution);

        public Task<bool> ScheduleRetryAsync(
            SyncRunLeaseGrant execution,
            DateTimeOffset nowUtc,
            DateTimeOffset nextAttemptAtUtc,
            string failureCode,
            int? pagesRead,
            int? receivedCount,
            CancellationToken cancellationToken)
        {
            ScheduleCallCount++;
            if (FailSchedule)
            {
                throw new InvalidOperationException("Synthetic durable handoff failure.");
            }

            NextAttemptAtUtc = nextAttemptAtUtc;
            ScheduledFailureCode = failureCode;
            PagesRead = pagesRead;
            ReceivedCount = receivedCount;
            return Task.FromResult(true);
        }

        public Task<bool> CompleteFailureAsync(
            SyncRunLeaseGrant execution,
            DateTimeOffset finishedAtUtc,
            string failureCode,
            int? pagesRead,
            int? receivedCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            TerminalCallCount++;
            TerminalFailureCode = failureCode;
            TerminalNextAttemptAtUtc = nextAttemptAtUtc;
            PagesRead = pagesRead;
            ReceivedCount = receivedCount;
            return Task.FromResult(true);
        }
    }
}
