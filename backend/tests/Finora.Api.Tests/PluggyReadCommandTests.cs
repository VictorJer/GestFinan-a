using Finora.Api;
using Finora.Application.Pluggy;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confere o roteamento local e os códigos de saída sem iniciar listener ou chamar a Pluggy.
[TestClass]
public sealed class PluggyReadCommandTests
{
    // Um token desconhecido ou argumentos extras falham antes de qualquer resolução de serviço.
    [TestMethod]
    [DataRow("unknown")]
    [DataRow("pluggy-read --item-id sensitive")]
    public async Task UnknownCommandOrExtraArgumentsAreRejected(string rawArguments)
    {
        var output = new StringWriter();
        await using var provider = new ServiceCollection().BuildServiceProvider();

        var exitCode = await PluggyReadCommand.RunAsync(
            rawArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            provider,
            output,
            CancellationToken.None);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(output.ToString(), "failure=invalid_arguments");
        Assert.IsFalse(output.ToString().Contains("sensitive", StringComparison.Ordinal));
    }

    // A validação do stderr recebe argumentos com sentinela e confirma que nenhum deles é refletido.
    [TestMethod]
    public async Task RejectedArgumentsNeverAppearOnStandardError()
    {
        var args = new[] { "pluggy-read", "client-secret-private-sentinel" };
        var standardError = new StringWriter();

        var exitCode = await PluggyReadCommand.RejectArgumentsAsync(args, standardError);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(standardError.ToString(), "failure=invalid_arguments");
        Assert.IsFalse(standardError.ToString().Contains("client-secret-private-sentinel", StringComparison.Ordinal));
    }

    // Flags de configuração não podem contornar o roteador e alcançar o host HTTP como segredos.
    [TestMethod]
    public void OnlyExactLocalCommandOrTestHostInvocationAreRecognized()
    {
        Assert.IsTrue(PluggyReadCommand.IsApiTestHostInvocation(
            ["--environment=Development", "--contentRoot=.", "--applicationName=Finora.Api"]));
        Assert.IsFalse(PluggyReadCommand.IsApiTestHostInvocation(
            ["--environment=Development", "--contentRoot=.", "--applicationName=Finora.Api",
                "--Pluggy:ClientSecret=private-sentinel"]));
        Assert.IsFalse(PluggyReadCommand.IsApiTestHostInvocation(
            ["--Pluggy:ClientSecret=private-sentinel"]));
    }

    // Cada resultado definido pelo desenho mantém seu exit code estável para scripts locais.
    [TestMethod]
    [DataRow(PluggyLocalReadOutcome.TransactionsRead, 0)]
    [DataRow(PluggyLocalReadOutcome.Failed, 1)]
    [DataRow(PluggyLocalReadOutcome.InvalidConfiguration, 2)]
    [DataRow(PluggyLocalReadOutcome.NoTransactions, 3)]
    [DataRow(PluggyLocalReadOutcome.ConnectionBlocked, 4)]
    public void OutcomeMapsToDocumentedExitCode(PluggyLocalReadOutcome outcome, int expected)
    {
        Assert.AreEqual(expected, PluggyReadCommand.MapExitCode(outcome));
    }

    // O comando conhecido resolve o handler por escopo e escreve exclusivamente o resumo tipado.
    [TestMethod]
    public async Task KnownCommandSendsQueryAndWritesSanitizedSummary()
    {
        var reader = new FakePluggyLocalReader(new PluggyLocalReadResult(
            PluggyLocalReadOutcome.TransactionsRead,
            PluggyLocalReadStage.Transactions,
            TransactionCount: 3,
            HasMoreTransactions: false));
        var services = new ServiceCollection();
        services.AddScoped<IPluggyLocalReader>(_ => reader);
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(PluggyLocalReadQuery).Assembly));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var output = new StringWriter();

        var exitCode = await PluggyReadCommand.RunAsync(
            ["pluggy-read"], provider, output, CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(output.ToString(), "status=transactions_read");
        StringAssert.Contains(output.ToString(), "transactions=3");
        StringAssert.Contains(output.ToString(), "has_more=false");
        Assert.IsFalse(output.ToString().Contains("private-description", StringComparison.Ordinal));
    }

    // O teste usa somente resultado fabricado; nenhuma resposta externa ou dado real é retido.
    private sealed class FakePluggyLocalReader(PluggyLocalReadResult result) : IPluggyLocalReader
    {
        public Task<PluggyLocalReadResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
