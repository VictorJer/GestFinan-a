namespace Finora.Domain.FinancialData;

/// <summary>Posse persistida por conexão que impede uma tentativa antiga de renovar geração substituída.</summary>
public sealed class SyncRunLease
{
    // A materialização EF restaura a linha única da conexão sem criar uma geração nova.
    private SyncRunLease() { }

    public Guid ConnectionId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid SyncRunId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTimeOffset AcquiredAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public long FencingToken { get; private set; }

    // A primeira posse começa na geração um; cada tomada após expiração recebe geração estritamente maior.
    public static SyncRunLease Create(
        Guid connectionId,
        Guid userId,
        Guid syncRunId,
        int attemptNumber,
        DateTimeOffset acquiredAtUtc,
        DateTimeOffset expiresAtUtc,
        long fencingToken)
    {
        var lease = new SyncRunLease
        {
            ConnectionId = connectionId,
            UserId = userId,
            SyncRunId = syncRunId,
            AttemptNumber = attemptNumber,
            AcquiredAtUtc = acquiredAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FencingToken = fencingToken
        };
        lease.ValidateForPersistence();
        return lease;
    }

    // A geração só muda após expiração, preservando monotonicidade mesmo ao retomar a mesma SyncRun.
    public void TakeOver(
        Guid syncRunId,
        int attemptNumber,
        DateTimeOffset acquiredAtUtc,
        DateTimeOffset expiresAtUtc,
        long fencingToken)
    {
        ValidateUtc(acquiredAtUtc, nameof(acquiredAtUtc));
        if (ExpiresAtUtc > acquiredAtUtc)
        {
            throw new InvalidOperationException("Uma posse vigente não pode ser substituída.");
        }

        if (fencingToken <= FencingToken)
        {
            throw new ArgumentOutOfRangeException(nameof(fencingToken));
        }

        SyncRunId = syncRunId;
        AttemptNumber = attemptNumber;
        AcquiredAtUtc = acquiredAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        FencingToken = fencingToken;
        ValidateForPersistence();
    }

    // Renovação mantém tentativa/geração e recusa claims antigos, relógio regressivo ou lease já expirado.
    public bool TryRenew(
        Guid syncRunId,
        int attemptNumber,
        long fencingToken,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        ValidateUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (SyncRunId != syncRunId || AttemptNumber != attemptNumber || FencingToken != fencingToken
            || nowUtc < AcquiredAtUtc || ExpiresAtUtc <= nowUtc)
        {
            return false;
        }

        if (expiresAtUtc <= ExpiresAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "A renovação precisa estender a expiração vigente.");
        }

        ExpiresAtUtc = expiresAtUtc;
        ValidateForPersistence();
        return true;
    }

    // O handoff de retry encerra somente a posse vigente, mantendo a geração para o próximo takeover.
    public bool TryExpire(
        Guid syncRunId,
        int attemptNumber,
        long fencingToken,
        DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (SyncRunId != syncRunId || AttemptNumber != attemptNumber || FencingToken != fencingToken
            || nowUtc < AcquiredAtUtc || ExpiresAtUtc <= nowUtc)
        {
            return false;
        }

        ExpiresAtUtc = nowUtc;
        ValidateForPersistence();
        return true;
    }

    // A barreira da persistência revalida identidade, tentativa, instantes e geração antes de cada gravação.
    public void ValidateForPersistence()
    {
        if (ConnectionId == Guid.Empty || UserId == Guid.Empty || SyncRunId == Guid.Empty
            || AttemptNumber < 1 || FencingToken < 1)
        {
            throw new InvalidOperationException("A posse persistida contém identificador, tentativa ou fencing inválido.");
        }

        ValidateUtc(AcquiredAtUtc, nameof(AcquiredAtUtc));
        ValidateUtc(ExpiresAtUtc, nameof(ExpiresAtUtc));
        if (ExpiresAtUtc <= AcquiredAtUtc)
        {
            throw new InvalidOperationException("A expiração precisa ser posterior à aquisição.");
        }
    }

    // Campos nomeados UTC não convertem offsets silenciosamente.
    private static void ValidateUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante precisa estar normalizado em UTC.", parameterName);
        }
    }
}
