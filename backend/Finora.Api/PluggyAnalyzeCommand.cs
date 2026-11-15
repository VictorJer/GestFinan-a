using System.Globalization;
using Finora.Application.Pluggy;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Api;

/// <summary>Executa a análise somente pelo comando local e apresenta uma allowlist de métricas agregadas.</summary>
internal static class PluggyAnalyzeCommand
{
    private const int OperationalFailure = 1;
    private const int InvalidArguments = 2;
    private const int EmptySample = 3;
    private const int ConnectionBlocked = 4;
    private const int Cancelled = 130;
    private static readonly HashSet<string> AllowedFieldNames = new(StringComparer.Ordinal)
    {
        "id", "accountId", "description", "amount", "currencyCode", "date", "type", "status",
        "operationType", "installmentNumber", "totalInstallments", "purchaseDate", "billId",
        "billPostDate", "billForecastDate", "paymentType"
    };
    private static readonly HashSet<string> AllowedContractCountNames = BuildAllowedContractCountNames();

    // O comando aceita somente um token fixo; filtros e credenciais permanecem em configuração protegida.
    internal static bool HasValidArguments(string[] args) =>
        args.Length == 1 && string.Equals(args[0], "pluggy-analyze", StringComparison.Ordinal);

    // Argumentos adicionais são rejeitados sem ecoar qualquer conteúdo para stderr.
    internal static async Task<int> RejectArgumentsAsync(string[] args, TextWriter error)
    {
        if (HasValidArguments(args))
        {
            return 0;
        }

        await error.WriteLineAsync("Pluggy analyze: failure=invalid_arguments");
        return InvalidArguments;
    }

    // O escopo limita cliente e API key à execução local; nenhuma rota HTTP financeira é criada.
    internal static async Task<int> RunAsync(
        string[] args,
        IServiceProvider rootProvider,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (!HasValidArguments(args))
        {
            await output.WriteLineAsync("Pluggy analyze: failure=invalid_arguments");
            return InvalidArguments;
        }

        try
        {
            await using var scope = rootProvider.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new PluggyAnalysisQuery(), cancellationToken);
            await WriteSummaryAsync(output, result);
            return MapExitCode(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("Pluggy analyze: status=cancelled");
            return Cancelled;
        }
        catch
        {
            // Erros imprevistos não propagam corpo, URL, exceção ou valores externos.
            await output.WriteLineAsync("Pluggy analyze: failure=operational");
            return OperationalFailure;
        }
    }

    // Mantém códigos D6 estáveis para scripts locais e não trata coleta incompleta como sucesso.
    internal static int MapExitCode(PluggyAnalysisOutcome outcome) => outcome switch
    {
        PluggyAnalysisOutcome.CompletedWithTransactions => 0,
        PluggyAnalysisOutcome.EmptySample => EmptySample,
        PluggyAnalysisOutcome.InvalidConfiguration => InvalidArguments,
        PluggyAnalysisOutcome.ConnectionBlocked => ConnectionBlocked,
        PluggyAnalysisOutcome.Incomplete or PluggyAnalysisOutcome.Failed => OperationalFailure,
        _ => OperationalFailure
    };

    // Cancelamento solicitado pelo titular preserva o código de saída convencional do comando local.
    internal static int MapExitCode(PluggyAnalysisResult result) =>
        result.Failure == PluggyAnalysisFailure.Cancelled ? Cancelled : MapExitCode(result.Outcome);

