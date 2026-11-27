using Finora.Application.AdministrativeConnections;
using Finora.Api.AdministrativeConnections;
using Finora.Infrastructure.Persistence;
using Finora.Infrastructure.Pluggy;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica o roteamento e as barreiras iniciais sem criar host, abrir banco ou consultar a Pluggy.
[TestClass]
public sealed class ConnectionLinkLocalCommandTests
{
    // O token literal seleciona o comando; argumentos adicionais continuam no ramo para rejeição sanitizada.
    [TestMethod]
    public void OnlyExactTokenSelectsTheAdministrativeCommand()
    {
        Assert.IsTrue(ConnectionLinkLocalCommand.IsAdministrativeCommand(["connection-link-local"]));
        Assert.IsTrue(ConnectionLinkLocalCommand.IsAdministrativeCommand(
            ["connection-link-local", "private-sentinel"]));
        Assert.IsFalse(ConnectionLinkLocalCommand.IsAdministrativeCommand(["connection-link-local-extra"]));
        Assert.IsFalse(ConnectionLinkLocalCommand.IsAdministrativeCommand(["unknown"]));
        Assert.IsTrue(ConnectionLinkLocalCommand.HasValidArguments(["connection-link-local"]));
        Assert.IsFalse(ConnectionLinkLocalCommand.HasValidArguments(
            ["connection-link-local", "private-sentinel"]));
        Assert.IsFalse(ConnectionLinkLocalCommand.HasValidArguments(["Connection-Link-Local"]));
    }

    // Argumentos adicionais são recusados antes de criar o terminal ou carregar configuração.
    [TestMethod]
    public async Task ExtraArgumentsAreRejectedBeforeCheckingTerminal()
    {
        var terminalFactoryCalls = 0;
        var hostBuilderCalls = 0;
        var error = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local", "item-private-sentinel"],
            error,
            () =>
            {
                terminalFactoryCalls++;
                return new FakeConnectionLinkLocalConsole();
            },
            () =>
            {
                hostBuilderCalls++;
                throw new InvalidOperationException("host private-sentinel");
            });

        Assert.AreEqual(2, exitCode);
        Assert.AreEqual(0, terminalFactoryCalls);
        Assert.AreEqual(0, hostBuilderCalls);
        Assert.AreEqual(
            "Connection link local: failure=invalid_arguments" + Environment.NewLine,
            error.ToString());
        Assert.IsFalse(error.ToString().Contains("item-private-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("private-sentinel", StringComparison.Ordinal));
    }

    // Sem TTY, a checagem de redirecionamento ocorre antes de carregar configuração ou criar dependências.
    [TestMethod]
    public async Task MissingInteractiveTerminalIsRejectedWithoutSideEffects()
    {
        var console = new FakeConnectionLinkLocalConsole { IsInputRedirected = true };
        var hostBuilderCalls = 0;
        var error = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () =>
            {
                hostBuilderCalls++;
                throw new InvalidOperationException("host private-sentinel");
            });

        Assert.AreEqual(2, exitCode);
        Assert.AreEqual(0, console.ReadLineCalls);
        Assert.AreEqual(0, hostBuilderCalls);
        Assert.AreEqual(
            "Connection link local: failure=interactive_terminal_required" + Environment.NewLine,
            error.ToString());
        Assert.IsFalse(error.ToString().Contains("private-sentinel", StringComparison.Ordinal));
    }

