using Finora.Application.Pluggy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Registra o cliente Pluggy somente na composição explícita do comando local.</summary>
public static class PluggyServiceCollectionExtensions
{
    // Usa fontes padrão do host e destino fixo; não valida segredos durante o modo HTTP normal.
    public static IServiceCollection AddPluggyApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = PluggyOptions.FromConfiguration(configuration);
        // Desativa logs padrão do HttpClient que podem incluir caminho, headers ou escopo do request.
        services.AddLogging(logging =>
            logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None));
        services.AddSingleton(options);
        services.AddHttpClient("Pluggy", client => client.BaseAddress = PluggyApiClient.BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(PluggyApiClient.CreatePrimaryHandler);
        services.AddScoped(serviceProvider => new PluggyApiClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Pluggy"),
            serviceProvider.GetRequiredService<PluggyOptions>()));
        services.AddScoped<PluggyConnectionInspector>();
        services.AddScoped<IPluggyLocalReader, PluggyLocalReader>();
        return services;
    }
}
