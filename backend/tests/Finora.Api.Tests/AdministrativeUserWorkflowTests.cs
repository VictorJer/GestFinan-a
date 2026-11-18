using Finora.Api;
using Finora.Api.AdministrativeUsers;
using Finora.Application.AdministrativeUsers;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica a ordem de prompts, pré-consulta e mutação sem Identity, EF ou terminal real.
[TestClass]
public sealed class AdministrativeUserWorkflowTests
{
    // Uma criação repetida termina após a pré-consulta, sem pedir senha nem abrir o caso de uso mutável.
    [TestMethod]
    public async Task ExistingCreateStopsBeforePasswordAndMutation()
    {
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.AlreadyExists)
        };
        var console = new ScriptedAdministrativeUserConsole(TypeLine("admin@example.com"), probe.Events);
        await using var services = CreateProvider(probe);

        var result = await AdministrativeUserCommand.RunWorkflowAsync(
            "user-create",
            services,
            new AdministrativeUserTerminal(console));

        Assert.AreEqual(AdministrativeUserWorkflowState.AlreadyExists, result);
        Assert.AreEqual(AdministrativeUserAction.Create, probe.ReceivedAction);
        Assert.AreEqual(0, probe.OperationCalls);
        Assert.IsFalse(console.WrittenLines.Contains(AdministrativeUserTerminal.PasswordPrompt));
        Assert.IsFalse(console.WrittenLines.Contains(AdministrativeUserTerminal.ConfirmationPrompt));
        Assert.IsTrue(probe.Events.IndexOf("email-prompt") < probe.Events.IndexOf("preflight"));
        Assert.IsTrue(probe.Events.IndexOf("preflight") < probe.Events.IndexOf("preflight-disposed"));
    }

    // Um reset sem titular retorna antes da senha, não chama reset e descarta o escopo que consultou o banco.
    [TestMethod]
    public async Task MissingResetUserStopsBeforeNewPassword()
    {
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.NotFound)
        };
        var console = new ScriptedAdministrativeUserConsole(TypeLine("missing@example.com"), probe.Events);
        await using var services = CreateProvider(probe);

        var result = await AdministrativeUserCommand.RunWorkflowAsync(
            "user-reset-password",
            services,
            new AdministrativeUserTerminal(console));

        Assert.AreEqual(AdministrativeUserWorkflowState.NotFound, result);
        Assert.AreEqual(AdministrativeUserAction.ResetPassword, probe.ReceivedAction);
        Assert.AreEqual(0, probe.OperationCalls);
        Assert.IsFalse(console.WrittenLines.Contains(AdministrativeUserTerminal.PasswordPrompt));
        Assert.IsTrue(probe.Events.Contains("preflight-disposed"));
    }

    // A consulta termina e seu escopo fecha antes do prompt; o reset recebe o ID observado e a senha sem alteração.
    [TestMethod]
    public async Task ResetPreflightScopeClosesBeforePasswordAndExpectedIdReachesHandler()
    {
        const string email = "admin@example.com";
        const string password = "Exact-password-9!";
        var expectedUserId = Guid.NewGuid();
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(
                AdministrativeUserPreflightState.Ready,
                expectedUserId),
            OperationResult = new AdministrativeUserOperationResult(AdministrativeUserOperationState.PasswordReset)
        };
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine(email).Concat(TypeLine(password)).Concat(TypeLine(password)),
            probe.Events);
        await using var services = CreateProvider(probe);

        var result = await AdministrativeUserCommand.RunWorkflowAsync(
            "user-reset-password",
            services,
            new AdministrativeUserTerminal(console));

        Assert.AreEqual(AdministrativeUserWorkflowState.PasswordReset, result);
        Assert.AreEqual(AdministrativeUserAction.ResetPassword, probe.ReceivedAction);
        Assert.AreEqual(expectedUserId, probe.ReceivedExpectedUserId);
        Assert.IsTrue(string.Equals(email, probe.ReceivedEmail, StringComparison.Ordinal));
        Assert.IsTrue(string.Equals(password, probe.ReceivedPassword, StringComparison.Ordinal));
        Assert.AreEqual(1, probe.OperationCalls);
        Assert.IsTrue(probe.Events.IndexOf("preflight-disposed") < probe.Events.IndexOf("password-prompt"));
        Assert.IsTrue(probe.Events.IndexOf("confirmation-complete") < probe.Events.IndexOf("reset-operation"));
    }

    // Criação só inicia a mutação depois da confirmação e encaminha a senha exata ao handler Application.
    [TestMethod]
    public async Task CreateMutationStartsAfterConfirmationWithExactPassword()
    {
        const string email = "new-admin@example.com";
        const string password = "Exact-create-password-9!";
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Ready),
            OperationResult = new AdministrativeUserOperationResult(AdministrativeUserOperationState.Created)
        };
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine(email).Concat(TypeLine(password)).Concat(TypeLine(password)),
            probe.Events);
        await using var services = CreateProvider(probe);

        var result = await AdministrativeUserCommand.RunWorkflowAsync(
            "user-create",
            services,
            new AdministrativeUserTerminal(console));

        Assert.AreEqual(AdministrativeUserWorkflowState.Created, result);
        Assert.AreEqual(AdministrativeUserAction.Create, probe.ReceivedAction);
        Assert.IsTrue(string.Equals(email, probe.ReceivedEmail, StringComparison.Ordinal));
        Assert.IsTrue(string.Equals(password, probe.ReceivedPassword, StringComparison.Ordinal));
        Assert.AreEqual(1, probe.OperationCalls);
        Assert.IsTrue(probe.Events.IndexOf("preflight-disposed") < probe.Events.IndexOf("password-prompt"));
        Assert.IsTrue(probe.Events.IndexOf("confirmation-complete") < probe.Events.IndexOf("create-operation"));
    }

    // Ctrl+C durante a confirmação encerra a entrada sem chamar criação/reset nem escrever credencial.
    [TestMethod]
    public async Task CancelledConfirmationDoesNotStartMutation()
    {
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Ready)
        };
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine("admin@example.com")
                .Concat(TypeLine("Exact-password-9!"))
                .Append(ControlCKey()),
            probe.Events);
        await using var services = CreateProvider(probe);

        var result = await AdministrativeUserCommand.RunWorkflowAsync(
            "user-create",
            services,
            new AdministrativeUserTerminal(console));

        Assert.AreEqual(AdministrativeUserWorkflowState.Cancelled, result);
        Assert.AreEqual(0, probe.OperationCalls);
        Assert.IsFalse(probe.Events.Contains("create-operation"));
        Assert.IsFalse(probe.Events.Contains("confirmation-complete"));
    }

    // Cancelamento sinalizado quando a confirmação termina é observado antes da primeira operação de escrita.
    [TestMethod]
    public async Task CancelledOperationTokenAfterInputDoesNotStartMutation()
    {
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Ready)
        };
        using var cancellation = new CancellationTokenSource();
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine("admin@example.com")
                .Concat(TypeLine("Exact-password-9!"))
                .Concat(TypeLine("Exact-password-9!")),
            probe.Events)
        {
            OnConfirmationComplete = cancellation.Cancel
        };
        await using var services = CreateProvider(probe);

        var result = await AdministrativeUserCommand.RunWorkflowAsync(
            "user-create",
            services,
            new AdministrativeUserTerminal(console),
            cancellation.Token);

        Assert.AreEqual(AdministrativeUserWorkflowState.Cancelled, result);
        Assert.IsTrue(probe.Events.Contains("confirmation-complete"));
        Assert.AreEqual(0, probe.OperationCalls);
    }

    // O entry point publica criação somente em stdout e mantém a resposta livre de e-mail/senha.
    [TestMethod]
    public async Task RunAsyncWritesCreatedStatusOnlyToStandardOutput()
    {
        const string email = "email-private-sentinel@example.com";
        const string password = "password-private-sentinel-9!";
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Ready),
            OperationResult = new AdministrativeUserOperationResult(AdministrativeUserOperationState.Created)
        };
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine(email).Concat(TypeLine(password)).Concat(TypeLine(password)),
            probe.Events);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await RunWithFakeHostAsync("user-create", probe, console, output, error);

        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(string.Equals("User admin: operation=create status=created" + Environment.NewLine,
            output.ToString(), StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, error.ToString());
        AssertOutputDoesNotContainSecrets(output, error, console, email, password);
    }

    // O reset publica aviso fixo de revogação pendente junto do sucesso, sem alegar logout global.
    [TestMethod]
    public async Task RunAsyncResetWritesPendingRevocationWarningToStandardOutput()
    {
        const string password = "reset-password-private-sentinel-9!";
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(
                AdministrativeUserPreflightState.Ready,
                Guid.NewGuid()),
            OperationResult = new AdministrativeUserOperationResult(AdministrativeUserOperationState.PasswordReset)
        };
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine("reset-email-private-sentinel@example.com")
                .Concat(TypeLine(password))
                .Concat(TypeLine(password)),
            probe.Events);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await RunWithFakeHostAsync("user-reset-password", probe, console, output, error);

        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(output.ToString().Contains(
            "User admin: operation=reset_password status=password_reset session_revocation=not_implemented",
            StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, error.ToString());
        AssertOutputDoesNotContainSecrets(
            output,
            error,
            console,
            "reset-email-private-sentinel@example.com",
            password);
    }

    // Uma criação repetida usa stdout com estado distinto e não pede senha, inclusive no contrato do entry point.
    [TestMethod]
    public async Task RunAsyncAlreadyExistsWritesDistinctSuccessWithoutPasswordPrompt()
    {
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.AlreadyExists)
        };
        var console = new ScriptedAdministrativeUserConsole(TypeLine("admin@example.com"), probe.Events);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await RunWithFakeHostAsync("user-create", probe, console, output, error);

        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(string.Equals("User admin: operation=create status=already_exists" + Environment.NewLine,
            output.ToString(), StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, error.ToString());
        Assert.AreEqual(0, probe.OperationCalls);
        Assert.IsFalse(console.WrittenLines.Contains(AdministrativeUserTerminal.PasswordPrompt));
    }

    // A tabela exaustiva fixa os códigos D6 para estados de entrada, Identity, conflito e cancelamento.
    [TestMethod]
    public void WorkflowStateMappingUsesStableExitCodesAndFixedMessages()
    {
        var cases = new (AdministrativeUserWorkflowState State, int ExitCode, string Message)[]
        {
            (AdministrativeUserWorkflowState.AlreadyExists, 0, "User admin: operation=create status=already_exists"),
            (AdministrativeUserWorkflowState.NotFound, 3, "User admin: failure=not_found"),
            (AdministrativeUserWorkflowState.InvalidArguments, 2, "User admin: failure=invalid_arguments"),
            (AdministrativeUserWorkflowState.InvalidConfiguration, 2, "User admin: failure=invalid_configuration"),
            (AdministrativeUserWorkflowState.InvalidEmail, 2, "User admin: failure=invalid_input message=e-mail_invalido"),
            (AdministrativeUserWorkflowState.PasswordPolicyViolation, 2,
                "User admin: failure=password_policy message=senha_minimo_6_maiuscula_minuscula_numero_simbolo_caractere_distinto"),
            (AdministrativeUserWorkflowState.PasswordMismatch, 2, "User admin: failure=password_mismatch"),
            (AdministrativeUserWorkflowState.EndOfInput, 2, "User admin: failure=invalid_input"),
            (AdministrativeUserWorkflowState.TerminalUnavailable, 2, "User admin: failure=interactive_terminal_required"),
            (AdministrativeUserWorkflowState.Conflict, 4, "User admin: failure=conflict"),
            (AdministrativeUserWorkflowState.Cancelled, 130, "User admin: failure=cancelled"),
            (AdministrativeUserWorkflowState.OperationalFailure, 1, "User admin: failure=operational")
        };

        foreach (var item in cases)
        {
            var mapped = AdministrativeUserCommand.MapWorkflowResult("user-create", item.State);
            Assert.AreEqual(item.ExitCode, mapped.ExitCode);
            Assert.IsTrue(string.Equals(item.Message, mapped.Message, StringComparison.Ordinal));
        }

        var resetAlreadyExists = AdministrativeUserCommand.MapWorkflowResult(
            "user-reset-password",
            AdministrativeUserWorkflowState.AlreadyExists);
        Assert.AreEqual(1, resetAlreadyExists.ExitCode);
        Assert.IsTrue(string.Equals("User admin: failure=operational", resetAlreadyExists.Message, StringComparison.Ordinal));
    }

    // Exceções de construção e persistência com sentinelas viram somente falha operacional fixa em stderr.
    [TestMethod]
    [DataRow("builder")]
    [DataRow("host")]
    [DataRow("preflight")]
    [DataRow("operation")]
    public async Task RunAsyncRedactsInitializationAndPersistenceExceptions(string failurePoint)
    {
        const string privateSentinel = "exception-private-sentinel";
        const string tokenSentinel = "token-private-sentinel";
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Ready),
            PreflightFailure = failurePoint == "preflight"
                ? new InvalidOperationException($"{privateSentinel} {tokenSentinel}")
                : null,
            OperationFailure = failurePoint == "operation"
                ? new InvalidOperationException($"{privateSentinel} {tokenSentinel}")
                : null
        };
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine("email-private-sentinel@example.com")
                .Concat(TypeLine("password-private-sentinel-9!"))
                .Concat(TypeLine("password-private-sentinel-9!")),
            probe.Events);
        var output = new StringWriter();
        var error = new StringWriter();
        HostApplicationBuilder? capturedBuilder = null;

        var exitCode = await AdministrativeUserCommand.RunAsync(
            ["user-create"],
            output,
            error,
            () =>
            {
                if (failurePoint == "builder")
                {
                    throw new InvalidOperationException(privateSentinel);
                }

                capturedBuilder = CreateConfiguredBuilder();
                return capturedBuilder;
            },
            (builder, _) =>
            {
                if (failurePoint == "host")
                {
                    throw new InvalidOperationException(privateSentinel);
                }

                return BuildFakeHost(builder, probe);
            },
            () => console);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsTrue(string.Equals("User admin: failure=operational" + Environment.NewLine,
            error.ToString(), StringComparison.Ordinal));
        AssertOutputDoesNotContainSecrets(
            output,
            error,
            console,
            privateSentinel,
            tokenSentinel,
            "email-private-sentinel@example.com",
            "password-private-sentinel-9!");
        if (capturedBuilder is not null)
        {
            Assert.IsFalse(capturedBuilder.Logging.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(ILoggerProvider)));
        }
    }

    // Senha rejeitada e e-mail inválido recebem estados fixos, sem refletir PII ou descrições do Identity.
    [TestMethod]
    [DataRow("email")]
    [DataRow("password")]
    public async Task RunAsyncDoesNotEchoIdentityInputsOrDescriptions(string rejectedInput)
    {
        const string email = "email-private-sentinel@example.com";
        const string password = "password-private-sentinel-9!";
        var probe = new WorkflowProbe
        {
            PreflightResult = new AdministrativeUserPreflightResult(
                rejectedInput == "email"
                    ? AdministrativeUserPreflightState.InvalidEmail
                    : AdministrativeUserPreflightState.Ready),
            OperationResult = new AdministrativeUserOperationResult(
                rejectedInput == "password"
                    ? AdministrativeUserOperationState.PasswordPolicyViolation
                    : AdministrativeUserOperationState.Created)
        };
        var keys = TypeLine(email);
        if (rejectedInput == "password")
        {
            keys = keys.Concat(TypeLine(password)).Concat(TypeLine(password));
        }

        var console = new ScriptedAdministrativeUserConsole(keys, probe.Events);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await RunWithFakeHostAsync("user-create", probe, console, output, error);

        Assert.AreEqual(2, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsTrue(error.ToString().StartsWith("User admin: failure=", StringComparison.Ordinal));
        AssertOutputDoesNotContainSecrets(output, error, console, email, password, "IdentityError.Description");
        Assert.AreEqual(rejectedInput == "email" ? 0 : 1, probe.OperationCalls);
    }

    // Cada categoria terminal/pré-consulta segue o código D6 e escreve a falha somente em stderr.
    [TestMethod]
    [DataRow("terminal", "user-create", 2, "interactive_terminal_required")]
    [DataRow("not-found", "user-reset-password", 3, "not_found")]
    [DataRow("conflict", "user-create", 4, "conflict")]
    [DataRow("cancelled", "user-create", 130, "cancelled")]
    [DataRow("mismatch", "user-create", 2, "password_mismatch")]
    [DataRow("eof", "user-create", 2, "invalid_input")]
    public async Task RunAsyncRoutesTerminalAndPreflightFailuresToStandardError(
        string scenario,
        string command,
        int expectedExitCode,
        string expectedFailure)
    {
        var probe = new WorkflowProbe
        {
            PreflightResult = scenario switch
            {
                "not-found" => new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.NotFound),
                "conflict" => new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Conflict),
                _ => new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Ready)
            }
        };
        var keys = scenario switch
        {
            "cancelled" => new[] { ControlCKey() },
            "not-found" or "conflict" => TypeLine("email-private-sentinel@example.com").ToArray(),
            "mismatch" => TypeLine("email-private-sentinel@example.com")
                .Concat(TypeLine("password-private-sentinel-9!"))
                .Concat(TypeLine("different-private-sentinel"))
                .ToArray(),
            "eof" => TypeLine("email-private-sentinel@example.com").Take(1).ToArray(),
            _ => Array.Empty<ConsoleKeyInfo?>()
        };
        var console = new ScriptedAdministrativeUserConsole(keys, probe.Events)
        {
            IsInputRedirected = scenario == "terminal"
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await RunWithFakeHostAsync(command, probe, console, output, error);

        Assert.AreEqual(expectedExitCode, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsTrue(error.ToString().Contains($"User admin: failure={expectedFailure}", StringComparison.Ordinal));
        AssertOutputDoesNotContainSecrets(
            output,
            error,
            console,
            "email-private-sentinel@example.com",
            "password-private-sentinel-9!",
            "different-private-sentinel");
        Assert.AreEqual(0, probe.OperationCalls);
    }

    // A camada de apresentação cria escopos reais do MediatR, mas mantém a persistência substituída pelo fake.
    private static ServiceProvider CreateProvider(WorkflowProbe probe)
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(CheckAdministrativeUserPreflightQuery).Assembly));
        services.AddScoped<IAdministrativeUserAccess>(_ => new FakeAdministrativeUserAccess(probe));
        return services.BuildServiceProvider(validateScopes: true);
    }

    // A fábrica fornece connection string sintética e preserva a mesma supressão de logs do host administrativo.
    private static HostApplicationBuilder CreateConfiguredBuilder()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Finora.Api",
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
            Args = Array.Empty<string>()
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Finora"] =
                "Host=127.0.0.1;Database=finora_integration_test;Username=connection-private-sentinel"
        });
        builder.Logging.ClearProviders();
        return builder;
    }

    // Monta somente MediatR e o fake scoped para exercitar RunAsync sem abrir conexão PostgreSQL.
    private static IHost BuildFakeHost(HostApplicationBuilder builder, WorkflowProbe probe)
    {
        builder.Services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(CheckAdministrativeUserPreflightQuery).Assembly));
        builder.Services.AddScoped<IAdministrativeUserAccess>(_ => new FakeAdministrativeUserAccess(probe));
        return builder.Build();
    }

    // Executa o entry point com host/console substituíveis, mantendo configuração e streams observáveis.
    private static Task<int> RunWithFakeHostAsync(
        string command,
        WorkflowProbe probe,
        ScriptedAdministrativeUserConsole console,
        TextWriter output,
        TextWriter error) =>
        AdministrativeUserCommand.RunAsync(
            [command],
            output,
            error,
            CreateConfiguredBuilder,
            (builder, _) => BuildFakeHost(builder, probe),
            () => console);

    // Inspeciona todos os canais capturados sem exibir valores privados em falhas de assert.
    private static void AssertOutputDoesNotContainSecrets(
        TextWriter output,
        TextWriter error,
        ScriptedAdministrativeUserConsole console,
        params string[] sentinels)
    {
        var captured = output.ToString() + error.ToString() + string.Join('\n', console.WrittenLines);
        Assert.IsTrue(sentinels
            .Append("connection-private-sentinel")
            .All(sentinel => !captured.Contains(sentinel, StringComparison.Ordinal)));
    }

    // Gera teclas sem Enter intermediário para que os testes controlem cada fronteira de prompt.
    private static IEnumerable<ConsoleKeyInfo?> TypeLine(string value) =>
        value.Select(character => (ConsoleKeyInfo?)new ConsoleKeyInfo(
            character,
            ConsoleKey.NoName,
            shift: false,
            alt: false,
            control: false))
            .Append(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

    // Ctrl+C chega ao adaptador como tecla interceptada enquanto a leitura protegida está ativa.
    private static ConsoleKeyInfo? ControlCKey() =>
        new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true);

    // Compartilha somente eventos e resultados tipados entre escopos para observar a ordem do fluxo.
    private sealed class WorkflowProbe
    {
        public AdministrativeUserPreflightResult PreflightResult { get; init; } =
            new(AdministrativeUserPreflightState.Ready);

        public AdministrativeUserOperationResult OperationResult { get; init; } =
            new(AdministrativeUserOperationState.Created);

        public Exception? PreflightFailure { get; init; }

        public Exception? OperationFailure { get; init; }

        public List<string> Events { get; } = [];

        public AdministrativeUserAction? ReceivedAction { get; set; }

        public Guid? ReceivedExpectedUserId { get; set; }

        public string? ReceivedEmail { get; set; }

        public string? ReceivedPassword { get; set; }

        public int OperationCalls { get; set; }
    }

    // Cada instância representa o acesso scoped; a saída de escopo torna verificável a separação antes da senha.
    private sealed class FakeAdministrativeUserAccess(WorkflowProbe probe) : IAdministrativeUserAccess, IAsyncDisposable
    {
        private bool _preflightCalled;

        // Registra a consulta e o estado escolhido sem simular Identity nem persistência.
        public Task<AdministrativeUserPreflightResult> PreflightAsync(
            AdministrativeUserAction action,
            string email,
            CancellationToken cancellationToken)
        {
            if (probe.PreflightFailure is not null)
            {
                throw probe.PreflightFailure;
            }

            _preflightCalled = true;
            probe.ReceivedAction = action;
            probe.ReceivedEmail = email;
            probe.Events.Add("preflight");
            return Task.FromResult(probe.PreflightResult);
        }

        // Registra a criação somente quando o handler recebe as duas entradas já confirmadas.
        public Task<AdministrativeUserOperationResult> CreateAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            if (probe.OperationFailure is not null)
            {
                throw probe.OperationFailure;
            }

            probe.ReceivedEmail = email;
            probe.ReceivedPassword = password;
            probe.OperationCalls++;
            probe.Events.Add("create-operation");
            return Task.FromResult(probe.OperationResult);
        }

        // Preserva o identificador observado na pré-consulta até a camada de persistência.
        public Task<AdministrativeUserOperationResult> ResetPasswordAsync(
            string email,
            Guid expectedUserId,
            string newPassword,
            CancellationToken cancellationToken)
        {
            if (probe.OperationFailure is not null)
            {
                throw probe.OperationFailure;
            }

            probe.ReceivedEmail = email;
            probe.ReceivedExpectedUserId = expectedUserId;
            probe.ReceivedPassword = newPassword;
            probe.OperationCalls++;
            probe.Events.Add("reset-operation");
            return Task.FromResult(probe.OperationResult);
        }

        // Registra o descarte do escopo da pré-consulta antes que a interação prossiga para senha.
        public ValueTask DisposeAsync()
        {
            if (_preflightCalled)
            {
                probe.Events.Add("preflight-disposed");
            }

            return ValueTask.CompletedTask;
        }
    }

    // Simula Console e marca somente os limites de prompt/entrada, sem imprimir conteúdo digitado.
    private sealed class ScriptedAdministrativeUserConsole : IAdministrativeUserConsole
    {
        private readonly Queue<ConsoleKeyInfo?> _keys;
        private readonly ICollection<string> _events;
        private bool _treatControlCAsInput;
        private bool _confirmationPromptShown;

        public ScriptedAdministrativeUserConsole(IEnumerable<ConsoleKeyInfo?> keys, ICollection<string> events)
        {
            _keys = new Queue<ConsoleKeyInfo?>(keys);
            _events = events;
        }

        public bool IsInputRedirected { get; init; }

        public bool IsOutputRedirected { get; init; }

        public bool TreatControlCAsInput
        {
            get => _treatControlCAsInput;
            set => _treatControlCAsInput = value;
        }

        public List<string> WrittenLines { get; } = [];

        public Action? OnConfirmationComplete { get; init; }

        // Devolve a sequência fornecida e marca a confirmação completa sem registrar teclas ou segredos.
        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            var key = _keys.Count == 0 ? null : _keys.Dequeue();
            if (_confirmationPromptShown && key?.Key == ConsoleKey.Enter)
            {
                _events.Add("confirmation-complete");
                OnConfirmationComplete?.Invoke();
            }

            return key;
        }

        // Registra prompts fixos para verificar a ordem, sem associar saída aos caracteres digitados.
        public void WriteLine(string value)
        {
            WrittenLines.Add(value);
            if (value == AdministrativeUserTerminal.EmailPrompt)
            {
                _events.Add("email-prompt");
            }
            else if (value == AdministrativeUserTerminal.PasswordPrompt)
            {
                _events.Add("password-prompt");
            }
            else if (value == AdministrativeUserTerminal.ConfirmationPrompt)
            {
                _confirmationPromptShown = true;
                _events.Add("confirmation-prompt");
            }
        }
    }
}
