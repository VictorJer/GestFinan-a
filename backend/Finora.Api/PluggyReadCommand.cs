using Finora.Application.Pluggy;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Api;

/// <summary>Executa o comando local em um escopo e apresenta somente o resumo permitido.</summary>
internal static class PluggyReadCommand
{
    private const int OperationalFailure = 1;
    private const int InvalidSelection = 2;
    private const int InconclusiveRead = 3;
    private const int ConnectionBlocked = 4;
    private const int Cancelled = 130;
    private static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(120);

    // O modo de comando aceita somente o token não sensível e rejeita qualquer argumento adicional.
    internal static bool HasValidArguments(string[] args) =>
        args.Length == 1 && string.Equals(args[0], "pluggy-read", StringComparison.Ordinal);

    // Reconhece apenas a linha fixa injetada pelo WebApplicationFactory nos testes do host HTTP.
    internal static bool IsApiTestHostInvocation(string[] args) =>
        args.Length == 3
        && args.Any(argument => argument is "--environment=Development"
            or "--environment=Production" or "--environment=Staging")
        && args.Any(argument => argument.StartsWith("--contentRoot=", StringComparison.Ordinal)
            && Directory.Exists(argument["--contentRoot=".Length..]))
        && args.Contains("--applicationName=Finora.Api", StringComparer.Ordinal);

    // Rejeita a linha de comando no stderr com mensagem constante, sem refletir argumentos recebidos.
    internal static async Task<int> RejectArgumentsAsync(string[] args, TextWriter error)
    {
        if (HasValidArguments(args))
        {
            return 0;
        }

        await error.WriteLineAsync("Pluggy read: failure=invalid_arguments");
        return InvalidSelection;
    }

    // O escopo limita dependências da consulta ao processo e mantém a API fora do caminho de execução.
    internal static async Task<int> RunAsync(
        string[] args,
        IServiceProvider rootProvider,
        TextWriter output,
        CancellationToken cancellationToken,
        TimeSpan? totalTimeout = null)
    {
        if (!HasValidArguments(args))
        {
            await output.WriteLineAsync("Pluggy read: failure=invalid_arguments");
            return InvalidSelection;
        }

        try
        {
            using var timeout = new CancellationTokenSource(totalTimeout ?? DefaultTotalTimeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);
            await using var scope = rootProvider.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new PluggyLocalReadQuery(), linkedCancellation.Token);
            await WriteSummaryAsync(output, result);
            return MapExitCode(result.Outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("Pluggy read: status=cancelled");
            return Cancelled;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("Pluggy read: failure=timeout");
            return OperationalFailure;
        }
        catch
        {
            // Falhas imprevistas não devem revelar exceções, URLs ou conteúdo retornado pelo provedor.
            await output.WriteLineAsync("Pluggy read: failure=operational");
            return OperationalFailure;
        }
    }

    // Traduz os resultados tipados para os códigos estáveis definidos no desenho da mudança.
    internal static int MapExitCode(PluggyLocalReadOutcome outcome) => outcome switch
    {
        PluggyLocalReadOutcome.TransactionsRead => 0,
        PluggyLocalReadOutcome.InvalidConfiguration => InvalidSelection,
        PluggyLocalReadOutcome.NoTransactions => InconclusiveRead,
        PluggyLocalReadOutcome.ConnectionBlocked => ConnectionBlocked,
        PluggyLocalReadOutcome.Failed => OperationalFailure,
        _ => OperationalFailure
    };

    // A saída é uma lista permitida: etapa, contagem, continuação e categoria enumerada de falha.
    private static Task WriteSummaryAsync(TextWriter output, PluggyLocalReadResult result)
    {
        var state = result.Outcome switch
        {
            PluggyLocalReadOutcome.TransactionsRead => "transactions_read",
            PluggyLocalReadOutcome.NoTransactions => "inconclusive",
            PluggyLocalReadOutcome.InvalidConfiguration => "invalid_configuration",
            PluggyLocalReadOutcome.ConnectionBlocked => "connection_blocked",
            _ => "failed"
        };
        var failure = result.FailureCategory?.ToString().ToLowerInvariant() ?? "none";
        var missing = result.MissingConfiguration is { Count: > 0 }
            ? string.Join(',', result.MissingConfiguration)
            : "none";
        var consent = result.ConsentExpiryCheck.ToString().ToLowerInvariant();
        return output.WriteLineAsync(
            $"Pluggy read: status={state} stage={result.Stage.ToString().ToLowerInvariant()} " +
            $"transactions={result.TransactionCount} has_more={result.HasMoreTransactions.ToString().ToLowerInvariant()} " +
            $"failure={failure} missing_configuration={missing} consent_expiry={consent}");
    }
}
