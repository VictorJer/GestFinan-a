namespace Finora.Domain.FinancialData;

public enum SyncRunState
{
    Pending,
    Running,
    Succeeded,
    Failed
}

public enum SyncCoverageState
{
    Unknown,
    Partial,
    CompleteForRequestedWindow
}

/// <summary>Allowlist de códigos internos; valores externos e mensagens de exceção não são aceitos.</summary>
public static class SyncRunFailureCodes
{
    public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";
    public const string RateLimited = "RATE_LIMITED";
    public const string AuthenticationRequired = "AUTH_REQUIRED";
    public const string InvalidResponse = "INVALID_RESPONSE";
    public const string ProviderTimeout = "PROVIDER_TIMEOUT";
    public const string PersistenceFailed = "PERSISTENCE_FAILED";
    public const string Cancelled = "CANCELLED";
    public const string Unknown = "UNKNOWN";

    // A comparação ordinal torna a lista fechada e impede texto arbitrário de entrar no registro.
    public static bool IsAllowed(string value) => value is
        ProviderUnavailable
        or RateLimited
        or AuthenticationRequired
        or InvalidResponse
        or ProviderTimeout
        or PersistenceFailed
        or Cancelled
        or Unknown;
}

/// <summary>Registra intervalo e resultado básico sem executar sincronização ou renovar sucesso da conexão.</summary>
public sealed class SyncRun
{
    public const int ApprovedAttemptLimit = 3;
    public static readonly TimeSpan MaximumAttemptDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaximumAutomaticRetryDelay = TimeSpan.FromMinutes(15);

    private SyncRun(
        Guid id,
        Guid userId,
        Guid connectionId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? finishedAtUtc,
        DateOnly requestedFrom,
        DateOnly? requestedToExclusive,
        SyncRunState state,
        int? pagesRead,
        int? receivedCount,
        int? uniqueCount,
        int? persistedCount,
        DateOnly? observedFrom,
        DateOnly? observedTo,
        SyncCoverageState coverageState,
        string? failureCode,
        string? idempotencyKeyHash,
        string? requestFingerprint,
        int attemptNumber,
        int attemptLimit,
        DateTimeOffset? attemptStartedAtUtc,
        DateTimeOffset? attemptDeadlineAtUtc,
        DateTimeOffset? nextAttemptAtUtc,
        DateTimeOffset? executionMessageEnqueuedAtUtc)
    {
        Id = id;
        UserId = userId;
        ConnectionId = connectionId;
        CreatedAtUtc = createdAtUtc;
        StartedAtUtc = startedAtUtc;
        FinishedAtUtc = finishedAtUtc;
        RequestedFrom = requestedFrom;
        RequestedToExclusive = requestedToExclusive;
        State = state;
        PagesRead = pagesRead;
        ReceivedCount = receivedCount;
        UniqueCount = uniqueCount;
        PersistedCount = persistedCount;
        ObservedFrom = observedFrom;
        ObservedTo = observedTo;
        CoverageState = coverageState;
        FailureCode = failureCode;
        IdempotencyKeyHash = idempotencyKeyHash;
        RequestFingerprint = requestFingerprint;
        AttemptNumber = attemptNumber;
        AttemptLimit = attemptLimit;
        AttemptStartedAtUtc = attemptStartedAtUtc;
        AttemptDeadlineAtUtc = attemptDeadlineAtUtc;
        NextAttemptAtUtc = nextAttemptAtUtc;
        ExecutionMessageEnqueuedAtUtc = executionMessageEnqueuedAtUtc;
    }

    // Setters privados permitem restaurar o registro sem expor atualização fora das regras do Domain.
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ConnectionId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? FinishedAtUtc { get; private set; }

    public DateOnly RequestedFrom { get; private set; }

    public DateOnly? RequestedToExclusive { get; private set; }

    public SyncRunState State { get; private set; }

    public int? PagesRead { get; private set; }

    public int? ReceivedCount { get; private set; }

    public int? UniqueCount { get; private set; }

    public int? PersistedCount { get; private set; }

    public DateOnly? ObservedFrom { get; private set; }

    public DateOnly? ObservedTo { get; private set; }

    public SyncCoverageState CoverageState { get; private set; }

