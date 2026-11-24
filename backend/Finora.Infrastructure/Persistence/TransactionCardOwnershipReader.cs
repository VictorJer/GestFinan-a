using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Verifica identidade do cartão com titulares e conexão em SQL, sem revelar dados de cartões alheios.
public sealed class TransactionCardOwnershipReader(FinoraDbContext dbContext)
    : ITransactionCardOwnershipReader
{
    public Task<bool> IsOwnedAsync(
        Guid userId,
        Guid connectionId,
        Guid cardId,
        CancellationToken cancellationToken) => dbContext.Set<CardAccount>()
        .AsNoTracking()
        .AnyAsync(card => card.Id == cardId
            && card.UserId == userId
            && card.ConnectionId == connectionId,
            cancellationToken);
}
