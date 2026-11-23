using Finora.Infrastructure.Persistence;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Usa somente o PostgreSQL descartável validado e o broker exclusivo do ensaio MQ-P01.
[TestClass]
[DoNotParallelize]
public sealed class MqP01CompatibilityTests
{
    private const string QueueName = "finora-mq-p01-compat";

    // Verifica mapeamento EF, rollback conjunto e entrega real via RabbitMQ sem implementar a FNR-013.
    [TestMethod]
    public async Task BusOutboxAndRabbitMqDeliverOnlyCommittedProbe()
    {
        var database = DisposablePostgresTestConfiguration.ReadRequired();
        var broker = MqP01BrokerSettings.ReadRequired();
        var receipt = new MqP01Receipt();
        var builder = Host.CreateApplicationBuilder();

        // Registra contexto e bus apenas neste host de teste; nenhuma composição da API é alterada.
        builder.Services.AddSingleton(receipt);
        builder.Services.AddDbContext<MqP01DbContext>(options => options.UseNpgsql(database.ConnectionString));
        builder.Services.AddMassTransit(registration =>
        {
            registration.AddConsumer<MqP01Consumer>();
            registration.AddEntityFrameworkOutbox<MqP01DbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });
            registration.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri($"rabbitmq://127.0.0.1:{broker.Port}/{broker.Vhost}"), host =>
                {
                    host.Username(broker.Username);
                    host.Password(broker.Password);
                });
                cfg.ReceiveEndpoint(QueueName, endpoint =>
                {
                    endpoint.Durable = true;
                    endpoint.AutoDelete = false;
                    endpoint.PrefetchCount = 1;
                    endpoint.ConfigureConsumer<MqP01Consumer>(context);
                });
            });
        });

        using var host = builder.Build();
        // EnsureCreated opera exclusivamente no database de teste allowlisted, sem migration da aplicação.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MqP01DbContext>();
            Assert.IsTrue(await db.Database.EnsureCreatedAsync());
        }

        await host.StartAsync();
        try
        {
            var rejectedId = Guid.NewGuid();
            // A transação revertida não pode deixar registro de teste nem mensagem da bus outbox.
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MqP01DbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Probes.Add(new MqP01Probe { Id = rejectedId });
                var sender = scope.ServiceProvider.GetRequiredService<ISendEndpointProvider>();
                var endpoint = await sender.GetSendEndpoint(new Uri($"queue:{QueueName}"));
                await endpoint.Send(new MqP01Message(rejectedId));
                await db.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MqP01DbContext>();
                Assert.AreEqual(0, await db.Probes.CountAsync());
                Assert.AreEqual(0, await db.Set<OutboxMessage>().CountAsync());
            }

            var acceptedId = Guid.NewGuid();
            // O segundo envio testa entrega do comando somente depois do commit do mesmo contexto EF.
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MqP01DbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Probes.Add(new MqP01Probe { Id = acceptedId });
                var sender = scope.ServiceProvider.GetRequiredService<ISendEndpointProvider>();
                var endpoint = await sender.GetSendEndpoint(new Uri($"queue:{QueueName}"));
                await endpoint.Send(new MqP01Message(acceptedId));
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            var observed = await receipt.Next.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(acceptedId, observed);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MqP01DbContext>();
                Assert.AreEqual(1, await db.Probes.CountAsync());
            }
        }
        finally
        {
            // Parar o host encerra o consumidor, mas não remove filas, volume ou mensagens pendentes.
            await host.StopAsync();
        }
    }
}

// O contexto sintético mantém as tabelas da outbox no mesmo provider sem tocar FinoraDbContext.
internal sealed class MqP01DbContext(DbContextOptions<MqP01DbContext> options) : DbContext(options)
{
    public DbSet<MqP01Probe> Probes => Set<MqP01Probe>();

    // Mapeia exatamente os componentes da bus outbox exigidos pelo pacote selecionado.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MqP01Probe>().HasKey(probe => probe.Id);
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

// A mensagem carrega apenas um identificador sintético, sem informações financeiras ou credenciais.
public sealed record MqP01Message(Guid ProbeId);

internal sealed class MqP01Probe
{
    public Guid Id { get; set; }
}

// Permite verificar o recebimento sem consultar uma fila por polling ou usar delays artificiais.
public sealed class MqP01Receipt
{
    public TaskCompletionSource<Guid> Next { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

// Confirma somente a entrega da mensagem sintética no transporte RabbitMQ real.
public sealed class MqP01Consumer(MqP01Receipt receipt) : IConsumer<MqP01Message>
{
    public Task Consume(ConsumeContext<MqP01Message> context)
    {
        receipt.Next.TrySetResult(context.Message.ProbeId);
        return Task.CompletedTask;
    }
}

// Valida porta, usuário e vhost antes de conectar, sem fallback para broker externo ou default guest.
internal sealed record MqP01BrokerSettings(int Port, string Vhost, string Username, string Password)
{
    public static MqP01BrokerSettings ReadRequired()
    {
        var portText = Environment.GetEnvironmentVariable("FINORA_MQP01_RABBIT_PORT");
        var vhost = Environment.GetEnvironmentVariable("FINORA_MQP01_RABBIT_VHOST");
        var username = Environment.GetEnvironmentVariable("FINORA_MQP01_RABBIT_USER");
        var password = Environment.GetEnvironmentVariable("FINORA_MQP01_RABBIT_PASSWORD");
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535
            || vhost != "finora-mq-p01" || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Configure o broker MQ-P01 isolado no loopback com vhost finora-mq-p01.");
        }

        return new MqP01BrokerSettings(port, vhost, username, password);
    }
}
