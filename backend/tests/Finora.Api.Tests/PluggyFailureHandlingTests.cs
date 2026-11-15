using System.Net;
using System.Text;
using Finora.Api;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Garante timeouts, cancelamento e erros de transporte categorizados sem expor mensagens externas.
[TestClass]
public sealed class PluggyFailureHandlingTests
{
    // Códigos HTTP de autorização, limite e indisponibilidade são categorias estáveis sem corpo bruto.
    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, PluggyLocalReadFailureCategory.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden, PluggyLocalReadFailureCategory.Forbidden)]
    [DataRow((HttpStatusCode)429, PluggyLocalReadFailureCategory.RateLimited)]
    [DataRow(HttpStatusCode.InternalServerError, PluggyLocalReadFailureCategory.ProviderUnavailable)]
    public async Task HttpFailuresAreMappedWithoutRetryOrProviderBody(
        HttpStatusCode statusCode,
        PluggyLocalReadFailureCategory expected)
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"apiKey\":\"test-key\"}")),
            _ => Task.FromResult(JsonResponse(statusCode, "provider-private-sentinel")));
        using var client = CreateClient(handler);
        Assert.IsTrue((await client.AuthenticateAsync(CancellationToken.None)).Succeeded);

        var result = await client.GetJsonAsync<PluggyItemResponse>("items/item-test", CancellationToken.None);

        Assert.AreEqual(expected, result.FailureCategory);
        Assert.AreEqual(2, handler.RequestCount);
        Assert.IsNull(result.Payload);
    }

    // Erros de rede são categorizados sem propagar a mensagem que pode conter host/detalhes de transporte.
    [TestMethod]
    public async Task TransportFailureIsSanitized()
    {
        var handler = new SequenceHandler(_ =>
            throw new HttpRequestException("transport-private-sentinel"));
        using var client = CreateClient(handler);

        var result = await client.AuthenticateAsync(CancellationToken.None);

        Assert.AreEqual(PluggyLocalReadFailureCategory.Transport, result.FailureCategory);
        Assert.AreEqual(1, handler.RequestCount);
    }

    // Timeout por request é menor apenas no teste; produção mantém o limite fixo de 30 segundos.
    [TestMethod]
    public async Task PerRequestTimeoutReturnsTimeoutCategory()
    {
        var handler = new SequenceHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var client = CreateClient(handler, TimeSpan.FromMilliseconds(25));

        var result = await client.AuthenticateAsync(CancellationToken.None);

        Assert.AreEqual(PluggyLocalReadFailureCategory.Timeout, result.FailureCategory);
        Assert.AreEqual(1, handler.RequestCount);
    }

    // Cancelamento do chamador não é confundido com timeout por chamada nem convertido em sucesso parcial.
    [TestMethod]
    public async Task CallerCancellationPropagatesFromProviderRequest()
    {
        var handler = new SequenceHandler(
            _ => Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"apiKey\":\"test-key\"}")),
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return JsonResponse(HttpStatusCode.OK, "{}");
            });
        using var client = CreateClient(handler);
        Assert.IsTrue((await client.AuthenticateAsync(CancellationToken.None)).Succeeded);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await client.GetJsonAsync<PluggyItemResponse>("items/item-test", cancellation.Token));
    }

    // O limite global usa código operacional; Ctrl+C mantém o código dedicado 130.
    [TestMethod]
    public async Task CommandDistinguishesTotalTimeoutFromCtrlC()
    {
        var services = CreateCommandServices(new HangingReader());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var timeoutOutput = new StringWriter();

        var timeoutCode = await PluggyReadCommand.RunAsync(
            ["pluggy-read"], provider, timeoutOutput, CancellationToken.None,
            TimeSpan.FromMilliseconds(25));

        Assert.AreEqual(1, timeoutCode);
        StringAssert.Contains(timeoutOutput.ToString(), "failure=timeout");
        Assert.IsFalse(timeoutOutput.ToString().Contains("transactions_read", StringComparison.Ordinal));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
        var cancelOutput = new StringWriter();
        var cancelCode = await PluggyReadCommand.RunAsync(
            ["pluggy-read"], provider, cancelOutput, cancellation.Token);

        Assert.AreEqual(130, cancelCode);
        StringAssert.Contains(cancelOutput.ToString(), "status=cancelled");
    }

    // A composição usa o handler Application real e um leitor suspenso que só observa cancelamento.
    private static ServiceCollection CreateCommandServices(IPluggyLocalReader reader)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPluggyLocalReader>(_ => reader);
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(PluggyLocalReadQuery).Assembly));
        return services;
    }

    private static PluggyApiClient CreateClient(SequenceHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler) { BaseAddress = PluggyApiClient.BaseAddress },
            new PluggyOptions("client-id", "client-secret", "item-test", null), timeout);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Simula uma sequência finita de respostas ou falhas e conta chamadas para detectar retries.
    private sealed class SequenceHandler(
        params Func<CancellationToken, Task<HttpResponseMessage>>[] responses) : HttpMessageHandler
    {
        private int _nextResponse;
        public int RequestCount => _nextResponse;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _nextResponse) - 1;
            return responses[index](cancellationToken);
        }
    }

    // Permanece suspenso até timeout/cancelamento para verificar que nenhuma resposta parcial é escrita.
    private sealed class HangingReader : IPluggyLocalReader
    {
        public async Task<PluggyLocalReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new PluggyLocalReadResult(PluggyLocalReadOutcome.TransactionsRead,
                PluggyLocalReadStage.Transactions, TransactionCount: 1);
        }
    }
}
