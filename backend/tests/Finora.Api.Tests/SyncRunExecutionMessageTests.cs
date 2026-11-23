using System.Text.Json;
using Finora.Infrastructure.Messaging;
using Finora.Infrastructure.Messaging.Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Usa transporte in-memory isolado para provar o destino e os headers sem broker, banco ou configuração da API.
[TestClass]
public sealed class SyncRunExecutionMessageTests
{
    // O default documenta o limite interno aprovado sem tratá-lo como enforcement do RabbitMQ classic.
    [TestMethod]
    public void DeliveryWatchdogUsesApprovedElevenMinuteApplicationLimit()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(11), SyncRunExecutionWatchdogOptions.Default.DeliveryDeadline);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new SyncRunExecutionWatchdogOptions(TimeSpan.FromMinutes(11) + TimeSpan.FromTicks(1)));
    }

    // A forma serializada contém só versão, ID persistido e instante UTC, sem identidade ou dados do provedor.
    [TestMethod]
    public void ContractSerializesOnlyMinimalVersionedExecutionEnvelope()
    {
        var syncRunId = Guid.NewGuid();
        var emittedAtUtc = new DateTimeOffset(2026, 11, 22, 12, 30, 0, TimeSpan.Zero);
        var message = ExecuteSyncRunV1Contract.Create(syncRunId, emittedAtUtc);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(message, options);
        using var document = JsonDocument.Parse(json);

        Assert.AreEqual(3, document.RootElement.EnumerateObject().Count());
        Assert.AreEqual(ExecuteSyncRunV1Contract.CurrentVersion,
            document.RootElement.GetProperty("contractVersion").GetInt32());
        Assert.AreEqual(syncRunId, document.RootElement.GetProperty("syncRunId").GetGuid());
        Assert.AreEqual(emittedAtUtc, document.RootElement.GetProperty("emittedAtUtc").GetDateTimeOffset());
        Assert.IsTrue(ExecuteSyncRunV1Contract.IsValid(JsonSerializer.Deserialize<ExecuteSyncRunV1>(json, options)));
        Assert.IsFalse(json.Contains("userId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("connectionId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("credential", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("transaction", StringComparison.OrdinalIgnoreCase));
    }

    // O validador recusa versões desconhecidas e identificadores/instantes inválidos antes da execução do consumer.
    [TestMethod]
    public void ContractRejectsUnsupportedVersionAndInvalidTechnicalFields()
    {
        var emittedAtUtc = new DateTimeOffset(2026, 11, 22, 12, 30, 0, TimeSpan.Zero);

        Assert.IsFalse(ExecuteSyncRunV1Contract.IsValid(
            new ExecuteSyncRunV1(ExecuteSyncRunV1Contract.CurrentVersion + 1, Guid.NewGuid(), emittedAtUtc)));
        Assert.IsFalse(ExecuteSyncRunV1Contract.IsValid(
            new ExecuteSyncRunV1(ExecuteSyncRunV1Contract.CurrentVersion, Guid.Empty, emittedAtUtc)));
        Assert.IsFalse(ExecuteSyncRunV1Contract.IsValid(
            new ExecuteSyncRunV1(ExecuteSyncRunV1Contract.CurrentVersion, Guid.NewGuid(), emittedAtUtc.ToOffset(TimeSpan.FromHours(1)))));
        Assert.ThrowsExactly<ArgumentException>(() => ExecuteSyncRunV1Contract.Create(Guid.Empty, emittedAtUtc));
    }

    // O adapter envia somente à fila específica e associa os headers MassTransit ao mesmo SyncRunId.
    [TestMethod]
    public async Task SenderRoutesVersionedCommandWithStableTransportMetadata()
    {
        var receipt = new SyncRunExecutionReceipt();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(receipt);
        builder.Services.AddScoped<SyncRunExecutionMessageSender>();
        builder.Services.AddMassTransit(registration =>
        {
            registration.AddConsumer<SyncRunExecutionCaptureConsumer>();
            registration.UsingInMemory((context, configurator) =>
                configurator.ReceiveEndpoint(SyncRunExecutionTopology.QueueName, endpoint =>
                {
                    endpoint.ConfigureConsumer<SyncRunExecutionCaptureConsumer>(context);
                }));
        });

        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var syncRunId = Guid.NewGuid();
            var emittedAt = new DateTimeOffset(2026, 11, 22, 12, 30, 0, TimeSpan.FromHours(-3));
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<SyncRunExecutionMessageSender>();
                await sender.SendAsync(syncRunId, emittedAt, CancellationToken.None);
            }

            var received = await receipt.Message.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(syncRunId, received.Message.SyncRunId);
            Assert.AreEqual(ExecuteSyncRunV1Contract.CurrentVersion, received.Message.ContractVersion);
            Assert.AreEqual(emittedAt.ToUniversalTime(), received.Message.EmittedAtUtc);
            Assert.AreEqual(syncRunId, received.MessageId);
            Assert.AreEqual(syncRunId, received.CorrelationId);
        }
        finally
        {
            // O host in-memory é encerrado ao final do teste e não mantém recursos externos ou mensagens duráveis.
            await host.StopAsync();
        }
    }
}

// O recibo imutável expõe somente o corpo do comando e os dois metadados que o adapter precisa fixar.
internal sealed record CapturedSyncRunExecution(
    ExecuteSyncRunV1 Message,
    Guid? MessageId,
    Guid? CorrelationId);

// A fila de teste conclui o recibo em vez de introduzir polling, delays ou conexão com RabbitMQ.
internal sealed class SyncRunExecutionReceipt
{
    private int _messageCount;

    public TaskCompletionSource<CapturedSyncRunExecution> Message { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int MessageCount => Volatile.Read(ref _messageCount);

    // Conta todas as entregas mesmo que a primeira já tenha concluído a espera do teste.
    public void Record(CapturedSyncRunExecution message)
    {
        Interlocked.Increment(ref _messageCount);
        Message.TrySetResult(message);
    }
}

// Consumer sintético comprova o roteamento, registra cada entrega e lê headers do transporte in-memory.
internal sealed class SyncRunExecutionCaptureConsumer(SyncRunExecutionReceipt receipt)
    : IConsumer<ExecuteSyncRunV1>
{
    public Task Consume(ConsumeContext<ExecuteSyncRunV1> context)
    {
        receipt.Record(new CapturedSyncRunExecution(
            context.Message,
            context.MessageId,
            context.CorrelationId));
        return Task.CompletedTask;
    }
}