    public string? FailureCode { get; private set; }

    /// <summary>SHA-256 da chave recebida; execuções legadas sem pedido HTTP permanecem sem chave.</summary>
    public string? IdempotencyKeyHash { get; private set; }

    /// <summary>Fingerprint versionado dos campos de intenção do pedido, sem persistir a chave original.</summary>
    public string? RequestFingerprint { get; private set; }

    public int AttemptNumber { get; private set; }

    public int AttemptLimit { get; private set; }

    public DateTimeOffset? AttemptStartedAtUtc { get; private set; }

    public DateTimeOffset? AttemptDeadlineAtUtc { get; private set; }

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? ExecutionMessageEnqueuedAtUtc { get; private set; }

    /// <summary>Cria somente o registro descritivo, validando intervalo, contadores, horários e falha interna.</summary>
    public static SyncRun Create(
        Guid id,
        Guid userId,
        Guid connectionId,
        DateTimeOffset createdAtUtc,
        DateOnly requestedFrom,
        DateOnly? requestedToExclusive = null,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? finishedAtUtc = null,
        SyncRunState state = SyncRunState.Pending,
        int? pagesRead = null,
        int? receivedCount = null,
        int? uniqueCount = null,
        int? persistedCount = null,
        DateOnly? observedFrom = null,
        DateOnly? observedTo = null,
        SyncCoverageState coverageState = SyncCoverageState.Unknown,
        string? failureCode = null,
        string? idempotencyKeyHash = null,
        string? requestFingerprint = null,
        int attemptNumber = 0,
        int attemptLimit = ApprovedAttemptLimit,
        DateTimeOffset? attemptStartedAtUtc = null,
        DateTimeOffset? attemptDeadlineAtUtc = null,
        DateTimeOffset? nextAttemptAtUtc = null,
        DateTimeOffset? executionMessageEnqueuedAtUtc = null)
    {
        var run = new SyncRun(
            id,
            userId,
            connectionId,
            createdAtUtc,
            startedAtUtc,
            finishedAtUtc,
            requestedFrom,
            requestedToExclusive,
            state,
            pagesRead,
            receivedCount,
            uniqueCount,
            persistedCount,
            observedFrom,
            observedTo,
            coverageState,
            failureCode,
            idempotencyKeyHash,
            requestFingerprint,
            attemptNumber,
            attemptLimit,
            attemptStartedAtUtc,
            attemptDeadlineAtUtc,
            nextAttemptAtUtc,
            executionMessageEnqueuedAtUtc);

        run.ValidateForPersistence();
        return run;
    }

    // A posse inicia uma tentativa somente quando o orçamento e a agenda persistidos permitem trabalho.
    public bool TryBeginAttempt(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (State is not (SyncRunState.Pending or SyncRunState.Running)
            || nowUtc < CreatedAtUtc
            || AttemptNumber >= AttemptLimit
            || (NextAttemptAtUtc is { } nextAttemptAtUtc && nextAttemptAtUtc > nowUtc))
        {
            return false;
        }

        var deadline = nowUtc.Add(MaximumAttemptDuration);
        AttemptNumber++;
        StartedAtUtc ??= nowUtc;
        AttemptStartedAtUtc = nowUtc;
        AttemptDeadlineAtUtc = deadline;
        NextAttemptAtUtc = null;
        ExecutionMessageEnqueuedAtUtc = null;
        FailureCode = null;
        PagesRead = null;
        ReceivedCount = null;
        UniqueCount = null;
        PersistedCount = null;
        ObservedFrom = null;
        ObservedTo = null;
        CoverageState = SyncCoverageState.Unknown;
        State = SyncRunState.Running;
        ValidateForPersistence();
        return true;
    }

