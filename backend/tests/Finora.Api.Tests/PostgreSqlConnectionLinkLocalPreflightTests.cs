using System.Data;
using System.Net;
using System.Text;
using Finora.Application.AdministrativeConnections;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.AdministrativeConnections;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Finora.Infrastructure.Pluggy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Usa apenas o PostgreSQL descartável allowlisted e respostas Pluggy sintéticas; nenhum teste chama o provedor real.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlConnectionLinkLocalPreflightTests
{
    private const string SyntheticPassword = "Synthetic!123";
    private const string ExternalItemId = "external-item-private-sentinel";
    private const string ExternalAccountId = "external-card-private-sentinel";

    private readonly SaveChangesCounter _saveChangesCounter = new();
    private DisposablePostgresMigrationFixture? _fixture;
    private ServiceProvider? _serviceProvider;

    // Aplica o schema completo somente no banco de integração validado pela fixture existente.
    [TestInitialize]
    public async Task InitializeAsync()
    {
        _fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await _fixture.ResetToEmptySchemaAsync();
        await _fixture.Context.Database.MigrateAsync();

        var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISaveChangesInterceptor>(_saveChangesCounter);
        services.AddFinoraIdentity(connectionString);
        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    // Libera escopos/conexões sem limpar o banco fora do reset explícito da fixture descartável.
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    // Um titular e um cartão inequívoco produzem somente dados internos para a próxima etapa, sem escrita ou transação.
    [TestMethod]
    public async Task ExactlyOneOwnerAndEligibleCardReturnReadyWithoutDatabaseWrites()
    {
        var userId = await CreateUserAsync("synthetic-owner@example.test");
        var execution = await RunPreflightAsync(ValidReplies(AccountsPage(Account(ExternalAccountId))));

        Assert.IsNull(execution.Error);
        Assert.AreEqual(ConnectionLinkLocalPreflightState.Ready, execution.Result!.State);
        Assert.AreEqual(userId, execution.Result.UserId);
        Assert.AreEqual(ExternalItemId, execution.Result.ExternalItemId);
        Assert.AreEqual(ExternalAccountId, execution.Result.ExternalAccountId);
        Assert.IsFalse(execution.Result.ToString().Contains(ExternalItemId, StringComparison.Ordinal));
        Assert.IsFalse(execution.Result.ToString().Contains(ExternalAccountId, StringComparison.Ordinal));
        Assert.AreEqual(3, execution.RequestPaths.Length);
        Assert.IsFalse(execution.RequestPaths.Any(path => path.Contains("v2/transactions", StringComparison.Ordinal)));
    }

    // Sem titular, a consulta falha antes de autenticar a Pluggy e não escolhe uma identidade arbitrária.
    [TestMethod]
    public async Task MissingOwnerFailsBeforeAnyPluggyRequestOrWrite()
    {
        var execution = await RunPreflightAsync([]);

        Assert.IsNull(execution.Error);
        Assert.AreEqual(ConnectionLinkLocalPreflightState.OwnerMissing, execution.Result!.State);
        Assert.AreEqual(0, execution.RequestPaths.Length);
    }

    // Mais de um titular fecha a operação antes da origem externa, mesmo que exista um item válido configurado.
    [TestMethod]
    public async Task MultipleOwnersFailBeforeAnyPluggyRequestOrWrite()
    {
        await CreateUserAsync("synthetic-owner-a@example.test");
        await CreateUserAsync("synthetic-owner-b@example.test");

        var execution = await RunPreflightAsync([]);

        Assert.IsNull(execution.Error);
        Assert.AreEqual(ConnectionLinkLocalPreflightState.OwnerAmbiguous, execution.Result!.State);
        Assert.AreEqual(0, execution.RequestPaths.Length);
    }

    // Rejeição de autenticação e indisponibilidade externa mantêm intacto o estado persistido do titular.
    [TestMethod]
    public async Task AuthenticationRejectionAndProviderUnavailableFailWithoutWrites()
    {
        await CreateUserAsync("synthetic-owner@example.test");

        var authenticationRejected = await RunPreflightAsync(
            [JsonResponse(HttpStatusCode.Unauthorized, "{\"error\":\"private-auth-sentinel\"}")]);
        var unavailable = await RunPreflightAsync(
            [JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"private-provider-sentinel\"}")]);

        Assert.AreEqual(
            ConnectionLinkLocalPreflightState.AuthenticationRejected,
            authenticationRejected.Result!.State);
        Assert.AreEqual(PluggyLocalReadFailureCategory.Unauthorized, authenticationRejected.Result.FailureCategory);
        Assert.AreEqual(ConnectionLinkLocalPreflightState.ExternalFailure, unavailable.Result!.State);
        Assert.AreEqual(PluggyLocalReadFailureCategory.ProviderUnavailable, unavailable.Result.FailureCategory);
        Assert.IsNull(authenticationRejected.Error);
        Assert.IsNull(unavailable.Error);
        Assert.AreEqual(1, authenticationRejected.RequestPaths.Length);
        Assert.AreEqual(1, unavailable.RequestPaths.Length);
    }

    // Estado do item que exige nova autorização bloqueia a pré-consulta sem tentar ler contas ou transações.
    [TestMethod]
    public async Task BlockedItemAuthorizationFailsWithoutWrites()
    {
        await CreateUserAsync("synthetic-owner@example.test");
        var replies = new[]
        {
            JsonResponse(HttpStatusCode.OK, "{\"apiKey\":\"synthetic-api-key\"}"),
            JsonResponse(HttpStatusCode.OK, Item("WAITING_USER_INPUT"))
        };

        var execution = await RunPreflightAsync(replies);

        Assert.IsNull(execution.Error);
        Assert.AreEqual(ConnectionLinkLocalPreflightState.ItemBlocked, execution.Result!.State);
        Assert.AreEqual(PluggyLocalReadFailureCategory.UnsupportedState, execution.Result.FailureCategory);
        Assert.AreEqual(2, execution.RequestPaths.Length);
    }

    // Ausência ou multiplicidade de cartões elegíveis nunca escolhe uma conta por posição ou aparência.
    [TestMethod]
    public async Task MissingOrAmbiguousCardFailsWithoutWrites()
    {
        await CreateUserAsync("synthetic-owner@example.test");

        var missingCard = await RunPreflightAsync(ValidReplies(AccountsPage()));
        var ambiguousCard = await RunPreflightAsync(ValidReplies(
            AccountsPage(Account("card-private-a"), Account("card-private-b"))));

        Assert.AreEqual(ConnectionLinkLocalPreflightState.CardSelectionInvalid, missingCard.Result!.State);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidAccountSelection, missingCard.Result.FailureCategory);
        Assert.AreEqual(ConnectionLinkLocalPreflightState.CardSelectionInvalid, ambiguousCard.Result!.State);
        Assert.AreEqual(PluggyLocalReadFailureCategory.InvalidAccountSelection, ambiguousCard.Result.FailureCategory);
        Assert.IsNull(missingCard.Error);
        Assert.IsNull(ambiguousCard.Error);
        Assert.AreEqual(3, missingCard.RequestPaths.Length);
        Assert.AreEqual(3, ambiguousCard.RequestPaths.Length);
    }

    // O orçamento total encerra chamadas externas bloqueadas; cancelamento explícito do operador continua propagado.
    [TestMethod]
    public async Task ExternalDeadlineReturnsTimeoutAndCallerCancellationPropagatesWithoutWrites()
    {
        await CreateUserAsync("synthetic-owner@example.test");

        var timedOut = await RunPreflightAsync(
            [],
            blockRequests: true,
            pluggyTimeout: TimeSpan.FromMilliseconds(50));
        using var cancellation = new CancellationTokenSource();
        var cancelled = await RunPreflightAsync(
            [],
            blockRequests: true,
            pluggyTimeout: TimeSpan.FromSeconds(2),
            requestStarted: cancellation.Cancel,
            cancellationToken: cancellation.Token);

        Assert.IsNull(timedOut.Error);
        Assert.AreEqual(ConnectionLinkLocalPreflightState.ExternalFailure, timedOut.Result!.State);
        Assert.AreEqual(PluggyLocalReadFailureCategory.Timeout, timedOut.Result.FailureCategory);
        Assert.IsInstanceOfType<OperationCanceledException>(cancelled.Error);
        Assert.IsNull(cancelled.Result);
        Assert.AreEqual(1, timedOut.RequestPaths.Length);
        Assert.AreEqual(1, cancelled.RequestPaths.Length);
    }

    // Cria titulares sintéticos pelo mesmo store Identity e não expõe as credenciais do fixture em testes.
    private async Task<Guid> CreateUserAsync(string email)
    {
        using var scope = _serviceProvider!.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };
        var result = await userManager.CreateAsync(user, SyntheticPassword);
        Assert.IsTrue(result.Succeeded);
        return user.Id;
    }

    // Executa a consulta com interceptor de SaveChanges e compara o grafo persistido antes/depois.
    private async Task<PreflightExecution> RunPreflightAsync(
        IReadOnlyList<HttpResponseMessage> replies,
        bool blockRequests = false,
        TimeSpan? pluggyTimeout = null,
        Action? requestStarted = null,
        CancellationToken cancellationToken = default)
    {
        var handler = new SyntheticPluggyHandler(replies, blockRequests, requestStarted);
        var options = new PluggyOptions("synthetic-client", "synthetic-secret", ExternalItemId, null);
        using var client = new PluggyApiClient(
            new HttpClient(handler) { BaseAddress = PluggyApiClient.BaseAddress },
            options);
        using var scope = _serviceProvider!.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        var before = await ReadSnapshotAsync(context);
        context.ChangeTracker.Clear();
        _saveChangesCounter.Reset();

        ConnectionLinkLocalPreflightResult? result = null;
        Exception? error = null;
        try
        {
            var preflight = new ConnectionLinkLocalPreflightHandler(
                context,
                new PluggyConnectionInspector(client),
                options,
                TimeSpan.FromSeconds(30),
                pluggyTimeout ?? TimeSpan.FromSeconds(2));
            result = await preflight.Handle(new ConnectionLinkLocalPreflightQuery(), cancellationToken);
        }
        catch (Exception exception)
        {
            error = exception;
        }

        var after = await ReadSnapshotAsync(context);
        Assert.AreEqual(before, after);
        Assert.AreEqual(0, _saveChangesCounter.Count);
        Assert.AreEqual(0, context.ChangeTracker.Entries().Count());
        Assert.IsNull(context.Database.CurrentTransaction);
        Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);
        return new PreflightExecution(result, error, handler.RequestPaths.ToArray());
    }

    // Fotografa somente contagens para provar que a pré-consulta não cria linhas financeiras nem de sincronização.
    private static async Task<DatabaseSnapshot> ReadSnapshotAsync(FinoraDbContext context) => new(
        await context.Users.CountAsync(),
        await context.Set<FinancialConnection>().CountAsync(),
        await context.Set<FinancialConnectionItemIdentity>().CountAsync(),
        await context.Set<CardAccount>().CountAsync(),
        await context.Set<FinancialTransaction>().CountAsync(),
        await context.Set<SyncRun>().CountAsync());

    // Fornece autenticação e item válidos seguidos de uma página de contas estritamente sintética.
    private static HttpResponseMessage[] ValidReplies(string accountsPage) =>
    [
        JsonResponse(HttpStatusCode.OK, "{\"apiKey\":\"synthetic-api-key\"}"),
        JsonResponse(HttpStatusCode.OK, Item("UPDATED")),
        JsonResponse(HttpStatusCode.OK, accountsPage)
    ];

    // Monta somente os campos mínimos que o inspector usa para validar autorização e estado do item.
    private static string Item(string status) =>
        $"{{\"status\":\"{status}\",\"executionStatus\":\"SUCCESS\",\"consentExpiresAt\":null,\"error\":null}}";

    // Monta uma página completa de contas sem valores financeiros nem outros metadados do titular.
    private static string AccountsPage(params string[] accounts) =>
        $"{{\"page\":1,\"total\":{accounts.Length},\"totalPages\":1,\"results\":[{string.Join(',', accounts)}]}}";

    // Cada conta sintética pertence ao item protegido e usa somente os campos necessários à seleção.
    private static string Account(string id) =>
        $"{{\"id\":\"{id}\",\"type\":\"CREDIT\",\"subtype\":\"CREDIT_CARD\",\"itemId\":\"{ExternalItemId}\"}}";

    // Mantém payloads e status exclusivamente no handler do teste.
    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    // Guarda apenas caminhos requisitados para demonstrar que a pré-consulta nunca lê transações.
    private sealed class SyntheticPluggyHandler(
        IReadOnlyList<HttpResponseMessage> replies,
        bool blockRequests,
        Action? requestStarted) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _replies = new(replies);

        public List<string> RequestPaths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri!.PathAndQuery);
            requestStarted?.Invoke();
            if (blockRequests)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return _replies.Count == 0
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : _replies.Dequeue();
        }
    }

    // Conta tentativas de persistência separadamente das alterações de estado observadas no banco descartável.
    private sealed class SaveChangesCounter : SaveChangesInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }

    // Retém somente resultados de teste e paths sem copiar corpos JSON ou headers de autenticação.
    private sealed record PreflightExecution(
        ConnectionLinkLocalPreflightResult? Result,
        Exception? Error,
        string[] RequestPaths);

    // Compara contagens que representam toda a fronteira coberta pela pré-consulta.
    private sealed record DatabaseSnapshot(
        int Owners,
        int Connections,
        int ItemIdentities,
        int Cards,
        int Transactions,
        int SyncRuns);
}
