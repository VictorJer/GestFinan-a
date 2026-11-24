using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Prova no PostgreSQL a distinção entre sucesso correlato, legado, execução mais nova e cobertura desconhecida.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlTransactionMovementCoverageTests
{
    private static readonly Guid UserId = Guid.Parse("4375abaf-4779-4286-8fed-c551118b93ef");
    private static readonly Guid ConnectionId = Guid.Parse("9f0eef44-f4d8-4754-a9cf-f82924a99c99");
    private static readonly Guid SuccessRunId = Guid.Parse("a762e5cc-b01d-4bb6-a996-547b8351980a");
    private static readonly Guid LatestRunId = Guid.Parse("2f1c956e-f0a0-49fc-ae19-88b75902118d");
    private static readonly DateTimeOffset SuccessAtUtc = new(2026, 11, 23, 11, 0, 0, TimeSpan.Zero);

    // Sem sucesso persistido, a contagem vazia não fabrica janela nem execução conhecida.
    [TestMethod]
    public async Task NoConfirmedSuccessLeavesCoverageUnknown()
    {
        await using var fixture = await CreateFixtureAsync();
        await SeedConnectionAsync(fixture.Context, lastSuccessfulImportAtUtc: null);

        var result = await ReadCoverageAsync(fixture.Context);

        Assert.AreEqual(0, result.FilteredCount);
        Assert.IsNotNull(result.Coverage);
        Assert.IsNull(result.Coverage.LastSuccessfulImportAtUtc);
        Assert.IsNull(result.Coverage.LastSuccessfulWindow);
        Assert.IsNull(result.Coverage.LatestExecution);
        var dashboard = await ReadDashboardAsync(fixture.Context);
        Assert.AreEqual(0, dashboard.FilteredCount);
        Assert.AreEqual(result.Coverage, dashboard.Coverage);
    }

    // Um timestamp legado sem uma única SyncRun própria com mesmo fim não atribui uma janela aproximada.
    [TestMethod]
    public async Task LegacySuccessWithoutCorrelatedRunDoesNotInventWindow()
    {
        await using var fixture = await CreateFixtureAsync();
        await SeedConnectionAsync(fixture.Context, SuccessAtUtc);

        var result = await ReadCoverageAsync(fixture.Context);

        Assert.AreEqual(SuccessAtUtc, result.Coverage?.LastSuccessfulImportAtUtc);
        Assert.IsNull(result.Coverage?.LastSuccessfulWindow);
        Assert.IsNull(result.Coverage?.LatestExecution);
        var dashboard = await ReadDashboardAsync(fixture.Context);
        Assert.AreEqual(result.Coverage, dashboard.Coverage);
    }

    // A janela só é exposta quando uma execução concluída corresponde ao instante do último sucesso da conexão.
    [TestMethod]
    public async Task CorrelatedCompleteSuccessProjectsRequestedAndObservedWindow()
    {
        await using var fixture = await CreateFixtureAsync();
        await SeedConnectionAsync(fixture.Context, SuccessAtUtc);
        fixture.Context.Set<SyncRun>().Add(CreateSuccessfulRun(SuccessRunId, SuccessAtUtc.AddMinutes(-2), SuccessAtUtc));
        await fixture.Context.SaveChangesAsync();

        var result = await ReadCoverageAsync(fixture.Context);
        var window = result.Coverage?.LastSuccessfulWindow;

        Assert.AreEqual(SuccessAtUtc, result.Coverage?.LastSuccessfulImportAtUtc);
        Assert.IsNotNull(window);
        Assert.AreEqual(new DateOnly(2026, 11, 1), window.RequestedFrom);
        Assert.AreEqual(new DateOnly(2026, 12, 1), window.RequestedToExclusive);
        Assert.AreEqual(new DateOnly(2026, 11, 2), window.ObservedFrom);
        Assert.AreEqual(new DateOnly(2026, 11, 20), window.ObservedTo);
        Assert.AreEqual(SyncCoverageState.CompleteForRequestedWindow, window.CoverageState);
        Assert.AreEqual(SyncRunState.Succeeded, result.Coverage?.LatestExecution?.State);
        var dashboard = await ReadDashboardAsync(fixture.Context);
        Assert.AreEqual(result.Coverage, dashboard.Coverage);
    }

    // Falha posterior não apaga janela antiga; estado recente e código desconhecido permanecem sanitizados.
    [TestMethod]
    public async Task LaterFailureKeepsOldSuccessSeparateAndSanitizesFailureCode()
    {
        await using var fixture = await CreateFixtureAsync();
        await SeedConnectionAsync(fixture.Context, SuccessAtUtc);
        fixture.Context.Set<SyncRun>().AddRange(
            CreateSuccessfulRun(SuccessRunId, SuccessAtUtc.AddMinutes(-2), SuccessAtUtc),
            CreateFailedRun(LatestRunId, SuccessAtUtc.AddMinutes(1)));
        await fixture.Context.SaveChangesAsync();
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SyncRun\" SET \"FailureCode\" = {"provider-message-sentinel"} WHERE \"Id\" = {LatestRunId}");

        var result = await ReadCoverageAsync(fixture.Context);

        Assert.AreEqual(SuccessAtUtc, result.Coverage?.LastSuccessfulImportAtUtc);
        Assert.AreEqual(SyncCoverageState.CompleteForRequestedWindow,
            result.Coverage?.LastSuccessfulWindow?.CoverageState);
        Assert.AreEqual(SyncRunState.Failed, result.Coverage?.LatestExecution?.State);
        Assert.AreEqual(SyncCoverageState.Partial, result.Coverage?.LatestExecution?.CoverageState);
        Assert.IsNull(result.Coverage?.LatestExecution?.FailureCode);
        var dashboard = await ReadDashboardAsync(fixture.Context);
        Assert.AreEqual(result.Coverage, dashboard.Coverage);
    }

    // A execução pendente é mostrada separadamente e não substitui nem reclassifica o sucesso previamente confirmado.
    [TestMethod]
    public async Task PendingLatestExecutionDoesNotReplaceConfirmedSuccess()
    {
        await using var fixture = await CreateFixtureAsync();
        await SeedConnectionAsync(fixture.Context, SuccessAtUtc);
        fixture.Context.Set<SyncRun>().AddRange(
            CreateSuccessfulRun(SuccessRunId, SuccessAtUtc.AddMinutes(-2), SuccessAtUtc),
            SyncRun.Create(
                LatestRunId,
                UserId,
                ConnectionId,
                SuccessAtUtc.AddMinutes(1),
                new DateOnly(2026, 11, 1),
                state: SyncRunState.Pending));
        await fixture.Context.SaveChangesAsync();

        var result = await ReadCoverageAsync(fixture.Context);

        Assert.AreEqual(SuccessAtUtc, result.Coverage?.LastSuccessfulImportAtUtc);
        Assert.AreEqual(SyncCoverageState.CompleteForRequestedWindow,
            result.Coverage?.LastSuccessfulWindow?.CoverageState);
        Assert.AreEqual(SyncRunState.Pending, result.Coverage?.LatestExecution?.State);
        Assert.AreEqual(SyncCoverageState.Unknown, result.Coverage?.LatestExecution?.CoverageState);
        Assert.IsNull(result.Coverage?.LatestExecution?.FailureCode);
        var dashboard = await ReadDashboardAsync(fixture.Context);
        Assert.AreEqual(result.Coverage, dashboard.Coverage);
    }

    // Cada teste usa schema PostgreSQL descartável atualizado explicitamente, sem migration no host da API.
    private static async Task<DisposablePostgresMigrationFixture> CreateFixtureAsync()
    {
        var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync();
        fixture.Context.Users.Add(new ApplicationUser(UserId));
        await fixture.Context.SaveChangesAsync();
        return fixture;
    }

    // A conexão de leitura mantém o mesmo titular e pode representar sucesso atual ou legado.
    private static async Task SeedConnectionAsync(
        FinoraDbContext context,
        DateTimeOffset? lastSuccessfulImportAtUtc)
    {
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            "coverage-test-item",
            lastSuccessfulImportAtUtc: lastSuccessfulImportAtUtc));
        await context.SaveChangesAsync();
    }

    // Critérios mínimos válidos exercitam a projeção de cobertura junto da contagem e página no snapshot.
    private static Task<PagedTransactionMovementSummary> ReadCoverageAsync(FinoraDbContext context) =>
        new TransactionMovementReader(context).ReadAsync(
            new ValidatedTransactionsQuery(UserId, ConnectionId, null, null, null, null, null, null, 1, 25),
            CancellationToken.None);

    // O dashboard usa os mesmos fatos de cobertura para o universo sem filtros adicionais.
    private static Task<TransactionMovementDashboardSummary> ReadDashboardAsync(FinoraDbContext context) =>
        new TransactionDashboardReader(context).ReadAsync(
            new ValidatedTransactionDashboardQuery(
                UserId,
                ConnectionId,
                null,
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);

    // O registro de sucesso tem janela e extremos conhecidos, com FinishedAtUtc igual ao último sucesso da conexão.
    private static SyncRun CreateSuccessfulRun(Guid id, DateTimeOffset createdAtUtc, DateTimeOffset finishedAtUtc) =>
        SyncRun.Create(
            id,
            UserId,
            ConnectionId,
            createdAtUtc,
            new DateOnly(2026, 11, 1),
            requestedToExclusive: new DateOnly(2026, 12, 1),
            startedAtUtc: createdAtUtc.AddMinutes(1),
            finishedAtUtc: finishedAtUtc,
            state: SyncRunState.Succeeded,
            pagesRead: 2,
            receivedCount: 5,
            uniqueCount: 4,
            persistedCount: 4,
            observedFrom: new DateOnly(2026, 11, 2),
            observedTo: new DateOnly(2026, 11, 20),
            coverageState: SyncCoverageState.CompleteForRequestedWindow);

    // A falha posterior contém apenas código interno permitido antes da prova de sanitização por SQL.
    private static SyncRun CreateFailedRun(Guid id, DateTimeOffset createdAtUtc) => SyncRun.Create(
        id,
        UserId,
        ConnectionId,
        createdAtUtc,
        new DateOnly(2026, 11, 1),
        requestedToExclusive: new DateOnly(2026, 12, 1),
        startedAtUtc: createdAtUtc.AddMinutes(1),
        finishedAtUtc: createdAtUtc.AddMinutes(2),
        state: SyncRunState.Failed,
        pagesRead: 1,
        receivedCount: 2,
        coverageState: SyncCoverageState.Partial,
        failureCode: SyncRunFailureCodes.ProviderUnavailable);
}
