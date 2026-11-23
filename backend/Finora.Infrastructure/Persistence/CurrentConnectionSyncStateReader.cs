using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Projeta conexão e execução mais recente pelo titular no SQL, sem consultar broker ou origem financeira.
public sealed class CurrentConnectionSyncStateReader(FinoraDbContext dbContext)
    : ICurrentConnectionSyncStateReader
{
    public async Task<CurrentConnectionSyncStateReadResult> ReadAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Duas linhas distinguem ausência, conexão única e ambiguidade sem selecionar uma conexão arbitrariamente.
        var connections = await dbContext.Set<FinancialConnection>()
            .AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .OrderBy(connection => connection.Id)
            .Take(2)
            .Select(connection => new
            {
                connection.Id,
                connection.LastSuccessfulImportAtUtc,
                connection.SourceUpdatedAtUtc
            })
            .ToArrayAsync(cancellationToken);
        if (connections.Length == 0)
        {
            return new CurrentConnectionSyncStateReadResult(CurrentSyncConnectionStatus.NotFound);
        }

        if (connections.Length > 1)
        {
            return new CurrentConnectionSyncStateReadResult(CurrentSyncConnectionStatus.Ambiguous);
        }

        var connection = connections[0];
        var runs = dbContext.Set<SyncRun>()
            .AsNoTracking()
            .Where(run => run.ConnectionId == connection.Id && run.UserId == userId);

        // A criação/id ordena a execução pertinente, e a tentativa vem do horário real persistido no worker.
        var latestExecution = await runs
            .OrderByDescending(run => run.CreatedAtUtc)
            .ThenByDescending(run => run.Id)
            .Select(run => new CurrentConnectionSyncExecutionSnapshot(
                run.Id,
                run.State,
                run.CreatedAtUtc,
                run.AttemptStartedAtUtc,
                run.FinishedAtUtc,
                run.CoverageState,
                run.FailureCode))
            .FirstOrDefaultAsync(cancellationToken);
        if (latestExecution?.FailureCode is { } failureCode
            && !SyncRunFailureCodes.IsAllowed(failureCode))
        {
            latestExecution = latestExecution with { FailureCode = null };
        }

        // A maior tentativa conhecida pode pertencer a uma execução anterior ainda que a mais recente aguarde fila.
        var lastAttemptAtUtc = await runs
            .Where(run => run.AttemptStartedAtUtc != null)
            .MaxAsync(run => run.AttemptStartedAtUtc, cancellationToken);

        return new CurrentConnectionSyncStateReadResult(
            CurrentSyncConnectionStatus.Found,
            new CurrentConnectionSyncStateSnapshot(
                connection.LastSuccessfulImportAtUtc,
                connection.SourceUpdatedAtUtc,
                lastAttemptAtUtc,
                latestExecution));
    }
}
