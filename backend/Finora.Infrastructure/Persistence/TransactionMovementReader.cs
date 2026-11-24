using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Projeta movimentos apenas do titular e da conexão resolvida, associando anotação própria por chave composta.
public sealed class TransactionMovementReader(FinoraDbContext dbContext) : ITransactionMovementReader
{
    public async Task<PagedTransactionMovementSummary> ReadAsync(
        ValidatedTransactionsQuery criteria,
        CancellationToken cancellationToken)
    {
        // Critérios sem titular/conexão confiáveis falham fechados, mesmo fora do handler validado.
        if (criteria.UserId == Guid.Empty || criteria.ConnectionId == Guid.Empty)
        {
            return new PagedTransactionMovementSummary(
                criteria.Page,
                criteria.PageSize,
                0,
                Array.Empty<TransactionMovementSummary>(),
                new TransactionImportCoverageSummary(null, null, null));
        }

        // Não transforma uma query construída fora do validador em paginação sem limite ou com deslocamento inválido.
        if (criteria.Page < 1
            || criteria.PageSize < 1
            || criteria.PageSize > TransactionQueryFilterValidator.MaximumPageSize)
        {
            return new PagedTransactionMovementSummary(
                criteria.Page,
                criteria.PageSize,
                0,
                Array.Empty<TransactionMovementSummary>(),
                new TransactionImportCoverageSummary(null, null, null));
        }

        // A busca permanece substring case-insensitive, escapando barra e curingas antes da parametrização EF.
        var searchPattern = criteria.Search is { Length: > 0 } search
            ? CreateLiteralSearchPattern(search)
            : null;

        // Titularidade do movimento, cartão e anotação é repetida no SQL para não depender apenas das FKs.
        var filteredMovements =
            from transaction in dbContext.Set<FinancialTransaction>().AsNoTracking()
            join card in dbContext.Set<CardAccount>().AsNoTracking()
                on new { transaction.CardAccountId, transaction.UserId }
                equals new { CardAccountId = card.Id, card.UserId }
            join annotation in dbContext.Set<TransactionAnnotation>()
                    .AsNoTracking()
                    .Where(value => value.UserId == criteria.UserId)
                on new { TransactionId = transaction.Id, transaction.UserId }
                equals new { annotation.TransactionId, annotation.UserId }
                into annotations
            from annotation in annotations.DefaultIfEmpty()
            where transaction.UserId == criteria.UserId
                && card.UserId == criteria.UserId
                && card.ConnectionId == criteria.ConnectionId
                && (!criteria.CardId.HasValue || transaction.CardAccountId == criteria.CardId.Value)
                && (!criteria.CategoryId.HasValue
                    || annotation != null
                        && annotation.CategoryId == criteria.CategoryId.Value
                        && (annotation.CategoryScopeId == criteria.UserId
                            || annotation.CategoryScopeId == Guid.Empty))
                && (!criteria.Situation.HasValue || transaction.Situation == criteria.Situation.Value)
                && (!criteria.From.HasValue
                    || transaction.TransactionDate.HasValue
                        && transaction.TransactionDate.Value >= criteria.From.Value)
                && (!criteria.ToExclusive.HasValue
                    || transaction.TransactionDate.HasValue
                        && transaction.TransactionDate.Value < criteria.ToExclusive.Value)
                && (searchPattern == null
                    || EF.Functions.ILike(transaction.Description, searchPattern, "\\"))
            select new { Transaction = transaction, Annotation = annotation };

        // Contagem e página compartilham snapshot PostgreSQL para não misturar estados durante uma promoção.
        await using var snapshot = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var filteredCount = await filteredMovements.CountAsync(cancellationToken);
        var offset = ((long)criteria.Page - 1) * criteria.PageSize;
        IReadOnlyList<TransactionMovementSummary> items = Array.Empty<TransactionMovementSummary>();

        // O offset é convertido só quando está dentro da contagem, evitando overflow para páginas além do fim.
        if (offset < filteredCount)
        {
            items = await filteredMovements
                .OrderByDescending(movement => movement.Transaction.TransactionDate.HasValue)
                .ThenByDescending(movement => movement.Transaction.TransactionDate)
                .ThenByDescending(movement => movement.Transaction.Id)
                .Skip((int)offset)
                .Take(criteria.PageSize)
                .Select(movement => new TransactionMovementSummary(
                    movement.Transaction.Id,
                    movement.Transaction.CardAccountId,
                    movement.Transaction.Description,
                    movement.Transaction.Amount,
                    movement.Transaction.CurrencyCode,
                    movement.Transaction.OriginalDate,
                    movement.Transaction.TransactionDate,
                    movement.Transaction.DateReason,
                    movement.Transaction.Nature,
                    movement.Transaction.NatureReason,
                    movement.Transaction.Situation,
                    movement.Transaction.SituationReason,
                    movement.Transaction.InstallmentNumber,
                    movement.Transaction.TotalInstallments,
                    movement.Annotation != null
                        && (movement.Annotation.CategoryScopeId == criteria.UserId
                            || movement.Annotation.CategoryScopeId == Guid.Empty)
                        ? movement.Annotation.CategoryId
                        : null,
                    movement.Annotation == null ? null : movement.Annotation.Note))
                .ToArrayAsync(cancellationToken);
        }

        // Último sucesso, sua janela correlata e a execução mais recente compartilham o snapshot da página.
        var coverage = await ReadCoverageAsync(criteria, cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new PagedTransactionMovementSummary(criteria.Page, criteria.PageSize, filteredCount, items, coverage);
    }

    // A janela é associada somente por titular, conexão, estado concluído e instante exato de sucesso persistido.
    private async Task<TransactionImportCoverageSummary> ReadCoverageAsync(
        ValidatedTransactionsQuery criteria,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.Set<FinancialConnection>()
            .AsNoTracking()
            .Where(value => value.Id == criteria.ConnectionId && value.UserId == criteria.UserId)
            .Select(value => new { value.LastSuccessfulImportAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return new TransactionImportCoverageSummary(null, null, null);
        }

        TransactionLastSuccessfulWindowSummary? lastSuccessfulWindow = null;
        if (connection.LastSuccessfulImportAtUtc is { } lastSuccessfulImportAtUtc)
        {
            // Zero ou mais de uma correspondência não prova a janela, portanto a cobertura permanece desconhecida.
            var correlatedSuccesses = await dbContext.Set<SyncRun>()
                .AsNoTracking()
                .Where(run => run.UserId == criteria.UserId
                    && run.ConnectionId == criteria.ConnectionId
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

        // A execução mais nova não substitui o sucesso antigo e usa o mesmo desempate determinístico do status atual.
        var latest = await dbContext.Set<SyncRun>()
            .AsNoTracking()
            .Where(run => run.UserId == criteria.UserId && run.ConnectionId == criteria.ConnectionId)
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

    // O caractere de escape também é escapado para que qualquer entrada seja buscada literalmente no ILIKE.
    private static string CreateLiteralSearchPattern(string search)
    {
        var escaped = search
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
