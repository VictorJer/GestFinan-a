using Finora.Domain.FinancialData;
using MediatR;

namespace Finora.Application.FinancialData;

/// <summary>Valida a intenção autenticada, fixa a janela e delega convergência/ownership à fronteira persistente.</summary>
public sealed class AcceptSyncRunHandler(
    ISyncRunAcceptanceStore acceptanceStore,
    TimeProvider timeProvider) : IRequestHandler<AcceptSyncRunCommand, SyncRunAcceptanceResult>
{
    public async Task<SyncRunAcceptanceResult> Handle(
        AcceptSyncRunCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty
            || request.ConnectionId == Guid.Empty
            || !SyncRunIdempotency.TryHashKey(request.IdempotencyKey, out var keyHash))
        {
            return new SyncRunAcceptanceResult(SyncRunAcceptanceStatus.InvalidRequest);
        }

        // A janela mantém o início de doze meses usado pelo importador atual e representa hoje por limite exclusivo.
        var createdAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var referenceDate = DateOnly.FromDateTime(createdAtUtc.UtcDateTime);
        var requestedFrom = referenceDate.AddMonths(-12);
        var requestedToExclusive = referenceDate.AddDays(1);
        var candidate = SyncRun.Create(
            Guid.NewGuid(),
            request.UserId,
            request.ConnectionId,
            createdAtUtc,
            requestedFrom,
            requestedToExclusive,
            idempotencyKeyHash: keyHash,
            requestFingerprint: SyncRunIdempotency.CreateRequestFingerprint(request.ConnectionId),
            nextAttemptAtUtc: createdAtUtc);

        // O store relê a chave antes de gravar e a constraint global serializa aceitações concorrentes.
        return await acceptanceStore.AcceptOrGetAsync(candidate, cancellationToken);
    }
}
