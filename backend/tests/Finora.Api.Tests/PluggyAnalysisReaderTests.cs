using System.Net;
using System.Text;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita paginação e cobertura com respostas sintéticas, sem rede ou dados da conexão real.
[TestClass]
public sealed class PluggyAnalysisReaderTests
{
    private static readonly DateTimeOffset Reference = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    // A paginação preserva filtros/cursor, conta duplicações por ID e mantém futuros na amostra.
    [TestMethod]
    public async Task ReadsMultiplePagesAndSeparatesReceivedFromUniqueTransactions()
    {
        const string firstTransaction = "{\"id\":\"fixture-1\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":12.34,\"currencyCode\":\"BRL\",\"date\":\"2026-11-15T12:00:00Z\",\"type\":\"DEBIT\",\"status\":\"POSTED\",\"operationType\":\"OUTROS\"}";
        const string futureTransaction = "{\"id\":\"fixture-2\",\"accountId\":\"card-test\",\"description\":\"Fixture futura\",\"amount\":2.00,\"currencyCode\":\"BRL\",\"date\":\"2026-11-16\",\"type\":\"DEBIT\",\"status\":\"PENDING\",\"operationType\":\"OUTROS\"}";
        var next = "?accountId=card-test&dateFrom=2025-11-15&after=opaque%2Fcursor";
        using var rig = CreateReader(
            TransactionsPage(firstTransaction, Json(next)),
            TransactionsPage($"{firstTransaction},{futureTransaction}", "null"));

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);

