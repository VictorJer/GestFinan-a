using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Infrastructure.Identity;

/// <summary>Compartilha stores e política Identity entre HTTP e CLI sem ativar cookies ou providers de reset.</summary>
public static class FinoraIdentityServiceCollectionExtensions
{
    // A composição registra somente serviços; não abre conexão ou aplica schema durante o startup.
    public static IdentityBuilder AddFinoraIdentity(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<FinoraDbContext>((provider, options) =>
        {
            FinoraDbContextConfiguration.Configure(options, connectionString, provider);
            // Mantém os interceptores explícitos de falha usados pelas provas transacionais existentes.
            var interceptors = provider.GetServices<ISaveChangesInterceptor>().Cast<IInterceptor>()
                .Concat(provider.GetServices<IDbTransactionInterceptor>());
            options.AddInterceptors(interceptors);
        });
        return services.AddIdentityCore<ApplicationUser>(ConfigureIdentityOptions)
            .AddEntityFrameworkStores<FinoraDbContext>();
    }

    // Um único ponto preserva modelo Version2/128, unicidade e política de senha administrativa aprovada.
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
        // O bloqueio limita novos logins e não invalida sessões existentes ou redefine senha administrativa.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    }
}
