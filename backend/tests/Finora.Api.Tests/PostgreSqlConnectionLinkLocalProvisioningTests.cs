using Finora.Application.AdministrativeConnections;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.AdministrativeConnections;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida a gravação inicial somente no PostgreSQL descartável allowlisted, sem chamar Pluggy ou banco persistente.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlConnectionLinkLocalProvisioningTests
{
    private static readonly Guid OwnerId = Guid.Parse("1a3ef3ea-4cc9-4a21-8d55-5df4071c5f95");
    private static readonly Guid OtherOwnerId = Guid.Parse("47112f05-c8a0-4a4a-87d1-6f7467ea3503");
    private static readonly Guid FirstConnectionId = Guid.Parse("8d9e5f47-866c-4ebc-a830-4b6c9e8e77d0");
    private static readonly Guid SecondConnectionId = Guid.Parse("8de15af2-4a3f-4b05-9e4e-628f57b8258e");
    private const string ExternalItemId = "synthetic-link-local-item";
    private DisposablePostgresMigrationFixture? _fixture;

    // Recria o schema completo somente após a fixture validar o database de integração permitido.
    [TestInitialize]
    public async Task InitializeAsync()
    {
        _fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await _fixture.ResetToEmptySchemaAsync();
        await _fixture.Context.Database.MigrateAsync();
    }

    // Fecha a conexão de teste sem alterar recursos fora do database descartável administrado pela fixture.
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    // A primeira vinculação confirma o par conexão/identidade para o único titular sem criar cartão ou importação.
    [TestMethod]
    public async Task FirstLinkPersistsOwnedConnectionAndActiveIdentityWithoutSuccessOrFinancialRecords()
    {
        var context = _fixture!.Context;
        await SeedOwnersAsync(OwnerId);
        context.ChangeTracker.Clear();

        var handler = new ConnectionLinkLocalProvisioningHandler(context);
        var result = await handler.Handle(
            new ConnectionLinkLocalLinkCommand(OwnerId, ExternalItemId),
            CancellationToken.None);

        Assert.AreEqual(ConnectionLinkLocalLinkState.Linked, result.State);
        Assert.IsFalse(new ConnectionLinkLocalLinkCommand(OwnerId, ExternalItemId)
            .ToString().Contains(ExternalItemId, StringComparison.Ordinal));
        Assert.AreEqual(1, await context.Users.CountAsync());

        context.ChangeTracker.Clear();
        var connections = await context.Set<FinancialConnection>().AsNoTracking().ToArrayAsync();
        var identities = await context.Set<FinancialConnectionItemIdentity>().AsNoTracking().ToArrayAsync();

        Assert.AreEqual(1, connections.Length);
        Assert.AreEqual(OwnerId, connections[0].UserId);
        Assert.AreEqual("pluggy", connections[0].Provider);
        Assert.AreEqual(ExternalItemId, connections[0].ExternalItemId);
        Assert.IsNull(connections[0].LastAttemptAtUtc);
        Assert.IsNull(connections[0].LastSuccessfulImportAtUtc);
        Assert.IsNull(connections[0].SourceUpdatedAtUtc);

        Assert.AreEqual(1, identities.Length);
        Assert.AreEqual(connections[0].Id, identities[0].ConnectionId);
        Assert.AreEqual(OwnerId, identities[0].UserId);
        Assert.AreEqual("pluggy", identities[0].Provider);
        Assert.AreEqual(ExternalItemId, identities[0].ExternalItemId);
        Assert.IsNull(identities[0].RetiredAtUtc);

        Assert.AreEqual(0, await context.Set<CardAccount>().CountAsync());
        Assert.AreEqual(0, await context.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(0, await context.Set<SyncRun>().CountAsync());
        Assert.IsNull(context.Database.CurrentTransaction);
    }

    // Repetir item e identidade ativa íntegros devolve already_linked sem alterar IDs, timestamps ou histórico.
    [TestMethod]
    public async Task RepeatingIntactLinkReturnsAlreadyLinkedWithoutUpdatingPersistedState()
    {
        await SeedExistingLinkAsync(OwnerId, FirstConnectionId, ExternalItemId, ExternalItemId);
        var context = _fixture!.Context;
        var before = await CaptureStateAsync(context);

        var result = await LinkAsync(OwnerId, ExternalItemId);

        Assert.AreEqual(ConnectionLinkLocalLinkState.AlreadyLinked, result.State);
        AssertStateUnchanged(before, await CaptureStateAsync(context));
    }

    // Um item configurado diferente da conexão existente é conflito e não substitui o vínculo atual.
    [TestMethod]
    public async Task DifferentConfiguredItemIsRejectedWithoutChangingExistingConnection()
    {
        await SeedExistingLinkAsync(OwnerId, FirstConnectionId, "existing-current-item", "existing-current-item");
        var context = _fixture!.Context;
        var before = await CaptureStateAsync(context);

        var result = await LinkAsync(OwnerId, ExternalItemId);

        Assert.AreEqual(ConnectionLinkLocalLinkState.Conflict, result.State);
        AssertStateUnchanged(before, await CaptureStateAsync(context));
    }

    // Identidade aposentada não pode ser reativada nem adotada como primeira vinculação.
    [TestMethod]
    public async Task RetiredConfiguredIdentityIsRejectedWithoutChangingHistory()
    {
        var context = _fixture!.Context;
        await SeedOwnersAsync(OwnerId);
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            FirstConnectionId, OwnerId, "pluggy", "existing-current-item"));
        context.Set<FinancialConnectionItemIdentity>().Add(
            FinancialConnectionItemIdentity.CreateActive(FirstConnectionId, OwnerId, "pluggy", "existing-current-item"));
        var retired = FinancialConnectionItemIdentity.CreateActive(
            FirstConnectionId, OwnerId, "pluggy", ExternalItemId);
        retired.Retire(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
        context.Set<FinancialConnectionItemIdentity>().Add(retired);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var before = await CaptureStateAsync(context);

        var result = await LinkAsync(OwnerId, ExternalItemId);

        Assert.AreEqual(ConnectionLinkLocalLinkState.Conflict, result.State);
        AssertStateUnchanged(before, await CaptureStateAsync(context));
    }

    // Registro global ativo já pertencente a outro titular não pode ser transferido para o titular esperado.
    [TestMethod]
    public async Task ItemIdentityOwnedByAnotherOwnerIsRejectedWithoutChangingEitherOwner()
    {
        await SeedOwnersAsync(OwnerId, OtherOwnerId);
        var context = _fixture!.Context;
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            FirstConnectionId, OtherOwnerId, "pluggy", ExternalItemId));
        context.Set<FinancialConnectionItemIdentity>().Add(FinancialConnectionItemIdentity.CreateActive(
            FirstConnectionId, OtherOwnerId, "pluggy", ExternalItemId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var before = await CaptureStateAsync(context);

        var result = await LinkAsync(OwnerId, ExternalItemId);

        Assert.AreEqual(ConnectionLinkLocalLinkState.Conflict, result.State);
        AssertStateUnchanged(before, await CaptureStateAsync(context));
    }

    // Conexão que aponta ao item esperado mas perdeu sua identidade ativa é corrupção, não replay bem-sucedido.
    [TestMethod]
    public async Task MissingActiveIdentityIsRejectedWithoutChangingConnection()
    {
        await SeedOwnersAsync(OwnerId);
        var context = _fixture!.Context;
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            FirstConnectionId, OwnerId, "pluggy", ExternalItemId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var before = await CaptureStateAsync(context);

        var result = await LinkAsync(OwnerId, ExternalItemId);

        Assert.AreEqual(ConnectionLinkLocalLinkState.Conflict, result.State);
        AssertStateUnchanged(before, await CaptureStateAsync(context));
    }

    // Mais de uma conexão do titular é ambígua, mesmo quando uma identidade coincide com a configuração atual.
    [TestMethod]
    public async Task MultipleExistingConnectionsAreRejectedWithoutChangingAnyLink()
    {
        await SeedOwnersAsync(OwnerId);
        var context = _fixture!.Context;
        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(FirstConnectionId, OwnerId, "pluggy", ExternalItemId),
            FinancialConnection.Create(SecondConnectionId, OwnerId, "pluggy", "second-existing-item"));
        context.Set<FinancialConnectionItemIdentity>().AddRange(
            FinancialConnectionItemIdentity.CreateActive(FirstConnectionId, OwnerId, "pluggy", ExternalItemId),
            FinancialConnectionItemIdentity.CreateActive(SecondConnectionId, OwnerId, "pluggy", "second-existing-item"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var before = await CaptureStateAsync(context);

        var result = await LinkAsync(OwnerId, ExternalItemId);

        Assert.AreEqual(ConnectionLinkLocalLinkState.Conflict, result.State);
        AssertStateUnchanged(before, await CaptureStateAsync(context));
    }

    // Duas conexões independentes competem sob o lock persistente do titular e convergem para um único par confirmado.
    [TestMethod]
    public async Task ConcurrentInvocationsCreateAtMostOneLinkAndReplayTheWinner()
    {
        await SeedOwnersAsync(OwnerId);
        await using var firstContext = CreateIndependentContext();
        await using var secondContext = CreateIndependentContext();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<ConnectionLinkLocalLinkResult> InvokeAsync(FinoraDbContext context)
        {
            await start.Task;
            return await new ConnectionLinkLocalProvisioningHandler(context).Handle(
                new ConnectionLinkLocalLinkCommand(OwnerId, ExternalItemId),
                CancellationToken.None);
        }

        var first = InvokeAsync(firstContext);
        var second = InvokeAsync(secondContext);
        start.SetResult(true);
        var results = await Task.WhenAll(first, second);

        Assert.AreEqual(1, results.Count(result => result.State == ConnectionLinkLocalLinkState.Linked));
        Assert.AreEqual(1, results.Count(result => result.State == ConnectionLinkLocalLinkState.AlreadyLinked));
        var persisted = await CaptureStateAsync(_fixture!.Context);
        Assert.AreEqual(1, persisted.Connections.Length);
        Assert.AreEqual(1, persisted.Identities.Length);
        Assert.AreEqual(0, persisted.Cards);
        Assert.AreEqual(0, persisted.Transactions);
        Assert.AreEqual(0, persisted.SyncRuns);
    }

    // Uma falha injetada depois de SaveChanges e antes de Commit provoca rollback do par inteiro.
    [TestMethod]
    public async Task FailureBeforeCommitRollsBackConnectionAndIdentityTogether()
    {
        await SeedOwnersAsync(OwnerId);
        var context = _fixture!.Context;
        var handler = new ConnectionLinkLocalProvisioningHandler(
            context,
            _ => Task.FromException(new InvalidOperationException("synthetic-private-failure")),
            afterCommit: null);

        var result = await handler.Handle(
            new ConnectionLinkLocalLinkCommand(OwnerId, ExternalItemId),
            CancellationToken.None);

        Assert.AreEqual(ConnectionLinkLocalLinkState.OperationalFailure, result.State);
        var persisted = await CaptureStateAsync(context);
        Assert.AreEqual(1, persisted.Users.Length);
        Assert.AreEqual(0, persisted.Connections.Length);
        Assert.AreEqual(0, persisted.Identities.Length);
        Assert.AreEqual(0, persisted.Cards);
        Assert.AreEqual(0, persisted.Transactions);
        Assert.AreEqual(0, persisted.SyncRuns);
    }

    // Se a confirmação local se perde depois do commit, a falha não finge sucesso e o replay reconcilia o vínculo durável.
    [TestMethod]
    public async Task LostConfirmationAfterCommitReturnsFailureAndReplayFindsDurableLink()
    {
        await SeedOwnersAsync(OwnerId);
        var context = _fixture!.Context;
        var handler = new ConnectionLinkLocalProvisioningHandler(
            context,
            beforeCommitAsync: null,
            afterCommit: () => throw new IOException("synthetic-private-acknowledgement-loss"));

        var uncertain = await handler.Handle(
            new ConnectionLinkLocalLinkCommand(OwnerId, ExternalItemId),
            CancellationToken.None);
        Assert.AreEqual(ConnectionLinkLocalLinkState.OperationalFailure, uncertain.State);

        var committed = await CaptureStateAsync(context);
        Assert.AreEqual(1, committed.Connections.Length);
        Assert.AreEqual(1, committed.Identities.Length);
        var replay = await LinkAsync(OwnerId, ExternalItemId);

        Assert.AreEqual(ConnectionLinkLocalLinkState.AlreadyLinked, replay.State);
        AssertStateUnchanged(committed, await CaptureStateAsync(context));
    }

    // Insere fixtures mínimas usando as mesmas fábricas do domínio e executa o handler no contexto PostgreSQL validado.
    private async Task SeedExistingLinkAsync(Guid userId, Guid connectionId, string currentItemId, string identityItemId)
    {
        var context = _fixture!.Context;
        await SeedOwnersAsync(userId);
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            connectionId, userId, "pluggy", currentItemId));
        context.Set<FinancialConnectionItemIdentity>().Add(FinancialConnectionItemIdentity.CreateActive(
            connectionId, userId, "pluggy", identityItemId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    // Cada cenário parte do conjunto explícito de titulares sem criar contas ou valores financeiros.
    private async Task SeedOwnersAsync(params Guid[] userIds)
    {
        var context = _fixture!.Context;
        context.Users.AddRange(userIds.Select(userId => new ApplicationUser(userId)));
        await context.SaveChangesAsync();
    }

    // Invoca somente o comando de persistência com identidade sintética já aprovada pela etapa anterior.
    private Task<ConnectionLinkLocalLinkResult> LinkAsync(Guid userId, string itemId) =>
        new ConnectionLinkLocalProvisioningHandler(_fixture!.Context).Handle(
            new ConnectionLinkLocalLinkCommand(userId, itemId),
            CancellationToken.None);

    // Cada concorrente recebe contexto/conexão independentes com a mesma string já validada pela fixture descartável.
    private static FinoraDbContext CreateIndependentContext()
    {
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(
            options,
            DisposablePostgresTestConfiguration.ReadRequired().ConnectionString);
        return new FinoraDbContext(options.Options);
    }

    // Captura linhas determinísticas e contagens financeiras para detectar UPDATE, adoção ou criação lateral.
    private static async Task<PersistedState> CaptureStateAsync(FinoraDbContext context)
    {
        context.ChangeTracker.Clear();
        var users = await context.Users.AsNoTracking()
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync();
        var connections = await context.Set<FinancialConnection>().AsNoTracking()
            .OrderBy(connection => connection.Id)
            .Select(connection => new ConnectionSnapshot(
                connection.Id,
                connection.UserId,
                connection.Provider,
                connection.ExternalItemId,
                connection.SourceStatus,
                connection.ExpiresAtUtc,
                connection.LastAttemptAtUtc,
                connection.LastSuccessfulImportAtUtc,
                connection.SourceUpdatedAtUtc))
            .ToArrayAsync();
        var identities = await context.Set<FinancialConnectionItemIdentity>().AsNoTracking()
            .OrderBy(identity => identity.Provider)
            .ThenBy(identity => identity.ExternalItemId)
            .Select(identity => new IdentitySnapshot(
                identity.ConnectionId,
                identity.UserId,
                identity.Provider,
                identity.ExternalItemId,
                identity.RetiredAtUtc))
            .ToArrayAsync();

        return new PersistedState(
            users,
            connections,
            identities,
            await context.Set<CardAccount>().CountAsync(),
            await context.Set<FinancialTransaction>().CountAsync(),
            await context.Set<TransactionAnnotation>().CountAsync(),
            await context.Set<SyncRun>().CountAsync());
    }

    // Compara conteúdo das linhas por igualdade de valor, não referências dos arrays de snapshots.
    private static void AssertStateUnchanged(PersistedState expected, PersistedState actual)
    {
        CollectionAssert.AreEqual(expected.Users, actual.Users);
        CollectionAssert.AreEqual(expected.Connections, actual.Connections);
        CollectionAssert.AreEqual(expected.Identities, actual.Identities);
        Assert.AreEqual(expected.Cards, actual.Cards);
        Assert.AreEqual(expected.Transactions, actual.Transactions);
        Assert.AreEqual(expected.Annotations, actual.Annotations);
        Assert.AreEqual(expected.SyncRuns, actual.SyncRuns);
    }

    // O snapshot preserva titular, item, IDs, horários e histórico de aposentadoria sem serialização externa.
    private sealed record PersistedState(
        Guid[] Users,
        ConnectionSnapshot[] Connections,
        IdentitySnapshot[] Identities,
        int Cards,
        int Transactions,
        int Annotations,
        int SyncRuns);

    private sealed record ConnectionSnapshot(
        Guid Id,
        Guid UserId,
        string Provider,
        string ExternalItemId,
        string? SourceStatus,
        DateTimeOffset? ExpiresAtUtc,
        DateTimeOffset? LastAttemptAtUtc,
        DateTimeOffset? LastSuccessfulImportAtUtc,
        DateTimeOffset? SourceUpdatedAtUtc);

    private sealed record IdentitySnapshot(
        Guid ConnectionId,
        Guid UserId,
        string Provider,
        string ExternalItemId,
        DateTimeOffset? RetiredAtUtc);
}
