using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Finora.Infrastructure.Persistence;

namespace Finora.Infrastructure.Messaging;

/// <summary>Compõe o bus outbox EF PostgreSQL sem escolher host, credenciais ou ciclo de vida do transporte.</summary>
public static class SyncRunExecutionOutboxServiceCollectionExtensions
{
    // O chamador fornece o transporte aprovado; o adapter e o armazenamento outbox usam o escopo do DbContext.
    public static IServiceCollection AddFinoraSyncRunExecutionOutbox(
        this IServiceCollection services,
        Action<IBusRegistrationConfigurator> configureTransport)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureTransport);

        // A fila classic não aplica ack timeout; o consumer mantém o limite interno aprovado de onze minutos.
        services.TryAddSingleton(SyncRunExecutionWatchdogOptions.Default);
        services.AddMassTransit(registration =>
        {
            // O contrato de consumo vive na Infrastructure; o host escolhe o transporte e configura a fila.
            registration.AddConsumer<SyncRunExecutionConsumer>();
            registration.AddEntityFrameworkOutbox<FinoraDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });
            configureTransport(registration);
        });
        services.TryAddScoped<SyncRunExecutionMessageSender>();
        services.TryAddScoped<SyncRunExecutionRecoveryCoordinator>();
        return services;
    }
}
