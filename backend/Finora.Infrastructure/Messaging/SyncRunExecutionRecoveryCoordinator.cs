using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Messaging;

/// <summary>Reconcilia somente obrigações de envio; a importação financeira continua exclusiva do consumer.</summary>
public sealed class SyncRunExecutionRecoveryCoordinator(
    FinoraDbContext dbContext,
    SyncRunExecutionMessageSender messageSender,
    TimeProvider timeProvider)
{
    private const int MaximumCandidatesPerPass = 50;

    // Seleciona poucos IDs elegíveis; cada um será revalidado sob lock antes de gravar marcador e outbox.
    public async Task<int> EnqueueRecoverableExecutionsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var candidates = await dbContext.Set<SyncRun>()
            .AsNoTracking()
            .Where(run => run.ExecutionMessageEnqueuedAtUtc == null
                && ((run.State == SyncRunState.Pending
                        && (run.NextAttemptAtUtc == null || run.NextAttemptAtUtc <= nowUtc))
                    || (run.State == SyncRunState.Running
                        && !dbContext.Set<SyncRunLease>().Any(lease =>
                            lease.ConnectionId == run.ConnectionId && lease.ExpiresAtUtc > nowUtc))))
            .OrderBy(run => run.CreatedAtUtc)
            .ThenBy(run => run.Id)
            .Select(run => run.Id)
            .Take(MaximumCandidatesPerPass)
            .ToArrayAsync(cancellationToken);

        var recoveredCount = 0;
        foreach (var syncRunId in candidates)
        {
            if (await TryRecoverOneAsync(syncRunId, nowUtc, cancellationToken))
            {
                recoveredCount++;
            }
        }

        return recoveredCount;
    }

    // Revalida estado/lease no mutex da conexão e confirma marcador+mensagem outbox na mesma transação curta.
    private async Task<bool> TryRecoverOneAsync(
        Guid syncRunId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var identity = await dbContext.Set<SyncRun>()
            .AsNoTracking()
            .Where(run => run.Id == syncRunId)
            .Select(run => new { run.ConnectionId, run.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null || !await LockOwnedConnectionAsync(identity.ConnectionId, identity.UserId, cancellationToken))
        {
            return false;
        }

        var run = await dbContext.Set<SyncRun>().SingleOrDefaultAsync(
            value => value.Id == syncRunId
                && value.ConnectionId == identity.ConnectionId
                && value.UserId == identity.UserId,
            cancellationToken);
        var lease = await dbContext.Set<SyncRunLease>()
            .SingleOrDefaultAsync(value => value.ConnectionId == identity.ConnectionId, cancellationToken);
        if (run is null || run.ExecutionMessageEnqueuedAtUtc is not null)
        {
            return false;
        }

        var duePending = run.State == SyncRunState.Pending
            && (run.NextAttemptAtUtc is null || run.NextAttemptAtUtc <= nowUtc);
        var abandonedRunning = run.State == SyncRunState.Running
            && (lease is null || lease.ExpiresAtUtc <= nowUtc);
        if (!duePending && !abandonedRunning)
        {
            return false;
        }

        if (abandonedRunning && run.AttemptNumber >= run.AttemptLimit)
        {
            // O último processo morreu após consumir o orçamento; encerra a mesma execução sem declarar sucesso.
            if (!run.TryCompleteAbandonedAttemptFailure(nowUtc))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (!run.TryMarkExecutionMessageEnqueued(nowUtc))
        {
            return false;
        }

        // A janela original fica no registro; o envelope conserva o timestamp de criação em todos os reenvios.
        await messageSender.SendAsync(run.Id, run.CreatedAtUtc, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // O lock PostgreSQL da conexão serializa instâncias concorrentes e a aquisição do consumer.
    private async Task<bool> LockOwnedConnectionAsync(
        Guid connectionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connections = await dbContext.Set<FinancialConnection>()
            .FromSqlInterpolated($"SELECT * FROM \"FinancialConnection\" WHERE \"Id\" = {connectionId} AND \"UserId\" = {userId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return connections.Count == 1;
    }
}
