using System.Data;
using Finora.Application.AdministrativeUsers;
using Finora.Api;
using Finora.Api.AdministrativeUsers;
using Finora.Infrastructure.Persistence;
using Finora.Infrastructure.Pluggy;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
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
                capturedBuilder = AdministrativeUserCommand.CreateHostBuilder(
                    Environments.Production,
                    disableDefaults: true);
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
                capturedBuilder = AdministrativeUserCommand.CreateHostBuilder(
                    Environments.Production,
                    disableDefaults: true);
                capturedBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Finora"] =
                        $"Host=127.0.0.1;Database=finora_integration_test;Username=finora_test;Port={privateConnectionSentinel}"
                });
                return capturedBuilder;
            });

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "failure=invalid_configuration");
        Assert.IsFalse(error.ToString().Contains(privateConnectionSentinel, StringComparison.Ordinal));
        Assert.IsNotNull(capturedBuilder);
        Assert.IsFalse(capturedBuilder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(FinoraDbContext)));
    }

    // Uma falha sintética na composição só publica a mensagem operacional fixa e ocorre antes de qualquer prompt.
    [TestMethod]
    public async Task RunAsyncSanitizesSyntheticHostCompositionFailure()
    {
        const string exceptionSentinel = "exception-private-sentinel";
        const string credentialSentinel = "credential-private-sentinel";
        var builder = AdministrativeUserCommand.CreateHostBuilder(Environments.Development, disableDefaults: true);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Finora"] =
                $"Host=127.0.0.1;Database=finora_integration_test;Username={credentialSentinel}"
        });
        Assert.IsTrue(builder.Configuration.Sources.All(source => source is MemoryConfigurationSource));
        var output = new StringWriter();
        var error = new StringWriter();
        var terminalConsole = new TrackingAdministrativeUserConsole();
        var builderFactoryCalls = 0;
        var hostFactoryCalls = 0;
        var terminalFactoryCalls = 0;

        var exitCode = await AdministrativeUserCommand.RunAsync(
            ["user-create"],
            output,
            error,
            () =>
            {
                builderFactoryCalls++;
                return builder;
            },
            (_, _) =>
            {
                hostFactoryCalls++;
                throw new InvalidOperationException($"{exceptionSentinel} {credentialSentinel}");
            },
            () =>
            {
                terminalFactoryCalls++;
                return terminalConsole;
            });

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(1, builderFactoryCalls);
        Assert.AreEqual(1, hostFactoryCalls);
        Assert.AreEqual(0, terminalFactoryCalls);
        Assert.AreEqual(0, terminalConsole.ReadKeyCalls);
        Assert.AreEqual(0, terminalConsole.WrittenLines.Count);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsTrue(string.Equals(
            "User admin: failure=operational" + Environment.NewLine,
            error.ToString(),
            StringComparison.Ordinal));

        var capturedOutput = output.ToString() + error.ToString()
            + string.Join(Environment.NewLine, terminalConsole.WrittenLines);
        Assert.IsFalse(capturedOutput.Contains(exceptionSentinel, StringComparison.Ordinal));
        Assert.IsFalse(capturedOutput.Contains(credentialSentinel, StringComparison.Ordinal));
    }

    // O ambiente é escolhido explicitamente e não herda Development do perfil dos comandos Pluggy.
    [TestMethod]
    public void AdministrativeEnvironmentDefaultsToProduction()
    {
        Assert.AreEqual(Environments.Production, AdministrativeUserCommand.ResolveEnvironmentName(null));
        Assert.AreEqual(Environments.Production, AdministrativeUserCommand.ResolveEnvironmentName(" "));
        Assert.AreEqual("Development", AdministrativeUserCommand.ResolveEnvironmentName("Development"));
    }

    // Desativa os defaults antes de criar cada builder e admite somente configuração em memória nos dois ambientes.
    [TestMethod]
    public void CreateHostBuilderCanBeIsolatedBeforeLoadingExternalConfiguration()
    {
        const string syntheticValue = "synthetic-only";
        foreach (var environmentName in new[] { Environments.Development, Environments.Production })
        {
            var builder = AdministrativeUserCommand.CreateHostBuilder(environmentName, disableDefaults: true);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdministrativeTest:Only"] = syntheticValue
            });

            var sources = builder.Configuration.Sources.ToArray();
            Assert.AreEqual(environmentName, builder.Environment.EnvironmentName);
            Assert.IsTrue(sources.Length > 0);
            Assert.IsTrue(sources.All(source => source is MemoryConfigurationSource),
                string.Join(", ", sources.Select(source => source.GetType().FullName)));
            Assert.AreEqual(syntheticValue, builder.Configuration["AdministrativeTest:Only"]);
            Assert.IsNull(builder.Configuration.GetConnectionString("Finora"));
        }
    }

    // A validação estrita confirma que a allowlist permite construir o host sem iniciá-lo nos dois ambientes.
    [TestMethod]
    public void AdministrativeHostBuildsInBothEnvironmentsWithStrictValidation()
    {
        foreach (var environmentName in new[] { Environments.Development, Environments.Production })
        {
            var builder = AdministrativeUserCommand.CreateHostBuilder(environmentName, disableDefaults: true);
            builder.ConfigureContainer<IServiceCollection>(new DefaultServiceProviderFactory(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                }));

            using var host = AdministrativeUserCommand.BuildHost(builder, SyntheticConnectionString);
            Assert.AreEqual(environmentName, host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        }
    }

    // Confere a allowlist registrada, resolve cada handler scoped e verifica que o host não ativa integrações externas.
    [TestMethod]
    public void AdministrativeCompositionResolvesOnlyAdministrativeHandlersWithoutOpeningConnections()
    {
        var expectedHandlerTypes = new[]
        {
            typeof(CheckAdministrativeUserPreflightHandler),
            typeof(CreateAdministrativeUserHandler),
            typeof(ResetAdministrativeUserPasswordHandler)
        };
        var expectedHandlerContracts = new[]
        {
            typeof(IRequestHandler<CheckAdministrativeUserPreflightQuery, AdministrativeUserPreflightResult>),
            typeof(IRequestHandler<CreateAdministrativeUserCommand, AdministrativeUserOperationResult>),
            typeof(IRequestHandler<ResetAdministrativeUserPasswordCommand, AdministrativeUserOperationResult>)
        };

        foreach (var environmentName in new[] { Environments.Development, Environments.Production })
        {
            // As validações são forçadas no provider para que o teste não dependa do ambiente escolhido.
            var builder = AdministrativeUserCommand.CreateHostBuilder(environmentName, disableDefaults: true);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdministrativeTest:Only"] = "synthetic-only"
            });
            builder.ConfigureContainer<IServiceCollection>(new DefaultServiceProviderFactory(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                }));
            using var host = AdministrativeUserCommand.BuildHost(builder, SyntheticConnectionString);

            // O contrato dos descritores precisa coincidir exatamente com os três handlers administrativos.
            var handlerDescriptors = builder.Services
                .Where(descriptor => descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .ToArray();
            var registeredHandlerTypes = handlerDescriptors
                .Select(descriptor => descriptor.ImplementationType)
                .OfType<Type>()
                .ToArray();
            Assert.AreEqual(expectedHandlerTypes.Length, handlerDescriptors.Length);
            CollectionAssert.AreEquivalent(expectedHandlerTypes, registeredHandlerTypes);

            // A resolução em escopo constrói o pipeline Identity, mas não executa consultas ou inicia o terminal.
            using var scope = host.Services.CreateScope();
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<ISender>());
            foreach (var handlerContract in expectedHandlerContracts)
            {
                Assert.IsNotNull(scope.ServiceProvider.GetRequiredService(handlerContract));
            }

            var context = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
            Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);

            // A composição CLI não inclui servidor/cliente HTTP, Pluggy nem transporte ou worker RabbitMQ.
            Assert.IsNull(scope.ServiceProvider.GetService<IServer>());
            Assert.IsNull(scope.ServiceProvider.GetService<IHttpClientFactory>());
            Assert.IsNull(scope.ServiceProvider.GetService<IHttpContextAccessor>());
            Assert.IsNull(scope.ServiceProvider.GetService<PluggyApiClient>());
            Assert.IsNull(scope.ServiceProvider.GetService<IBus>());
            Assert.IsNull(scope.ServiceProvider.GetService<IPublishEndpoint>());
            Assert.IsFalse(builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)));
        }
    }

    // A composição Identity não registra cliente Pluggy, servidor HTTP nem providers de logging.
    [TestMethod]
    public void AdministrativeHostDoesNotRegisterHttpPluggyOrLoggingProviders()
    {
        var builder = AdministrativeUserCommand.CreateHostBuilder(
            Environments.Production,
            disableDefaults: true);
        var loggingProviders = builder.Logging.Services
            .Where(descriptor => descriptor.ServiceType == typeof(ILoggerProvider));
        using var host = AdministrativeUserCommand.BuildHost(builder, SyntheticConnectionString);

        Assert.IsFalse(loggingProviders.Any());
        Assert.IsNull(host.Services.GetService<PluggyApiClient>());
        Assert.IsNull(host.Services.GetService<IServer>());
        Assert.IsFalse(builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)));
        Assert.IsNotNull(host.Services.GetRequiredService<FinoraDbContext>());
    }

    // O fake registra qualquer interação caso uma regressão alcance a etapa de prompts do terminal.
    private sealed class TrackingAdministrativeUserConsole : IAdministrativeUserConsole
    {
        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public bool TreatControlCAsInput { get; set; }

        public int ReadKeyCalls { get; private set; }

        public List<string> WrittenLines { get; } = [];

        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            ReadKeyCalls++;
            return null;
        }

        public void WriteLine(string value) => WrittenLines.Add(value);
    }
}
