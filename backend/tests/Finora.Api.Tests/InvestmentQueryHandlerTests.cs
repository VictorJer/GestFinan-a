using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida conciliação e estabilidade da query sem banco nem dependência do grafo de movimentos.
[TestClass]
public sealed class InvestmentQueryHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("0b6ea802-74b2-40d2-a102-b7fe46300800");
    private static readonly Guid FirstAlphaId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondAlphaId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid BetaId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid GrowthId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    // Um único conjunto materializado alimenta lista, totais, diferença negativa e distribuição por classe.
    [TestMethod]
    public async Task QueryOrdersItemsAndReconcilesTotalsAndClassValuesFromOwnedSet()
    {
        var store = new QueryStore
        {
            Items =
            [
                Item(BetaId, "Beta", InvestmentClassCatalog.RealEstateFunds, 5m, 25m),
                Item(GrowthId, "Crescimento", InvestmentClassCatalog.VariableIncome, 10m, 10m),
                Item(SecondAlphaId, "Alpha", InvestmentClassCatalog.FixedIncome, 100m, 85m),
                Item(FirstAlphaId, "Alpha", InvestmentClassCatalog.FixedIncome, 20m, 0m)
            ]
        };
        var handler = new GetInvestmentPortfolioHandler(store);

        var result = await handler.Handle(new GetInvestmentPortfolioQuery(UserId), default);

        Assert.AreEqual(UserId, store.LastReadUserId);
        Assert.AreEqual(1, store.ReadCalls);
        CollectionAssert.AreEqual(
            new[] { FirstAlphaId, SecondAlphaId, BetaId, GrowthId },
            result.Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(135m, result.Summary.TotalAppliedAmount);
        Assert.AreEqual(120m, result.Summary.TotalCurrentAmount);
        Assert.AreEqual(-15m, result.Summary.Difference);
        AssertClassValue(result.Summary, InvestmentClassCatalog.FixedIncome, "Renda fixa", 85m);
        AssertClassValue(result.Summary, InvestmentClassCatalog.VariableIncome, "Renda variável", 10m);
        AssertClassValue(result.Summary, InvestmentClassCatalog.RealEstateFunds, "Fundos imobiliários", 25m);
    }

    // Reconsultar após mudanças usa os dados atuais do adapter e remove do resumo posições já excluídas.
    [TestMethod]
    public async Task EachQueryReconcilesUpdatedAndDeletedItemsFromCurrentOwnedSet()
    {
        var updatedItem = Item(
            FirstAlphaId, "Reserva", InvestmentClassCatalog.FixedIncome, 100m, 110m);
        var deletedItem = Item(
            BetaId, "Fundo", InvestmentClassCatalog.RealEstateFunds, 50m, 40m);
        var store = new QueryStore { Items = [updatedItem, deletedItem] };
        var handler = new GetInvestmentPortfolioHandler(store);

        var beforeMutation = await handler.Handle(new GetInvestmentPortfolioQuery(UserId), default);
        Assert.AreEqual(150m, beforeMutation.Summary.TotalAppliedAmount);
        Assert.AreEqual(150m, beforeMutation.Summary.TotalCurrentAmount);
        Assert.AreEqual(0m, beforeMutation.Summary.Difference);

        store.Items = [updatedItem with { CurrentAmount = 80m }];
        var afterMutation = await handler.Handle(new GetInvestmentPortfolioQuery(UserId), default);

        Assert.AreEqual(1, afterMutation.Items.Count);
        Assert.AreEqual(100m, afterMutation.Summary.TotalAppliedAmount);
        Assert.AreEqual(80m, afterMutation.Summary.TotalCurrentAmount);
        Assert.AreEqual(-20m, afterMutation.Summary.Difference);
        AssertClassValue(afterMutation.Summary, InvestmentClassCatalog.FixedIncome, "Renda fixa", 80m);
        AssertClassValue(afterMutation.Summary, InvestmentClassCatalog.RealEstateFunds, "Fundos imobiliários", 0m);
        Assert.AreEqual(2, store.ReadCalls);
    }

    // Carteira vazia retorna totais e todas as classes em zero, sem construir percentuais artificiais.
    [TestMethod]
    public async Task EmptyPortfolioReturnsZeroTotalsAndZeroForEveryClass()
    {
        var handler = new GetInvestmentPortfolioHandler(new QueryStore());

        var result = await handler.Handle(new GetInvestmentPortfolioQuery(UserId), default);

        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0m, result.Summary.TotalAppliedAmount);
        Assert.AreEqual(0m, result.Summary.TotalCurrentAmount);
        Assert.AreEqual(0m, result.Summary.Difference);
        Assert.AreEqual(InvestmentClassCatalog.Options.Count, result.Summary.CurrentAmountsByClass.Count);
        Assert.IsTrue(result.Summary.CurrentAmountsByClass.All(value => value.CurrentAmount == 0m));
    }

    // Total atual zero mantém o aplicado, expressa diferença negativa e deixa toda a distribuição zerada.
    [TestMethod]
    public async Task ZeroCurrentTotalKeepsAppliedAmountAndNegativeDifference()
    {
        var store = new QueryStore
        {
            Items =
            [
                Item(FirstAlphaId, "Reserva", InvestmentClassCatalog.FixedIncome, 20m, 0m),
                Item(BetaId, "Fundo", InvestmentClassCatalog.RealEstateFunds, 10m, 0m)
            ]
        };
        var handler = new GetInvestmentPortfolioHandler(store);

        var result = await handler.Handle(new GetInvestmentPortfolioQuery(UserId), default);

        Assert.AreEqual(30m, result.Summary.TotalAppliedAmount);
        Assert.AreEqual(0m, result.Summary.TotalCurrentAmount);
        Assert.AreEqual(-30m, result.Summary.Difference);
        Assert.IsTrue(result.Summary.CurrentAmountsByClass.All(value => value.CurrentAmount == 0m));
    }

    // Usuário vazio é tratado como ausência de sessão interna e não provoca leitura no adapter.
    [TestMethod]
    public async Task EmptyUserIdDoesNotReadAnyPortfolio()
    {
        var store = new QueryStore
        {
            Items = [Item(FirstAlphaId, "Privado", InvestmentClassCatalog.FixedIncome, 1m, 1m)]
        };
        var handler = new GetInvestmentPortfolioHandler(store);

        var result = await handler.Handle(new GetInvestmentPortfolioQuery(Guid.Empty), default);

        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, store.ReadCalls);
        Assert.AreEqual(0m, result.Summary.TotalCurrentAmount);
    }

    // Mantém a prova de rótulos e valores por classe legível sem expor qualquer estrutura de movimentos.
    private static void AssertClassValue(
        InvestmentPortfolioSummary summary,
        string classCode,
        string expectedLabel,
        decimal expectedAmount)
    {
        var classValue = summary.CurrentAmountsByClass.Single(value => value.Class == classCode);
        Assert.AreEqual(expectedLabel, classValue.Label);
        Assert.AreEqual(expectedAmount, classValue.CurrentAmount);
    }

    // Os itens do teste são projeções persistidas; nenhum construtor de transação ou leitor de movimentos é usado.
    private static InvestmentItem Item(
        Guid id,
        string name,
        string @class,
        decimal appliedAmount,
        decimal currentAmount) =>
        new(id, name, @class, appliedAmount, currentAmount);

    // O fake expõe apenas o conjunto manual e registra qual titular pediu a leitura.
    private sealed class QueryStore : IInvestmentStore
    {
        public IReadOnlyList<InvestmentItem> Items { get; set; } = Array.Empty<InvestmentItem>();
        public int ReadCalls { get; private set; }
        public Guid LastReadUserId { get; private set; }

        public Task<IReadOnlyList<InvestmentItem>> ReadOwnedAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            LastReadUserId = userId;
            return Task.FromResult(Items);
        }

        public Task<InvestmentItem> CreateAsync(Investment investment, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A query não deve criar investimentos.");

        public Task<InvestmentItem?> UpdateOwnedAsync(Investment investment, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A query não deve editar investimentos.");

        public Task<bool> DeleteOwnedAsync(Guid userId, Guid investmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A query não deve excluir investimentos.");
    }
}
