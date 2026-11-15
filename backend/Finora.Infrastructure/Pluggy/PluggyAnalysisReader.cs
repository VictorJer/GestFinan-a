using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Finora.Application.Pluggy;
using Finora.Domain.Transactions;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Coleta páginas para análise e conserva somente contagens, hashes temporários e campos agregados.</summary>
internal sealed class PluggyAnalysisReader(
    PluggyConnectionInspector connectionInspector,
    PluggyApiClient client,
    TimeProvider? timeProvider = null,
    TimeSpan? maximumDuration = null,
    int maximumPages = 100) : IPluggyAnalysisReader
{
    private const int ProductionMaximumPages = 100;
    private static readonly TimeSpan ProductionMaximumDuration = TimeSpan.FromSeconds(120);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan totalBudget = maximumDuration ?? ProductionMaximumDuration;
    private readonly int pageLimit = maximumPages;

    // A referência temporal e o orçamento são fixados uma vez para que todas as páginas usem o mesmo filtro.
    public async Task<PluggyAnalysisResult> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var reference = clock.GetUtcNow();
        var dateFrom = DateOnly.FromDateTime(reference.UtcDateTime).AddMonths(-12);
        TimeZoneInfo businessTimeZone;
        DateOnly referenceDate;
        try
        {
            businessTimeZone = TimeZoneInfo.FindSystemTimeZoneById(TransactionDateNormalizer.BusinessTimeZoneId);
            referenceDate = TransactionDateNormalizer.GetReferenceDate(reference, businessTimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return new PluggyAnalysisResult(
                PluggyAnalysisOutcome.Failed,
                Duration: Stopwatch.GetElapsedTime(startedAt),
                Failure: PluggyAnalysisFailure.UnsupportedTimeZone);
        }
        catch (InvalidTimeZoneException)
        {
            return new PluggyAnalysisResult(
                PluggyAnalysisOutcome.Failed,
                Duration: Stopwatch.GetElapsedTime(startedAt),
                Failure: PluggyAnalysisFailure.UnsupportedTimeZone);
        }
        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var totalTimeoutTimer = clock.CreateTimer(
            static state => ((CancellationTokenSource)state!).Cancel(),
            totalTimeout,
            totalBudget,
            Timeout.InfiniteTimeSpan);

        PluggyConnectionInspection connection;
        try
        {
            connection = await connectionInspector.InspectAsync(totalTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            return Interrupted(cancellationToken, startedAt);
        }

        if (!connection.IsReady || connection.AccountId is null)
        {
            var outcome = connection.FailureOutcome switch
            {
                PluggyLocalReadOutcome.InvalidConfiguration => PluggyAnalysisOutcome.InvalidConfiguration,
                PluggyLocalReadOutcome.ConnectionBlocked => PluggyAnalysisOutcome.ConnectionBlocked,
                _ => PluggyAnalysisOutcome.Failed
            };
            return new PluggyAnalysisResult(outcome, Duration: Stopwatch.GetElapsedTime(startedAt));
        }

        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var uniqueTransactions = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var observedMonths = new HashSet<int>();
        var availableFields = new Dictionary<string, int>(StringComparer.Ordinal);
        var observedContractCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var uniqueContractCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var pagesRead = 0;
        var received = 0;
        var duplicates = 0;
        var future = 0;
        string? next = null;

        // Cada página é validada e descartada antes da próxima; nenhum payload bruto entra no resultado.
        while (true)
        {
            if (pagesRead >= pageLimit)
            {
                return Incomplete(PluggyAnalysisFailure.PageLimit);
            }

            PluggyApiResponse<PluggyAnalysisTransactionsPage> response;
            try
            {
                if (!PluggyApiClient.TryBuildAnalysisPath(connection.AccountId, dateFrom, next, out _))
                {
                    return Incomplete(PluggyAnalysisFailure.UnsafeContinuation);
                }

                response = await client.GetAnalysisTransactionsPageAsync(
                    connection.AccountId,
                    dateFrom,
                    next,
                    totalTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                return Interrupted(cancellationToken, startedAt, pagesRead, received, uniqueTransactions.Count, duplicates);
            }

            if (response.Payload is null)
            {
                return Incomplete(response.FailureCategory == PluggyLocalReadFailureCategory.Timeout
                    ? PluggyAnalysisFailure.RequestTimeout
                    : PluggyAnalysisFailure.RequestFailure);
            }

            var page = response.Payload;
            if (page.Results is null || !TryGetNext(page.Next, out next))
            {
                return Incomplete(PluggyAnalysisFailure.InvalidResponse);
            }

            pagesRead++;
            foreach (var dto in page.Results)
            {
                if (dto is null || !dto.TryToContract(out var transaction) || transaction is null
                    || !string.Equals(transaction.AccountId, connection.AccountId, StringComparison.Ordinal))
                {
                    return Incomplete(PluggyAnalysisFailure.InvalidResponse);
                }

                received++;
                CountAvailableFields(transaction, availableFields);
                var normalizedDate = TransactionDateNormalizer.Normalize(transaction.OriginalDate, businessTimeZone);
                var contractFactKeys = GetContractFactKeys(transaction, normalizedDate, businessTimeZone);
                AddContractFacts(contractFactKeys, observedContractCounts);
                if (normalizedDate.TransactionDate is { } transactionDate && transactionDate > referenceDate)
                {
                    future++;
                }

                if (normalizedDate.TransactionDate is { } observedDate)
                {
                    observedMonths.Add(observedDate.Year * 100 + observedDate.Month);
                }

                var fingerprint = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(transaction));
                if (uniqueTransactions.TryGetValue(transaction.ExternalId, out var prior))
                {
                    if (!CryptographicOperations.FixedTimeEquals(prior, fingerprint))
                    {
                        return Incomplete(PluggyAnalysisFailure.ContradictoryDuplicate);
                    }

                    duplicates++;
                }
                else
                {
                    uniqueTransactions.Add(transaction.ExternalId, fingerprint);
                    AddContractFacts(contractFactKeys, uniqueContractCounts);
                }
            }

            if (next is null)
            {
                return new PluggyAnalysisResult(
                    received == 0 ? PluggyAnalysisOutcome.EmptySample : PluggyAnalysisOutcome.CompletedWithTransactions,
                    pagesRead,
                    received,
                    uniqueTransactions.Count,
                    duplicates,
                    Stopwatch.GetElapsedTime(startedAt),
                    PaginationCompleted: true,
                    future,
                    new Dictionary<string, int>(availableFields, StringComparer.Ordinal),
                    RequestedDateFrom: dateFrom,
                    DistinctObservedMonths: observedMonths.Count,
                    ObservedContractCounts: new Dictionary<string, int>(observedContractCounts, StringComparer.Ordinal),
                    UniqueObservedContractCounts: new Dictionary<string, int>(uniqueContractCounts, StringComparer.Ordinal));
            }

            if (!seenCursors.Add(next))
            {
                return Incomplete(PluggyAnalysisFailure.RepeatedContinuation);
            }
        }

        // Converte falhas em resultados incompletos sem devolver IDs, valores ou mensagens externas.
        PluggyAnalysisResult Incomplete(PluggyAnalysisFailure failure) => new(
            PluggyAnalysisOutcome.Incomplete,
            pagesRead,
            received,
            uniqueTransactions.Count,
            duplicates,
            Stopwatch.GetElapsedTime(startedAt),
            PaginationCompleted: false,
            future,
            new Dictionary<string, int>(availableFields, StringComparer.Ordinal),
            failure,
            dateFrom,
            observedMonths.Count,
            new Dictionary<string, int>(observedContractCounts, StringComparer.Ordinal),
            new Dictionary<string, int>(uniqueContractCounts, StringComparer.Ordinal));
    }

    // Trata cancelamento do titular e limite total como resultados distintos, mantendo só métricas agregadas.
    private static PluggyAnalysisResult Interrupted(
        CancellationToken userToken,
        long startedAt,
        int pagesRead = 0,
        int received = 0,
        int unique = 0,
        int duplicates = 0) => new(
            PluggyAnalysisOutcome.Incomplete,
            pagesRead,
            received,
            unique,
            duplicates,
            Stopwatch.GetElapsedTime(startedAt),
            PaginationCompleted: false,
            Failure: userToken.IsCancellationRequested
                ? PluggyAnalysisFailure.Cancelled
                : PluggyAnalysisFailure.TotalTimeout);

    // `next` precisa ser nulo ou uma query não vazia; a validação de origem/filtros ocorre no cliente fixo.
    private static bool TryGetNext(JsonElement value, out string? next)
    {
        next = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        next = value.GetString();
        return !string.IsNullOrWhiteSpace(next);
    }

    // Conta apenas disponibilidade por campo; conteúdo individual não é acumulado no relatório.
    private static void CountAvailableFields(PluggyTransactionData transaction, Dictionary<string, int> counts)
    {
        Add("id", !string.IsNullOrWhiteSpace(transaction.ExternalId));
        Add("accountId", !string.IsNullOrWhiteSpace(transaction.AccountId));
        Add("description", !string.IsNullOrWhiteSpace(transaction.Description));
        Add("amount", true);
        Add("currencyCode", !string.IsNullOrWhiteSpace(transaction.CurrencyCode));
        Add("date", !string.IsNullOrWhiteSpace(transaction.OriginalDate));
        Add("type", transaction.Type is not null);
        Add("status", transaction.Status is not null);
        Add("operationType", transaction.OperationType is not null);
        Add("installmentNumber", transaction.InstallmentNumber is not null);
        Add("totalInstallments", transaction.TotalInstallments is not null);
        Add("purchaseDate", transaction.PurchaseDate is not null);
        Add("billId", transaction.BillId is not null);
        Add("billPostDate", transaction.BillPostDate is not null);
        Add("billForecastDate", transaction.BillForecastDate is not null);
        Add("paymentType", transaction.PaymentType is not null);

        // A allowlist de nomes permite exibir cobertura agregada sem revelar valores externos.
        void Add(string name, bool present)
        {
            if (present)
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }
    }

    // Agrupa enumerações externas em categorias internas fixas e nunca registra valores desconhecidos.
    private static IReadOnlyList<string> GetContractFactKeys(
        PluggyTransactionData transaction,
        TransactionDateResult normalizedDate,
        TimeZoneInfo businessTimeZone)
    {
        var keys = new List<string>();
        var direction = transaction.Type switch
        {
            "DEBIT" => "debit",
            "CREDIT" => "credit",
            _ => "other"
        };
        var sign = transaction.Amount switch
        {
            > 0 => "positive",
            < 0 => "negative",
            _ => "zero"
        };
        Add($"direction_{direction}");
        Add($"sign_{sign}");

        var status = transaction.Status switch
        {
            "POSTED" => "posted",
            "PENDING" => "pending",
            _ => "other"
        };
        Add($"status_{status}");

        var operation = transaction.OperationType switch
        {
            "PAGAMENTO" => "pagamento",
            "PAGAMENTO_FATURA" => "pagamento_fatura",
            "ESTORNO" => "estorno",
            "TARIFA" => "tarifa",
            "CASHBACK" => "cashback",
            "OPERACOES_CREDITO_CONTRATADAS_CARTAO" => "operacoes_credito_contratadas_cartao",
            "OUTROS" => "outros",
            null or "" => "missing",
            _ => "other"
        };
        Add($"operation_{operation}");
        Add($"cross_operation_{operation}_direction_{direction}_sign_{sign}_status_{status}");

        var paymentType = transaction.PaymentType switch
        {
            "SINGLE" => "single",
            "INSTALLMENT" => "installment",
            "A_VISTA" => "a_vista",
            "A_PRAZO" => "a_prazo",
            null or "" => "missing",
            _ => "other"
        };
        Add($"payment_type_{paymentType}");

        var installment = (transaction.InstallmentNumber, transaction.TotalInstallments) switch
        {
            (null, null) => "absent",
            (null, not null) or (not null, null) => "partial",
            (int number, int total) when number > 0 && total > 0 && number <= total => "complete_valid",
            _ => "invalid"
        };
        Add($"installments_{installment}");
        Add($"installments_payment_type_{installment}_{paymentType}");

        Add(transaction.PurchaseDate is null or "" ? "purchase_date_missing" : "purchase_date_present");
        AddDateValidity("date", transaction.OriginalDate, businessTimeZone);
        AddDateValidity("purchase_date", transaction.PurchaseDate, businessTimeZone);
        AddDateValidity("bill_post_date", transaction.BillPostDate, businessTimeZone);
        AddBillForecastValidity(transaction.BillForecastDate);
        Add($"status_{status}_bill_id_{(!string.IsNullOrWhiteSpace(transaction.BillId) ? "present" : "absent_or_null")}");

        AddDateRelation("date", transaction.OriginalDate, "purchase_date", transaction.PurchaseDate, false);
        AddDateRelation("date", transaction.OriginalDate, "bill_post_date", transaction.BillPostDate, false);
        AddDateRelation("purchase_date", transaction.PurchaseDate, "bill_post_date", transaction.BillPostDate, false);
        AddDateRelation("date", transaction.OriginalDate, "bill_forecast_date", transaction.BillForecastDate, true);
        AddDateRelation("purchase_date", transaction.PurchaseDate, "bill_forecast_date", transaction.BillForecastDate, true);
        AddDateRelation("bill_post_date", transaction.BillPostDate, "bill_forecast_date", transaction.BillForecastDate, true);

        var nature = TransactionNatureClassifier.Classify(PluggyTransactionMapper.ToFacts(transaction));
        Add(nature.Nature switch
        {
            TransactionNature.BillPayment => "nature_bill_payment",
            TransactionNature.Refund => "nature_refund",
            TransactionNature.Fee => "nature_fee",
            TransactionNature.Adjustment => "nature_adjustment",
            TransactionNature.Purchase => "nature_purchase",
            _ => "nature_unknown"
        });

        Add(normalizedDate.TransactionDate is not null
            ? transaction.OriginalDate.Length == 10 ? "date_calendar" : "date_instant"
            : normalizedDate.ExclusionReason switch
            {
                TransactionExclusionReason.MissingDateOffset => "date_ambiguous",
                TransactionExclusionReason.TimeZoneUnavailable => "date_timezone_unavailable",
                _ => "date_invalid"
            });

        // Apenas chaves produzidas pelos mapeamentos acima podem chegar aos agregados.
        return keys;

        void Add(string key) => keys.Add(key);

        void AddDateValidity(string field, string? value, TimeZoneInfo zone)
        {
            var parsed = TransactionDateNormalizer.Normalize(value, zone);
            Add($"{field}_{(parsed.TransactionDate is null ? "missing_or_invalid" : "valid")}");
        }

        // O forecast pode ser período mensal ou uma data/instante já aceito pelo contrato temporal.
        void AddBillForecastValidity(string? value)
        {
            var bucket = value is null
                ? "missing"
                : TryParseBillForecastMonth(value, businessTimeZone, out _) ? "valid" : "invalid";
            Add($"bill_forecast_date_{bucket}");
        }

        void AddDateRelation(string leftName, string? left, string rightName, string? right, bool compareMonth)
        {
            var relation = "missing_or_invalid";
            if (compareMonth)
            {
                var leftDate = TransactionDateNormalizer.Normalize(left, businessTimeZone).TransactionDate;
                if (leftDate is not null
                    && TryParseBillForecastMonth(right, businessTimeZone, out var forecastMonth))
                {
                    var leftMonth = (leftDate.Value.Year, leftDate.Value.Month);
                    relation = leftMonth == forecastMonth
                        ? "same_month"
                        : leftMonth.CompareTo(forecastMonth) < 0 ? "before_forecast" : "after_forecast";
                }
            }
            else
            {
                var leftDate = TransactionDateNormalizer.Normalize(left, businessTimeZone).TransactionDate;
                var rightDate = TransactionDateNormalizer.Normalize(right, businessTimeZone).TransactionDate;
                relation = leftDate is null || rightDate is null
                    ? "missing_or_invalid"
                    : leftDate.Value == rightDate.Value ? "same_day" : "different_day";
            }
            Add($"date_pair_{leftName}_{rightName}_{relation}");
        }
    }

    // Analisa yyyy-MM sem fabricar dia; formatos de data/instante já aceitos seguem o contrato existente.
    private static bool TryParseBillForecastMonth(
        string? value,
        TimeZoneInfo businessTimeZone,
        out (int Year, int Month) month)
    {
        month = default;
        if (value is null)
        {
            return false;
        }

        if (value.Length == 7
            && value[4] == '-'
            && IsAsciiDigits(value.AsSpan(0, 4))
            && IsAsciiDigits(value.AsSpan(5, 2))
            && int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(value.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var numericMonth)
            && year is >= 1 and <= 9999
            && numericMonth is >= 1 and <= 12)
        {
            month = (year, numericMonth);
            return true;
        }

        var normalized = TransactionDateNormalizer.Normalize(value, businessTimeZone).TransactionDate;
        if (normalized is null)
        {
            return false;
        }

        month = (normalized.Value.Year, normalized.Value.Month);
        return true;
    }

    // O formato aprovado usa dígitos ASCII literais, evitando aceitar variantes Unicode como yyyy-MM.
    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    // Incrementa uma ocorrência em cada bucket fixo derivado da transação, sem reter seus campos externos.
    private static void AddContractFacts(IEnumerable<string> keys, Dictionary<string, int> counts)
    {
        foreach (var key in keys)
        {
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
    }
}
