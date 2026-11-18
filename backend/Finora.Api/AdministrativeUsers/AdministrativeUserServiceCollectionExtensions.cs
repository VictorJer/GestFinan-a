using Finora.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Api.AdministrativeUsers;

/// <summary>Acrescenta tokens locais ao Identity sem persistir chaves de proteção no disco.</summary>
public static class AdministrativeUserServiceCollectionExtensions
{
    /// <summary>Registra um provider efêmero singleton e os providers padrão usados pelo reset no processo.</summary>
    public static IServiceCollection AddAdministrativeUserIdentity(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Uma instância única permite gerar e consumir o token no mesmo processo sem criar key ring persistente.
        services.AddSingleton<IDataProtectionProvider>(_ => new EphemeralDataProtectionProvider());
        services.AddFinoraAdministrativeIdentity(connectionString)
            .AddDefaultTokenProviders();

        return services;
    }
}
