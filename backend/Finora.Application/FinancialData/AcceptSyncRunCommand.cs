using MediatR;

namespace Finora.Application.FinancialData;

/// <summary>Distinguishes the legacy user-triggered request from an explicit authenticated-entry request.</summary>
public enum SyncRunRequestMode
{
    Manual,
    OnEntry
}

/// <summary>Solicita uma execução para uma identidade de titular já validada pela fronteira autenticada.</summary>
public sealed record AcceptSyncRunCommand(
    Guid UserId,
    Guid ConnectionId,
    string? IdempotencyKey,
    SyncRunRequestMode Mode = SyncRunRequestMode.Manual) : IRequest<SyncRunAcceptanceResult>
{
    // O valor opaco não deve aparecer em logs automáticos ou diagnósticos que formatem o comando.
    public override string ToString() => "AcceptSyncRunCommand { IdempotencyKey = [redacted] }";
}
