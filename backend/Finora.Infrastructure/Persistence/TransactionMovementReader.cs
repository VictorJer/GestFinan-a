using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
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

        // A lista e o dashboard usam o mesmo predicado; somente a lista projeta os campos da anotação.
        var matchingTransactions = TransactionMovementFilter.Build(
            dbContext,
            criteria.UserId,
            criteria.ConnectionId,
            criteria.CardId,
            criteria.CategoryId,
            criteria.Situation,
            criteria.From,
            criteria.ToExclusive,
            criteria.Search);
        var filteredMovements =
            from transaction in matchingTransactions
            join annotation in dbContext.Set<TransactionAnnotation>()
                    .AsNoTracking()
                    .Where(value => value.UserId == criteria.UserId)
                on new { TransactionId = transaction.Id, transaction.UserId }
                equals new { annotation.TransactionId, annotation.UserId }
                into annotations
            from annotation in annotations.DefaultIfEmpty()
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
        var coverage = await TransactionImportCoverageReader.ReadAsync(
            dbContext,
            criteria.UserId,
            criteria.ConnectionId,
            cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new PagedTransactionMovementSummary(criteria.Page, criteria.PageSize, filteredCount, items, coverage);
    }
}
