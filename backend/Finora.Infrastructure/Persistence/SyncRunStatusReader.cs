using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Projeta somente os campos operacionais e aplica o filtro de titular no próprio SQL enviado ao PostgreSQL.
public sealed class SyncRunStatusReader(FinoraDbContext dbContext) : ISyncRunStatusReader
{
    public async Task<SyncRunStatusResult?> FindOwnedAsync(
        Guid userId,
        Guid syncRunId,
        CancellationToken cancellationToken)
    {
        var result = await dbContext.Set<SyncRun>()
            .AsNoTracking()
            .Where(run => run.Id == syncRunId && run.UserId == userId)
            .Select(run => new SyncRunStatusResult(
                run.Id,
                run.State,
                run.CreatedAtUtc,
                run.StartedAtUtc,
                run.FinishedAtUtc,
                run.RequestedFrom,
                run.RequestedToExclusive,
                run.PagesRead,
                run.ReceivedCount,
                run.UniqueCount,
                run.PersistedCount,
                run.ObservedFrom,
                run.ObservedTo,
                run.CoverageState,
                run.FailureCode,
                run.NextAttemptAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        // Dados históricos fora da allowlist são omitidos, nunca serializados como texto operacional externo.
        return result is null
            || result.FailureCode is null
            || SyncRunFailureCodes.IsAllowed(result.FailureCode)
                ? result
                : result with { FailureCode = null };
    }
}
