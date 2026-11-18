using Finora.Application.AdministrativeUsers;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        // Cada escopo recebe seu contexto; o provider real alimenta as mesmas opções de modelo da factory.
        services.AddDbContext<FinoraDbContext>((serviceProvider, options) =>
        {
            FinoraDbContextConfiguration.Configure(options, connectionString, serviceProvider);
            // Interceptores de gravação/transação são explícitos e permitem validar falhas no mesmo contexto Identity.
            var interceptors = serviceProvider.GetServices<ISaveChangesInterceptor>()
                .Cast<IInterceptor>()
                .Concat(serviceProvider.GetServices<IDbTransactionInterceptor>());
            options.AddInterceptors(interceptors);
        });

        // O acesso administrativo é scoped ao mesmo contexto/Identity usado pela pré-consulta.
        services.AddScoped<IAdministrativeUserAccess, AdministrativeUserAccessService>();

        // O modelo Identity e a política escolhida ficam fixos no mesmo ponto de composição.
        return services.AddIdentityCore<ApplicationUser>(ConfigureIdentityOptions)
            .AddEntityFrameworkStores<FinoraDbContext>();
    }

    // As opções de senha valem igualmente para criação e reset, sem override externo que as enfraqueça.
    private static void ConfigureIdentityOptions(IdentityOptions options)
    {
        FinoraIdentityModelOptions.Configure(options);
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 1;
    }
}
