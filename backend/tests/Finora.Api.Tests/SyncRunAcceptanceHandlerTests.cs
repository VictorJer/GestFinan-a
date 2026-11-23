using System.Security.Cryptography;
using System.Text;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;

namespace Finora.Api.Tests;

// Exercita o contrato de aceite com store sintético, sem rede, provider externo ou banco não descartável.
[TestClass]
public sealed class SyncRunAcceptanceHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("1d51ded1-041b-4275-a912-39e6fa82c409");
    private static readonly Guid ConnectionId = Guid.Parse("42995166-5ea4-48b1-a468-ea92e9a4b46c");
    private static readonly DateTimeOffset InitialTime = new(2026, 11, 22, 23, 30, 0, TimeSpan.Zero);

    // O aceite captura um intervalo fechado no primeiro pedido e nunca persiste a chave opaca em claro.
    [TestMethod]
    public async Task AcceptCreatesPendingRunWithHashedKeyAndUtcWindow()
    {
        const string key = "sync-attempt-0001";
        var store = new InMemoryAcceptanceStore();
        var handler = CreateHandler(store, new MutableTimeProvider(InitialTime));

        var result = await handler.Handle(new AcceptSyncRunCommand(UserId, ConnectionId, key), CancellationToken.None);
        var candidate = store.Runs.Single();

        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, result.Status);
        Assert.AreEqual(candidate.Id, result.SyncRunId);
        Assert.AreEqual(SyncRunState.Pending, candidate.State);
        Assert.AreEqual(new DateOnly(2025, 11, 22), candidate.RequestedFrom);
        Assert.AreEqual(new DateOnly(2026, 11, 23), candidate.RequestedToExclusive);
        Assert.AreEqual(0, candidate.AttemptNumber);
        Assert.AreEqual(SyncRun.ApprovedAttemptLimit, candidate.AttemptLimit);
        Assert.AreEqual(InitialTime, candidate.NextAttemptAtUtc);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(key))), candidate.IdempotencyKeyHash);
        Assert.AreNotEqual(key, candidate.IdempotencyKeyHash);
        Assert.AreEqual(SyncRunIdempotency.CreateRequestFingerprint(ConnectionId), candidate.RequestFingerprint);
        // A representação do comando também redige a chave para formatadores ou logs incidentais.
        var command = new AcceptSyncRunCommand(UserId, ConnectionId, key);
        Assert.IsFalse(command.ToString().Contains(key, StringComparison.Ordinal));
    }

    // A mesma SyncRun persiste as três tentativas, deadlines de dez minutos e agenda sem inventar contadores conhecidos.
    [TestMethod]
    public async Task AttemptBudgetAndRetryScheduleRemainOnTheAcceptedRun()
    {
        var store = new InMemoryAcceptanceStore();
        var handler = CreateHandler(store, new MutableTimeProvider(InitialTime));
        await handler.Handle(
            new AcceptSyncRunCommand(UserId, ConnectionId, "sync-attempt-budget"), CancellationToken.None);
        var run = store.Runs.Single();

        for (var attempt = 1; attempt <= SyncRun.ApprovedAttemptLimit; attempt++)
        {
            var attemptAt = attempt == 1
                ? InitialTime
                : run.NextAttemptAtUtc!.Value;
            Assert.IsTrue(run.TryBeginAttempt(attemptAt));
            Assert.AreEqual(attempt, run.AttemptNumber);
            Assert.AreEqual(attemptAt, run.AttemptStartedAtUtc);
            Assert.AreEqual(attemptAt.AddMinutes(10), run.AttemptDeadlineAtUtc);
            Assert.IsNull(run.NextAttemptAtUtc);
            Assert.IsNull(run.PagesRead);
            Assert.IsNull(run.ReceivedCount);
            Assert.IsNull(run.UniqueCount);
            Assert.IsNull(run.PersistedCount);

            if (attempt < SyncRun.ApprovedAttemptLimit)
            {
                var failedAt = attemptAt.AddSeconds(90);
                var scheduledAt = failedAt.AddMinutes(attempt);
                Assert.IsFalse(run.CanScheduleRetry(
                    failedAt,
                    failedAt.AddMinutes(16),
                    SyncRunFailureCodes.ProviderUnavailable));
                Assert.IsTrue(run.CanScheduleRetry(failedAt, scheduledAt, SyncRunFailureCodes.ProviderUnavailable));
                run.ScheduleRetry(failedAt, scheduledAt, SyncRunFailureCodes.ProviderUnavailable);
                Assert.AreEqual(SyncRunState.Pending, run.State);
                Assert.AreEqual(scheduledAt, run.NextAttemptAtUtc);
            }
            else
            {
                Assert.IsFalse(run.CanScheduleRetry(
                    attemptAt.AddSeconds(90),
                    attemptAt.AddMinutes(16),
                    SyncRunFailureCodes.ProviderUnavailable));
                Assert.IsFalse(run.TryBeginAttempt(attemptAt.AddMinutes(20)));
            }
        }

        Assert.AreEqual(1, store.Runs.Count);
        Assert.AreEqual(SyncRunState.Running, run.State);
        Assert.AreEqual(SyncRun.ApprovedAttemptLimit, run.AttemptNumber);
    }

    // Replays ativo e terminal reutilizam ID e janela originais, mesmo quando o relógio cruza a meia-noite UTC.
    [TestMethod]
    public async Task SameKeyReplaysTheSameRunAfterActiveAndTerminalStates()
    {
        const string key = "sync-attempt-0002";
        var store = new InMemoryAcceptanceStore();
        var clock = new MutableTimeProvider(InitialTime);
        var handler = CreateHandler(store, clock);
        var command = new AcceptSyncRunCommand(UserId, ConnectionId, key);

        var accepted = await handler.Handle(command, CancellationToken.None);
        var originallyStored = store.Runs.Single();
        var active = WithState(originallyStored, SyncRunState.Running);
        store.Replace(active);
        clock.Set(InitialTime.AddDays(1));

        var activeReplay = await handler.Handle(command, CancellationToken.None);
        store.Replace(WithState(active, SyncRunState.Succeeded));
        var terminalReplay = await handler.Handle(command, CancellationToken.None);

        Assert.AreEqual(SyncRunAcceptanceStatus.Replayed, activeReplay.Status);
        Assert.AreEqual(originallyStored.Id, activeReplay.SyncRunId);
        Assert.AreEqual(SyncRunState.Running, activeReplay.RunState);
        Assert.AreEqual(originallyStored.RequestedFrom, activeReplay.RequestedFrom);
        Assert.AreEqual(originallyStored.RequestedToExclusive, activeReplay.RequestedToExclusive);
        Assert.AreEqual(SyncRunAcceptanceStatus.Replayed, terminalReplay.Status);
        Assert.AreEqual(originallyStored.Id, terminalReplay.SyncRunId);
        Assert.AreEqual(SyncRunState.Succeeded, terminalReplay.RunState);
        Assert.AreEqual(1, store.Runs.Count);
        Assert.AreEqual(3, store.CallCount);
    }

    // A intenção de entrada conserva a referência original quando a primeira resposta se perde e há retry ativo/terminal.
    [TestMethod]
    public async Task OnEntryRequestReplaysOriginalAfterLostResponseAndTerminalCompletion()
    {
        const string key = "sync-entry-replay-0001";
        var store = new InMemoryAcceptanceStore();
        var clock = new MutableTimeProvider(InitialTime);
        var handler = CreateHandler(store, clock);
        var command = new AcceptSyncRunCommand(UserId, ConnectionId, key, SyncRunRequestMode.OnEntry);

        var originalResponse = await handler.Handle(command, CancellationToken.None);
        var originalRun = store.Runs.Single();
        // O teste simula perda da resposta descartando a referência do primeiro retorno antes do replay.
        store.Replace(WithState(originalRun, SyncRunState.Running));
        var activeReplay = await handler.Handle(command, CancellationToken.None);
        store.Replace(WithState(store.Runs.Single(), SyncRunState.Succeeded));
        var terminalReplay = await handler.Handle(command, CancellationToken.None);

        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, originalResponse.Status);
        Assert.AreEqual(SyncRunAcceptanceStatus.Replayed, activeReplay.Status);
        Assert.AreEqual(SyncRunAcceptanceStatus.Replayed, terminalReplay.Status);
        Assert.AreEqual(originalRun.Id, activeReplay.SyncRunId);
        Assert.AreEqual(originalRun.Id, terminalReplay.SyncRunId);
        Assert.AreEqual(1, store.Runs.Count);
    }

    // A chave manual mantém exatamente o fingerprint v1 histórico e recebe outro namespace no modo de entrada.
    [TestMethod]
    public void ManualFingerprintRemainsVersionOneAndEntryFingerprintIsDistinct()
    {
        var canonicalManual = $"sync-execution-request:v1\nconnectionId={ConnectionId:D}";
        var canonicalEntry = $"sync-execution-on-entry-request:v1\nconnectionId={ConnectionId:D}";
        var expectedManual = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(canonicalManual)));
        var expectedEntry = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(canonicalEntry)));

        Assert.AreEqual(expectedManual, SyncRunIdempotency.CreateRequestFingerprint(ConnectionId));
        Assert.AreEqual(expectedManual, SyncRunIdempotency.CreateRequestFingerprint(ConnectionId, SyncRunRequestMode.Manual));
        Assert.AreEqual(expectedEntry, SyncRunIdempotency.CreateRequestFingerprint(ConnectionId, SyncRunRequestMode.OnEntry));
        Assert.AreNotEqual(expectedManual, expectedEntry);
    }

    // Reutilizar uma chave para trocar o modo é conflito nos dois sentidos e nunca cria uma segunda execução.
    [TestMethod]
    public async Task SameKeyCannotChangeBetweenManualAndOnEntryModes()
    {
        var modePairs = new[]
        {
            (SyncRunRequestMode.Manual, SyncRunRequestMode.OnEntry),
            (SyncRunRequestMode.OnEntry, SyncRunRequestMode.Manual)
        };

        foreach (var (firstMode, secondMode) in modePairs)
        {
            var store = new InMemoryAcceptanceStore();
            var handler = CreateHandler(store, new MutableTimeProvider(InitialTime));
            const string key = "sync-mode-conflict-0001";

            var first = await handler.Handle(
                new AcceptSyncRunCommand(UserId, ConnectionId, key, firstMode), CancellationToken.None);
            var conflicting = await handler.Handle(
                new AcceptSyncRunCommand(UserId, ConnectionId, key, secondMode), CancellationToken.None);

            Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, first.Status);
            Assert.AreEqual(SyncRunAcceptanceStatus.IdempotencyConflict, conflicting.Status);
            Assert.AreEqual(1, store.Runs.Count);
        }
    }

    // A chave global em outro escopo conflita genericamente e não devolve referência ou estado da execução anterior.
    [TestMethod]
    public async Task ReusingKeyForAnotherOwnerOrConnectionDoesNotExposeTheStoredRun()
    {
        var store = new InMemoryAcceptanceStore();
        var handler = CreateHandler(store, new MutableTimeProvider(InitialTime));
        const string key = "sync-attempt-0003";
        var first = await handler.Handle(new AcceptSyncRunCommand(UserId, ConnectionId, key), CancellationToken.None);

        var conflicting = await handler.Handle(
            new AcceptSyncRunCommand(Guid.NewGuid(), Guid.NewGuid(), key), CancellationToken.None);

        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, first.Status);
        Assert.AreEqual(SyncRunAcceptanceStatus.IdempotencyConflict, conflicting.Status);
        Assert.IsNull(conflicting.SyncRunId);
        Assert.IsNull(conflicting.RunState);
        Assert.AreEqual(1, store.Runs.Count);
    }

    // A validação local evita qualquer consulta de persistência para identidade ou chave malformada.
    [TestMethod]
    public async Task InvalidIdentityOrKeyDoesNotReachTheStore()
    {
        var store = new InMemoryAcceptanceStore();
        var handler = CreateHandler(store, new MutableTimeProvider(InitialTime));

        var emptyUser = await handler.Handle(
            new AcceptSyncRunCommand(Guid.Empty, ConnectionId, "sync-attempt-0004"), CancellationToken.None);
        var whitespace = await handler.Handle(
            new AcceptSyncRunCommand(UserId, ConnectionId, "sync attempt"), CancellationToken.None);
        var tooLong = await handler.Handle(
            new AcceptSyncRunCommand(UserId, ConnectionId, new string('x', SyncRunIdempotency.MaximumKeyLength + 1)),
            CancellationToken.None);

        Assert.AreEqual(SyncRunAcceptanceStatus.InvalidRequest, emptyUser.Status);
        Assert.AreEqual(SyncRunAcceptanceStatus.InvalidRequest, whitespace.Status);
        Assert.AreEqual(SyncRunAcceptanceStatus.InvalidRequest, tooLong.Status);
        Assert.AreEqual(0, store.CallCount);
    }

    // Ausência ou titularidade divergente da conexão não gera execução nem referência de recurso.
    [TestMethod]
    public async Task UnownedConnectionIsNotAccepted()
    {
        var store = new InMemoryAcceptanceStore(ownsConnection: false);
        var handler = CreateHandler(store, new MutableTimeProvider(InitialTime));

        var result = await handler.Handle(
            new AcceptSyncRunCommand(UserId, ConnectionId, "sync-attempt-0005"), CancellationToken.None);

        Assert.AreEqual(SyncRunAcceptanceStatus.ConnectionUnavailable, result.Status);
        Assert.IsNull(result.SyncRunId);
        Assert.AreEqual(0, store.Runs.Count);
    }

    // O fake preserva o contrato de replay para testes isolados; a constraint EF garante a unicidade no PostgreSQL.
    private sealed class InMemoryAcceptanceStore(bool ownsConnection = true) : ISyncRunAcceptanceStore
    {
        private readonly Dictionary<string, SyncRun> _runs = new(StringComparer.Ordinal);

        public int CallCount { get; private set; }
        public IReadOnlyCollection<SyncRun> Runs => _runs.Values;

        public Task<SyncRunAcceptanceResult> AcceptOrGetAsync(
            SyncRun candidate,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var keyHash = candidate.IdempotencyKeyHash!;
            if (_runs.TryGetValue(keyHash, out var existing))
            {
                return Task.FromResult(SyncRunAcceptancePolicy.ResolveExisting(existing, candidate));
            }

            if (!ownsConnection)
            {
                return Task.FromResult(new SyncRunAcceptanceResult(SyncRunAcceptanceStatus.ConnectionUnavailable));
            }

            _runs.Add(keyHash, candidate);
            return Task.FromResult(SyncRunAcceptancePolicy.ToResult(candidate, SyncRunAcceptanceStatus.Accepted));
        }

        // Os testes substituem somente o estado persistido e mantêm identidade, chave, fingerprint e janela.
        public void Replace(SyncRun run) => _runs[run.IdempotencyKeyHash!] = run;
    }

    // A fixture reproduz uma transição sem expor setters de estado fora do Domain.
    private static SyncRun WithState(SyncRun current, SyncRunState state)
    {
        DateTimeOffset? started = state is SyncRunState.Running or SyncRunState.Succeeded
            ? current.CreatedAtUtc.AddSeconds(1)
            : null;
        DateTimeOffset? finished = state is SyncRunState.Succeeded or SyncRunState.Failed
            ? current.CreatedAtUtc.AddSeconds(2)
            : null;
        return SyncRun.Create(
            current.Id,
            current.UserId,
            current.ConnectionId,
            current.CreatedAtUtc,
            current.RequestedFrom,
            current.RequestedToExclusive,
            startedAtUtc: started,
            finishedAtUtc: finished,
            state: state,
            coverageState: state == SyncRunState.Succeeded
                ? SyncCoverageState.CompleteForRequestedWindow
                : SyncCoverageState.Unknown,
            failureCode: state == SyncRunState.Failed ? SyncRunFailureCodes.Unknown : null,
            idempotencyKeyHash: current.IdempotencyKeyHash,
            requestFingerprint: current.RequestFingerprint);
    }

    // O caso de uso usa relógio injetável para tornar a janela UTC reproduzível.
    private static AcceptSyncRunHandler CreateHandler(ISyncRunAcceptanceStore store, TimeProvider clock) => new(store, clock);

    private sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Set(DateTimeOffset value) => _current = value;
    }
}
