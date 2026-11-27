using Finora.Application.AdministrativeConnections;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Pluggy;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Infrastructure.AdministrativeConnections;

/// <summary>Registra exclusivamente os serviços necessários à pré-consulta administrativa local.</summary>
public static class ConnectionLinkLocalServiceCollectionExtensions
{
    // A composição não adiciona listener HTTP, pipeline financeiro, sincronização ou handlers administrativos alheios.
    public static IServiceCollection AddConnectionLinkLocalPreflight(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddFinoraIdentity(connectionString);
        services.AddPluggyConnectionInspector(configuration);
        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssembly(typeof(ConnectionLinkLocalPreflightHandler).Assembly);
            // A allowlist expõe somente a leitura prévia e a gravação curta após confirmação terminal.
            options.TypeEvaluator = type => type == typeof(ConnectionLinkLocalPreflightHandler)
                || type == typeof(ConnectionLinkLocalProvisioningHandler);
        });
        return services;
    }
}
