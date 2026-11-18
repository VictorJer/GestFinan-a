using Finora.Api;
using Finora.Infrastructure.Persistence;
using Finora.Infrastructure.Pluggy;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confere o roteamento e o Generic Host administrativo sem iniciar HTTP ou acessar banco/provedor.
[TestClass]
public sealed class AdministrativeUserCommandTests
{
    private const string SyntheticConnectionString =
        "Host=127.0.0.1;Database=finora_integration_test;Username=finora_test";

    // Só os nomes exatos selecionam o ramo administrativo e nenhum argumento extra é permitido.
    [TestMethod]
    public void OnlyExactAdministrativeTokensAreRecognized()
    {
        Assert.IsTrue(AdministrativeUserCommand.IsAdministrativeCommand(["user-create"]));
        Assert.IsTrue(AdministrativeUserCommand.IsAdministrativeCommand(["user-reset-password", "extra"]));
        Assert.IsFalse(AdministrativeUserCommand.IsAdministrativeCommand(["user-create-extra"]));
        Assert.IsFalse(AdministrativeUserCommand.IsAdministrativeCommand(["unknown"]));
        Assert.IsTrue(AdministrativeUserCommand.HasValidArguments(["user-create"]));
        Assert.IsTrue(AdministrativeUserCommand.HasValidArguments(["user-reset-password"]));
        Assert.IsFalse(AdministrativeUserCommand.HasValidArguments(["user-create", "private-sentinel"]));
        Assert.IsFalse(AdministrativeUserCommand.HasValidArguments(["unknown"]));
    }

    // Argumentos desconhecidos/extras são rejeitados antes de criar builder ou resolver qualquer serviço.
    [TestMethod]
    [DataRow("user-create private-sentinel")]
    [DataRow("unknown private-sentinel")]
    public async Task InvalidArgumentsAreRejectedBeforeHostComposition(string rawArguments)
    {
        var builderFactoryCalled = false;
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await AdministrativeUserCommand.RunAsync(
            rawArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            output,
            error,
            () =>
            {
                builderFactoryCalled = true;
                throw new InvalidOperationException("composition private-sentinel");
            });

        Assert.AreEqual(2, exitCode);
        Assert.IsFalse(builderFactoryCalled);
        StringAssert.Contains(error.ToString(), "failure=invalid_arguments");
        Assert.IsFalse(output.ToString().Contains("private-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("private-sentinel", StringComparison.Ordinal));
    }

    // A configuração ausente retorna código 2 sem registrar nem construir serviços de persistência.
    [TestMethod]
    public async Task MissingConnectionConfigurationReturnsTwoBeforeComposition()
    {
        HostApplicationBuilder? capturedBuilder = null;
        var error = new StringWriter();

        var exitCode = await AdministrativeUserCommand.RunAsync(
            ["user-create"],
            new StringWriter(),
            error,
            () =>
            {
                capturedBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    ApplicationName = "Finora.Api",
                    ContentRootPath = AppContext.BaseDirectory,
                    EnvironmentName = Environments.Production,
                    Args = Array.Empty<string>()
                });
                capturedBuilder.Configuration.Sources.Clear();
                capturedBuilder.Logging.ClearProviders();
                return capturedBuilder;
            });

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "failure=invalid_configuration");
        Assert.IsNotNull(capturedBuilder);
        Assert.IsFalse(capturedBuilder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(FinoraDbContext)));
    }

    // Uma connection string malformada também falha antes de registrar persistência ou expor seu conteúdo.
    [TestMethod]
    public async Task MalformedConnectionConfigurationReturnsTwoWithoutEchoingValue()
    {
        const string privateConnectionSentinel = "invalid-port-private-sentinel";
        HostApplicationBuilder? capturedBuilder = null;
        var error = new StringWriter();

        var exitCode = await AdministrativeUserCommand.RunAsync(
            ["user-reset-password"],
            new StringWriter(),
            error,
            () =>
            {
                capturedBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    ApplicationName = "Finora.Api",
                    ContentRootPath = AppContext.BaseDirectory,
                    EnvironmentName = Environments.Production,
                    Args = Array.Empty<string>()
                });
                capturedBuilder.Configuration.Sources.Clear();
                capturedBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Finora"] =
                        $"Host=127.0.0.1;Database=finora_integration_test;Username=finora_test;Port={privateConnectionSentinel}"
                });
                capturedBuilder.Logging.ClearProviders();
                return capturedBuilder;
            });

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "failure=invalid_configuration");
        Assert.IsFalse(error.ToString().Contains(privateConnectionSentinel, StringComparison.Ordinal));
        Assert.IsNotNull(capturedBuilder);
        Assert.IsFalse(capturedBuilder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(FinoraDbContext)));
    }

    // O ambiente é escolhido explicitamente e não herda Development do perfil dos comandos Pluggy.
    [TestMethod]
    public void AdministrativeEnvironmentDefaultsToProduction()
    {
        Assert.AreEqual(Environments.Production, AdministrativeUserCommand.ResolveEnvironmentName(null));
        Assert.AreEqual(Environments.Production, AdministrativeUserCommand.ResolveEnvironmentName(" "));
        Assert.AreEqual("Development", AdministrativeUserCommand.ResolveEnvironmentName("Development"));
    }

    // A composição Identity não registra cliente Pluggy, servidor HTTP nem providers de logging.
    [TestMethod]
    public void AdministrativeHostDoesNotRegisterHttpPluggyOrLoggingProviders()
    {
        var builder = AdministrativeUserCommand.CreateHostBuilder(Environments.Production);
        var loggingProviders = builder.Logging.Services
            .Where(descriptor => descriptor.ServiceType == typeof(ILoggerProvider));
        using var host = AdministrativeUserCommand.BuildHost(builder, SyntheticConnectionString);

        Assert.IsFalse(loggingProviders.Any());
        Assert.IsNull(host.Services.GetService<PluggyApiClient>());
        Assert.IsNull(host.Services.GetService<IServer>());
        Assert.IsFalse(builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)));
        Assert.IsNotNull(host.Services.GetRequiredService<FinoraDbContext>());
    }
}
