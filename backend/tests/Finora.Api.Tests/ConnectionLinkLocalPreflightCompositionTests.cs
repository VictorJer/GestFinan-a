using Finora.Application.AdministrativeConnections;
using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Infrastructure.AdministrativeConnections;
using Finora.Infrastructure.Pluggy;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confere que a pré-consulta compõe somente persistência Identity e leitura mínima do item Pluggy.
[TestClass]
public sealed class ConnectionLinkLocalPreflightCompositionTests
{
    private const string SyntheticConnectionString =
        "Host=127.0.0.1;Port=55432;Database=finora_local_synthetic;Username=synthetic";

    // A composição registra o handler e não ativa host HTTP, leitor de transações ou serviços de sincronização.
    [TestMethod]
    public void PreflightCompositionRegistersOnlyRequiredServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pluggy:ClientId"] = "synthetic-client",
                ["Pluggy:ClientSecret"] = "synthetic-secret",
                ["Pluggy:ItemId"] = "synthetic-item"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConnectionLinkLocalPreflight(configuration, SyntheticConnectionString);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();

        Assert.IsNotNull(provider.GetRequiredService<ISender>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IRequestHandler<
            ConnectionLinkLocalPreflightQuery,
            ConnectionLinkLocalPreflightResult>>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IRequestHandler<
            ConnectionLinkLocalLinkCommand,
            ConnectionLinkLocalLinkResult>>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<PluggyConnectionInspector>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<PluggyApiClient>());
        Assert.IsNull(provider.GetService<IPluggyLocalReader>());
        Assert.IsNull(provider.GetService<IPluggyAnalysisReader>());
        Assert.IsNull(provider.GetService<ITransactionImportReader>());
        Assert.IsNull(provider.GetService<IHostedService>());
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(WebApplication)));
    }
}
