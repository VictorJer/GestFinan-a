using Finora.Application.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Prova a validação da Application e o encaminhamento da anotação sem abrir banco ou serviço de provedor.
[TestClass]
public sealed class TransactionAnnotationHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("9910fc28-1226-4d78-8ef6-25db09c6e001");
    private static readonly Guid TransactionId = Guid.Parse("d9bad62c-94e7-40a1-9ec0-24f64ac8f001");
    private static readonly Guid CategoryId = Guid.Parse("0a9555f8-e16c-46eb-b3ae-24b2bb2ad001");

    // IDs vazios, inconsistência de presença, ausência de campos e limites inválidos não chegam ao adapter.
    [TestMethod]
    public async Task InvalidCommandsAreRejectedBeforePersistence()
    {
        var store = new CapturingAnnotationStore();
        var handler = new UpdateTransactionAnnotationHandler(store, TimeProvider.System);
        var invalidCommands = new[]
        {
            new UpdateTransactionAnnotationCommand(Guid.Empty, TransactionId, false, null, true, "nota"),
            new UpdateTransactionAnnotationCommand(UserId, Guid.Empty, false, null, true, "nota"),
            new UpdateTransactionAnnotationCommand(UserId, TransactionId, false, null, false, null),
            new UpdateTransactionAnnotationCommand(UserId, TransactionId, false, CategoryId, true, "nota"),
            new UpdateTransactionAnnotationCommand(UserId, TransactionId, true, Guid.Empty, false, null),
            new UpdateTransactionAnnotationCommand(UserId, TransactionId, true, null, false, "nota"),
            new UpdateTransactionAnnotationCommand(UserId, TransactionId, false, null, true, new string('x', 501))
        };

        foreach (var command in invalidCommands)
        {
            var result = await handler.Handle(command, default);
            Assert.AreEqual(UpdateTransactionAnnotationStatus.InvalidRequest, result.Status);
        }

        Assert.AreEqual(0, store.CallCount);
    }

    // A atualização repassa titular confiável, presença dos campos e instante UTC ao único adapter de persistência.
    [TestMethod]
    public async Task ValidCommandDelegatesWithExplicitPresenceAndUtcTimestamp()
    {
        var store = new CapturingAnnotationStore
        {
            Result = new(UpdateTransactionAnnotationStatus.Updated, CategoryId, "nota preservada")
        };
        var handler = new UpdateTransactionAnnotationHandler(store, TimeProvider.System);
        var command = new UpdateTransactionAnnotationCommand(UserId, TransactionId, true, CategoryId, false, null);

        var result = await handler.Handle(command, default);

        Assert.AreEqual(UpdateTransactionAnnotationStatus.Updated, result.Status);
        Assert.AreEqual(CategoryId, result.CategoryId);
        Assert.AreEqual("nota preservada", result.Note);
        Assert.AreEqual(1, store.CallCount);
        Assert.AreEqual(command, store.Command);
        Assert.IsNotNull(store.UpdatedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, store.UpdatedAtUtc!.Value.Offset);
    }

    // Uma string vazia limpa a observação, mantendo HasNote para não confundi-la com campo omitido.
    [TestMethod]
    public async Task EmptyNoteIsNormalizedToExplicitClear()
    {
        var store = new CapturingAnnotationStore();
        var handler = new UpdateTransactionAnnotationHandler(store, TimeProvider.System);
        var command = new UpdateTransactionAnnotationCommand(UserId, TransactionId, false, null, true, string.Empty);

        await handler.Handle(command, default);

        Assert.AreEqual(1, store.CallCount);
        Assert.IsTrue(store.Command!.HasNote);
        Assert.IsNull(store.Command.Note);
    }

    // Ausência do movimento e da categoria são estados distintos internamente para o Controller sanitizar em 404.
    [TestMethod]
    public async Task StoreStatusesAreReturnedWithoutReclassifyingOwnership()
    {
        var store = new CapturingAnnotationStore();
        var handler = new UpdateTransactionAnnotationHandler(store, TimeProvider.System);
        var command = new UpdateTransactionAnnotationCommand(UserId, TransactionId, true, CategoryId, false, null);

        foreach (var status in new[]
                 {
                     UpdateTransactionAnnotationStatus.TransactionNotFound,
                     UpdateTransactionAnnotationStatus.CategoryNotFound
                 })
        {
            store.Result = new(status);
            var result = await handler.Handle(command, default);
            Assert.AreEqual(status, result.Status);
        }

        Assert.AreEqual(2, store.CallCount);
    }

    // O fake captura apenas o contrato da Application; não consulta banco, Pluggy ou qualquer serviço externo.
    private sealed class CapturingAnnotationStore : ITransactionAnnotationStore
    {
        public int CallCount { get; private set; }
        public UpdateTransactionAnnotationCommand? Command { get; private set; }
        public DateTimeOffset? UpdatedAtUtc { get; private set; }
        public UpdateTransactionAnnotationResult Result { get; set; } = new(UpdateTransactionAnnotationStatus.Updated);

        public Task<UpdateTransactionAnnotationResult> UpdateAsync(
            UpdateTransactionAnnotationCommand command,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Command = command;
            UpdatedAtUtc = updatedAtUtc;
            return Task.FromResult(Result);
        }
    }
}
