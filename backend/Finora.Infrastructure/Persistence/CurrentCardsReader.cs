using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Lê cartões persistidos sem rastreamento e sem retornar identificadores externos ou dados financeiros extras.
public sealed class CurrentCardsReader(FinoraDbContext dbContext) : ICurrentCardsReader
{
    public async Task<CurrentCardsQueryResult> ReadAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Duas linhas do próprio titular distinguem ausência, conexão única e ambiguidade sem selecionar arbitrariamente.
        var connectionIds = await dbContext.Set<FinancialConnection>()
            .AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .OrderBy(connection => connection.Id)
            .Select(connection => connection.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);

        if (connectionIds.Length == 0)
        {
            return new CurrentCardsQueryResult(CurrentCardsQueryStatus.ConnectionNotFound);
        }

        if (connectionIds.Length > 1)
        {
            return new CurrentCardsQueryResult(CurrentCardsQueryStatus.ConnectionAmbiguous);
        }

        // Titular e conexão são ambos predicados da consulta para manter isolamento em profundidade no banco.
        var cards = await dbContext.Set<CardAccount>()
            .AsNoTracking()
            .Where(card => card.UserId == userId && card.ConnectionId == connectionIds[0])
            .OrderBy(card => card.Id)
            .Select(card => new CurrentCardSummary(
                card.Id,
                card.Name,
                card.LastFour,
                card.CurrencyCode))
            .ToArrayAsync(cancellationToken);

        return new CurrentCardsQueryResult(CurrentCardsQueryStatus.Found, cards);
    }
}