    // Configuração ausente, remota, ambígua, de integração ou sem porta falha sem prompts nem acesso externo.
    [TestMethod]
    public async Task UnsafeOrMissingConnectionConfigurationsAreRejectedBeforePrompt()
    {
        var unsafeConfigurations = new string?[]
        {
            null,
            "Host=db.example.test;Port=5432;Database=finora_local;Username=synthetic",
            "Host=127.0.0.1,127.0.0.2;Port=5432;Database=finora_local;Username=synthetic",
            "Host=/var/run/postgresql;Port=5432;Database=finora_local;Username=synthetic",
            "Host=127.0.0.1;Port=5432;Database=finora_integration_test;Username=synthetic",
            "Host=127.0.0.1;Database=finora_local;Username=synthetic",
            "Host=localhost;Port=5432;Database=finora_local;Username=synthetic"
        };

        foreach (var connectionString in unsafeConfigurations)
        {
            var builder = CreateConfiguredBuilder(connectionString);
            var console = new FakeConnectionLinkLocalConsole();
            var hostBuilderCalls = 0;
            var error = new StringWriter();

            var exitCode = await ConnectionLinkLocalCommand.RunAsync(
                ["connection-link-local"],
                error,
                () => console,
                () =>
                {
                    hostBuilderCalls++;
                    return builder;
                });

            Assert.AreEqual(2, exitCode);
            Assert.AreEqual(1, hostBuilderCalls);
            Assert.AreEqual(0, console.ReadLineCalls);
            Assert.AreEqual(0, console.Prompts.Count);
            Assert.IsFalse(builder.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(FinoraDbContext)));
            Assert.IsFalse(builder.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(PluggyApiClient)));
            Assert.AreEqual(
                "Connection link local: failure=invalid_configuration" + Environment.NewLine,
                error.ToString());
            Assert.IsFalse(error.ToString().Contains("db.example.test", StringComparison.Ordinal));
            Assert.IsFalse(error.ToString().Contains("finora_integration_test", StringComparison.Ordinal));
        }
    }

    // A porta é obrigatória e a conexão de integração é bloqueada sem revelar os valores fornecidos.
    [TestMethod]
    public void ConnectionStringValidationRequiresExplicitPortAndRejectsIntegrationDatabase()
    {
        Assert.IsFalse(ConnectionLinkLocalCommand.TryValidateConnectionString(
            "Host=127.0.0.1;Database=finora_local;Username=synthetic",
            out _));
        Assert.IsFalse(ConnectionLinkLocalCommand.TryValidateConnectionString(
            "Host=127.0.0.1;Port=5432;Database=FINORA_INTEGRATION_TEST;Username=synthetic",
            out _));
        Assert.IsTrue(ConnectionLinkLocalCommand.TryValidateConnectionString(
            "Host=127.0.0.1;Port=55432;Database=finora_local_synthetic;Username=synthetic",
            out var databaseName));
        Assert.AreEqual("finora_local_synthetic", databaseName);
    }

    // O nome esperado precisa coincidir exatamente; a saída não revela o nome carregado ou digitado.
    [TestMethod]
    public async Task MismatchedExpectedDatabaseIsRejectedBeforeOwnershipConfirmation()
    {
        const string configuredDatabase = "finora_local_private_sentinel";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic;Password=credential-private-sentinel");
        var console = new FakeConnectionLinkLocalConsole("different-private-sentinel");
        var error = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder);

        Assert.AreEqual(2, exitCode);
        Assert.AreEqual(1, console.ReadLineCalls);
        Assert.AreEqual(1, console.Prompts.Count);
        Assert.AreEqual(
            "Connection link local: failure=invalid_configuration" + Environment.NewLine,
            error.ToString());
        Assert.IsFalse(error.ToString().Contains(configuredDatabase, StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("different-private-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("credential-private-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(console.Prompts.Any(prompt => prompt.Contains(configuredDatabase, StringComparison.Ordinal)));
    }

    // Uma configuração local sintética é aceita após a confirmação do destino, sem criar host ou chamar serviços.
    [TestMethod]
    public async Task SyntheticLocalConfigurationRequiresNameAndContainerConfirmationWithoutConnecting()
    {
        const string configuredDatabase = "finora_local_synthetic";
        const string credentialSentinel = "credential-private-sentinel";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic;Password={credentialSentinel}");
        var console = new FakeConnectionLinkLocalConsole(configuredDatabase, "SIM");
        var preflight = CreateReadyPreflightWithSentinels();
        var error = new StringWriter();
        var output = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder,
            CreatePreflightHostFactory(preflight),
            output);

        Assert.AreEqual(2, exitCode);
        Assert.AreEqual(2, console.ReadLineCalls);
        Assert.AreEqual(1, preflight.Calls);
        Assert.AreEqual(3, console.Prompts.Count);
        Assert.AreEqual(
            "Connection link local: failure=interactive_terminal_required" + Environment.NewLine,
            error.ToString());
        Assert.IsFalse(error.ToString().Contains(configuredDatabase, StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains(credentialSentinel, StringComparison.Ordinal));
        AssertNoPrivateSentinels(console.Prompts, error.ToString(), configuredDatabase, credentialSentinel);
        AssertNoMutationInvocation(preflight);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsFalse(preflight.RegisteredServices.Contains(typeof(FinoraDbContext)));
        Assert.IsFalse(preflight.RegisteredServices.Contains(typeof(PluggyApiClient)));
        Assert.IsTrue(builder.Configuration.Sources.All(source => source is MemoryConfigurationSource));
    }

    // Recusa após uma pré-consulta válida não dispara outro request nem revela titular/item/cartão nos streams.
    [TestMethod]
    public async Task FinalRefusalStopsBeforeAnyMutableOperationAndRedactsPrivateSentinels()
    {
        const string configuredDatabase = "finora_local_synthetic";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic");
        var console = new FakeConnectionLinkLocalConsole(configuredDatabase, "SIM");
        console.EnqueueKeys(Key('N', ConsoleKey.N), EnterKey());
        var preflight = CreateReadyPreflightWithSentinels();
        var error = new StringWriter();
        var output = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder,
            CreatePreflightHostFactory(preflight),
            output);

        Assert.AreEqual(130, exitCode);
        Assert.AreEqual(1, preflight.Calls);
        Assert.AreEqual(2, console.ReadLineCalls);
        Assert.AreEqual(2, console.ReadKeyCalls);
        Assert.IsFalse(console.TreatControlCAsInput);
        Assert.IsTrue(console.ControlCWasTreatedAsInputDuringRead);
        Assert.AreEqual("Connection link local: failure=cancelled" + Environment.NewLine, error.ToString());
        AssertNoPrivateSentinels(console.Prompts, error.ToString(), configuredDatabase, "credential-private-sentinel");
        AssertNoMutationInvocation(preflight);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsFalse(preflight.RegisteredServices.Contains(typeof(FinoraDbContext)));
        Assert.IsFalse(preflight.RegisteredServices.Contains(typeof(PluggyApiClient)));
    }

    // EOF depois da validação não é confirmação e termina sem abrir outra fronteira de serviço.
    [TestMethod]
    public async Task FinalConfirmationEndOfInputFailsClosedWithoutSensitiveOutput()
    {
        const string configuredDatabase = "finora_local_synthetic";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic");
        var console = new FakeConnectionLinkLocalConsole(configuredDatabase, "SIM");
        var preflight = CreateReadyPreflightWithSentinels();
        var error = new StringWriter();
        var output = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder,
            CreatePreflightHostFactory(preflight),
            output);

        Assert.AreEqual(2, exitCode);
        Assert.AreEqual(1, preflight.Calls);
        Assert.AreEqual(1, console.ReadKeyCalls);
        Assert.AreEqual("Connection link local: failure=interactive_terminal_required" + Environment.NewLine, error.ToString());
        AssertNoPrivateSentinels(console.Prompts, error.ToString(), configuredDatabase, "credential-private-sentinel");
        AssertNoMutationInvocation(preflight);
        Assert.AreEqual(string.Empty, output.ToString());
    }

    // Ctrl+C durante a confirmação final é tratado como cancelamento e não aciona operação mutável.
    [TestMethod]
    public async Task FinalConfirmationControlCIsCancellationWithoutSensitiveOutput()
    {
        const string configuredDatabase = "finora_local_synthetic";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic");
        var console = new FakeConnectionLinkLocalConsole(configuredDatabase, "SIM");
        console.EnqueueKeys(new ConsoleKeyInfo('\0', ConsoleKey.C, false, false, true));
        var preflight = CreateReadyPreflightWithSentinels();
        var error = new StringWriter();
        var output = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder,
            CreatePreflightHostFactory(preflight),
            output);

        Assert.AreEqual(130, exitCode);
        Assert.AreEqual(1, preflight.Calls);
        Assert.AreEqual(1, console.ReadKeyCalls);
        Assert.IsFalse(console.TreatControlCAsInput);
        Assert.IsTrue(console.ControlCWasTreatedAsInputDuringRead);
        Assert.AreEqual("Connection link local: failure=cancelled" + Environment.NewLine, error.ToString());
        AssertNoPrivateSentinels(console.Prompts, error.ToString(), configuredDatabase, "credential-private-sentinel");
        AssertNoMutationInvocation(preflight);
        Assert.AreEqual(string.Empty, output.ToString());
    }

    // A confirmação encaminha a identidade validada uma única vez e só divulga o estado após o resultado do store.
    [TestMethod]
    public async Task FinalConfirmationDispatchesLinkOnlyAfterPreflightAndKeepsOutputSanitized()
    {
        const string configuredDatabase = "finora_local_synthetic";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic");
        var console = new FakeConnectionLinkLocalConsole(configuredDatabase, "SIM");
        console.EnqueueKeys(Key('S', ConsoleKey.S), Key('I', ConsoleKey.I), Key('M', ConsoleKey.M), EnterKey());
        var preflight = CreateReadyPreflightWithSentinels();
        var error = new StringWriter();
        var output = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder,
            CreatePreflightHostFactory(preflight),
            output);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(1, preflight.Calls);
        Assert.AreEqual(1, preflight.LinkCalls);
        Assert.AreEqual(4, console.ReadKeyCalls);
        Assert.AreEqual(string.Empty, error.ToString());
        Assert.AreEqual("Connection link local: operation=link status=linked" + Environment.NewLine, output.ToString());
        AssertNoPrivateSentinels(console.Prompts, error.ToString() + output, configuredDatabase, "credential-private-sentinel");
        AssertRegisteredHandlersAreExplicit(preflight);
    }

    // Estados de falha da pré-consulta mantêm códigos estáveis e não exibem detalhes do titular ou da Pluggy.
    [TestMethod]
    [DataRow(ConnectionLinkLocalPreflightState.OwnerMissing, 4, "Connection link local: failure=conflict")]
    [DataRow(ConnectionLinkLocalPreflightState.OwnerAmbiguous, 4, "Connection link local: failure=conflict")]
    [DataRow(ConnectionLinkLocalPreflightState.InvalidConfiguration, 2, "Connection link local: failure=invalid_configuration")]
    [DataRow(ConnectionLinkLocalPreflightState.AuthenticationRejected, 1, "Connection link local: failure=operational")]
    [DataRow(ConnectionLinkLocalPreflightState.ItemBlocked, 1, "Connection link local: failure=operational")]
    [DataRow(ConnectionLinkLocalPreflightState.CardSelectionInvalid, 1, "Connection link local: failure=operational")]
    [DataRow(ConnectionLinkLocalPreflightState.ExternalFailure, 1, "Connection link local: failure=operational")]
    public async Task PreflightStatesMapToSanitizedExitCodes(
        ConnectionLinkLocalPreflightState state,
        int expectedExitCode,
        string expectedMessage)
    {
        const string configuredDatabase = "finora_local_synthetic";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic;Password=password-private-sentinel");
        var console = new FakeConnectionLinkLocalConsole(configuredDatabase, "SIM");
        var preflight = new FakePreflightState(new ConnectionLinkLocalPreflightResult(
            state,
            Guid.NewGuid(),
            "item-private-sentinel",
            "card-private-sentinel"));
        var error = new StringWriter();
        var output = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder,
            CreatePreflightHostFactory(preflight),
            output);

        Assert.AreEqual(expectedExitCode, exitCode);
        Assert.AreEqual(1, preflight.Calls);
        Assert.AreEqual(0, preflight.LinkCalls);
        Assert.AreEqual(2, console.Prompts.Count);
        Assert.AreEqual(0, console.ReadKeyCalls);
        Assert.AreEqual(expectedMessage + Environment.NewLine, error.ToString());
        Assert.AreEqual(string.Empty, output.ToString());
        AssertNoPrivateSentinels(console.Prompts, output.ToString() + error, configuredDatabase, "password-private-sentinel");
    }

    // Conflitos, repetição íntegra e falhas do store usam somente os códigos e estados públicos aprovados.
    [TestMethod]
    [DataRow(ConnectionLinkLocalLinkState.AlreadyLinked, 0, "Connection link local: operation=link status=already_linked", "")]
    [DataRow(ConnectionLinkLocalLinkState.Conflict, 4, "", "Connection link local: failure=conflict")]
    [DataRow(ConnectionLinkLocalLinkState.InvalidRequest, 2, "", "Connection link local: failure=invalid_configuration")]
    [DataRow(ConnectionLinkLocalLinkState.OperationalFailure, 1, "", "Connection link local: failure=operational")]
    public async Task LinkStatesMapToSanitizedExitCodes(
        ConnectionLinkLocalLinkState linkState,
        int expectedExitCode,
        string expectedOutput,
        string expectedError)
    {
        const string configuredDatabase = "finora_local_synthetic";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic");
        var console = new FakeConnectionLinkLocalConsole(configuredDatabase, "SIM");
        EnqueueSimConfirmation(console);
        var preflight = CreateReadyPreflightWithSentinels(linkState);
        var error = new StringWriter();
        var output = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder,
            CreatePreflightHostFactory(preflight),
            output);

        Assert.AreEqual(expectedExitCode, exitCode);
        Assert.AreEqual(1, preflight.Calls);
        Assert.AreEqual(1, preflight.LinkCalls);
        Assert.AreEqual(expectedOutput + (expectedOutput.Length == 0 ? string.Empty : Environment.NewLine), output.ToString());
        Assert.AreEqual(expectedError + (expectedError.Length == 0 ? string.Empty : Environment.NewLine), error.ToString());
        AssertNoPrivateSentinels(console.Prompts, output.ToString() + error, configuredDatabase, "password-private-sentinel");
    }

    // Exceções contendo URL, PII, segredos e payloads viram falha fixa e não chegam aos logs nem aos streams.
    [TestMethod]
    public async Task UnexpectedExceptionWithPrivateSentinelsIsRedactedFromOutputErrorAndLogs()
    {
        const string configuredDatabase = "finora_local_synthetic";
        const string credentialSentinel = "password-private-sentinel";
        const string exceptionSentinels =
            "https://api-private.example.test owner-private@example.test item-private-sentinel " +
            "card-private-sentinel response-private-sentinel private-exception-sentinel";
        var builder = CreateConfiguredBuilder(
            $"Host=127.0.0.1;Port=55432;Database={configuredDatabase};Username=synthetic;Password={credentialSentinel}");
        var logger = new CapturingLoggerProvider();
        builder.Logging.AddProvider(logger);
        var console = new FakeConnectionLinkLocalConsole(configuredDatabase, "SIM");
        var error = new StringWriter();
        var output = new StringWriter();

        var exitCode = await ConnectionLinkLocalCommand.RunAsync(
            ["connection-link-local"],
            error,
            () => console,
            () => builder,
            (_, _) => throw new InvalidOperationException(exceptionSentinels),
            output);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual("Connection link local: failure=operational" + Environment.NewLine, error.ToString());
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(string.Empty, logger.Content);
        AssertNoPrivateSentinels(
            console.Prompts,
            output.ToString() + error + logger.Content,
            configuredDatabase,
            credentialSentinel,
            "https://api-private.example.test",
            "owner-private@example.test",
            "response-private-sentinel",
            "private-exception-sentinel");
    }

    // O host administrativo conserva a política de não registrar providers que possam serializar dados privados.
    [TestMethod]
    public void AdministrativeHostStartsWithoutLoggingProviders()
    {
        var builder = ConnectionLinkLocalCommand.CreateHostBuilder(
            Environments.Production,
            disableDefaults: true);

        Assert.IsFalse(builder.Logging.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(ILoggerProvider)));
    }

    // A configuração isolada prova que o teste não depende de arquivo .env ou de credenciais externas.
    [TestMethod]
    public void IsolatedConfigurationBuilderDoesNotLoadDotEnvFallback()
    {
        var builder = ConnectionLinkLocalCommand.CreateHostBuilder(
            Environments.Development,
            disableDefaults: true);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Finora"] =
                "Host=127.0.0.1;Port=55432;Database=finora_local_synthetic;Username=synthetic"
        });

        Assert.AreEqual(Environments.Development, builder.Environment.EnvironmentName);
        Assert.IsTrue(builder.Configuration.Sources.All(source => source is MemoryConfigurationSource));
        Assert.IsTrue(ConnectionLinkLocalCommand.TryValidateConnectionString(
            builder.Configuration.GetConnectionString("Finora"),
            out _));
    }

    // O comando preserva o ambiente escolhido pelo operador em vez de habilitar Development implicitamente.
    [TestMethod]
    public void HostBuilderRespectsExplicitProductionEnvironment()
    {
        var builder = ConnectionLinkLocalCommand.CreateHostBuilder(
            Environments.Production,
            disableDefaults: true);

        Assert.AreEqual(Environments.Production, builder.Environment.EnvironmentName);
        Assert.IsTrue(builder.Configuration.Sources.All(source => source is MemoryConfigurationSource));
    }

    // Cria um builder sem fontes externas para que cada cenário controle integralmente sua configuração sintética.
    private static HostApplicationBuilder CreateConfiguredBuilder(string? connectionString)
    {
        var builder = ConnectionLinkLocalCommand.CreateHostBuilder(
            Environments.Development,
            disableDefaults: true);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Finora"] = connectionString
        });
        return builder;
    }

    // O host sintético contém somente o request read-only; outros handlers não estão registrados nem podem mutar dados.
    private static Func<HostApplicationBuilder, string, IHost> CreatePreflightHostFactory(
        FakePreflightState state) => (builder, _) =>
    {
        builder.Services.AddSingleton(state);
        builder.Services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(ConnectionLinkLocalPreflightQuery).Assembly);
            configuration.TypeEvaluator = _ => false;
        });
        builder.Services.AddScoped<
            IRequestHandler<ConnectionLinkLocalPreflightQuery, ConnectionLinkLocalPreflightResult>,
            SyntheticPreflightHandler>();
        builder.Services.AddScoped<
            IRequestHandler<ConnectionLinkLocalLinkCommand, ConnectionLinkLocalLinkResult>,
            SyntheticLinkHandler>();

        state.RegisteredServices.AddRange(builder.Services.Select(descriptor => descriptor.ServiceType));
        return builder.Build();
    };

    // IDs internos sentinela comprovam que o terminal usa somente o estado Ready da pré-consulta.
    private static FakePreflightState CreateReadyPreflightWithSentinels(
        ConnectionLinkLocalLinkState linkState = ConnectionLinkLocalLinkState.Linked) => new(
        new ConnectionLinkLocalPreflightResult(
            ConnectionLinkLocalPreflightState.Ready,
            Guid.NewGuid(),
            "item-private-sentinel",
            "card-private-sentinel"),
        new ConnectionLinkLocalLinkResult(linkState));

    // Mantém a sequência terminal determinística e não imprime as teclas digitadas nos testes.
    private static void EnqueueSimConfirmation(FakeConnectionLinkLocalConsole console) => console.EnqueueKeys(
        Key('S', ConsoleKey.S),
        Key('I', ConsoleKey.I),
        Key('M', ConsoleKey.M),
        EnterKey());

    // Confere os dois canais observáveis do comando, incluindo prompts e status final capturados como stdout.
    private static void AssertNoPrivateSentinels(
        IEnumerable<string> standardOutput,
        string standardError,
        params string[] sentinels)
    {
        var output = string.Join(Environment.NewLine, standardOutput);
        foreach (var sentinel in sentinels.Concat(
                     ["item-private-sentinel", "card-private-sentinel", "owner-private@example.test"]))
        {
            Assert.IsFalse(output.Contains(sentinel, StringComparison.Ordinal));
            Assert.IsFalse(standardError.Contains(sentinel, StringComparison.Ordinal));
        }
    }

    // Recusa/EOF/Ctrl+C não podem chamar o handler de mutação, embora ele esteja disponível no host sintético.
    private static void AssertNoMutationInvocation(FakePreflightState state)
    {
        Assert.AreEqual(0, state.LinkCalls);
        AssertRegisteredHandlersAreExplicit(state);
    }

    // O host de teste permite somente os dois requests administrativos explicitamente esperados.
    private static void AssertRegisteredHandlersAreExplicit(FakePreflightState state)
    {
        var handlers = state.RegisteredServices
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
            .ToArray();
        CollectionAssert.AreEquivalent(
            new Type[]
            {
                typeof(IRequestHandler<ConnectionLinkLocalPreflightQuery, ConnectionLinkLocalPreflightResult>),
                typeof(IRequestHandler<ConnectionLinkLocalLinkCommand, ConnectionLinkLocalLinkResult>)
            },
            handlers);
    }

    // O fake de pré-consulta expõe somente um handler read-only e registra sua única execução.
    private sealed class FakePreflightState(
        ConnectionLinkLocalPreflightResult result,
        ConnectionLinkLocalLinkResult? linkResult = null)
    {
        public ConnectionLinkLocalPreflightResult Result { get; } = result;

        public ConnectionLinkLocalLinkResult LinkResult { get; } =
            linkResult ?? new ConnectionLinkLocalLinkResult(ConnectionLinkLocalLinkState.Linked);

        public int Calls { get; set; }

        public int LinkCalls { get; set; }

        public List<Type> RegisteredServices { get; } = [];
    }

    // Devolve resultado sintético sem abrir banco ou consultar Pluggy real.
    private sealed class SyntheticPreflightHandler(FakePreflightState state) :
        IRequestHandler<ConnectionLinkLocalPreflightQuery, ConnectionLinkLocalPreflightResult>
    {
        public Task<ConnectionLinkLocalPreflightResult> Handle(
            ConnectionLinkLocalPreflightQuery request,
            CancellationToken cancellationToken)
        {
            state.Calls++;
            return Task.FromResult(state.Result);
        }
    }

    // Captura log messages de qualquer nível para provar que exceções do fluxo não viram diagnóstico textual.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _entries = [];

        public string Content => string.Join(Environment.NewLine, _entries);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose() { }

        private sealed class CapturingLogger(List<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add($"{formatter(state, exception)} {exception}");
        }
    }

    // Conta mutações efetivamente despachadas e devolve resultado fixo sem banco ou chamadas externas.
    private sealed class SyntheticLinkHandler(FakePreflightState state) :
        IRequestHandler<ConnectionLinkLocalLinkCommand, ConnectionLinkLocalLinkResult>
    {
        public Task<ConnectionLinkLocalLinkResult> Handle(
            ConnectionLinkLocalLinkCommand request,
            CancellationToken cancellationToken)
        {
            state.LinkCalls++;
            return Task.FromResult(state.LinkResult);
        }
    }

    // Cria teclas sem imprimir seu conteúdo, permitindo testar confirmação, recusa e Ctrl+C sem TTY.
    private static ConsoleKeyInfo Key(char value, ConsoleKey key) =>
        new(value, key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo EnterKey() =>
        new('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);

    // O fake fornece respostas de terminal sem ecoar os valores nem depender de TTY no agente de testes.
    private sealed class FakeConnectionLinkLocalConsole(params string?[] responses) : IConnectionLinkLocalConsole
    {
        private readonly Queue<string?> _responses = new(responses);
        private readonly Queue<ConsoleKeyInfo?> _keys = [];

        public bool IsInputRedirected { get; init; }

        public bool IsOutputRedirected { get; init; }

        public bool TreatControlCAsInput { get; set; }

        public int ReadLineCalls { get; private set; }

        public int ReadKeyCalls { get; private set; }

        public bool ControlCWasTreatedAsInputDuringRead { get; private set; }

        public List<string> Prompts { get; } = [];

        public void EnqueueKeys(params ConsoleKeyInfo?[] keys)
        {
            foreach (var key in keys)
            {
                _keys.Enqueue(key);
            }
        }

        public string? ReadLine()
        {
            ReadLineCalls++;
            return _responses.Count == 0 ? null : _responses.Dequeue();
        }

        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            ReadKeyCalls++;
            ControlCWasTreatedAsInputDuringRead = TreatControlCAsInput;
            return _keys.Count == 0 ? null : _keys.Dequeue();
        }

        public void WriteLine(string value) => Prompts.Add(value);
    }
}
