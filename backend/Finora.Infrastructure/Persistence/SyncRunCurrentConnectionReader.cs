using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Seleciona exclusivamente as conexões do titular e não escolhe uma arbitrariamente quando há mais de uma.
public sealed class SyncRunCurrentConnectionReader(FinoraDbContext dbContext) : ICurrentSyncConnectionReader
{
    public async Task<CurrentSyncConnectionResult> ResolveAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Duas linhas bastam para diferenciar ausência, conexão única e ambiguidade sem carregar dados externos.
        var connectionIds = await dbContext.Set<FinancialConnection>()
            .AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .Select(connection => connection.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);

        return connectionIds.Length switch
        {
            0 => new CurrentSyncConnectionResult(CurrentSyncConnectionStatus.NotFound),
            1 => new CurrentSyncConnectionResult(CurrentSyncConnectionStatus.Found, connectionIds[0]),
            _ => new CurrentSyncConnectionResult(CurrentSyncConnectionStatus.Ambiguous)
        };
    }
}
