using System.Data;
using Finora.Application.FinancialData;
using Finora.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Produz contagens descritivas do universo filtrado sem carregar valores monetários dos movimentos.
public sealed class TransactionDashboardReader(FinoraDbContext dbContext) : ITransactionDashboardReader
{
    // Total, eixos e cobertura são lidos no mesmo snapshot PostgreSQL para impedir uma resposta híbrida.
    public async Task<TransactionMovementDashboardSummary> ReadAsync(
        ValidatedTransactionDashboardQuery criteria,
        CancellationToken cancellationToken)
    {
        if (criteria.UserId == Guid.Empty || criteria.ConnectionId == Guid.Empty)
        {
            return CreateEmptySummary();
        }

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

        await using var snapshot = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var filteredCount = await matchingTransactions.CountAsync(cancellationToken);

        // Cada eixo é agrupado no banco sem selecionar Amount nem depender da cardinalidade do outro eixo.
        var natureCounts = await matchingTransactions
            .GroupBy(transaction => transaction.Nature)
            .Select(group => new { Nature = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var situationCounts = await matchingTransactions
            .GroupBy(transaction => transaction.Situation)
            .Select(group => new { Situation = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);

        // Códigos sem ocorrência aparecem com zero, sempre referente ao universo persistido filtrado.
        var natureCountsByValue = natureCounts.ToDictionary(value => value.Nature, value => value.Count);
        var countsByNature = Enum.GetValues<TransactionNature>()
            .Select(nature => new TransactionNatureCountSummary(
                nature,
                natureCountsByValue.GetValueOrDefault(nature)))
            .ToArray();
        var situationCountsByValue = situationCounts.ToDictionary(value => value.Situation, value => value.Count);
        var countsBySituation = Enum.GetValues<TransactionSituation>()
            .Select(situation => new TransactionSituationCountSummary(
                situation,
                situationCountsByValue.GetValueOrDefault(situation)))
            .ToArray();

        // A cobertura reutiliza os fatos correlatos da lista sem iniciar sincronização ou consulta externa.
        var coverage = await TransactionImportCoverageReader.ReadAsync(
            dbContext,
            criteria.UserId,
            criteria.ConnectionId,
            cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        return new TransactionMovementDashboardSummary(
            filteredCount,
            countsByNature,
            countsBySituation,
            coverage);
    }

    // Critérios sem identidade autorizável falham fechados e ainda preservam o formato fixo dos eixos.
    private static TransactionMovementDashboardSummary CreateEmptySummary() => new(
        0,
        Enum.GetValues<TransactionNature>()
            .Select(nature => new TransactionNatureCountSummary(nature, 0))
            .ToArray(),
        Enum.GetValues<TransactionSituation>()
            .Select(situation => new TransactionSituationCountSummary(situation, 0))
            .ToArray(),
        new TransactionImportCoverageSummary(null, null, null));
}
