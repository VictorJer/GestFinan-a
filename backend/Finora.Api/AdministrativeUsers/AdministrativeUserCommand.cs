using Finora.Application.AdministrativeUsers;
using Finora.Api.AdministrativeUsers;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Finora.Api;

/// <summary>Roteia os comandos administrativos e prepara um host isolado, sem iniciar a API.</summary>
internal static class AdministrativeUserCommand
{
    private const int OperationalFailure = 1;
    private const int InvalidArguments = 2;
    private const string InvalidArgumentsMessage = "User admin: failure=invalid_arguments";
    private const string InvalidConfigurationMessage = "User admin: failure=invalid_configuration";
    private const string OperationalFailureMessage = "User admin: failure=operational";

    // Registra os handlers Application no mesmo host isolado que contém os serviços scoped de Identity.
    internal static HostApplicationBuilder AddAdministrativeUserHandlers(HostApplicationBuilder builder)
    {
        builder.Services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(CheckAdministrativeUserPreflightQuery).Assembly));
        return builder;
    }

    // Somente estes nomes escolhem o ramo administrativo; o restante continua no roteador existente.
    internal static bool IsAdministrativeCommand(string[] args) =>
        args.Length > 0 && args[0] is "user-create" or "user-reset-password";

    // Um comando administrativo válido contém exatamente um token, nunca opções ou dados posicionais.
    internal static bool HasValidArguments(string[] args) =>
        args.Length == 1 && IsAdministrativeCommand(args);

    // Usa Production quando DOTNET_ENVIRONMENT está ausente ou vazio, sem herdar o perfil Pluggy.
    internal static string ResolveEnvironmentName(string? dotnetEnvironment) =>
        string.IsNullOrWhiteSpace(dotnetEnvironment) ? Environments.Production : dotnetEnvironment;

    // Cria configuração da aplicação sem encaminhar os argumentos do comando como providers de configuração.
    internal static HostApplicationBuilder CreateHostBuilder(string? environmentName = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = ResolveEnvironmentName(
                environmentName ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")),
            Args = Array.Empty<string>()
        });

        // A saída administrativa é allowlist; providers do host/EF não podem registrar dados sensíveis.
        builder.Logging.ClearProviders();
        return builder;
    }

    // Constrói somente Identity e persistência, sem cliente Pluggy, autenticação HTTP ou listener.
    internal static IHost BuildHost(HostApplicationBuilder builder, string connectionString)
    {
        AddAdministrativeUserHandlers(builder);
        builder.Services.AddAdministrativeUserIdentity(connectionString);
        return builder.Build();
    }

    // Executa a interação em duas fases, destruindo o escopo de pré-consulta antes de pedir a senha.
    internal static async Task<AdministrativeUserWorkflowState> RunWorkflowAsync(
        string command,
        IServiceProvider services,
        AdministrativeUserTerminal terminal,
        CancellationToken cancellationToken = default)
    {
        var action = command switch
        {
            "user-create" => AdministrativeUserAction.Create,
            "user-reset-password" => AdministrativeUserAction.ResetPassword,
            _ => (AdministrativeUserAction?)null
        };
        if (action is null)
        {
            return AdministrativeUserWorkflowState.InvalidArguments;
        }

        var emailInput = terminal.ReadEmail();
        if (emailInput.State != AdministrativeUserTerminalState.EmailCollected)
        {
            return MapTerminalState(emailInput.State);
        }

        // Este escopo contém a conexão da pré-consulta e é encerrado antes de qualquer leitura da senha.
        AdministrativeUserPreflightResult preflight;
        await using (var preflightScope = services.CreateAsyncScope())
        {
            var sender = preflightScope.ServiceProvider.GetRequiredService<ISender>();
            preflight = await sender.Send(
                new CheckAdministrativeUserPreflightQuery(action.Value, emailInput.Email!),
                cancellationToken);
        }

        var preflightState = MapPreflightState(action.Value, preflight.State);
        if (preflightState != AdministrativeUserWorkflowState.ReadyForPassword)
        {
            return preflightState;
        }

        var passwordInput = terminal.ReadPasswordConfirmation();
        if (passwordInput.State != AdministrativeUserTerminalState.PasswordCollected)
        {
            return MapTerminalState(passwordInput.State);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return AdministrativeUserWorkflowState.Cancelled;
        }

        // O escopo mutável só começa depois da entrada; a Infrastructure limita o timeout à operação de banco.
        await using var operationScope = services.CreateAsyncScope();
        var operationSender = operationScope.ServiceProvider.GetRequiredService<ISender>();
        var operation = action.Value switch
        {
            AdministrativeUserAction.Create => await operationSender.Send(
                new CreateAdministrativeUserCommand(emailInput.Email!, passwordInput.Password!),
                cancellationToken),
            AdministrativeUserAction.ResetPassword when preflight.CandidateUserId is Guid expectedUserId =>
                await operationSender.Send(
                    new ResetAdministrativeUserPasswordCommand(
                        emailInput.Email!,
                        expectedUserId,
                        passwordInput.Password!),
                    cancellationToken),
            _ => new AdministrativeUserOperationResult(AdministrativeUserOperationState.OperationalFailure)
        };

        return MapOperationState(operation.State);
    }

    // Converte estados do terminal em resultados internos sem transportar texto digitado para o roteador.
    private static AdministrativeUserWorkflowState MapTerminalState(AdministrativeUserTerminalState state) => state switch
    {
        AdministrativeUserTerminalState.Cancelled => AdministrativeUserWorkflowState.Cancelled,
        AdministrativeUserTerminalState.EndOfInput => AdministrativeUserWorkflowState.EndOfInput,
        AdministrativeUserTerminalState.ConfirmationMismatch => AdministrativeUserWorkflowState.PasswordMismatch,
        AdministrativeUserTerminalState.TerminalUnavailable => AdministrativeUserWorkflowState.TerminalUnavailable,
        _ => AdministrativeUserWorkflowState.OperationalFailure
    };

    // Impede que resultados de consulta peçam senha em repetição, ausência, e-mail inválido ou conflito.
    private static AdministrativeUserWorkflowState MapPreflightState(
        AdministrativeUserAction action,
        AdministrativeUserPreflightState state) => state switch
    {
        AdministrativeUserPreflightState.Ready => AdministrativeUserWorkflowState.ReadyForPassword,
        AdministrativeUserPreflightState.AlreadyExists when action == AdministrativeUserAction.Create =>
            AdministrativeUserWorkflowState.AlreadyExists,
        AdministrativeUserPreflightState.NotFound when action == AdministrativeUserAction.ResetPassword =>
            AdministrativeUserWorkflowState.NotFound,
        AdministrativeUserPreflightState.InvalidEmail => AdministrativeUserWorkflowState.InvalidEmail,
        AdministrativeUserPreflightState.Conflict => AdministrativeUserWorkflowState.Conflict,
        _ => AdministrativeUserWorkflowState.OperationalFailure
    };

    // Reduz a operação Identity a estados enumerados para a camada de saída allowlist da tarefa 4.3.
    private static AdministrativeUserWorkflowState MapOperationState(AdministrativeUserOperationState state) => state switch
    {
        AdministrativeUserOperationState.Created => AdministrativeUserWorkflowState.Created,
        AdministrativeUserOperationState.AlreadyExists => AdministrativeUserWorkflowState.AlreadyExists,
        AdministrativeUserOperationState.PasswordReset => AdministrativeUserWorkflowState.PasswordReset,
        AdministrativeUserOperationState.NotFound => AdministrativeUserWorkflowState.NotFound,
        AdministrativeUserOperationState.InvalidEmail => AdministrativeUserWorkflowState.InvalidEmail,
        AdministrativeUserOperationState.PasswordPolicyViolation => AdministrativeUserWorkflowState.PasswordPolicyViolation,
        AdministrativeUserOperationState.Conflict => AdministrativeUserWorkflowState.Conflict,
        _ => AdministrativeUserWorkflowState.OperationalFailure
    };

    // Valida tokens antes de construir o host e a conexão antes de registrar ou resolver persistência.
    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<HostApplicationBuilder>? hostBuilderFactory = null,
        Func<HostApplicationBuilder, string, IHost>? hostFactory = null,
        Func<IAdministrativeUserConsole>? terminalConsoleFactory = null)
    {
        if (!HasValidArguments(args))
        {
            await error.WriteLineAsync(InvalidArgumentsMessage);
            return InvalidArguments;
        }

        try
        {
            var builder = hostBuilderFactory?.Invoke() ?? CreateHostBuilder();
            var configuredConnection = builder.Configuration.GetConnectionString("Finora");
            if (!TryValidateConnectionString(configuredConnection))
            {
                return await WriteWorkflowResultAsync(
                    args[0],
                    AdministrativeUserWorkflowState.InvalidConfiguration,
                    output,
                    error);
            }

            AdministrativeUserWorkflowState workflowState;
            using (var host = hostFactory?.Invoke(builder, configuredConnection!)
                ?? BuildHost(builder, configuredConnection!))
            {
                var terminal = new AdministrativeUserTerminal(
                    terminalConsoleFactory?.Invoke() ?? new SystemAdministrativeUserConsole());
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    cancellation.Cancel();
                };
                Console.CancelKeyPress += cancelHandler;
                try
                {
                    workflowState = await RunWorkflowAsync(
                        args[0],
                        host.Services,
                        terminal,
                        cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    workflowState = AdministrativeUserWorkflowState.Cancelled;
                }
                finally
                {
                    Console.CancelKeyPress -= cancelHandler;
                }
            }

            // A resposta só é emitida depois do descarte do host; inclusive falha de descarte vira falha sanitizada.
            return await WriteWorkflowResultAsync(args[0], workflowState, output, error);
        }
        catch
        {
            // Falhas de composição não expõem valores de configuração, serviços ou exceções.
            await error.WriteLineAsync(OperationalFailureMessage);
            return OperationalFailure;
        }
    }

    // Publica o único texto autorizado e escolhe stdout/stderr exclusivamente pelo código de saída.
    private static async Task<int> WriteWorkflowResultAsync(
        string command,
        AdministrativeUserWorkflowState state,
        TextWriter output,
        TextWriter error)
    {
        var result = MapWorkflowResult(command, state);
        var writer = result.ExitCode == 0 ? output : error;
        await writer.WriteLineAsync(result.Message);
        return result.ExitCode;
    }

    // Mapeia apenas estados enumerados a códigos estáveis e mensagens fixas sem PII, segredos ou exceções.
    internal static AdministrativeUserCommandResult MapWorkflowResult(
        string command,
        AdministrativeUserWorkflowState state) => (command, state) switch
    {
        ("user-create", AdministrativeUserWorkflowState.Created) =>
            new(0, "User admin: operation=create status=created"),
        ("user-create", AdministrativeUserWorkflowState.AlreadyExists) =>
            new(0, "User admin: operation=create status=already_exists"),
        ("user-reset-password", AdministrativeUserWorkflowState.PasswordReset) =>
            new(0, "User admin: operation=reset_password status=password_reset session_revocation=not_implemented"),
        (_, AdministrativeUserWorkflowState.NotFound) => new(3, "User admin: failure=not_found"),
        (_, AdministrativeUserWorkflowState.InvalidArguments) => new(2, InvalidArgumentsMessage),
        (_, AdministrativeUserWorkflowState.InvalidConfiguration) => new(2, InvalidConfigurationMessage),
        (_, AdministrativeUserWorkflowState.InvalidEmail) =>
            new(2, "User admin: failure=invalid_input message=e-mail_invalido"),
        (_, AdministrativeUserWorkflowState.PasswordPolicyViolation) =>
            new(2, "User admin: failure=password_policy message=senha_minimo_6_maiuscula_minuscula_numero_simbolo_caractere_distinto"),
        (_, AdministrativeUserWorkflowState.PasswordMismatch) => new(2, "User admin: failure=password_mismatch"),
        (_, AdministrativeUserWorkflowState.EndOfInput) => new(2, "User admin: failure=invalid_input"),
        (_, AdministrativeUserWorkflowState.TerminalUnavailable) =>
            new(2, "User admin: failure=interactive_terminal_required"),
        (_, AdministrativeUserWorkflowState.Conflict) => new(4, "User admin: failure=conflict"),
        (_, AdministrativeUserWorkflowState.Cancelled) => new(130, "User admin: failure=cancelled"),
        _ => new(OperationalFailure, OperationalFailureMessage)
    };

    // A validação sintática em memória evita iniciar uma conexão ou refletir a string recebida.
    private static bool TryValidateConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(parsed.Host)
                && !string.IsNullOrWhiteSpace(parsed.Database)
                && !string.IsNullOrWhiteSpace(parsed.Username);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

// Mantém os resultados do fluxo independentes de códigos de saída e mensagens finais da tarefa 4.3.
internal enum AdministrativeUserWorkflowState
{
    InvalidArguments,
    InvalidConfiguration,
    ReadyForPassword,
    Created,
    AlreadyExists,
    PasswordReset,
    NotFound,
    InvalidEmail,
    PasswordPolicyViolation,
    PasswordMismatch,
    EndOfInput,
    Cancelled,
    TerminalUnavailable,
    Conflict,
    OperationalFailure
}

// Mantém somente o código e a mensagem fixa que o roteador pode publicar nos streams do processo.
internal readonly record struct AdministrativeUserCommandResult(int ExitCode, string Message);
