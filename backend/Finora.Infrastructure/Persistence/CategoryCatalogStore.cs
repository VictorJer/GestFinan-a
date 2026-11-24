using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finora.Infrastructure.Persistence;

// Acesso ao catálogo é isolado de conexões financeiras, sincronização e qualquer serviço do provedor.
public sealed class CategoryCatalogStore(FinoraDbContext context) : ICategoryCatalogStore
{
    private const string ScopeNameUniqueIndex = "IX_Category_ScopeId_NormalizedName";

    // A checagem antecipada facilita 409; o índice único permanece o árbitro da corrida entre requests.
    public async Task<Guid?> CreatePersonalAsync(Guid userId, CategoryName name, CancellationToken cancellationToken)
    {
        if (await context.Set<Category>().AsNoTracking().AnyAsync(
                category => category.ScopeId == userId && category.NormalizedName == name.Key,
                cancellationToken))
        {
            return null;
        }

        var category = Category.Create(Guid.NewGuid(), userId, false, name.DisplayName, name.Key);
        context.Set<Category>().Add(category);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return category.Id;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ScopeNameUniqueIndex })
        {
            // Somente a constraint de nome por escopo é conflito de negócio; demais falhas preservam 503/500.
            return null;
        }
    }

    // Seleciona as sete entradas de sistema e somente linhas pessoais do titular, sem join financeiro.
    public async Task<IReadOnlyList<Category>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Set<Category>().AsNoTracking()
            .Where(category => category.IsSystem || category.UserId == userId)
            .ToArrayAsync(cancellationToken);
}
