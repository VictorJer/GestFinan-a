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
        string? failureCode)
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
        string? failureCode = null)
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
            failureCode);

        run.ValidateForPersistence();
        return run;
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
        EnsureInterval(RequestedFrom, RequestedToExclusive, ObservedFrom, ObservedTo);
        EnsureTimestamps(CreatedAtUtc, StartedAtUtc, FinishedAtUtc);
        EnsureCounts(PagesRead, ReceivedCount, UniqueCount, PersistedCount);
        EnsureEnums(State, CoverageState);
        EnsureFailureCode(FailureCode);
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
