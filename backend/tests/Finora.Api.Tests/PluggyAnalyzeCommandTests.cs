using Finora.Api;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida roteamento local, despacho MediatR, códigos D6 e allowlist de saída da análise.
[TestClass]
public sealed class PluggyAnalyzeCommandTests
{
    // Um token exato é aceito; argumento extra ou valor sensível nunca é refletido.
    [TestMethod]
    public async Task RejectsExtraArgumentsWithoutEcho()
    {
        var error = new StringWriter();
        var args = new[] { "pluggy-analyze", "private-sentinel" };

        var exitCode = await PluggyAnalyzeCommand.RejectArgumentsAsync(args, error);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "failure=invalid_arguments");
        Assert.IsFalse(error.ToString().Contains("private-sentinel", StringComparison.Ordinal));
    }

    // O query é despachado pelo ISender e o resumo apresenta somente nomes/contagens autorizados.
    [TestMethod]
    public async Task DispatchesQueryAndWritesSanitizedSummary()
    {
        var reader = new FakeAnalysisReader(new PluggyAnalysisResult(
            PluggyAnalysisOutcome.CompletedWithTransactions,
            PagesRead: 2,
            TransactionsReceived: 5,
            UniqueTransactions: 4,
            DuplicateTransactions: 1,
            Duration: TimeSpan.FromMilliseconds(123),
            PaginationCompleted: true,
            FutureTransactions: 1,
            AvailableFieldCounts: new Dictionary<string, int>
            {
                ["currencyCode"] = 5,
                ["private-sentinel"] = 99
            },
            RequestedDateFrom: new DateOnly(2025, 11, 15),
            DistinctObservedMonths: 2,
            ObservedContractCounts: new Dictionary<string, int>
            {
                ["status_posted"] = 5,
                ["private-sentinel"] = 99
            },
            UniqueObservedContractCounts: new Dictionary<string, int>
            {
                ["operation_pagamento"] = 4,
                ["date_pair_date_purchase_date_same_day"] = 2,
                ["private-sentinel"] = 99
            }));
        var services = new ServiceCollection();
        services.AddScoped<IPluggyAnalysisReader>(_ => reader);
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(PluggyAnalysisQuery).Assembly));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var output = new StringWriter();

        var exitCode = await PluggyAnalyzeCommand.RunAsync(
            ["pluggy-analyze"], provider, output, CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(output.ToString(), "pages=2 received=5 unique=4 duplicates=1");
        StringAssert.Contains(output.ToString(), "currencyCode:5");
        StringAssert.Contains(output.ToString(), "distinct_months=2");
        StringAssert.Contains(output.ToString(), "contract_received=status_posted:5");
        StringAssert.Contains(output.ToString(), "contract_unique=date_pair_date_purchase_date_same_day:2,operation_pagamento:4");
        Assert.IsFalse(output.ToString().Contains("private-sentinel", StringComparison.Ordinal));
    }

    // Configuração ausente termina antes de autenticação HTTP, ainda que o handler real seja despachado.
    [TestMethod]
    public async Task MissingConfigurationBlocksBeforeNetworkRequest()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(PluggyAnalysisQuery).Assembly));
        services.AddPluggyApiClient(configuration);
        var networkGuard = new NetworkGuardHandler();
        services.AddHttpClient("Pluggy").ConfigurePrimaryHttpMessageHandler(() => networkGuard);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var output = new StringWriter();

        var exitCode = await PluggyAnalyzeCommand.RunAsync(
            ["pluggy-analyze"], provider, output, CancellationToken.None);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(output.ToString(), "invalidconfiguration");
        StringAssert.Contains(output.ToString(), "pages=0");
        Assert.AreEqual(0, networkGuard.RequestCount);
    }

    // Códigos estáveis distinguem coleta, vazio, configuração, conexão e falha incompleta.
    [TestMethod]
    [DataRow(PluggyAnalysisOutcome.CompletedWithTransactions, 0)]
    [DataRow(PluggyAnalysisOutcome.EmptySample, 3)]
    [DataRow(PluggyAnalysisOutcome.InvalidConfiguration, 2)]
    [DataRow(PluggyAnalysisOutcome.ConnectionBlocked, 4)]
    [DataRow(PluggyAnalysisOutcome.Incomplete, 1)]
    [DataRow(PluggyAnalysisOutcome.Failed, 1)]
    public void MapsAnalysisOutcomesToDocumentedExitCodes(PluggyAnalysisOutcome outcome, int expected)
    {
        Assert.AreEqual(expected, PluggyAnalyzeCommand.MapExitCode(outcome));
    }

    // Cancelamento explícito do titular usa 130 mesmo com coleta reportada como incompleta.
    [TestMethod]
    public void MapsUserCancellationToExitCode130()
    {
        var result = new PluggyAnalysisResult(
            PluggyAnalysisOutcome.Incomplete,
            Failure: PluggyAnalysisFailure.Cancelled);

        Assert.AreEqual(130, PluggyAnalyzeCommand.MapExitCode(result));
    }

    // Fake substitui apenas o limite externo e preserva o handler de Application real.
    private sealed class FakeAnalysisReader(PluggyAnalysisResult result) : IPluggyAnalysisReader
    {
        public Task<PluggyAnalysisResult> AnalyzeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    // O guard falha o teste se qualquer chamada HTTP ocorrer e nunca encaminha tráfego externo.
    private sealed class NetworkGuardHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("Network access is forbidden in this test.");
        }
    }
}