    // Sucesso só fecha a tentativa corrente antes do deadline e no mesmo registro aceito.
    public bool TryCompleteAttempt(
        int attemptNumber,
        DateTimeOffset finishedAtUtc,
        int? pagesRead,
        int? receivedCount,
        int? uniqueCount,
        int? persistedCount,
        DateOnly? observedFrom,
        DateOnly? observedTo)
    {
        EnsureUtc(finishedAtUtc, nameof(finishedAtUtc));
        if (!IsCurrentAttempt(attemptNumber, finishedAtUtc)
            || pagesRead < 0 || receivedCount < 0 || uniqueCount < 0 || persistedCount < 0)
        {
            return false;
        }

        FinishedAtUtc = finishedAtUtc;
        PagesRead = pagesRead;
        ReceivedCount = receivedCount;
        UniqueCount = uniqueCount;
        PersistedCount = persistedCount;
        ObservedFrom = observedFrom;
        ObservedTo = observedTo;
        CoverageState = SyncCoverageState.CompleteForRequestedWindow;
        FailureCode = null;
        NextAttemptAtUtc = null;
        ExecutionMessageEnqueuedAtUtc = null;
        State = SyncRunState.Succeeded;
        ValidateForPersistence();
        return true;
    }

    // Falha terminal fecha a execução aceita; Retry-After acima do teto permanece visível sem criar retry automático.
    public bool TryCompleteAttemptFailure(
        int attemptNumber,
        DateTimeOffset finishedAtUtc,
        string failureCode,
        int? pagesRead,
        int? receivedCount,
        DateTimeOffset? nextAttemptAtUtc = null)
    {
        EnsureUtc(finishedAtUtc, nameof(finishedAtUtc));
        EnsureUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (!IsCurrentAttempt(attemptNumber, finishedAtUtc)
            || !SyncRunFailureCodes.IsAllowed(failureCode)
            || pagesRead < 0 || receivedCount < 0
            || (nextAttemptAtUtc is { } retryAtUtc && retryAtUtc <= finishedAtUtc))
        {
            return false;
        }

        FinishedAtUtc = finishedAtUtc;
        FailureCode = failureCode;
        PagesRead = pagesRead;
        ReceivedCount = receivedCount;
        CoverageState = pagesRead is > 0 ? SyncCoverageState.Partial : SyncCoverageState.Unknown;
        NextAttemptAtUtc = nextAttemptAtUtc;
        ExecutionMessageEnqueuedAtUtc = null;
        State = SyncRunState.Failed;
        ValidateForPersistence();
        return true;
    }

    // Uma tentativa interrompida após consumir o orçamento fecha a mesma execução sem fingir conclusão financeira.
    public bool TryCompleteAbandonedAttemptFailure(DateTimeOffset recoveredAtUtc)
    {
        EnsureUtc(recoveredAtUtc, nameof(recoveredAtUtc));
        if (State != SyncRunState.Running
            || AttemptNumber != AttemptLimit
            || AttemptStartedAtUtc is not { } startedAtUtc
            || recoveredAtUtc < startedAtUtc)
        {
            return false;
        }

        FinishedAtUtc = recoveredAtUtc;
        FailureCode = SyncRunFailureCodes.Unknown;
        NextAttemptAtUtc = null;
        ExecutionMessageEnqueuedAtUtc = null;
        CoverageState = PagesRead is > 0 ? SyncCoverageState.Partial : SyncCoverageState.Unknown;
        State = SyncRunState.Failed;
        ValidateForPersistence();
        return true;
    }

    // O marcador e a mensagem de transporte são gravados no mesmo commit para impedir reenfileiramento por polling.
    public bool TryMarkExecutionMessageEnqueued(DateTimeOffset enqueuedAtUtc)
    {
        EnsureUtc(enqueuedAtUtc, nameof(enqueuedAtUtc));
        if (State is not (SyncRunState.Pending or SyncRunState.Running)
            || ExecutionMessageEnqueuedAtUtc is not null
            || enqueuedAtUtc < CreatedAtUtc)
        {
            return false;
        }

        ExecutionMessageEnqueuedAtUtc = enqueuedAtUtc;
        ValidateForPersistence();
        return true;
    }

    // Tentativa, estado, início e deadline precisam continuar vigentes no instante de escrita/commit.
    private bool IsCurrentAttempt(int attemptNumber, DateTimeOffset nowUtc) =>
        State == SyncRunState.Running
        && attemptNumber == AttemptNumber
        && AttemptStartedAtUtc is { } startedAtUtc
        && AttemptDeadlineAtUtc is { } deadlineAtUtc
        && nowUtc >= startedAtUtc
        && nowUtc < deadlineAtUtc;

