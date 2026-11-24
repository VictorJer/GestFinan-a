using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// A lista e o dashboard compartilham a correlação conservadora de cobertura no snapshot do chamador.
internal static class TransactionImportCoverageReader
{
    // Só uma execução de sucesso com instante exato correlaciona a janela confirmada da conexão.
    internal static async Task<TransactionImportCoverageSummary> ReadAsync(
        FinoraDbContext dbContext,
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.Set<FinancialConnection>()
            .AsNoTracking()
            .Where(value => value.Id == connectionId && value.UserId == userId)
            .Select(value => new { value.LastSuccessfulImportAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return new TransactionImportCoverageSummary(null, null, null);
        }

        TransactionLastSuccessfulWindowSummary? lastSuccessfulWindow = null;
        if (connection.LastSuccessfulImportAtUtc is { } lastSuccessfulImportAtUtc)
        {
            // Uma ou várias correspondências não comprovam qual janela foi concluída.
            var correlatedSuccesses = await dbContext.Set<SyncRun>()
                .AsNoTracking()
                .Where(run => run.UserId == userId
                    && run.ConnectionId == connectionId
                    && run.State == SyncRunState.Succeeded
                    && run.FinishedAtUtc == lastSuccessfulImportAtUtc)
                .OrderBy(run => run.Id)
                .Take(2)
                .Select(run => new TransactionLastSuccessfulWindowSummary(
                    run.RequestedFrom,
                    run.RequestedToExclusive,
                    run.ObservedFrom,
                    run.ObservedTo,
                    run.CoverageState))
                .ToArrayAsync(cancellationToken);
            if (correlatedSuccesses.Length == 1)
            {
                lastSuccessfulWindow = correlatedSuccesses[0];
            }
        }

        // A execução mais recente permanece separada do último sucesso e usa desempate determinístico.
        var latest = await dbContext.Set<SyncRun>()
            .AsNoTracking()
            .Where(run => run.UserId == userId && run.ConnectionId == connectionId)
            .OrderByDescending(run => run.CreatedAtUtc)
            .ThenByDescending(run => run.Id)
            .Select(run => new TransactionLatestExecutionSummary(
                run.State,
                run.CoverageState,
                run.FailureCode))
            .FirstOrDefaultAsync(cancellationToken);
        if (latest?.FailureCode is { } failureCode && !SyncRunFailureCodes.IsAllowed(failureCode))
        {
            latest = latest with { FailureCode = null };
        }

        return new TransactionImportCoverageSummary(
            connection.LastSuccessfulImportAtUtc,
            lastSuccessfulWindow,
            latest);
    }
}
