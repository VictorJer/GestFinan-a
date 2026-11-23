using Finora.Infrastructure.Messaging.Contracts;
using MassTransit;

namespace Finora.Infrastructure.Messaging;

/// <summary>Adapta a obrigação de executar uma SyncRun para envio MassTransit a um destino lógico específico.</summary>
public sealed class SyncRunExecutionMessageSender(ISendEndpointProvider sendEndpointProvider)
{
    // IDs de transporte derivados da execução permanecem estáveis em redeliveries e correlacionam o trabalho.
    public async Task SendAsync(Guid syncRunId, DateTimeOffset emittedAtUtc, CancellationToken cancellationToken)
    {
        var message = ExecuteSyncRunV1Contract.Create(syncRunId, emittedAtUtc);
        var endpoint = await sendEndpointProvider.GetSendEndpoint(SyncRunExecutionTopology.QueueAddress);

        // O envio é capturado pela bus outbox EF e participa da transação da SyncRun aceita.
        await endpoint.Send(message, context =>
        {
            context.MessageId = message.SyncRunId;
            context.CorrelationId = message.SyncRunId;
            context.Durable = true;
        }, cancellationToken);
    }
}
