using Finora.Application.FinancialData;
using Finora.Domain.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica filtros e limites de propriedade do caso de uso sem banco, HTTP ou provedor externo.
[TestClass]
public sealed class GetTransactionDashboardQueryHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("d082609b-d6bd-41c5-9088-2252950fae21");
    private static readonly Guid ConnectionId = Guid.Parse("58d2f4c8-13f8-41f9-a27c-e06c7f2417a9");
    private static readonly Guid CardId = Guid.Parse("fa03c394-6cc8-441f-a733-0532c86d84bd");
    private static readonly Guid OtherCardId = Guid.Parse("cb2475bc-6da6-4d16-9350-4e28f47d13e2");
    private static readonly Guid MissingCardId = Guid.Parse("8c2dff48-0b16-4ff1-a182-6f898b09f9bc");
    private static readonly Guid CategoryId = Guid.Parse("923ad8bb-9fd2-4e41-9cda-26b0e7bf61ce");
    private static readonly TransactionMovementDashboardSummary EmptySummary = new(
        0,
        Array.Empty<TransactionNatureCountSummary>(),
        Array.Empty<TransactionSituationCountSummary>(),
        new TransactionImportCoverageSummary(null, null, null));

    // ISO, filtros combinados y caracteres curinga chegam ao reader como critérios validados e literais.
    [TestMethod]
    public async Task ReusesListFilterValidationWithoutExposingPagination()
    {
        var connectionReader = new FakeConnectionReader(
            new CurrentSyncConnectionResult(CurrentSyncConnectionStatus.Found, ConnectionId));
        var cardReader = new FakeCardOwnershipReader(isOwned: true);
        var dashboardReader = new FakeDashboardReader();
        var handler = CreateHandler(connectionReader, cardReader, dashboardReader);

        var result = await handler.Handle(
            new GetTransactionDashboardQuery(
                UserId,
                CardId.ToString("D"),
                CategoryId.ToString("D"),
                Situation: "Pending",
                From: "2026-11-01",
                ToExclusive: "2026-12-01",
                Search: "literal %_ \\ description"),
            CancellationToken.None);

        Assert.AreEqual(TransactionsQueryStatus.Ready, result.Status);
        Assert.AreSame(EmptySummary, result.Summary);
        Assert.AreEqual(1, connectionReader.Calls);
        Assert.AreEqual(1, cardReader.Calls);
        Assert.AreEqual(1, dashboardReader.Calls);
        Assert.IsNotNull(dashboardReader.LastQuery);
        Assert.AreEqual(ConnectionId, dashboardReader.LastQuery.ConnectionId);
        Assert.AreEqual(CardId, dashboardReader.LastQuery.CardId);
        Assert.AreEqual(CategoryId, dashboardReader.LastQuery.CategoryId);
        Assert.AreEqual(TransactionSituation.Pending, dashboardReader.LastQuery.Situation);
        Assert.AreEqual(new DateOnly(2026, 11, 1), dashboardReader.LastQuery.From);
        Assert.AreEqual(new DateOnly(2026, 12, 1), dashboardReader.LastQuery.ToExclusive);
        Assert.AreEqual("literal %_ \\ description", dashboardReader.LastQuery.Search);
    }

    // Filtros malformados falham antes de resolver conexão, testar cartão ou chamar o reader agregado.
    [TestMethod]
    public async Task RejectsInvalidDateAndIdentifiersBeforePersistenceAccess()
    {
        var connectionReader = new FakeConnectionReader(
            new CurrentSyncConnectionResult(CurrentSyncConnectionStatus.Found, ConnectionId));
        var cardReader = new FakeCardOwnershipReader(isOwned: true);
        var dashboardReader = new FakeDashboardReader();
        var handler = CreateHandler(connectionReader, cardReader, dashboardReader);

        var invalidDate = await handler.Handle(
            new GetTransactionDashboardQuery(UserId, From: "01/11/2026"),
            CancellationToken.None);
        var invalidCard = await handler.Handle(
            new GetTransactionDashboardQuery(UserId, CardId: "not-a-guid"),
            CancellationToken.None);

        Assert.AreEqual(TransactionsQueryStatus.InvalidRequest, invalidDate.Status);
        Assert.AreEqual(TransactionsQueryStatus.InvalidRequest, invalidCard.Status);
        Assert.AreEqual(0, connectionReader.Calls);
        Assert.AreEqual(0, cardReader.Calls);
        Assert.AreEqual(0, dashboardReader.Calls);
    }

    // Conexão ausente ou ambígua é resolvida antes da verificação do cartão e não chega à agregação.
    [TestMethod]
    public async Task ReturnsTheListConnectionStatusesWithoutReadingDashboard()
    {
        foreach (var (connectionStatus, expectedQueryStatus) in new[]
        {
            (CurrentSyncConnectionStatus.NotFound, TransactionsQueryStatus.ConnectionNotFound),
            (CurrentSyncConnectionStatus.Ambiguous, TransactionsQueryStatus.ConnectionAmbiguous)
        })
        {
            var connectionReader = new FakeConnectionReader(new CurrentSyncConnectionResult(connectionStatus));
            var cardReader = new FakeCardOwnershipReader(isOwned: true);
            var dashboardReader = new FakeDashboardReader();
            var handler = CreateHandler(connectionReader, cardReader, dashboardReader);

            var result = await handler.Handle(
                new GetTransactionDashboardQuery(UserId, CardId: CardId.ToString("D")),
                CancellationToken.None);

            Assert.AreEqual(expectedQueryStatus, result.Status);
            Assert.AreEqual(1, connectionReader.Calls);
            Assert.AreEqual(0, cardReader.Calls);
            Assert.AreEqual(0, dashboardReader.Calls);
        }
    }

    // Cartão alheio e ID inexistente produzem o mesmo resultado antes de consultar movimentos ou cobertura.
    [TestMethod]
    public async Task ReturnsTheSameStatusForForeignAndMissingCards()
    {
        foreach (var requestedCardId in new[] { OtherCardId, MissingCardId })
        {
            var connectionReader = new FakeConnectionReader(
                new CurrentSyncConnectionResult(CurrentSyncConnectionStatus.Found, ConnectionId));
            var cardReader = new FakeCardOwnershipReader(isOwned: false);
            var dashboardReader = new FakeDashboardReader();
            var handler = CreateHandler(connectionReader, cardReader, dashboardReader);

            var result = await handler.Handle(
                new GetTransactionDashboardQuery(UserId, CardId: requestedCardId.ToString("D")),
                CancellationToken.None);

            Assert.AreEqual(TransactionsQueryStatus.CardNotFound, result.Status);
            Assert.AreEqual(1, connectionReader.Calls);
            Assert.AreEqual(1, cardReader.Calls);
            Assert.AreEqual(0, dashboardReader.Calls);
        }
    }

    // A fábrica mantém os dublês explícitos para cada teste observar a ordem das fronteiras de acesso.
    private static GetTransactionDashboardQueryHandler CreateHandler(
        FakeConnectionReader connectionReader,
        FakeCardOwnershipReader cardReader,
        FakeDashboardReader dashboardReader) =>
        new(connectionReader, cardReader, dashboardReader);

    // O dublê retorna cardinalidade de conexão determinística e contabiliza a identidade consultada.
    private sealed class FakeConnectionReader(CurrentSyncConnectionResult result) : ICurrentSyncConnectionReader
    {
        public int Calls { get; private set; }

        public Task<CurrentSyncConnectionResult> ResolveAsync(Guid userId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    // O dublê representa a consulta de propriedade sem distinguir cartão ausente de cartão de outro titular.
    private sealed class FakeCardOwnershipReader(bool isOwned) : ITransactionCardOwnershipReader
    {
        public int Calls { get; private set; }

        public Task<bool> IsOwnedAsync(
            Guid userId,
            Guid connectionId,
            Guid cardId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(isOwned);
        }
    }

    // O dublê captura os filtros finais e devolve apenas um resumo vazio e sintético.
    private sealed class FakeDashboardReader : ITransactionDashboardReader
    {
        public int Calls { get; private set; }
        public ValidatedTransactionDashboardQuery? LastQuery { get; private set; }

        public Task<TransactionMovementDashboardSummary> ReadAsync(
            ValidatedTransactionDashboardQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Task.FromResult(EmptySummary);
        }
    }
}
