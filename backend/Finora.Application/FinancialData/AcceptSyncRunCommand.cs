using MediatR;

namespace Finora.Application.FinancialData;

/// <summary>Solicita uma execução para uma identidade de titular já validada pela fronteira autenticada.</summary>
public sealed record AcceptSyncRunCommand(
    Guid UserId,
    Guid ConnectionId,
    string? IdempotencyKey) : IRequest<SyncRunAcceptanceResult>
{
    // O valor opaco não deve aparecer em logs automáticos ou diagnósticos que formatem o comando.
    public override string ToString() => "AcceptSyncRunCommand { IdempotencyKey = [redacted] }";
}