        Assert.AreEqual(PluggyAnalysisOutcome.CompletedWithTransactions, result.Outcome);
        Assert.AreEqual(2, result.PagesRead);
        Assert.AreEqual(3, result.TransactionsReceived);
        Assert.AreEqual(2, result.UniqueTransactions);
        Assert.AreEqual(1, result.DuplicateTransactions);
        Assert.AreEqual(1, result.FutureTransactions);
        Assert.AreEqual(new DateOnly(2025, 11, 15), result.RequestedDateFrom);
        Assert.AreEqual(1, result.DistinctObservedMonths);
        Assert.IsTrue(result.PaginationCompleted);
        Assert.AreEqual("?accountId=card-test&dateFrom=2025-11-15&after=opaque%2Fcursor",
            rig.RequestUris[4].Query);
    }

    // Cursor final nulo e amostra vazia terminam sem inferir cobertura histórica.
    [TestMethod]
    public async Task NullCursorCompletesAndEmptyResultsRemainInconclusive()
    {
        using var rig = CreateReader(TransactionsPage(string.Empty, "null"));

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);

        Assert.AreEqual(PluggyAnalysisOutcome.EmptySample, result.Outcome);
        Assert.AreEqual(1, result.PagesRead);
        Assert.IsTrue(result.PaginationCompleted);
        Assert.AreEqual(0, result.UniqueTransactions);
        Assert.AreEqual(0, result.DistinctObservedMonths);
    }

    // Meses sem registros não são preenchidos nem contados como cobertura observada.
    [TestMethod]
    public async Task ReportsOnlyDistinctMonthsPresentInFixtures()
    {
        const string january = "{\"id\":\"fixture-jan\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-01-20\"}";
        const string november = "{\"id\":\"fixture-nov\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":2,\"currencyCode\":\"BRL\",\"date\":\"2026-11-01\"}";
        using var rig = CreateReader(TransactionsPage($"{january},{november}", "null"));

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);

        Assert.AreEqual(2, result.DistinctObservedMonths);
        Assert.AreEqual(2, result.UniqueTransactions);
    }

    // Distribuições usam buckets internos estáveis e nunca preservam enums externos desconhecidos.
    [TestMethod]
    public async Task ReportsSanitizedContractCategoryCounts()
    {
        const string debit = "{\"id\":\"fixture-debit\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":4,\"currencyCode\":\"BRL\",\"date\":\"2026-10-01\",\"type\":\"DEBIT\",\"status\":\"POSTED\"}";
        const string payment = "{\"id\":\"fixture-payment\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":-4,\"currencyCode\":\"BRL\",\"date\":\"2026-10-02\",\"type\":\"CREDIT\",\"status\":\"POSTED\",\"operationType\":\"PAGAMENTO_FATURA\"}";
        const string fee = "{\"id\":\"fixture-fee\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":2,\"currencyCode\":\"BRL\",\"date\":\"2026-10-03\",\"type\":\"DEBIT\",\"status\":\"PENDING\",\"operationType\":\"TARIFA\"}";
        const string unknown = "{\"id\":\"fixture-unknown\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":0,\"currencyCode\":\"BRL\",\"date\":\"2026-10-04\",\"type\":\"FUTURE_DIRECTION\",\"status\":\"FUTURE_STATUS\",\"operationType\":\"FUTURE_OPERATION\"}";
        using var rig = CreateReader(TransactionsPage($"{debit},{payment},{fee},{unknown}", "null"));

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);
        var counts = result.ObservedContractCounts!;

        Assert.AreEqual(2, counts["direction_debit"]);
        Assert.AreEqual(1, counts["direction_credit"]);
        Assert.AreEqual(1, counts["direction_other"]);
        Assert.AreEqual(1, counts["operation_pagamento_fatura"]);
        Assert.AreEqual(1, counts["operation_tarifa"]);
        Assert.AreEqual(1, counts["operation_missing"]);
        Assert.AreEqual(1, counts["operation_other"]);
        Assert.AreEqual(1, counts["nature_bill_payment"]);
        Assert.AreEqual(1, counts["nature_fee"]);
        Assert.AreEqual(2, counts["nature_unknown"]);
        Assert.IsFalse(counts.Keys.Any(key => key.Contains("FUTURE", StringComparison.Ordinal)));
    }

    // O adendo agrega as seis relações, metadados e cruzamentos sem confundir ocorrências com IDs únicos.
    [TestMethod]
    public async Task ReportsExpandedCrossTabsAndSeparatesReceivedFromUniqueBuckets()
    {
        const string known = "{\"id\":\"fixture-known\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":5,\"currencyCode\":\"BRL\",\"date\":\"2026-06-10\",\"type\":\"DEBIT\",\"status\":\"POSTED\",\"operationType\":\"PAGAMENTO\",\"creditCardMetadata\":{\"installmentNumber\":2,\"totalInstallments\":3,\"purchaseDate\":\"2026-06-09\",\"billId\":\"private-bill-id\",\"billPostDate\":\"2026-06-10\",\"billForecastDate\":\"2026-07-12\",\"paymentType\":\"SINGLE\"}}";
        const string unexpected = "{\"id\":\"fixture-other\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":-1,\"currencyCode\":\"BRL\",\"date\":\"2026-06-11\",\"type\":\"UNEXPECTED_DIRECTION_SENTINEL\",\"status\":\"UNEXPECTED_STATUS_SENTINEL\",\"operationType\":\"UNEXPECTED_OPERATION_SENTINEL\",\"creditCardMetadata\":{\"billId\":null,\"paymentType\":\"UNEXPECTED_PAYMENT_SENTINEL\"}}";
        var cursor = Json("?accountId=card-test&dateFrom=2025-11-15&after=fixture-cursor");
        using var rig = CreateReader(
            TransactionsPage(known, cursor), TransactionsPage($"{known},{unexpected}", "null"));

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);
        var received = result.ObservedContractCounts!;
        var unique = result.UniqueObservedContractCounts!;

        Assert.AreEqual(2, received["operation_pagamento"]);
        Assert.AreEqual(1, unique["operation_pagamento"]);
        Assert.AreEqual(2, received["cross_operation_pagamento_direction_debit_sign_positive_status_posted"]);
        Assert.AreEqual(1, unique["cross_operation_pagamento_direction_debit_sign_positive_status_posted"]);
        Assert.AreEqual(1, received["cross_operation_other_direction_other_sign_negative_status_other"]);
        Assert.AreEqual(2, received["status_posted_bill_id_present"]);
        Assert.AreEqual(1, unique["status_posted_bill_id_present"]);
        Assert.AreEqual(2, received["payment_type_single"]);
        Assert.AreEqual(1, received["payment_type_other"]);
        Assert.AreEqual(2, received["installments_complete_valid"]);
        Assert.AreEqual(1, received["installments_absent"]);
        Assert.AreEqual(2, received["installments_payment_type_complete_valid_single"]);
        Assert.AreEqual(2, received["date_pair_date_purchase_date_different_day"]);
        Assert.AreEqual(2, received["date_pair_date_bill_post_date_same_day"]);
        Assert.AreEqual(2, received["date_pair_purchase_date_bill_post_date_different_day"]);
        Assert.AreEqual(2, received["date_pair_date_bill_forecast_date_before_forecast"]);
        Assert.AreEqual(2, received["date_pair_purchase_date_bill_forecast_date_before_forecast"]);
        Assert.AreEqual(2, received["date_pair_bill_post_date_bill_forecast_date_before_forecast"]);
        Assert.IsFalse(received.Keys.Any(key => key.Contains("UNEXPECTED", StringComparison.Ordinal)));
        Assert.IsFalse(unique.Keys.Any(key => key.Contains("UNEXPECTED", StringComparison.Ordinal)));
    }

    // Forecast mensal valida yyyy-MM estritamente e compara ano/mês sem inventar um dia.
    [TestMethod]
    public async Task ValidatesBillForecastPeriodsAndComparesAcrossYearBoundaries()
    {
        const string decemberBeforeJanuary = "{\"id\":\"fixture-period-before\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2025-12-31\",\"creditCardMetadata\":{\"billForecastDate\":\"2026-01\"}}";
        const string januaryAfterDecember = "{\"id\":\"fixture-period-after\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\",\"creditCardMetadata\":{\"billForecastDate\":\"2025-12\"}}";
        const string sameMonth = "{\"id\":\"fixture-period-same\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-01-31\",\"creditCardMetadata\":{\"billForecastDate\":\"2026-01\"}}";
        const string invalidMonthZero = "{\"id\":\"fixture-month-zero\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\",\"creditCardMetadata\":{\"billForecastDate\":\"2026-00\"}}";
        const string invalidMonthThirteen = "{\"id\":\"fixture-month-thirteen\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\",\"creditCardMetadata\":{\"billForecastDate\":\"2026-13\"}}";
        const string absentForecast = "{\"id\":\"fixture-forecast-absent\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\",\"creditCardMetadata\":{}}";
        const string calendarForecast = "{\"id\":\"fixture-forecast-calendar\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-02-01\",\"creditCardMetadata\":{\"billForecastDate\":\"2026-02-28\"}}";
        const string offsetForecast = "{\"id\":\"fixture-forecast-offset\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-02-01\",\"creditCardMetadata\":{\"billForecastDate\":\"2026-03-01T01:30:00Z\"}}";
        using var rig = CreateReader(TransactionsPage(
            string.Join(',', decemberBeforeJanuary, januaryAfterDecember, sameMonth,
                invalidMonthZero, invalidMonthThirteen, absentForecast, calendarForecast, offsetForecast),
            "null"));

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);
        var counts = result.ObservedContractCounts!;

        Assert.AreEqual(5, counts["bill_forecast_date_valid"]);
        Assert.AreEqual(1, counts["bill_forecast_date_missing"]);
        Assert.AreEqual(2, counts["bill_forecast_date_invalid"]);
        Assert.AreEqual(1, counts["date_pair_date_bill_forecast_date_before_forecast"]);
        Assert.AreEqual(1, counts["date_pair_date_bill_forecast_date_after_forecast"]);
        Assert.AreEqual(3, counts["date_pair_date_bill_forecast_date_same_month"]);
        Assert.AreEqual(3, counts["date_pair_date_bill_forecast_date_missing_or_invalid"]);
    }

    // Identidade contraditória e vínculo com outra conta interrompem a coleta como incompleta.
    [TestMethod]
    public async Task RejectsContradictoryDuplicatesAndTransactionsFromOtherAccount()
    {
        const string original = "{\"id\":\"fixture-1\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":12.34,\"currencyCode\":\"BRL\",\"date\":\"2026-11-15\"}";
        const string contradiction = "{\"id\":\"fixture-1\",\"accountId\":\"card-test\",\"description\":\"Fixture\",\"amount\":99.99,\"currencyCode\":\"BRL\",\"date\":\"2026-11-15\"}";
        var cursor = Json("?accountId=card-test&dateFrom=2025-11-15&after=opaque");
        using var duplicateRig = CreateReader(
            TransactionsPage(original, cursor), TransactionsPage(contradiction, "null"));
        var duplicateResult = await duplicateRig.Reader.AnalyzeAsync(CancellationToken.None);
        Assert.AreEqual(PluggyAnalysisOutcome.Incomplete, duplicateResult.Outcome);
        Assert.AreEqual(PluggyAnalysisFailure.ContradictoryDuplicate, duplicateResult.Failure);
        Assert.IsFalse(duplicateResult.PaginationCompleted);

        using var foreignRig = CreateReader(TransactionsPage(
            original.Replace("card-test", "other-card", StringComparison.Ordinal), "null"));
        var foreignResult = await foreignRig.Reader.AnalyzeAsync(CancellationToken.None);
        Assert.AreEqual(PluggyAnalysisFailure.InvalidResponse, foreignResult.Failure);
    }

    // Query de início usa o limite de calendário fixado pela referência e não envia dateTo.
    [TestMethod]
    public async Task UsesTwelveMonthDateFromWithoutUpperDateFilter()
    {
        using var rig = CreateReader(TransactionsPage(string.Empty, "null"));

        await rig.Reader.AnalyzeAsync(CancellationToken.None);

        var transactionQuery = rig.RequestUris[3].Query;
        StringAssert.Contains(transactionQuery, "dateFrom=2025-11-15");
        Assert.IsFalse(transactionQuery.Contains("dateTo", StringComparison.Ordinal));
    }

    // Origens externas, caminhos alternativos, filtros ausentes/alterados e parâmetros ambíguos são rejeitados.
    [TestMethod]
    [DataRow("https://evil.example/v2/transactions?accountId=card-test&dateFrom=2025-11-15&after=x")]
    [DataRow("/v2/transactions?accountId=card-test&dateFrom=2025-11-15&after=x")]
    [DataRow("?accountId=card-test&after=x")]
    [DataRow("?dateFrom=2025-11-15&after=x")]
    [DataRow("?accountId=other-card&dateFrom=2025-11-15&after=x")]
    [DataRow("?accountId=card-test&dateFrom=2025-11-14&after=x")]
    [DataRow("?accountId=card-test&dateFrom=2025-11-15&dateTo=2026-01-01&after=x")]
    [DataRow("?accountId=card-test&accountId=card-test&dateFrom=2025-11-15&after=x")]
    [DataRow("?accountId=card-test&dateFrom=2025-11-15&after=x&after=y")]
    [DataRow("?accountId=card-test&dateFrom=2025-11-15&after=x#fragment")]
    public void RejectsUnsafeOrConflictingContinuationQueries(string next)
    {
        Assert.IsFalse(PluggyApiClient.TryBuildAnalysisPath("card-test", new DateOnly(2025, 11, 15), next, out _));
    }

    // Um cursor repetido interrompe antes de enviar uma terceira requisição de transações.
    [TestMethod]
    public async Task RepeatedCursorStopsBeforeAnotherRequest()
    {
        var repeated = Json("?accountId=card-test&dateFrom=2025-11-15&after=same-cursor");
        using var rig = CreateReader(
            TransactionsPage(string.Empty, repeated),
            TransactionsPage(string.Empty, repeated));

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);

        Assert.AreEqual(PluggyAnalysisFailure.RepeatedContinuation, result.Failure);
        Assert.AreEqual(5, rig.RequestUris.Count);
        Assert.IsFalse(result.PaginationCompleted);
    }

    // Continuação externa é rejeitada antes de autenticar um request para o destino indicado.
    [TestMethod]
    public async Task UnsafeContinuationDoesNotProduceAnotherRequest()
    {
        using var rig = CreateReader(TransactionsPage(
            string.Empty,
            Json("https://evil.example/v2/transactions?accountId=card-test&dateFrom=2025-11-15&after=x")));

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);

        Assert.AreEqual(PluggyAnalysisFailure.UnsafeContinuation, result.Failure);
        Assert.AreEqual(4, rig.RequestUris.Count);
        Assert.IsTrue(rig.RequestUris.All(uri => uri.Host == "api.pluggy.ai"));
    }

    // A query aprovada é anexada literalmente ao caminho fixo, sem decodificar/recriar o cursor.
    [TestMethod]
    public void PreservesOpaqueCursorWhenBuildingFixedEndpointPath()
    {
        const string next = "?accountId=card-test&dateFrom=2025-11-15&after=opaque%2Fcursor%3D";

        var valid = PluggyApiClient.TryBuildAnalysisPath(
            "card-test", new DateOnly(2025, 11, 15), next, out var path);

        Assert.IsTrue(valid);
        Assert.AreEqual("v2/transactions" + next, path);
    }

    // O limite de páginas produz coleta incompleta sem solicitar a página excedente.
    [TestMethod]
    public async Task StopsAtOneHundredPagesWithoutRetrying()
    {
        var pages = Enumerable.Range(0, 100)
            .Select(index => TransactionsPage(
                string.Empty,
                Json($"?accountId=card-test&dateFrom=2025-11-15&after=cursor-{index + 1}")))
            .ToArray();
        using var rig = CreateReader(pages);

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);

        Assert.AreEqual(PluggyAnalysisFailure.PageLimit, result.Failure);
        Assert.AreEqual(100, result.PagesRead);
        Assert.AreEqual(103, rig.RequestUris.Count);
        Assert.IsFalse(result.PaginationCompleted);
    }

    // Timeout por chamada é classificado sem incluir URL, resposta ou mensagem externa.
    [TestMethod]
    public async Task PerRequestTimeoutProducesSanitizedIncompleteResult()
    {
        var clock = new ControllableTimeProvider(Reference);
        using var rig = CreateReaderWithOptions(
            requestTimeout: TimeSpan.FromMilliseconds(20),
            totalTimeout: TimeSpan.FromSeconds(2),
            maximumPages: 100,
            responseDelay: null,
            failTransactionPage: null,
            timeProvider: clock,
            blockTransactionUntilCancelled: true,
            transactionPages: [TransactionsPage(string.Empty, "null")]);

        var analysis = rig.Reader.AnalyzeAsync(CancellationToken.None);
        await rig.TransactionRequestStarted.Task;
        clock.Advance(TimeSpan.FromMilliseconds(20));
        var result = await analysis;

        Assert.AreEqual(PluggyAnalysisFailure.RequestTimeout, result.Failure);
        Assert.IsFalse(result.PaginationCompleted);
    }

    // Timeout total e cancelamento do titular interrompem a coleta sem sucesso parcial.
    [TestMethod]
    public async Task TotalTimeoutAndUserCancellationAreDistinguished()
    {
        var totalClock = new ControllableTimeProvider(Reference);
        using var timed = CreateReaderWithOptions(
            requestTimeout: TimeSpan.FromMinutes(1),
            totalTimeout: TimeSpan.FromMilliseconds(20),
            maximumPages: 100,
            responseDelay: null,
            failTransactionPage: null,
            timeProvider: totalClock,
            blockTransactionUntilCancelled: true,
            transactionPages: [TransactionsPage(string.Empty, "null")]);
        var timedAnalysis = timed.Reader.AnalyzeAsync(CancellationToken.None);
        await timed.TransactionRequestStarted.Task;
        totalClock.Advance(TimeSpan.FromMilliseconds(20));
        var timeoutResult = await timedAnalysis;
        Assert.AreEqual(PluggyAnalysisFailure.TotalTimeout, timeoutResult.Failure);

        var userClock = new ControllableTimeProvider(Reference);
        using var cancelled = CreateReaderWithOptions(
            requestTimeout: TimeSpan.FromMinutes(1),
            totalTimeout: TimeSpan.FromMinutes(2),
            maximumPages: 100,
            responseDelay: null,
            failTransactionPage: null,
            timeProvider: userClock,
            blockTransactionUntilCancelled: true,
            transactionPages: [TransactionsPage(string.Empty, "null")]);
        using var cancellation = new CancellationTokenSource();
        var cancelledAnalysis = cancelled.Reader.AnalyzeAsync(cancellation.Token);
        await cancelled.TransactionRequestStarted.Task;
        cancellation.Cancel();
        var cancelledResult = await cancelledAnalysis;
        Assert.AreEqual(PluggyAnalysisFailure.Cancelled, cancelledResult.Failure);
    }

    // Erro HTTP na segunda página mantém o resultado incompleto e não repete a chamada.
    [TestMethod]
    public async Task FailureOnSecondPageDoesNotReportCompleteCollection()
    {
        using var rig = CreateReaderWithOptions(
            requestTimeout: null,
            totalTimeout: null,
            maximumPages: 100,
            responseDelay: null,
            failTransactionPage: 2,
            transactionPages:
            [
                TransactionsPage(string.Empty, Json("?accountId=card-test&dateFrom=2025-11-15&after=cursor-1")),
                TransactionsPage(string.Empty, "null")
            ]);

        var result = await rig.Reader.AnalyzeAsync(CancellationToken.None);

        Assert.AreEqual(PluggyAnalysisFailure.RequestFailure, result.Failure);
        Assert.AreEqual(1, result.PagesRead);
        Assert.AreEqual(5, rig.RequestUris.Count);
        Assert.IsFalse(result.PaginationCompleted);
    }

    // O rig simula autenticação, inspeção de conexão/cartão e respostas paginadas na ordem esperada.
    private static ReaderRig CreateReader(params string[] transactionPages)
        => CreateReaderWithOptions(null, null, 100, null, null, transactionPages: transactionPages);

    // Permite testar limites curtos sem alterar os orçamentos usados em produção.
    private static ReaderRig CreateReaderWithOptions(
        TimeSpan? requestTimeout,
        TimeSpan? totalTimeout,
        int maximumPages,
        TimeSpan? responseDelay,
        int? failTransactionPage,
        TimeProvider? timeProvider = null,
        bool blockTransactionUntilCancelled = false,
        params string[] transactionPages)
    {
        var responses = new Queue<string>([
            "{\"apiKey\":\"synthetic-api-key\"}",
            "{\"status\":\"UPDATED\",\"executionStatus\":\"SUCCESS\",\"consentExpiresAt\":null,\"error\":null}",
            "{\"page\":1,\"total\":1,\"totalPages\":1,\"results\":[{\"id\":\"card-test\",\"type\":\"CREDIT\",\"subtype\":\"CREDIT_CARD\",\"itemId\":\"test-item\"}]}"
        ]);
        foreach (var page in transactionPages)
        {
            responses.Enqueue(page);
        }

        var transactionCalls = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/v2/transactions"
                && ++transactionCalls == failTransactionPage)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }, responseDelay, blockTransactionUntilCancelled);
        var clock = timeProvider ?? new FixedTimeProvider(Reference);
        var client = new PluggyApiClient(
            new HttpClient(handler) { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("client-id", "client-secret", "test-item", null), requestTimeout, clock);
        var reader = new PluggyAnalysisReader(
            new PluggyConnectionInspector(client), client, clock, totalTimeout, maximumPages);
        return new ReaderRig(reader, client, handler.RequestUris, handler.TransactionRequestStarted);
    }

    // Fixtures JSON mantêm o cursor como texto e não contêm valores ou identificadores reais.
    private static string TransactionsPage(string transactions, string next) =>
        $"{{\"results\":[{transactions}],\"next\":{next}}}";

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    // O handler registra somente URIs sintéticas e devolve respostas locais sem abrir sockets.
    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        TimeSpan? responseDelay,
        bool blockTransactionUntilCancelled) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];
        public TaskCompletionSource TransactionRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (request.RequestUri?.AbsolutePath == "/v2/transactions")
            {
                TransactionRequestStarted.TrySetResult();
                if (blockTransactionUntilCancelled)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                if (responseDelay is not null)
                {
                    await Task.Delay(responseDelay.Value, cancellationToken);
                }
            }

            return respond(request);
        }
    }

    // Clock fixo torna o filtro de calendário determinístico sem consultar o ambiente.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Relógio virtual dispara timers apenas quando o teste avança o tempo explicitamente.
    private sealed class ControllableTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ControllableTimer> timers = [];
        private DateTimeOffset utcNow = initialTime;

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ControllableTimer(this, callback, state, dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }
            return timer;
        }

        // Avança o relógio e dispara callbacks vencidos sem depender do escalonador real.
        public void Advance(TimeSpan amount)
        {
            ControllableTimer[] snapshot;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow += amount;
                now = utcNow;
                snapshot = timers.ToArray();
            }

            foreach (var timer in snapshot)
            {
                timer.FireIfDue(now);
            }
        }

        // Timer de disparo único basta para modelar os limites de request e da coleta.
        private sealed class ControllableTimer(
            ControllableTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) : ITimer
        {
            private readonly object timerGate = new();
            private DateTimeOffset? dueAt = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : owner.GetUtcNow() + dueTime;
            private bool disposed;

            public bool Change(TimeSpan nextDueTime, TimeSpan nextPeriod)
            {
                lock (timerGate)
                {
                    if (disposed)
                    {
                        return false;
                    }
                    dueAt = nextDueTime == Timeout.InfiniteTimeSpan
                        ? null
                        : owner.GetUtcNow() + nextDueTime;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (timerGate)
                {
                    disposed = true;
                    dueAt = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                lock (timerGate)
                {
                    if (disposed || dueAt is null || dueAt > now)
                    {
                        return;
                    }
                    dueAt = period == Timeout.InfiniteTimeSpan
                        ? null
                        : dueAt.Value + period;
                }
                callback(state);
            }
        }
    }

    // A API key simulada é descartada ao finalizar cada caso de teste.
    private sealed class ReaderRig(
        PluggyAnalysisReader reader,
        PluggyApiClient client,
        List<Uri> requestUris,
        TaskCompletionSource transactionRequestStarted) : IDisposable
    {
        public PluggyAnalysisReader Reader { get; } = reader;
        public List<Uri> RequestUris { get; } = requestUris;
        public TaskCompletionSource TransactionRequestStarted { get; } = transactionRequestStarted;
        public void Dispose() => client.Dispose();
    }
}
