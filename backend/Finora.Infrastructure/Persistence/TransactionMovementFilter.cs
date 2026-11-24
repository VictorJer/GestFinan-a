using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// A lista e o resumo consultam o mesmo universo parametrizado e isolado por titular/conexão.
internal static class TransactionMovementFilter
{
    // A categoria só corresponde a uma anotação própria ou de sistema ligada ao movimento do titular.
    internal static IQueryable<FinancialTransaction> Build(
        FinoraDbContext dbContext,
        Guid userId,
        Guid connectionId,
        Guid? cardId,
        Guid? categoryId,
        TransactionSituation? situation,
        DateOnly? fromDate,
        DateOnly? toExclusive,
        string? search)
    {
        // Escapa os caracteres especiais do ILIKE para preservar a busca por substring literal.
        var searchPattern = search is { Length: > 0 }
            ? CreateLiteralSearchPattern(search)
            : null;

        return
            from transaction in dbContext.Set<FinancialTransaction>().AsNoTracking()
            join card in dbContext.Set<CardAccount>().AsNoTracking()
                on new { transaction.CardAccountId, transaction.UserId }
                equals new { CardAccountId = card.Id, card.UserId }
            where transaction.UserId == userId
                && card.UserId == userId
                && card.ConnectionId == connectionId
                && (!cardId.HasValue || transaction.CardAccountId == cardId.Value)
                && (!categoryId.HasValue
                    || dbContext.Set<TransactionAnnotation>()
                        .AsNoTracking()
                        .Any(annotation => annotation.TransactionId == transaction.Id
                            && annotation.UserId == userId
                            && annotation.CategoryId == categoryId.Value
                            && (annotation.CategoryScopeId == userId
                                || annotation.CategoryScopeId == Guid.Empty)))
                && (!situation.HasValue || transaction.Situation == situation.Value)
                && (!fromDate.HasValue
                    || transaction.TransactionDate.HasValue
                        && transaction.TransactionDate.Value >= fromDate.Value)
                && (!toExclusive.HasValue
                    || transaction.TransactionDate.HasValue
                        && transaction.TransactionDate.Value < toExclusive.Value)
                && (searchPattern == null
                    || EF.Functions.ILike(transaction.Description, searchPattern, "\\"))
            select transaction;
    }

    // Barra, porcentagem e sublinhado são escapados antes de compor o padrão parametrizado do PostgreSQL.
    private static string CreateLiteralSearchPattern(string search)
    {
        var escaped = search
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