    // Uma falha elegível deixa obrigação durável na própria SyncRun antes de o consumer confirmar a entrega.
    public bool CanScheduleRetry(DateTimeOffset nowUtc, DateTimeOffset nextAttemptAtUtc, string failureCode)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (!SyncRunFailureCodes.IsAllowed(failureCode))
        {
            throw new ArgumentException("O código de falha não pertence à allowlist interna.", nameof(failureCode));
        }

        return State == SyncRunState.Running
            && AttemptNumber > 0
            && AttemptNumber < AttemptLimit
            && AttemptStartedAtUtc is not null
            && AttemptDeadlineAtUtc is not null
            && nowUtc >= AttemptStartedAtUtc
            && nowUtc < AttemptDeadlineAtUtc
            && nextAttemptAtUtc > nowUtc
            && nextAttemptAtUtc <= nowUtc.Add(MaximumAutomaticRetryDelay);
    }

    // O agendamento fecha a tentativa atual como recuperável sem criar execução terminal paralela.
    public void ScheduleRetry(
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        string failureCode,
        int? pagesRead = null,
        int? receivedCount = null)
    {
        if (!CanScheduleRetry(nowUtc, nextAttemptAtUtc, failureCode))
        {
            throw new InvalidOperationException("A execução não pode receber outra tentativa automática neste estado.");
        }

        State = SyncRunState.Pending;
        FailureCode = failureCode;
        NextAttemptAtUtc = nextAttemptAtUtc;
        ExecutionMessageEnqueuedAtUtc = null;
        PagesRead = pagesRead;
        ReceivedCount = receivedCount;
        CoverageState = pagesRead is > 0 ? SyncCoverageState.Partial : SyncCoverageState.Unknown;
        ValidateForPersistence();
    }

    /// <summary>Revalida UUIDs, intervalos, contadores, estados, código de falha e horários antes de cada gravação EF.</summary>
    public void ValidateForPersistence()
    {
        EnsureNonEmpty(Id, nameof(Id));
        EnsureNonEmpty(UserId, nameof(UserId));
        EnsureNonEmpty(ConnectionId, nameof(ConnectionId));
        EnsureUtc(CreatedAtUtc, nameof(CreatedAtUtc));
        EnsureUtc(StartedAtUtc, nameof(StartedAtUtc));
        EnsureUtc(FinishedAtUtc, nameof(FinishedAtUtc));
        EnsureUtc(AttemptStartedAtUtc, nameof(AttemptStartedAtUtc));
        EnsureUtc(AttemptDeadlineAtUtc, nameof(AttemptDeadlineAtUtc));
        EnsureUtc(NextAttemptAtUtc, nameof(NextAttemptAtUtc));
        EnsureUtc(ExecutionMessageEnqueuedAtUtc, nameof(ExecutionMessageEnqueuedAtUtc));
        EnsureInterval(RequestedFrom, RequestedToExclusive, ObservedFrom, ObservedTo);
        EnsureTimestamps(CreatedAtUtc, StartedAtUtc, FinishedAtUtc);
        EnsureAttemptState();
        EnsureCounts(PagesRead, ReceivedCount, UniqueCount, PersistedCount);
        EnsureEnums(State, CoverageState);
        EnsureFailureCode(FailureCode);
        EnsureIdempotencyFingerprints(IdempotencyKeyHash, RequestFingerprint);
    }

    // Orçamento fica limitado ao valor aprovado e a janela persistida não excede dez minutos da posse.
    private void EnsureAttemptState()
    {
        if (AttemptLimit is < 1 or > ApprovedAttemptLimit
            || AttemptNumber < 0
            || AttemptNumber > AttemptLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(AttemptNumber), "Tentativa e orçamento persistidos são inválidos.");
        }

        if ((AttemptStartedAtUtc.HasValue != AttemptDeadlineAtUtc.HasValue)
            || (AttemptStartedAtUtc.HasValue && AttemptNumber == 0))
        {
            throw new ArgumentException("Início e deadline da tentativa precisam existir juntos após a primeira posse.");
        }

        if (AttemptStartedAtUtc is { } startedAtUtc)
        {
            if (startedAtUtc < CreatedAtUtc || AttemptDeadlineAtUtc <= startedAtUtc
                || AttemptDeadlineAtUtc > startedAtUtc.Add(MaximumAttemptDuration))
            {
                throw new ArgumentException("A janela persistida da tentativa excede o orçamento temporal aprovado.");
            }
        }

        if (NextAttemptAtUtc is { } nextAttemptAtUtc && nextAttemptAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException("A próxima tentativa não pode anteceder a aceitação.", nameof(NextAttemptAtUtc));
        }

        if (ExecutionMessageEnqueuedAtUtc is { } enqueuedAtUtc && enqueuedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException("O marcador de envio não pode anteceder a aceitação.", nameof(ExecutionMessageEnqueuedAtUtc));
        }
    }

    // Janelas solicitadas usam limite superior exclusivo; extremos observados mantêm ordem cronológica.
    private static void EnsureInterval(
        DateOnly requestedFrom,
        DateOnly? requestedToExclusive,
        DateOnly? observedFrom,
        DateOnly? observedTo)
    {
        if (requestedToExclusive.HasValue && requestedToExclusive.Value <= requestedFrom)
        {
            throw new ArgumentException("O limite superior exclusivo precisa ser posterior ao início solicitado.");
        }

        if (observedFrom.HasValue && observedTo.HasValue && observedFrom.Value > observedTo.Value)
        {
            throw new ArgumentException("O extremo observado inicial não pode ser posterior ao final.");
        }
    }

    // Fim não precede início nem criação; nenhum horário é convertido silenciosamente para UTC.
    private static void EnsureTimestamps(
        DateTimeOffset createdAtUtc,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? finishedAtUtc)
    {
        if (startedAtUtc.HasValue && startedAtUtc.Value < createdAtUtc)
        {
            throw new ArgumentException("O início não pode preceder a criação.", nameof(startedAtUtc));
        }

        var earliestFinish = startedAtUtc ?? createdAtUtc;
        if (finishedAtUtc.HasValue && finishedAtUtc.Value < earliestFinish)
        {
            throw new ArgumentException("O fim não pode preceder o início ou a criação.", nameof(finishedAtUtc));
        }
    }

    // Contagem desconhecida permanece null; qualquer contagem conhecida precisa ser não negativa.
    private static void EnsureCounts(params int?[] counts)
    {
        if (counts.Any(count => count < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(counts), "Contagens conhecidas não podem ser negativas.");
        }
    }

    // Enumerações inválidas por cast não podem ser gravadas como estados desconhecidos implícitos.
    private static void EnsureEnums(SyncRunState state, SyncCoverageState coverageState)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (!Enum.IsDefined(coverageState))
        {
            throw new ArgumentOutOfRangeException(nameof(coverageState));
        }
    }

    // Somente os oito códigos aprovados podem ser armazenados; mensagens externas são rejeitadas sem eco.
    private static void EnsureFailureCode(string? failureCode)
    {
        if (failureCode is not null && !SyncRunFailureCodes.IsAllowed(failureCode))
        {
            throw new ArgumentException("O código de falha não pertence à allowlist interna.", nameof(failureCode));
        }
    }

    // Chave e fingerprint são um par opcional para preservar registros históricos sem aceitar dados parciais.
    private static void EnsureIdempotencyFingerprints(string? keyHash, string? requestFingerprint)
    {
        if ((keyHash is null) != (requestFingerprint is null))
        {
            throw new ArgumentException("A chave e o fingerprint de idempotência precisam ser persistidos juntos.");
        }

        if (keyHash is not null && (!IsSha256Hex(keyHash) || !IsSha256Hex(requestFingerprint!)))
        {
            throw new ArgumentException("Fingerprints de idempotência precisam conter 64 dígitos hexadecimais.");
        }
    }

    // SHA-256 fica em hexadecimal ASCII fixo, evitando armazenar a chave opaca enviada pelo cliente.
    private static bool IsSha256Hex(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    // UUIDs vazios não representam uma execução, titular ou conexão persistível.
    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("O UUID não pode ser vazio.", parameterName);
        }
    }

    // Campos nomeados UTC aceitam apenas instantes já normalizados em offset zero.
    private static void EnsureUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue && value.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante precisa estar normalizado em UTC.", parameterName);
        }
    }
}
