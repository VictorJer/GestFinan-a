using Finora.Application.AdministrativeUsers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Infrastructure.Identity;

/// <summary>Compõe o contexto e os stores scoped do Identity para comandos administrativos locais.</summary>
public static class AdministrativeIdentityServiceCollectionExtensions
{
    /// <summary>Registra Identity básico sem roles, cookies, UI ou endpoints de autenticação.</summary>
    public static IdentityBuilder AddFinoraAdministrativeIdentity(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // O acesso administrativo é scoped ao mesmo contexto/Identity usado pela pré-consulta.
        services.AddScoped<IAdministrativeUserAccess, AdministrativeUserAccessService>();
        return services.AddFinoraIdentity(connectionString);
    }
}
