using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Cada busca de escrita repete o UserId no SQL para que IDs de outro titular sejam indistinguíveis dos ausentes.
public sealed class InvestmentStore(FinoraDbContext dbContext) : IInvestmentStore
{
    // A consulta projeta somente campos da carteira e nunca navega para cartões ou movimentos financeiros.
    public async Task<IReadOnlyList<InvestmentItem>> ReadOwnedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<InvestmentItem>();
        }

        return await dbContext.Set<Investment>()
            .AsNoTracking()
            .Where(investment => investment.UserId == userId)
            .Select(investment => new InvestmentItem(
                investment.Id,
                investment.Name,
                investment.Class,
                investment.AppliedAmount,
                investment.CurrentAmount))
            .ToArrayAsync(cancellationToken);
    }

    // A entidade validada é gravada diretamente; a resposta confirma apenas os campos persistidos da carteira.
    public async Task<InvestmentItem> CreateAsync(
        Investment investment,
        CancellationToken cancellationToken)
    {
        dbContext.Set<Investment>().Add(investment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return InvestmentItem.From(investment);
    }

    // A consulta conjunta por ID e titular evita carregar ou revelar a posição pertencente a outra sessão.
    public async Task<InvestmentItem?> UpdateOwnedAsync(
        Investment requested,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<Investment>().SingleOrDefaultAsync(
            investment => investment.Id == requested.Id && investment.UserId == requested.UserId,
            cancellationToken);
        if (existing is null)
        {
            return null;
        }

        existing.Update(requested.Name, requested.Class, requested.AppliedAmount, requested.CurrentAmount);
        await dbContext.SaveChangesAsync(cancellationToken);
        return InvestmentItem.From(existing);
    }

    // A remoção usa o mesmo filtro de propriedade da edição e retorna apenas se uma linha própria foi removida.
    public async Task<bool> DeleteOwnedAsync(
        Guid userId,
        Guid investmentId,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<Investment>().SingleOrDefaultAsync(
            investment => investment.Id == investmentId && investment.UserId == userId,
            cancellationToken);
        if (existing is null)
        {
            return false;
        }

        dbContext.Set<Investment>().Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
