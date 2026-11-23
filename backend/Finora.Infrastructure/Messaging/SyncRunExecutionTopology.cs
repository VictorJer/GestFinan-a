namespace Finora.Infrastructure.Messaging;

/// <summary>Centraliza o destino lógico estável usado pelo envio scoped e pela futura configuração do consumer.</summary>
public static class SyncRunExecutionTopology
{
    public const string QueueName = "finora-sync-execution";
    public const int ConsumerConcurrencyLimit = 1;
    public const int ConsumerPrefetchCount = 1;

    // O endereço queue: força envio ao destino específico, sem broadcast por exchange de eventos.
    public static Uri QueueAddress { get; } = new($"queue:{QueueName}", UriKind.Absolute);
}
