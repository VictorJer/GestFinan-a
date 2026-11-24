using MediatR;

namespace Finora.Application.FinancialData;

// O comando transporta presença e valor separadamente para que null limpe sem confundir campo omitido.
public sealed record UpdateTransactionAnnotationCommand(
    Guid UserId,
    Guid TransactionId,
    bool HasCategoryId,
    Guid? CategoryId,
    bool HasNote,
    string? Note) : IRequest<UpdateTransactionAnnotationResult>;

// Estados distintos permitem ao Controller mapear falhas sem revelar titularidade ou detalhes da persistência.
public enum UpdateTransactionAnnotationStatus
{
    Updated,
    InvalidRequest,
    TransactionNotFound,
    CategoryNotFound
}

// O resultado contém somente os dois campos pessoais permitidos na resposta da operação.
public sealed record UpdateTransactionAnnotationResult(
    UpdateTransactionAnnotationStatus Status,
    Guid? CategoryId = null,
    string? Note = null);

// O adapter confere movimento, cartão, conexão e escopo da categoria dentro da mesma transação de escrita.
public interface ITransactionAnnotationStore
{
    Task<UpdateTransactionAnnotationResult> UpdateAsync(
        UpdateTransactionAnnotationCommand command,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken);
}

// Valida a forma do comando antes do banco e conserva a distinção entre campo ausente e limpeza explícita.
public sealed class UpdateTransactionAnnotationHandler(ITransactionAnnotationStore store, TimeProvider clock)
    : IRequestHandler<UpdateTransactionAnnotationCommand, UpdateTransactionAnnotationResult>
{
    public async Task<UpdateTransactionAnnotationResult> Handle(
        UpdateTransactionAnnotationCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty
            || request.TransactionId == Guid.Empty
            || (!request.HasCategoryId && request.CategoryId.HasValue)
            || (request.HasCategoryId && request.CategoryId == Guid.Empty)
            || (!request.HasNote && request.Note is not null)
            || (request.Note?.Length ?? 0) > 500
            || (!request.HasCategoryId && !request.HasNote))
        {
            return new(UpdateTransactionAnnotationStatus.InvalidRequest);
        }

        // Texto vazio tem a mesma semântica de limpeza que null, sem aparar nem transformar texto não vazio.
        var normalized = request.HasNote && string.IsNullOrEmpty(request.Note)
            ? request with { Note = null }
            : request;
        return await store.UpdateAsync(normalized, clock.GetUtcNow().ToUniversalTime(), cancellationToken);
    }
}
