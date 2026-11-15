using System.Net;
using System.Text;
using Finora.Api;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Captura a saída do comando e os logs reais do HttpClientFactory usando respostas inteiramente fictícias.
[TestClass]
public sealed class PluggyPrivacyTests
{
    // O caminho de sucesso não escreve credenciais, API key, IDs, query, descrição ou corpo nos logs/saída.
    [TestMethod]
    public async Task SuccessfulCommandEmitsOnlyAllowlistedSummary()
    {
        var capture = new CapturingLoggerProvider();
        var handler = new SentinelHandler([
            Json(HttpStatusCode.OK, "{\"apiKey\":\"api-key-private-sentinel\"}"),
            Json(HttpStatusCode.OK,
                "{\"status\":\"UPDATED\",\"executionStatus\":\"SUCCESS\",\"consentExpiresAt\":null,\"error\":null}"),
            Json(HttpStatusCode.OK,
                "{\"page\":1,\"total\":1,\"totalPages\":1,\"results\":[{\"id\":\"account-id-private-sentinel\",\"type\":\"CREDIT\",\"subtype\":\"CREDIT_CARD\",\"itemId\":\"item-id-private-sentinel\"}]}"),
            Json(HttpStatusCode.OK,
                "{\"results\":[{\"id\":\"transaction-id-private-sentinel\",\"accountId\":\"account-id-private-sentinel\",\"description\":\"purchase-description-private-sentinel\",\"amount\":9876.54}],\"next\":\"opaque-cursor-private-sentinel\"}")
        ]);
        await using var provider = CreateProvider(capture, handler);
        var output = new StringWriter();

        var exitCode = await PluggyReadCommand.RunAsync(
            ["pluggy-read"], provider, output, CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(output.ToString(), "status=transactions_read");
        StringAssert.Contains(output.ToString(), "transactions=1");
        AssertNoSentinels(output.ToString() + capture.Content);
        Assert.AreEqual(4, handler.RequestCount);
    }

    // Respostas HTTP de falha também produzem somente categoria/etapa e nunca o corpo do provedor.
    [TestMethod]
    public async Task ProviderFailureDoesNotLeakResponseBodyOrRequestMetadata()
    {
        var capture = new CapturingLoggerProvider();
        var handler = new SentinelHandler([
            Json(HttpStatusCode.Unauthorized,
                "provider-body-private-sentinel client-secret-private-sentinel api-key-private-sentinel")
        ]);
        await using var provider = CreateProvider(capture, handler);
        var output = new StringWriter();

        var exitCode = await PluggyReadCommand.RunAsync(
            ["pluggy-read"], provider, output, CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        StringAssert.Contains(output.ToString(), "failure=unauthorized");
        AssertNoSentinels(output.ToString() + capture.Content);
        Assert.AreEqual(1, handler.RequestCount);
    }

    // Exceções de transporte com texto sensível também são reduzidas a uma categoria fixa.
    [TestMethod]
    public async Task TransportExceptionDoesNotLeakExceptionMessageToOutputOrLogs()
    {
        var capture = new CapturingLoggerProvider();
        await using var provider = CreateProvider(capture, new ThrowingHandler());
        var output = new StringWriter();

        var exitCode = await PluggyReadCommand.RunAsync(
            ["pluggy-read"], provider, output, CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        StringAssert.Contains(output.ToString(), "failure=transport");
        AssertNoSentinels(output.ToString() + capture.Content);
    }

    // A análise amplia a sentinela para metadados de cartão, pagamentos e cursor sem imprimir payload bruto.
    [TestMethod]
    public async Task AnalysisSuccessEmitsOnlyAggregateFieldsAndSanitizesNestedMetadata()
    {
        var capture = new CapturingLoggerProvider();
        var handler = new SentinelHandler(AnalysisSuccessResponses());
        await using var provider = CreateProvider(capture, handler);
        var output = new StringWriter();

        var exitCode = await PluggyAnalyzeCommand.RunAsync(
            ["pluggy-analyze"], provider, output, CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(output.ToString(), "status=completedwithtransactions");
        StringAssert.Contains(output.ToString(), "received=1 unique=1");
        AssertNoSentinels(output.ToString() + capture.Content);
        Assert.AreEqual(5, handler.RequestCount);
    }

    // Statuses externos comuns são reduzidos a resultado fixo sem copiar mensagem ou corpo da resposta.
    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    [DataRow((HttpStatusCode)429)]
    [DataRow(HttpStatusCode.InternalServerError)]
    public async Task AnalysisProviderStatusesDoNotLeakExternalResponse(HttpStatusCode status)
    {
        var capture = new CapturingLoggerProvider();
        var handler = new SentinelHandler([
            Json(status, "provider-body-private-sentinel client-secret-private-sentinel " +
                "https://url-private-sentinel.invalid/path transaction-value-private-sentinel")
        ]);
        await using var provider = CreateProvider(capture, handler);
        var output = new StringWriter();

        var exitCode = await PluggyAnalyzeCommand.RunAsync(
            ["pluggy-analyze"], provider, output, CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        AssertNoSentinels(output.ToString() + capture.Content);
        Assert.AreEqual(1, handler.RequestCount);
    }

    // JSON inválido, timeout e exceção inesperada não revelam cursor, URL nem texto do provedor.
    [TestMethod]
    public async Task AnalysisInvalidJsonTimeoutAndUnexpectedFailureStaySanitized()
    {
        var invalidCapture = new CapturingLoggerProvider();
        var invalidResponses = AnalysisSuccessResponses().ToArray();
        invalidResponses[3] = Json(HttpStatusCode.OK, "invalid-json-private-sentinel");
        await using var invalidProvider = CreateProvider(invalidCapture, new SentinelHandler(invalidResponses));
        var invalidOutput = new StringWriter();
        Assert.AreEqual(1, await PluggyAnalyzeCommand.RunAsync(
            ["pluggy-analyze"], invalidProvider, invalidOutput, CancellationToken.None));
        AssertNoSentinels(invalidOutput.ToString() + invalidCapture.Content);

        var timeoutCapture = new CapturingLoggerProvider();
        await using var timeoutProvider = CreateProvider(timeoutCapture, new TimeoutHandler());
        var timeoutOutput = new StringWriter();
        Assert.AreEqual(1, await PluggyAnalyzeCommand.RunAsync(
            ["pluggy-analyze"], timeoutProvider, timeoutOutput, CancellationToken.None));
        AssertNoSentinels(timeoutOutput.ToString() + timeoutCapture.Content);

        var unexpectedCapture = new CapturingLoggerProvider();
        await using var unexpectedProvider = CreateProvider(unexpectedCapture, new UnexpectedHandler());
        var unexpectedOutput = new StringWriter();
        Assert.AreEqual(1, await PluggyAnalyzeCommand.RunAsync(
            ["pluggy-analyze"], unexpectedProvider, unexpectedOutput, CancellationToken.None));
        AssertNoSentinels(unexpectedOutput.ToString() + unexpectedCapture.Content);
    }

    // Cancelamento recebido encerra o comando com código 130 sem expor dados nem exceção.
    [TestMethod]
    public async Task AnalysisCancellationUsesCode130WithoutSensitiveOutput()
    {
        var capture = new CapturingLoggerProvider();
        await using var provider = CreateProvider(capture, new TimeoutHandler());
        var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = await PluggyAnalyzeCommand.RunAsync(
            ["pluggy-analyze"], provider, output, cancellation.Token);

        Assert.AreEqual(130, exitCode);
        AssertNoSentinels(output.ToString() + capture.Content);
    }

    // A composição de teste substitui somente o handler de rede e mantém registro/logging de produção.
    private static ServiceProvider CreateProvider(
        CapturingLoggerProvider capture,
        HttpMessageHandler handler)
    {
        var values = new Dictionary<string, string?>
        {
            ["Pluggy:ClientId"] = "client-id-private-sentinel",
            ["Pluggy:ClientSecret"] = "client-secret-private-sentinel",
            ["Pluggy:ItemId"] = "item-id-private-sentinel"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(capture);
        });
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(PluggyLocalReadQuery).Assembly));
        services.AddPluggyApiClient(configuration);
        services.Configure<HttpClientFactoryOptions>("Pluggy", options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Fixtures usam sentinelas para detectar regressões sem inserir dados reais no repositório.
    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) =>
        new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    // A sequência sintética percorre autenticação, item, cartão e uma página sem dados financeiros reais.
    private static HttpResponseMessage[] AnalysisSuccessResponses()
    {
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-12).ToString("yyyy-MM-dd");
        return
        [
        Json(HttpStatusCode.OK, "{\"apiKey\":\"api-key-private-sentinel\"}"),
        Json(HttpStatusCode.OK,
            "{\"status\":\"UPDATED\",\"executionStatus\":\"SUCCESS\",\"consentExpiresAt\":null,\"error\":null}"),
        Json(HttpStatusCode.OK,
            "{\"page\":1,\"total\":1,\"totalPages\":1,\"results\":[{\"id\":\"account-id-private-sentinel\",\"type\":\"CREDIT\",\"subtype\":\"CREDIT_CARD\",\"itemId\":\"item-id-private-sentinel\"}]}"),
        Json(HttpStatusCode.OK,
                $"{{\"results\":[{{\"id\":\"transaction-id-private-sentinel\",\"accountId\":\"account-id-private-sentinel\",\"description\":\"purchase-description-private-sentinel\",\"amount\":9876.54,\"currencyCode\":\"BRL\",\"date\":\"individual-date-private-sentinel\",\"type\":\"type-private-sentinel\",\"status\":\"status-private-sentinel\",\"operationType\":\"operation-private-sentinel\",\"creditCardMetadata\":{{\"cardNumber\":\"card-number-private-sentinel\",\"installmentNumber\":2,\"totalInstallments\":3,\"purchaseDate\":\"purchase-date-private-sentinel\",\"billId\":\"bill-id-private-sentinel\",\"billPostDate\":\"bill-post-date-private-sentinel\",\"billForecastDate\":\"bill-forecast-date-private-sentinel\",\"paymentType\":\"payment-type-private-sentinel\"}},\"paymentData\":{{\"reason\":\"payment-data-private-sentinel\"}}}}],\"next\":\"?accountId=account-id-private-sentinel&dateFrom={dateFrom}&after=opaque-cursor-private-sentinel\"}}"),
        Json(HttpStatusCode.OK, "{\"results\":[],\"next\":null}")
        ];
    }

    // A lista é explícita para que cada credencial, token, ID, query e descrição seja verificado.
    private static void AssertNoSentinels(string captured)
    {
        foreach (var sentinel in new[]
        {
            "client-id-private-sentinel",
            "client-secret-private-sentinel",
            "api-key-private-sentinel",
            "item-id-private-sentinel",
            "account-id-private-sentinel",
            "transaction-id-private-sentinel",
            "purchase-description-private-sentinel",
            "opaque-cursor-private-sentinel",
            "provider-body-private-sentinel",
            "payment-data-private-sentinel",
            "card-number-private-sentinel",
            "url-private-sentinel",
            "transaction-value-private-sentinel",
            "type-private-sentinel",
            "status-private-sentinel",
            "operation-private-sentinel",
            "individual-date-private-sentinel",
            "purchase-date-private-sentinel",
            "bill-id-private-sentinel",
            "bill-post-date-private-sentinel",
            "bill-forecast-date-private-sentinel",
            "payment-type-private-sentinel",
            "9876.54"
        })
        {
            Assert.IsFalse(captured.Contains(sentinel, StringComparison.Ordinal),
                $"A saída/log capturado continha um campo protegido ({sentinel.Split('-')[0]}).");
        }
    }

    // O handler guarda somente contador e devolve sequência local; nenhuma chamada de rede é aberta.
    private sealed class SentinelHandler(IReadOnlyList<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _requestCount) - 1;
            return Task.FromResult(responses[index]);
        }
    }

