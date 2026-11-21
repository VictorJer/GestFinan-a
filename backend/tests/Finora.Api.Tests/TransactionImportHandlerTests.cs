using Finora.Application.Pluggy;

namespace Finora.Api.Tests;

// Mantém as regras de propriedade e a saída sanitizada verificáveis sem rede ou banco de dados.
[TestClass]
public sealed class TransactionImportHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("92b87b22-00b0-4bb5-b95e-f2cd1d19b87a");
    private static readonly Guid ConnectionId = Guid.Parse("29bfb5c2-a51e-4fb3-b435-b9b45f009d4a");
    private static readonly DateTimeOffset Reference = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    // Conexão inexistente ou de outro titular deve bloquear a origem antes de qualquer leitura.
    [TestMethod]
    public async Task MissingOrMismatchedConnectionDoesNotCallProvider()
    {
        var connectionReader = new StubConnectionReader(null);
        var sourceReader = new StubImportReader(TransactionImportReadResult.Failed(
            TransactionImportFailureCode.ProviderUnavailable));
        var promoter = new StubImportPromoter();
        var handler = CreateHandler(connectionReader, sourceReader, promoter);

        var result = await handler.Handle(new ImportTransactionsCommand(UserId, ConnectionId), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.ConnectionUnavailable, result.FailureCode);
        Assert.AreEqual(0, sourceReader.CallCount);
        Assert.AreEqual(0, promoter.CallCount);
    }

    // Credenciais/itens indisponíveis são convertidos em códigos internos sem propagar valores sensíveis.
    [TestMethod]
    public async Task UnavailableProviderItemReturnsSanitizedFailure()
    {
        var connectionReader = new StubConnectionReader(new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "item-sensitive-sentinel", null));
        var sourceReader = new StubImportReader(TransactionImportReadResult.Failed(
            TransactionImportFailureCode.ProviderUnavailable));
        var promoter = new StubImportPromoter();
        var handler = CreateHandler(connectionReader, sourceReader, promoter);

        var result = await handler.Handle(new ImportTransactionsCommand(UserId, ConnectionId), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.ProviderUnavailable, result.FailureCode);
        Assert.IsFalse(result.ToString()!.Contains("item-sensitive-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, sourceReader.CallCount);
        Assert.AreEqual(0, promoter.CallCount);
    }

    // Falhas externas geram resumo sem identificadores, mensagem do provedor ou contadores não confirmados.
    [TestMethod]
    public async Task ProviderFailureIsRecordedAndReturnedWithoutSensitiveData()
    {
        var connectionReader = new StubConnectionReader(new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "item-sensitive-sentinel", null));
        var sourceReader = new StubImportReader(TransactionImportReadResult.Failed(
            TransactionImportFailureCode.ProviderTimeout, pagesRead: 3, receivedCount: 8));
        var promoter = new StubImportPromoter();
        var handler = CreateHandler(connectionReader, sourceReader, promoter);

        var result = await handler.Handle(new ImportTransactionsCommand(UserId, ConnectionId), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TransactionImportFailureCode.ProviderTimeout, result.FailureCode);
        Assert.IsFalse(result.ToString()!.Contains("item-sensitive-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(result.ToString()!.Contains("private provider response", StringComparison.Ordinal));
        Assert.AreEqual(1, promoter.FailureCallCount);
        Assert.AreEqual(TransactionImportFailureCode.ProviderTimeout, promoter.RecordedFailure);
        Assert.AreEqual(3, promoter.RecordedPagesRead);
        Assert.AreEqual(8, promoter.RecordedReceivedCount);
    }

    // A referência de doze meses é fixada em UTC e enviada como data inicial sem filtro superior.
    [TestMethod]
    public async Task FixesRequestedWindowAndReturnsOnlyAggregates()
    {
        var connectionReader = new StubConnectionReader(new TransactionImportConnection(
            ConnectionId, UserId, "pluggy", "item-sensitive-sentinel", null));
        var sourceReader = new StubImportReader(new TransactionImportReadResult(
            new TransactionImportSnapshot(
                [new TransactionImportAccount("account-private", "CREDIT", "CREDIT_CARD", "BRL")],
                [new PluggyTransactionData("transaction-private", "account-private", "private description", 12.34m,
                    "BRL", "2026-11-01", "DEBIT", "POSTED", "TARIFA")],
                new DateOnly(2025, 11, 20),
                true),
            null));
        var promoter = new StubImportPromoter();
        var handler = CreateHandler(connectionReader, sourceReader, promoter);

        var result = await handler.Handle(new ImportTransactionsCommand(UserId, ConnectionId), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(new DateOnly(2025, 11, 20), sourceReader.RequestedDateFrom);
        Assert.AreEqual(1, result.AccountCount);
        Assert.AreEqual(1, result.TransactionCount);
        Assert.AreEqual(1, result.PersistedCount);
        Assert.AreEqual(1, promoter.CallCount);
        Assert.IsFalse(result.ToString()!.Contains("transaction-private", StringComparison.Ordinal));
        Assert.IsFalse(result.ToString()!.Contains("private description", StringComparison.Ordinal));
    }

    // A API permanece inicializável sem configuração Pluggy porque a composição HTTP não registra o adaptador.
    [TestMethod]
    public async Task ApiStartsWithoutPluggyCredentials()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.IsTrue(response.IsSuccessStatusCode);
    }

    // Permite verificar a decisão do caso de uso sem registrar qualquer dependência externa.
    private static ImportTransactionsHandler CreateHandler(
        ITransactionImportConnectionReader connectionReader,
        ITransactionImportReader reader,
        ITransactionImportPromoter promoter) => new(connectionReader, reader, promoter, new FixedTimeProvider(Reference));

    private sealed class StubConnectionReader(TransactionImportConnection? connection)
        : ITransactionImportConnectionReader
    {
        public Task<TransactionImportConnection?> FindOwnedAsync(
            Guid connectionId,
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(
                connection is { Id: var storedId, UserId: var storedUserId }
                && storedId == connectionId && storedUserId == userId ? connection : null);
    }

    private sealed class StubImportReader(TransactionImportReadResult result) : ITransactionImportReader
    {
        public int CallCount { get; private set; }
        public DateOnly? RequestedDateFrom { get; private set; }

        public Task<TransactionImportReadResult> ReadAsync(
            string externalItemId,
            DateOnly dateFrom,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestedDateFrom = dateFrom;
            return Task.FromResult(result);
        }
    }

    // Confirma que somente snapshots completos seguem para o contrato transacional de promoção.
    private sealed class StubImportPromoter : ITransactionImportPromoter
    {
        public int CallCount { get; private set; }
        public int FailureCallCount { get; private set; }
        public TransactionImportFailureCode? RecordedFailure { get; private set; }
        public int? RecordedPagesRead { get; private set; }
        public int? RecordedReceivedCount { get; private set; }

        public Task<TransactionImportPromotionResult> PromoteAsync(
            TransactionImportConnection connection,
            TransactionImportSnapshot snapshot,
            DateOnly normalizationReferenceDate,
            DateTimeOffset startedAtUtc,
            DateTimeOffset finishedAtUtc,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new TransactionImportPromotionResult(true, null, snapshot.Transactions.Count));
        }

        // O stub retorna confirmação sintética sem trocar IDs externos ou acessar PostgreSQL.
        public Task<TransactionImportPromotionResult> PromoteReconnectionAsync(
            TransactionImportConnection connection,
            string replacementExternalItemId,
            TransactionImportSnapshot snapshot,
            DateOnly normalizationReferenceDate,
            DateTimeOffset startedAtUtc,
            DateTimeOffset finishedAtUtc,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new TransactionImportPromotionResult(true, null, snapshot.Transactions.Count));
        }

        // A prova confirma que somente código e contagens validadas chegam ao registro técnico.
        public Task<TransactionImportPromotionResult> RecordFailureAsync(
            TransactionImportConnection connection,
            DateOnly requestedDateFrom,
            DateTimeOffset startedAtUtc,
            DateTimeOffset finishedAtUtc,
            TransactionImportFailureCode failureCode,
            int? pagesRead,
            int? receivedCount,
            CancellationToken cancellationToken)
        {
            FailureCallCount++;
            RecordedFailure = failureCode;
            RecordedPagesRead = pagesRead;
            RecordedReceivedCount = receivedCount;
            return Task.FromResult(new TransactionImportPromotionResult(true, null));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
