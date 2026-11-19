using Finora.Application.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Finora.Infrastructure.Identity;

/// <summary>Compõe a persistência de autenticação HTTP, sem providers administrativos ou cliente externo.</summary>
public static class SessionAuthenticationServiceCollectionExtensions
{
    // A conexão precisa ter sido validada pela raiz; registrar serviços não acessa o banco.
    public static IServiceCollection AddFinoraSessionPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddFinoraIdentity(connectionString);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<SyntheticCredential>();
        services.AddScoped<IAuthenticationLogin, IdentityLoginService>();
        services.AddScoped<IAuthenticationSessionReader, AuthenticationSessionReader>();
        services.AddScoped<IAuthenticationSessionRevoker, IdentityAuthenticationSessionRevoker>();
        return services;
    }
}
