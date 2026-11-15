using Finora.Application.Pluggy;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita o handler real pela composição MediatR e substitui somente o limite externo Pluggy.
[TestClass]
public sealed class PluggyLocalReadHandlerTests
{
    // A descoberta do assembly Application e o escopo resolvem a query sem cliente HTTP real.
    [TestMethod]
    public async Task SenderDispatchesQueryThroughScopedProviderAndForwardsCancellation()
    {
        var expected = new PluggyLocalReadResult(
            PluggyLocalReadOutcome.TransactionsRead,
            PluggyLocalReadStage.Transactions,
            TransactionCount: 2,
            HasMoreTransactions: true);
        var reader = new FakePluggyLocalReader(expected);
        var services = new ServiceCollection();
        services.AddScoped<IPluggyLocalReader>(_ => reader);
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(PluggyLocalReadQuery).Assembly));

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        using var cancellation = new CancellationTokenSource();

        var actual = await sender.Send(new PluggyLocalReadQuery(), cancellation.Token);

        Assert.AreSame(expected, actual);
        Assert.AreEqual(cancellation.Token, reader.ReceivedCancellationToken);
    }

    // O substituto mantém o teste determinístico e confirma que nenhum transporte externo é necessário.
    private sealed class FakePluggyLocalReader(PluggyLocalReadResult result) : IPluggyLocalReader
    {
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<PluggyLocalReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