    // A saída limita campos a nomes estáveis e agrega contagens sem valores, IDs, cursores ou datas individuais.
    private static Task WriteSummaryAsync(TextWriter output, PluggyAnalysisResult result)
    {
        var outcome = result.Outcome.ToString().ToLowerInvariant();
        var failure = result.Failure?.ToString().ToLowerInvariant() ?? "none";
        var available = result.AvailableFieldCounts is null
            ? "none"
            : string.Join(',', result.AvailableFieldCounts
                .Where(pair => AllowedFieldNames.Contains(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value}"));
        var receivedContractCounts = FormatContractCounts(result.ObservedContractCounts);
        var uniqueContractCounts = FormatContractCounts(result.UniqueObservedContractCounts);
        var requestedFrom = result.RequestedDateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none";
        var durationMs = result.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);

        return output.WriteLineAsync(
            $"Pluggy analyze: status={outcome} pages={result.PagesRead} received={result.TransactionsReceived} " +
            $"unique={result.UniqueTransactions} duplicates={result.DuplicateTransactions} " +
            $"duration_ms={durationMs} pagination_completed={result.PaginationCompleted.ToString().ToLowerInvariant()} " +
            $"requested_from={requestedFrom} distinct_months={result.DistinctObservedMonths} " +
            $"future_records={result.FutureTransactions} fields={available} " +
            $"contract_received={receivedContractCounts} contract_unique={uniqueContractCounts} failure={failure}");
    }

    // A allowlist combina somente dimensões internas fixas; nenhuma string externa cria uma chave imprimível.
    private static HashSet<string> BuildAllowedContractCountNames()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "direction_debit", "direction_credit", "direction_other", "sign_positive", "sign_negative", "sign_zero",
            "status_posted", "status_pending", "status_other", "operation_missing", "operation_other",
            "payment_type_single", "payment_type_installment", "payment_type_a_vista", "payment_type_a_prazo",
            "payment_type_missing", "payment_type_other", "installments_absent", "installments_partial",
            "installments_complete_valid", "installments_invalid", "purchase_date_missing", "purchase_date_present",
            "date_valid", "date_missing_or_invalid", "purchase_date_valid", "purchase_date_missing_or_invalid",
            "bill_post_date_valid", "bill_post_date_missing_or_invalid", "bill_forecast_date_valid",
            "bill_forecast_date_missing", "bill_forecast_date_invalid", "nature_purchase", "nature_refund", "nature_bill_payment",
            "nature_fee", "nature_adjustment", "nature_unknown", "date_calendar", "date_instant", "date_ambiguous",
            "date_invalid", "date_timezone_unavailable"
        };
        var operations = new[]
        {
            "pagamento", "pagamento_fatura", "estorno", "tarifa", "cashback",
            "operacoes_credito_contratadas_cartao", "outros", "missing", "other"
        };
        var directions = new[] { "debit", "credit", "other" };
        var signs = new[] { "positive", "negative", "zero" };
        var statuses = new[] { "posted", "pending", "other" };
        foreach (var operation in operations)
        {
            allowed.Add($"operation_{operation}");
            foreach (var direction in directions)
            foreach (var sign in signs)
            foreach (var status in statuses)
            {
                allowed.Add($"cross_operation_{operation}_direction_{direction}_sign_{sign}_status_{status}");
            }
        }

        foreach (var status in statuses)
        foreach (var presence in new[] { "present", "absent_or_null" })
        {
            allowed.Add($"status_{status}_bill_id_{presence}");
        }

        foreach (var installment in new[] { "absent", "partial", "complete_valid", "invalid" })
        foreach (var paymentType in new[] { "single", "installment", "a_vista", "a_prazo", "missing", "other" })
        {
            allowed.Add($"installments_payment_type_{installment}_{paymentType}");
        }

        foreach (var relation in new[]
        {
            "date_purchase_date", "date_bill_post_date", "purchase_date_bill_post_date"
        })
        foreach (var category in new[] { "same_day", "different_day", "missing_or_invalid" })
        {
            allowed.Add($"date_pair_{relation}_{category}");
        }

        foreach (var relation in new[]
        {
            "date_bill_forecast_date", "purchase_date_bill_forecast_date", "bill_post_date_bill_forecast_date"
        })
        foreach (var category in new[] { "same_month", "before_forecast", "after_forecast", "missing_or_invalid" })
        {
            allowed.Add($"date_pair_{relation}_{category}");
        }

        return allowed;
    }

    // Formata apenas contagens cujas chaves estejam na allowlist, inclusive quando o resultado vier de um fake.
    private static string FormatContractCounts(IReadOnlyDictionary<string, int>? counts) => counts is null
        ? "none"
        : string.Join(',', counts
            .Where(pair => AllowedContractCountNames.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{pair.Value}"));
}
