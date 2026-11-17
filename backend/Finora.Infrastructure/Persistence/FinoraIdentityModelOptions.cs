using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Infrastructure.Persistence;

/// <summary>Centraliza as opções Identity que alteram o modelo EF e precisam coincidir no design-time e runtime.</summary>
public static class FinoraIdentityModelOptions
{
    public const int MaxLengthForKeys = 128;

    public static Version SchemaVersion => IdentitySchemaVersions.Version2;

    // O provider mínimo mantém as opções acessíveis ao modelo EF da factory e dos testes sem iniciar um host HTTP.
    internal static IServiceProvider DesignTimeServiceProvider { get; } = CreateDesignTimeServiceProvider();

    /// <summary>Aplica as mesmas opções à configuração futura do Identity na composição da aplicação.</summary>
    public static void Configure(IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Stores.SchemaVersion = SchemaVersion;
        options.Stores.MaxLengthForKeys = MaxLengthForKeys;
    }

    // Resolve IOptions<IdentityOptions> para o modelo design-time, sem registrar stores, autenticação ou middleware.
    private static IServiceProvider CreateDesignTimeServiceProvider()
    {
        var services = new ServiceCollection();
        services.Configure<IdentityOptions>(Configure);
        return services.BuildServiceProvider();
    }
}