    // Simula erro de rede cuja mensagem contém marcador sensível e não deve ser exibida.
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("transport-private-sentinel");
    }

    // Simula timeout externo sem esperar pelo orçamento de produção e sem criar uma conexão real.
    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new OperationCanceledException("timeout-private-sentinel");
    }

    // Exceção não prevista deve ser reduzida pelo comando a uma mensagem operacional genérica.
    private sealed class UnexpectedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("unexpected-private-sentinel");
    }

    // Registra mensagens e escopos estruturados para testar logs mesmo se o provedor mudar de formato.
    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly List<string> _entries = [];
        private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
        public string Content => string.Join('\n', _entries);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

        public void Dispose() { }

        private void Add(string category, string message, Exception? exception, object? state)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> fields
                ? string.Join(' ', fields.Select(field => $"{field.Key}={field.Value}"))
                : state?.ToString();
            var scopes = new StringBuilder();
            _scopeProvider.ForEachScope((scope, builder) => builder.Append(scope).Append(' '), scopes);
            _entries.Add($"{category} {message} {values} {scopes} {exception}");
        }

        // O logger captura todos os níveis para detectar dados que poderiam aparecer em Trace.
        private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                owner._scopeProvider.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Add(category, formatter(state, exception), exception, state);
        }
    }
}
