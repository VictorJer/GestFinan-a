using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

/// <summary>Adapta o vínculo persistido entre conexão e titular ao contrato interno da Application.</summary>
public sealed class TransactionImportConnectionReader(FinoraDbContext dbContext)
    : ITransactionImportConnectionReader
{
    public Task<TransactionImportConnection?> FindOwnedAsync(
        Guid connectionId,
        Guid userId,
        CancellationToken cancellationToken) => dbContext.Set<FinancialConnection>()
        .AsNoTracking()
        .Where(connection => connection.Id == connectionId && connection.UserId == userId)
        .Select(connection => new TransactionImportConnection(
            connection.Id,
            connection.UserId,
            connection.Provider,
            connection.ExternalItemId,
            connection.LastSuccessfulImportAtUtc))
        .SingleOrDefaultAsync(cancellationToken);
}
