using Finora.Domain.FinancialData;
using MediatR;

namespace Finora.Application.FinancialData;

// O projection contém somente metadados operacionais públicos, sem identidade, conexão ou chave idempotente.
public sealed record SyncRunStatusResult(
    Guid SyncRunId,
    SyncRunState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    DateOnly RequestedFrom,
    DateOnly? RequestedToExclusive,
    int? PagesRead,
    int? ReceivedCount,
    int? UniqueCount,
    int? PersistedCount,
    DateOnly? ObservedFrom,
    DateOnly? ObservedTo,
    SyncCoverageState CoverageState,
    string? FailureCode,
    DateTimeOffset? NextAttemptAtUtc = null);

// A consulta carrega titular e execução da sessão autenticada, não de parâmetros de identidade enviados pelo cliente.
public sealed record GetSyncRunStatusQuery(Guid UserId, Guid SyncRunId) : IRequest<SyncRunStatusResult?>;

/// <summary>Consulta uma execução somente quando seu identificador pertence ao titular informado.</summary>
public interface ISyncRunStatusReader
{
    Task<SyncRunStatusResult?> FindOwnedAsync(
        Guid userId,
        Guid syncRunId,
        CancellationToken cancellationToken);
}

// A camada Application rejeita IDs vazios antes de delegar a consulta protegida ao adapter de persistência.
public sealed class GetSyncRunStatusHandler(ISyncRunStatusReader reader)
    : IRequestHandler<GetSyncRunStatusQuery, SyncRunStatusResult?>
{
    public Task<SyncRunStatusResult?> Handle(
        GetSyncRunStatusQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || request.SyncRunId == Guid.Empty)
        {
            return Task.FromResult<SyncRunStatusResult?>(null);
        }

        return reader.FindOwnedAsync(request.UserId, request.SyncRunId, cancellationToken);
    }
}
