using System.Data.Common;
using Finora.Application.AdministrativeConnections;
using Finora.Infrastructure.AdministrativeConnections;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Finora.Api.AdministrativeConnections;

/// <summary>Reconhece a entrada administrativa local antes de qualquer composição do host HTTP.</summary>
internal static class ConnectionLinkLocalCommand
{
    private const string CommandToken = "connection-link-local";
    private const int OperationalFailure = 1;
    private const int InvalidArguments = 2;
    private const int Conflict = 4;
    private const int Cancelled = 130;
    private const string IntegrationDatabase = "finora_integration_test";

    // Detecta o token inicial mesmo com argumentos extras para rejeitá-los neste fluxo isolado.
    internal static bool IsAdministrativeCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CommandToken, StringComparison.Ordinal);

    // A única forma válida é o token literal, sem IDs ou opções posicionais.
    internal static bool HasValidArguments(string[] args) =>
        args.Length == 1 && IsAdministrativeCommand(args);

    // Rejeita argumentos e falta de terminal antes de carregar configuração ou compor qualquer serviço.
    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter error,
        Func<IConnectionLinkLocalConsole>? terminalConsoleFactory = null,
        Func<HostApplicationBuilder>? hostBuilderFactory = null,
        Func<HostApplicationBuilder, string, IHost>? hostFactory = null,
        TextWriter? output = null)
    {
        if (!HasValidArguments(args))
        {
            await error.WriteLineAsync("Connection link local: failure=invalid_arguments");
            return InvalidArguments;
        }

        var console = terminalConsoleFactory?.Invoke() ?? new SystemConnectionLinkLocalConsole();
        if (console.IsInputRedirected || console.IsOutputRedirected)
        {
            await error.WriteLineAsync("Connection link local: failure=interactive_terminal_required");
            return InvalidArguments;
        }

        try
        {
            // O ambiente selecionado fornece a configuração; nenhum provider, conexão ou cliente é resolvido ainda.
            var builder = hostBuilderFactory?.Invoke() ?? CreateHostBuilder();
            var connectionString = builder.Configuration.GetConnectionString("Finora");
            if (!TryValidateConnectionString(connectionString, out var configuredDatabase))
            {
                await error.WriteLineAsync("Connection link local: failure=invalid_configuration");
                return InvalidArguments;
            }

            // Falhas do dispositivo só são classificadas como terminal durante as leituras humanas de destino.
            string? expectedDatabase;
            string? ownershipConfirmation;
            try
            {
                // A comparação usa a configuração carregada sem reproduzir seu conteúdo ou credenciais no prompt.
                console.WriteLine("Connection link local: informe o nome esperado do database:");
                expectedDatabase = console.ReadLine();
                if (string.IsNullOrWhiteSpace(expectedDatabase)
                    || !string.Equals(expectedDatabase, configuredDatabase, StringComparison.Ordinal))
                {
                    await error.WriteLineAsync("Connection link local: failure=invalid_configuration");
                    return InvalidArguments;
                }

                console.WriteLine(
                    "Connection link local: confirme o contêiner/volume local autorizado digitando SIM:");
                ownershipConfirmation = console.ReadLine();
            }
            catch (Exception exception) when (IsTerminalFailure(exception))
            {
                await error.WriteLineAsync("Connection link local: failure=interactive_terminal_required");
                return InvalidArguments;
            }

            if (!string.Equals(ownershipConfirmation, "SIM", StringComparison.Ordinal))
            {
                await error.WriteLineAsync("Connection link local: failure=cancelled");
                return Cancelled;
            }

            // Ctrl+C cancela leituras e trabalho pré-commit; a confirmação terminal trata a tecla como entrada explícita.
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            ConnectionLinkLocalLinkResult? linkResult = null;
            try
            {
                // O host isolado fecha seus escopos antes do prompt e só dispõe o handler mutável após SIM.
                using (var host = hostFactory?.Invoke(builder, connectionString!) ?? BuildHost(builder, connectionString!))
                {
                    ConnectionLinkLocalPreflightResult preflight;
                    await using (var scope = host.Services.CreateAsyncScope())
                    {
                        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                        preflight = await sender.Send(
                            new ConnectionLinkLocalPreflightQuery(),
                            cancellation.Token);
                    }

                    if (preflight.State != ConnectionLinkLocalPreflightState.Ready)
                    {
                        return await WritePreflightFailureAsync(preflight.State, error);
                    }

                    if (preflight.UserId is not Guid userId || string.IsNullOrWhiteSpace(preflight.ExternalItemId))
                    {
                        await error.WriteLineAsync("Connection link local: failure=invalid_configuration");
                        return InvalidArguments;
                    }

                    // A confirmação mostra apenas que as verificações passaram, sem nome, e-mail ou IDs externos.
                    var confirmation = new ConnectionLinkLocalTerminal(console).ConfirmLink();
                    if (confirmation != ConnectionLinkLocalConfirmationState.Confirmed)
                    {
                        return confirmation is ConnectionLinkLocalConfirmationState.Refused
                                or ConnectionLinkLocalConfirmationState.Cancelled
                            ? await WriteCancelledAsync(error)
                            : await WriteTerminalFailureAsync(error);
                    }

                    // A operação mutável só começa depois do prompt; o resultado não é publicado antes do descarte do host.
                    linkResult = await ExecuteLinkAsync(
                        host,
                        userId,
                        preflight.ExternalItemId,
                        cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return await WriteCancelledAsync(error);
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }

            return await WriteLinkResultAsync(linkResult!, output ?? Console.Out, error);
        }
        catch
        {
            // Falhas ao carregar configuração permanecem sanitizadas e não alcançam banco ou provedor.
            await error.WriteLineAsync("Connection link local: failure=operational");
            return OperationalFailure;
        }
    }

    // Compõe somente Identity/persistência e o inspector de item, sem iniciar servidor HTTP ou leitores financeiros.
    internal static IHost BuildHost(HostApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddConnectionLinkLocalPreflight(builder.Configuration, connectionString);
        return builder.Build();
    }

    // A consulta mutável é despachada somente depois de SIM e usa apenas o resultado opaco da pré-consulta.
    private static async Task<ConnectionLinkLocalLinkResult> ExecuteLinkAsync(
        IHost host,
        Guid userId,
        string externalItemId,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(
            new ConnectionLinkLocalLinkCommand(userId, externalItemId),
            cancellationToken);
    }

    // Converte estados da pré-consulta para códigos fixos: cardinalidade conflitante 4, configuração 2 e I/O 1.
    private static Task<int> WritePreflightFailureAsync(
        ConnectionLinkLocalPreflightState state,
        TextWriter error) => state switch
    {
        ConnectionLinkLocalPreflightState.OwnerMissing or ConnectionLinkLocalPreflightState.OwnerAmbiguous =>
            WriteConflictAsync(error),
        ConnectionLinkLocalPreflightState.InvalidConfiguration => WriteInvalidRequestAsync(error),
        _ => WriteOperationalFailureAsync(error)
    };

    // Só estados enumerados chegam aos streams; commit incerto e falhas do provedor permanecem operacionais.
    private static Task<int> WriteLinkResultAsync(
        ConnectionLinkLocalLinkResult result,
        TextWriter output,
        TextWriter error) => result.State switch
    {
        ConnectionLinkLocalLinkState.Linked => WriteLinkedAsync(output),
        ConnectionLinkLocalLinkState.AlreadyLinked => WriteAlreadyLinkedAsync(output),
        ConnectionLinkLocalLinkState.Conflict => WriteConflictAsync(error),
        ConnectionLinkLocalLinkState.InvalidRequest => WriteInvalidRequestAsync(error),
        _ => WriteOperationalFailureAsync(error)
    };

    // Somente o commit concluído produz o estado sanitizado de vínculo criado em stdout.
    private static async Task<int> WriteLinkedAsync(TextWriter output)
    {
        await output.WriteLineAsync("Connection link local: operation=link status=linked");
        return 0;
    }

    // A repetição íntegra é um sucesso distinto e não representa nova importação ou alteração do vínculo.
    private static async Task<int> WriteAlreadyLinkedAsync(TextWriter output)
    {
        await output.WriteLineAsync("Connection link local: operation=link status=already_linked");
        return 0;
    }

    // Colisões são reportadas sem divulgar titular, item externo ou detalhes das constraints.
    private static async Task<int> WriteConflictAsync(TextWriter error)
    {
        await error.WriteLineAsync("Connection link local: failure=conflict");
        return Conflict;
    }

    // Entrada interna inválida não tenta corrigir, substituir ou reinterpretar a identidade recebida.
    private static async Task<int> WriteInvalidRequestAsync(TextWriter error)
    {
        await error.WriteLineAsync("Connection link local: failure=invalid_configuration");
        return InvalidArguments;
    }

    // Falhas operacionais não propagam mensagem de provider, SQL ou exceção ao terminal.
    private static async Task<int> WriteOperationalFailureAsync(TextWriter error)
    {
        await error.WriteLineAsync("Connection link local: failure=operational");
        return OperationalFailure;
    }

    // Recusa explícita e Ctrl+C compartilham o estado estável de cancelamento antes de qualquer mutação.
    private static async Task<int> WriteCancelledAsync(TextWriter error)
    {
        await error.WriteLineAsync("Connection link local: failure=cancelled");
        return Cancelled;
    }

    // EOF e falhas do dispositivo encerram a operação sem confirmar ou repetir a entrada por um canal com eco.
    private static async Task<int> WriteTerminalFailureAsync(TextWriter error)
    {
        await error.WriteLineAsync("Connection link local: failure=interactive_terminal_required");
        return InvalidArguments;
    }

    // Reutiliza a seleção de ambiente segura dos outros comandos; User Secrets só entram se DOTNET_ENVIRONMENT os habilitar.
    internal static HostApplicationBuilder CreateHostBuilder(
        string? environmentName = null,
        bool disableDefaults = false) =>
        AdministrativeUserCommand.CreateHostBuilder(environmentName, disableDefaults);

    // Faz a validação sintática exclusivamente em memória e aceita apenas o destino local descrito no design.
    internal static bool TryValidateConnectionString(
        string? connectionString,
        out string databaseName)
    {
        databaseName = string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            // O builder tipado expõe valores default como opções; o builder ADO confirma se Port veio configurada.
            var suppliedOptions = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };
            if (!suppliedOptions.ContainsKey("Port"))
            {
                return false;
            }

            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal)
                || parsed.Port is < 1 or > 65535
                || string.IsNullOrWhiteSpace(parsed.Database)
                || string.Equals(parsed.Database, IntegrationDatabase, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            databaseName = parsed.Database;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Console.ReadLine pode falhar quando o terminal é fechado ou perde capacidade durante a confirmação.
    private static bool IsTerminalFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or PlatformNotSupportedException;
}
