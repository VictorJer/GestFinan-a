using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Autoriza e persiste somente os campos pessoais, sem consultar o provedor ou alterar o snapshot financeiro.
public sealed class TransactionAnnotationStore(FinoraDbContext dbContext) : ITransactionAnnotationStore
{
    // O movimento é bloqueado antes da leitura da anotação para serializar inclusive suas duas primeiras escritas.
    public async Task<UpdateTransactionAnnotationResult> UpdateAsync(
        UpdateTransactionAnnotationCommand command,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (command.UserId == Guid.Empty
            || command.TransactionId == Guid.Empty
            || updatedAtUtc.Offset != TimeSpan.Zero
            || (!command.HasCategoryId && command.CategoryId.HasValue)
            || (command.HasCategoryId && command.CategoryId == Guid.Empty)
            || (!command.HasNote && command.Note is not null)
            || (command.Note?.Length ?? 0) > 500
            || (!command.HasCategoryId && !command.HasNote))
        {
            return new(UpdateTransactionAnnotationStatus.InvalidRequest);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // O predicado repete titularidade do movimento, cartão e conexão; somente a linha do movimento recebe lock.
        var ownedMovements = await dbContext.Set<FinancialTransaction>()
            .FromSqlInterpolated($"""
                SELECT movement.*
                FROM "FinancialTransaction" AS movement
                JOIN "CardAccount" AS card
                    ON card."Id" = movement."CardAccountId"
                    AND card."UserId" = movement."UserId"
                JOIN "FinancialConnection" AS connection
                    ON connection."Id" = card."ConnectionId"
                    AND connection."UserId" = card."UserId"
                WHERE movement."Id" = {command.TransactionId}
                    AND movement."UserId" = {command.UserId}
                FOR UPDATE OF movement
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (ownedMovements.Count == 0)
        {
            return new(UpdateTransactionAnnotationStatus.TransactionNotFound);
        }

        // Categorias aceitas são somente as de sistema persistidas ou as pessoais com titular e escopo próprios.
        Category? selectedCategory = null;
        if (command.HasCategoryId && command.CategoryId is { } categoryId)
        {
            selectedCategory = await dbContext.Set<Category>()
                .AsNoTracking()
                .SingleOrDefaultAsync(category => category.Id == categoryId
                    && ((category.IsSystem && category.UserId == null && category.ScopeId == Guid.Empty)
                        || (!category.IsSystem && category.UserId == command.UserId
                            && category.ScopeId == command.UserId)),
                    cancellationToken);
            if (selectedCategory is null)
            {
                return new(UpdateTransactionAnnotationStatus.CategoryNotFound);
            }
        }

        // O lock do movimento garante que leitores concorrentes vejam o commit anterior antes de aplicar campos presentes.
        var existing = await dbContext.Set<TransactionAnnotation>()
            .SingleOrDefaultAsync(annotation => annotation.TransactionId == command.TransactionId
                && annotation.UserId == command.UserId,
                cancellationToken);
        var resultingCategoryId = command.HasCategoryId ? command.CategoryId : existing?.CategoryId;
        var resultingCategoryScopeId = command.HasCategoryId ? selectedCategory?.ScopeId : existing?.CategoryScopeId;
        var resultingNote = command.HasNote ? command.Note : existing?.Note;

        // Uma limpeza sobre anotação ausente continua sendo sucesso, mas não cria linha sem conteúdo pessoal.
        if (existing is null && resultingCategoryId is null && resultingNote is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(UpdateTransactionAnnotationStatus.Updated);
        }

        // A factory de domínio valida categoria/escopo/texto antes de EF persistir a alteração atômica.
        var updated = existing is null
            ? TransactionAnnotation.Create(
                command.TransactionId,
                command.UserId,
                resultingCategoryId,
                resultingCategoryScopeId,
                resultingNote,
                updatedAtUtc)
            : existing.WithCategoryAndNote(
                resultingCategoryId,
                resultingCategoryScopeId,
                resultingNote,
                updatedAtUtc);
        if (existing is null)
        {
            dbContext.Set<TransactionAnnotation>().Add(updated);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(updated);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(UpdateTransactionAnnotationStatus.Updated, resultingCategoryId, resultingNote);
    }
}
