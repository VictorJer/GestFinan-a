using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confere validação e resultados de comandos sem banco, HTTP ou dependência de origem financeira.
[TestClass]
public sealed class InvestmentCommandHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("018fdb02-5a54-48cd-a623-6d7c1cf7d7b0");
    private static readonly Guid InvestmentId = Guid.Parse("17f163a8-2452-44a1-b86f-9cd4ac8962d8");

    // Entrada inválida de create/update/delete é recusada antes de qualquer chamada ao adapter.
    [TestMethod]
    public async Task InvalidCommandsAreRejectedBeforePersistence()
    {
        var store = new FakeInvestmentStore();
        var create = new CreateInvestmentHandler(store);
        var update = new UpdateInvestmentHandler(store);
        var delete = new DeleteInvestmentHandler(store);

        var createCommands = new[]
        {
            new CreateInvestmentCommand(Guid.Empty, "Carteira", InvestmentClassCatalog.FixedIncome, 1m, 1m),
            new CreateInvestmentCommand(UserId, null, InvestmentClassCatalog.FixedIncome, 1m, 1m),
            new CreateInvestmentCommand(UserId, "Carteira", "unknown", 1m, 1m),
            new CreateInvestmentCommand(UserId, "Carteira", InvestmentClassCatalog.FixedIncome, -1m, 1m),
            new CreateInvestmentCommand(UserId, "Carteira", InvestmentClassCatalog.FixedIncome, 1m, 1.234m)
        };
        foreach (var command in createCommands)
        {
            Assert.AreEqual(InvestmentOperationStatus.InvalidRequest, (await create.Handle(command, default)).Status);
        }

        Assert.AreEqual(
            InvestmentOperationStatus.InvalidRequest,
            (await update.Handle(new UpdateInvestmentCommand(
                UserId, Guid.Empty, "Carteira", InvestmentClassCatalog.FixedIncome, 1m, 1m), default)).Status);
        Assert.AreEqual(
            InvestmentOperationStatus.InvalidRequest,
            (await delete.Handle(new DeleteInvestmentCommand(Guid.Empty, InvestmentId), default)).Status);
        Assert.AreEqual(0, store.CreateCalls);
        Assert.AreEqual(0, store.UpdateCalls);
        Assert.AreEqual(0, store.DeleteCalls);
    }

    // Criação envia ao adapter a entidade validada com o UserId recebido da camada confiável.
    [TestMethod]
    public async Task CreatePersistsValidatedInvestmentAndReturnsStoredProjection()
    {
        var store = new FakeInvestmentStore { CreatedId = InvestmentId };
        var handler = new CreateInvestmentHandler(store);

        var result = await handler.Handle(new CreateInvestmentCommand(
            UserId,
            "Reserva sintética",
            InvestmentClassCatalog.FixedIncome,
            120.50m,
            125.75m), default);

        Assert.AreEqual(InvestmentOperationStatus.Created, result.Status);
        Assert.AreEqual(InvestmentId, result.Item!.Id);
        Assert.AreEqual("Reserva sintética", result.Item.Name);
        Assert.AreEqual(UserId, store.CreatedInvestment!.UserId);
        Assert.AreEqual(120.50m, store.CreatedInvestment.AppliedAmount);
        Assert.AreEqual(125.75m, store.CreatedInvestment.CurrentAmount);
    }

    // Ausência própria/alheia é devolvida como NotFound; atualização válida só confirma o item salvo.
    [TestMethod]
    public async Task UpdateReturnsNotFoundOrTheConfirmedOwnedProjection()
    {
        var store = new FakeInvestmentStore();
        var handler = new UpdateInvestmentHandler(store);
        var command = new UpdateInvestmentCommand(
            UserId,
            InvestmentId,
            "Carteira atualizada",
            InvestmentClassCatalog.VariableIncome,
            200m,
            180m);

        Assert.AreEqual(InvestmentOperationStatus.NotFound, (await handler.Handle(command, default)).Status);
        Assert.AreEqual(InvestmentId, store.UpdateInvestment!.Id);
        Assert.AreEqual(UserId, store.UpdateInvestment.UserId);

        store.UpdatedItem = new InvestmentItem(
            InvestmentId, command.Name!, command.Class!, command.AppliedAmount, command.CurrentAmount);
        var updated = await handler.Handle(command, default);

        Assert.AreEqual(InvestmentOperationStatus.Updated, updated.Status);
        Assert.AreEqual("Carteira atualizada", updated.Item!.Name);
        Assert.AreEqual(InvestmentClassCatalog.VariableIncome, updated.Item.Class);
        Assert.AreEqual(200m, updated.Item.AppliedAmount);
        Assert.AreEqual(180m, updated.Item.CurrentAmount);
    }

    // Remoção mantém status uniforme para ID alheio/inexistente e confirma somente delete próprio.
    [TestMethod]
    public async Task DeleteReturnsUniformNotFoundAndConfirmsOwnedRemoval()
    {
        var store = new FakeInvestmentStore();
        var handler = new DeleteInvestmentHandler(store);
        var command = new DeleteInvestmentCommand(UserId, InvestmentId);

        var missing = await handler.Handle(command, default);
        store.DeleteResult = true;
        var removed = await handler.Handle(command, default);

        Assert.AreEqual(InvestmentOperationStatus.NotFound, missing.Status);
        Assert.AreEqual(InvestmentOperationStatus.Deleted, removed.Status);
        Assert.AreEqual(UserId, store.LastDeletedUserId);
        Assert.AreEqual(InvestmentId, store.LastDeletedInvestmentId);
        Assert.IsNull(removed.Item);
    }

    // O fake captura somente o contrato Application, sem representar persistência PostgreSQL.
    private sealed class FakeInvestmentStore : IInvestmentStore
    {
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public Guid CreatedId { get; init; } = Guid.Parse("d7b23be4-2138-4366-8fbf-65f681dc6037");
        public Investment? CreatedInvestment { get; private set; }
        public Investment? UpdateInvestment { get; private set; }
        public InvestmentItem? UpdatedItem { get; set; }
        public Guid LastDeletedUserId { get; private set; }
        public Guid LastDeletedInvestmentId { get; private set; }
        public bool DeleteResult { get; set; }

        // Os testes focais de escrita não consultam nem simulam uma carteira pré-existente.
        public Task<IReadOnlyList<InvestmentItem>> ReadOwnedAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InvestmentItem>>(Array.Empty<InvestmentItem>());

        public Task<InvestmentItem> CreateAsync(Investment investment, CancellationToken cancellationToken)
        {
            CreateCalls++;
            CreatedInvestment = investment;
            return Task.FromResult(new InvestmentItem(
                CreatedId,
                investment.Name,
                investment.Class,
                investment.AppliedAmount,
                investment.CurrentAmount));
        }

        public Task<InvestmentItem?> UpdateOwnedAsync(Investment investment, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            UpdateInvestment = investment;
            return Task.FromResult(UpdatedItem);
        }

        public Task<bool> DeleteOwnedAsync(Guid userId, Guid investmentId, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            LastDeletedUserId = userId;
            LastDeletedInvestmentId = investmentId;
            return Task.FromResult(DeleteResult);
        }

    }
}
