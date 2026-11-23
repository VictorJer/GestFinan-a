using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finora.Infrastructure.Persistence;

/// <summary>Coordena posses no PostgreSQL; locks locais/da instância não participam da exclusão mútua.</summary>
public sealed class SyncRunLeaseStore(
    FinoraDbContext dbContext,
    ILogger<SyncRunLeaseStore>? logger = null) : ISyncRunLeaseStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(120);

    public async Task<SyncRunLeaseAcquisitionResult> AcquireAsync(
        Guid syncRunId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ValidateRequest(syncRunId, nowUtc);
        nowUtc = NormalizePostgreSqlTimestamp(nowUtc);

        // A execução aceita é a fonte de titular/conexão; nenhum desses identificadores vem do envelope do broker.
        var identity = await dbContext.Set<SyncRun>()
            .AsNoTracking()
            .Where(run => run.Id == syncRunId)
            .Select(run => new { run.ConnectionId, run.UserId, run.State })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null)
        {
            return new SyncRunLeaseAcquisitionResult(SyncRunLeaseAcquisitionStatus.RunUnavailable);
        }

        if (identity.State is SyncRunState.Succeeded or SyncRunState.Failed)
        {
            return new SyncRunLeaseAcquisitionResult(SyncRunLeaseAcquisitionStatus.AlreadyTerminal);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOwnedConnectionAsync(identity.ConnectionId, identity.UserId, cancellationToken))
        {
            return new SyncRunLeaseAcquisitionResult(SyncRunLeaseAcquisitionStatus.RunUnavailable);
        }

        // A releitura sob o lock descarta terminalização concorrente antes de criar/alterar a posse.
        var activeRun = await dbContext.Set<SyncRun>()
            .SingleOrDefaultAsync(run => run.Id == syncRunId
                && run.ConnectionId == identity.ConnectionId
                && run.UserId == identity.UserId
                && (run.State == SyncRunState.Pending || run.State == SyncRunState.Running), cancellationToken);
        if (activeRun is null)
        {
            var currentState = await dbContext.Set<SyncRun>().AsNoTracking()
                .Where(run => run.Id == syncRunId
                    && run.ConnectionId == identity.ConnectionId
                    && run.UserId == identity.UserId)
                .Select(run => (SyncRunState?)run.State)
                .SingleOrDefaultAsync(cancellationToken);
            return new SyncRunLeaseAcquisitionResult(
                currentState is SyncRunState.Succeeded or SyncRunState.Failed
                    ? SyncRunLeaseAcquisitionStatus.AlreadyTerminal
                    : SyncRunLeaseAcquisitionStatus.RunUnavailable);
        }

        var lease = await dbContext.Set<SyncRunLease>()
            .SingleOrDefaultAsync(item => item.ConnectionId == identity.ConnectionId, cancellationToken);
        if (lease is not null && lease.ExpiresAtUtc > nowUtc)
        {
            // Uma redelivery enquanto a posse está viva não recebe o fencing atual para evitar dois workers donos.
            return new SyncRunLeaseAcquisitionResult(SyncRunLeaseAcquisitionStatus.Busy);
        }

        // Tentativa/deadline e posse mudam na mesma transação antes que qualquer chamada externa comece.
        if (!activeRun.TryBeginAttempt(nowUtc))
        {
            return new SyncRunLeaseAcquisitionResult(SyncRunLeaseAcquisitionStatus.NotEligible);
        }

        var expiresAtUtc = nowUtc.Add(LeaseDuration);
        if (lease is null)
        {
            lease = SyncRunLease.Create(
                identity.ConnectionId, identity.UserId, syncRunId, activeRun.AttemptNumber,
                nowUtc, expiresAtUtc, fencingToken: 1);
            dbContext.Add(lease);
        }
        else
        {
            // A posse expirada é reaproveitada; checked impede wraparound que tornaria fencing antigo vigente.
            var nextFencingToken = checked(lease.FencingToken + 1);
            lease.TakeOver(syncRunId, activeRun.AttemptNumber, nowUtc, expiresAtUtc, nextFencingToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SyncRunLeaseAcquisitionResult(
            SyncRunLeaseAcquisitionStatus.Acquired,
            ToGrant(lease, activeRun));
    }

    public async Task<SyncRunLeaseGrant?> RenewAsync(
        SyncRunLeaseGrant grant,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ValidateRequest(grant.SyncRunId, nowUtc);
        nowUtc = NormalizePostgreSqlTimestamp(nowUtc);
        if (grant.ConnectionId == Guid.Empty || grant.UserId == Guid.Empty || grant.FencingToken < 1)
        {
            throw new ArgumentException("O claim de posse é inválido.", nameof(grant));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOwnedConnectionAsync(grant.ConnectionId, grant.UserId, cancellationToken))
        {
            return null;
        }

        // Renovação só prossegue enquanto a mesma SyncRun continua ativa para esse titular/conexão.
        var activeRun = await dbContext.Set<SyncRun>()
            .AsNoTracking()
            .SingleOrDefaultAsync(run => run.Id == grant.SyncRunId
                && run.ConnectionId == grant.ConnectionId
                && run.UserId == grant.UserId
                && run.AttemptNumber == grant.AttemptNumber
                && (run.State == SyncRunState.Pending || run.State == SyncRunState.Running), cancellationToken);
        if (activeRun is null)
        {
            return null;
        }

        var lease = await dbContext.Set<SyncRunLease>()
            .SingleOrDefaultAsync(item => item.ConnectionId == grant.ConnectionId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var expiresAtUtc = nowUtc.Add(LeaseDuration);
        if (!lease.TryRenew(
                grant.SyncRunId,
                grant.AttemptNumber,
                grant.FencingToken,
                nowUtc,
                expiresAtUtc))
        {
            return null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToGrant(lease, activeRun);
    }

    public async Task<bool> ScheduleRetryAsync(
        SyncRunLeaseGrant grant,
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        string failureCode,
        int? pagesRead,
        int? receivedCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ValidateRequest(grant.SyncRunId, nowUtc);
        if (nextAttemptAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O prazo registrado precisa estar em UTC.", nameof(nextAttemptAtUtc));
        }
        nowUtc = NormalizePostgreSqlTimestamp(nowUtc);
        nextAttemptAtUtc = NormalizePostgreSqlTimestamp(nextAttemptAtUtc);
        if (grant.ConnectionId == Guid.Empty || grant.UserId == Guid.Empty || grant.AttemptNumber < 1
            || grant.FencingToken < 1)
        {
            throw new ArgumentException("O claim de posse é inválido.", nameof(grant));
        }

        // Uma promoção malsucedida pode ter deixado entidades apenas no tracking; nenhuma delas deve entrar no handoff.
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOwnedConnectionAsync(grant.ConnectionId, grant.UserId, cancellationToken))
        {
            return false;
        }

        // O update é permitido somente para a tentativa Running que ainda apresenta o fencing recebido.
        var run = await dbContext.Set<SyncRun>()
            .SingleOrDefaultAsync(item => item.Id == grant.SyncRunId
                && item.ConnectionId == grant.ConnectionId
                && item.UserId == grant.UserId
                && item.AttemptNumber == grant.AttemptNumber
                && item.State == SyncRunState.Running, cancellationToken);
        var lease = await dbContext.Set<SyncRunLease>()
            .SingleOrDefaultAsync(item => item.ConnectionId == grant.ConnectionId, cancellationToken);
        var runMatchesGrant = run is not null
            && run.ConnectionId == grant.ConnectionId
            && run.UserId == grant.UserId
            && run.AttemptNumber == grant.AttemptNumber
            && run.AttemptStartedAtUtc == grant.AttemptStartedAtUtc
            && run.AttemptDeadlineAtUtc == grant.AttemptDeadlineAtUtc
            && run.RequestedFrom == grant.RequestedFrom
            && run.RequestedToExclusive == grant.RequestedToExclusive;
        var leaseMatchesGrant = lease is not null
            && lease.UserId == grant.UserId
            && lease.SyncRunId == grant.SyncRunId
            && lease.AttemptNumber == grant.AttemptNumber
            && lease.FencingToken == grant.FencingToken
            && lease.AcquiredAtUtc == grant.AcquiredAtUtc;
        var retryIsAllowed = run?.CanScheduleRetry(nowUtc, nextAttemptAtUtc, failureCode) == true;
        if (!runMatchesGrant || !leaseMatchesGrant || !retryIsAllowed)
        {
            // Registra somente predicados booleanos para localizar handoff recusado sem expor IDs ou conteúdo financeiro.
            logger?.LogWarning(
                "O handoff de retry foi recusado: runMatchesGrant={RunMatchesGrant}, leaseMatchesGrant={LeaseMatchesGrant}, retryIsAllowed={RetryIsAllowed}.",
                runMatchesGrant, leaseMatchesGrant, retryIsAllowed);
            return false;
        }

        // A agenda persistida na SyncRun é o handoff recuperável; liberar a posse evita reter o worker no backoff.
        if (!lease!.TryExpire(grant.SyncRunId, grant.AttemptNumber, grant.FencingToken, nowUtc))
        {
            // Expiração/fencing recusados são diagnósticos fixos e não carregam o claim recebido do consumer.
            logger?.LogWarning("O handoff de retry não conseguiu liberar a posse vigente.");
            return false;
        }

        run!.ScheduleRetry(nowUtc, nextAttemptAtUtc, failureCode, pagesRead, receivedCount);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CompleteFailureAsync(
        SyncRunLeaseGrant grant,
        DateTimeOffset finishedAtUtc,
        string failureCode,
        int? pagesRead,
        int? receivedCount,
        DateTimeOffset? nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ValidateRequest(grant.SyncRunId, finishedAtUtc);
        if (grant.ConnectionId == Guid.Empty || grant.UserId == Guid.Empty || grant.AttemptNumber < 1
            || grant.FencingToken < 1 || !SyncRunFailureCodes.IsAllowed(failureCode))
        {
            throw new ArgumentException("A conclusão de falha não atende ao contrato persistente.", nameof(grant));
        }

        if (nextAttemptAtUtc is { } retryAtUtc && retryAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O prazo registrado precisa estar em UTC.", nameof(nextAttemptAtUtc));
        }
        finishedAtUtc = NormalizePostgreSqlTimestamp(finishedAtUtc);
        if (nextAttemptAtUtc is { } retryAtUtcToNormalize)
        {
            nextAttemptAtUtc = NormalizePostgreSqlTimestamp(retryAtUtcToNormalize);
        }

        // O estado anterior ao erro de promoção é descartado antes de carregar a falha no mesmo contexto scoped.
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOwnedConnectionAsync(grant.ConnectionId, grant.UserId, cancellationToken))
        {
            return false;
        }

        // A falha fecha somente a execução/tentativa vigente e libera o fencing dentro do mesmo commit.
        var run = await dbContext.Set<SyncRun>().SingleOrDefaultAsync(item => item.Id == grant.SyncRunId
            && item.ConnectionId == grant.ConnectionId
            && item.UserId == grant.UserId
            && item.AttemptNumber == grant.AttemptNumber
            && item.State == SyncRunState.Running, cancellationToken);
        var lease = await dbContext.Set<SyncRunLease>()
            .SingleOrDefaultAsync(item => item.ConnectionId == grant.ConnectionId, cancellationToken);
        if (run is null || lease is null
            || lease.UserId != grant.UserId
            || lease.SyncRunId != grant.SyncRunId
            || lease.AttemptNumber != grant.AttemptNumber
            || lease.FencingToken != grant.FencingToken
            || lease.AcquiredAtUtc != grant.AcquiredAtUtc
            || run.AttemptStartedAtUtc != grant.AttemptStartedAtUtc
            || run.AttemptDeadlineAtUtc != grant.AttemptDeadlineAtUtc
            || run.RequestedFrom != grant.RequestedFrom
            || run.RequestedToExclusive != grant.RequestedToExclusive
            || !lease.TryExpire(grant.SyncRunId, grant.AttemptNumber, grant.FencingToken, finishedAtUtc)
            || !run.TryCompleteAttemptFailure(
                grant.AttemptNumber, finishedAtUtc, failureCode, pagesRead, receivedCount, nextAttemptAtUtc))
        {
            return false;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Todas as operações de posse usam o lock da conexão como mutex PostgreSQL compartilhado entre instâncias.
    private async Task<bool> LockOwnedConnectionAsync(
        Guid connectionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connections = await dbContext.Set<FinancialConnection>()
            .FromSqlInterpolated($"""SELECT * FROM "FinancialConnection" WHERE "Id" = {connectionId} AND "UserId" = {userId} FOR UPDATE""")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return connections.Count == 1;
    }

    // A duração é única e aprovada; a camada de consumer controla a frequência de renovação de 30 segundos.
    private static void ValidateRequest(Guid syncRunId, DateTimeOffset nowUtc)
    {
        if (syncRunId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(syncRunId), "A execução precisa ser válida.");
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante precisa estar normalizado em UTC.", nameof(nowUtc));
        }
    }

    // Timestamptz do PostgreSQL tem precisão de microssegundos; grants persistidos não podem reter ticks sub-micro.
    private static DateTimeOffset NormalizePostgreSqlTimestamp(DateTimeOffset value)
    {
        var utcTicks = value.UtcTicks;
        return new DateTimeOffset(utcTicks - utcTicks % 10, TimeSpan.Zero);
    }

    // O claim expõe apenas a posse persistida necessária ao consumer e nunca inclui segredo ou payload financeiro.
    private static SyncRunLeaseGrant ToGrant(SyncRunLease lease, SyncRun run)
    {
        if (run.AttemptStartedAtUtc is not { } attemptStartedAtUtc
            || run.AttemptDeadlineAtUtc is not { } attemptDeadlineAtUtc)
        {
            throw new InvalidOperationException("A tentativa adquirida não possui sua janela temporal persistida.");
        }

        return new SyncRunLeaseGrant(
            lease.ConnectionId,
            lease.UserId,
            lease.SyncRunId,
            lease.AttemptNumber,
            lease.FencingToken,
            lease.AcquiredAtUtc,
            lease.ExpiresAtUtc,
            run.RequestedFrom,
            run.RequestedToExclusive,
            attemptStartedAtUtc,
            attemptDeadlineAtUtc);
    }
}
