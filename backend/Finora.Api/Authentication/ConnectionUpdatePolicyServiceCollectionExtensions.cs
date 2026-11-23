using Finora.Application.FinancialData;
using Microsoft.Extensions.Options;

namespace Finora.Api.Authentication;

/// <summary>Composes validated update-policy configuration for the authenticated HTTP host.</summary>
public static class ConnectionUpdatePolicyServiceCollectionExtensions
{
    /// <summary>Binds policy durations, validates them at startup, and exposes the resolved immutable host instance.</summary>
    public static IServiceCollection AddFinoraConnectionUpdatePolicyOptions(this IServiceCollection services)
    {
        // Invalid durations fail startup before an authenticated request can accept an unintended intention.
        services.AddOptions<ConnectionUpdatePolicyOptions>()
            .BindConfiguration(ConnectionUpdatePolicyOptions.ConfigurationSectionName)
            .Validate(ConnectionUpdatePolicyOptions.IsValid,
                "Connection update policy durations must be positive.")
            .ValidateOnStart();

        // Application handlers receive the already-bound options without depending on the options infrastructure.
        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<ConnectionUpdatePolicyOptions>>().Value);

        return services;
    }
}
