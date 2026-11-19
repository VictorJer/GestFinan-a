using Finora.Application.AdministrativeUsers;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Usa somente o PostgreSQL descartável validado e serializa operações que reinicializam o schema compartilhado.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlAdministrativeUserCreationTests
{
    private DisposablePostgresMigrationFixture? _fixture;
    private ServiceProvider? _serviceProvider;

    // Aplica explicitamente o schema FNR-007 e compõe stores Identity por escopo no destino permitido.
    [TestInitialize]
    public async Task InitializeAsync()
    {
        _fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await _fixture.ResetToEmptySchemaAsync();
        await _fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);

        _serviceProvider = BuildServiceProvider();
    }

    // Descarta scopes/provider e contexto da fixture; o banco temporário não é limpo por atalhos de produção.
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

    // A criação real usa hash Identity, ID não vazio e repetição que preserva credenciais/stamps/identificador.
    [TestMethod]
    public async Task CreationHashesPasswordAndRepeatedEmailDoesNotMutateUser()
    {
        const string email = "owner@example.com";
        const string originalPassword = "Original!Pass123";
        const string repeatedPassword = "Replacement!Pass456";

        var created = await CreateAsync(email, originalPassword);
        var beforeRepeat = await ReadCredentialSnapshotAsync(email, originalPassword);
        var repeated = await CreateAsync("  OWNER@EXAMPLE.COM  ", repeatedPassword);
        var afterRepeat = await ReadCredentialSnapshotAsync(email, originalPassword, repeatedPassword);

        Assert.AreEqual(AdministrativeUserOperationState.Created, created.State);
        Assert.AreEqual(AdministrativeUserOperationState.AlreadyExists, repeated.State);
        Assert.AreNotEqual(Guid.Empty, beforeRepeat.UserId);
        Assert.AreNotEqual(originalPassword, beforeRepeat.PasswordHash);
        Assert.IsTrue(beforeRepeat.OriginalPasswordMatches);
        Assert.IsFalse(afterRepeat.RepeatedPasswordMatches);
        Assert.AreEqual(beforeRepeat.UserId, afterRepeat.UserId);
        Assert.AreEqual(beforeRepeat.PasswordHash, afterRepeat.PasswordHash);
        Assert.AreEqual(beforeRepeat.SecurityStamp, afterRepeat.SecurityStamp);
        Assert.AreEqual(beforeRepeat.ConcurrencyStamp, afterRepeat.ConcurrencyStamp);
        Assert.AreEqual(1, await CountUsersAsync());
    }

    // Duas scopes simultâneas usam contexts/conexões independentes e o lock serializa o mesmo e-mail normalizado.
    [TestMethod]
    public async Task ConcurrentCreationForSameEmailHasOneWinnerAndPreservesItsPassword()
    {
        const string firstPassword = "First!Concurrent123";
        const string secondPassword = "Second!Concurrent456";
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstConnectionReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondConnectionReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = Task.Run(() => CreateInIndependentScopeAsync(
            "race@example.com", firstPassword, firstConnectionReady, start.Task));
        var secondTask = Task.Run(() => CreateInIndependentScopeAsync(
            "  RACE@EXAMPLE.COM ", secondPassword, secondConnectionReady, start.Task));
        var backendPids = await Task.WhenAll(firstConnectionReady.Task, secondConnectionReady.Task)
            .WaitAsync(TimeSpan.FromSeconds(15));
        start.SetResult(true);
        var outcomes = await Task.WhenAll(firstTask, secondTask);
        var results = outcomes.Select(outcome => outcome.Result).ToArray();

        Assert.AreNotEqual(backendPids[0], backendPids[1]);
        Assert.AreEqual(1, results.Count(result => result.State == AdministrativeUserOperationState.Created));
        Assert.AreEqual(1, results.Count(result => result.State == AdministrativeUserOperationState.AlreadyExists));
        var winningPassword = results[0].State == AdministrativeUserOperationState.Created
            ? firstPassword
            : secondPassword;
        var losingPassword = results[0].State == AdministrativeUserOperationState.AlreadyExists
            ? firstPassword
            : secondPassword;
        var credentials = await ReadCredentialSnapshotAsync("race@example.com", winningPassword, losingPassword);

        Assert.IsTrue(credentials.OriginalPasswordMatches);
        Assert.IsFalse(credentials.RepeatedPasswordMatches);
        Assert.AreEqual(1, await CountUsersAsync());
    }

    // E-mails distintos também competem pelo mesmo lock global, impedindo a criação de um segundo titular.
    [TestMethod]
    public async Task ConcurrentCreationForDifferentEmailsLeavesOneTitular()
    {
        const string firstEmail = "first@example.com";
        const string secondEmail = "second@example.com";
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstConnectionReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondConnectionReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = Task.Run(() => CreateInIndependentScopeAsync(
            firstEmail, "First!Owner123", firstConnectionReady, start.Task));
        var secondTask = Task.Run(() => CreateInIndependentScopeAsync(
            secondEmail, "Second!Owner456", secondConnectionReady, start.Task));
        var backendPids = await Task.WhenAll(firstConnectionReady.Task, secondConnectionReady.Task)
            .WaitAsync(TimeSpan.FromSeconds(15));
        start.SetResult(true);
        var outcomes = await Task.WhenAll(firstTask, secondTask);
        var results = outcomes.Select(outcome => outcome.Result).ToArray();

        Assert.AreNotEqual(backendPids[0], backendPids[1]);
        Assert.AreEqual(1, results.Count(result => result.State == AdministrativeUserOperationState.Created));
        Assert.AreEqual(1, results.Count(result => result.State == AdministrativeUserOperationState.Conflict));
        var winningEmail = results[0].State == AdministrativeUserOperationState.Created ? firstEmail : secondEmail;
        var winningPassword = results[0].State == AdministrativeUserOperationState.Created
            ? "First!Owner123"
            : "Second!Owner456";
        var winner = await ReadCredentialSnapshotAsync(winningEmail, winningPassword);

        Assert.AreEqual(winningEmail, winner.Email, ignoreCase: true);
        Assert.IsTrue(winner.OriginalPasswordMatches);
        Assert.AreEqual(1, await CountUsersAsync());
    }

    // Cada regra da política Identity recusa a criação sem inserir usuário nem transformar erro de validação em sucesso.
    [TestMethod]
    public async Task WeakPasswordsAreRejectedWithoutPersistence()
    {
        var invalidPasswords = new[]
        {
            "Aa1!a",          // abaixo do mínimo de seis caracteres
            "lower!123",      // sem maiúscula
            "UPPER!123",      // sem minúscula
            "Lower!Case",     // sem número
            "Lowercase123",   // sem caractere não alfanumérico
            string.Empty       // também não atende ao mínimo de caracteres distintos
        };

        for (var index = 0; index < invalidPasswords.Length; index++)
        {
            var result = await CreateAsync($"weak-{index}@example.com", invalidPasswords[index]);

            Assert.AreEqual(AdministrativeUserOperationState.PasswordPolicyViolation, result.State);
            Assert.AreEqual(0, await CountUsersAsync());
        }
    }

    // Uma falha injetada depois do SaveChanges mas antes do commit precisa reverter o INSERT e liberar o lock.
    [TestMethod]
    public async Task FailureAfterIdentitySaveRollsBackAndReleasesAdministrativeLock()
    {
        var interceptor = new FailAfterFirstSaveChangesInterceptor();
        await using var provider = BuildServiceProvider(interceptor);

        var failed = await CreateAsync("rollback@example.com", "Rollback!Pass123", provider);
        Assert.AreEqual(AdministrativeUserOperationState.OperationalFailure, failed.State);
        Assert.IsTrue(interceptor.WasTriggered);
        Assert.AreEqual(0, await CountUsersAsync(provider));

        var retry = await CreateAsync("rollback@example.com", "Rollback!Pass123", provider);

        Assert.AreEqual(AdministrativeUserOperationState.Created, retry.State);
        Assert.AreEqual(1, await CountUsersAsync(provider));
    }

    // Timeout de lock é operacional, não faz retry automático e uma nova invocação funciona após liberar o bloqueio.
    [TestMethod]
    public async Task LockWaitTimeoutReturnsFailureAndDoesNotRetryAutomatically()
    {
        await using var blockerConnection = await OpenTestConnectionAsync();
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        await AcquireAdministrativeLockAsync(blockerConnection, blockerTransaction);
        await using var shortTimeoutProvider = BuildServiceProvider(
            databaseOperationTimeout: TimeSpan.FromSeconds(1.5));

        var timedOperation = CreateAsync(
            "timeout@example.com",
            "Timeout!Pass123",
            shortTimeoutProvider);
        Assert.IsTrue(await WaitForAdvisoryLockWaiterAsync(blockerConnection, TimeSpan.FromSeconds(10)));

        var timedOut = await timedOperation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(AdministrativeUserOperationState.OperationalFailure, timedOut.State);
        Assert.AreEqual(0, await CountUsersAsync(shortTimeoutProvider));

        await blockerTransaction.CommitAsync();
        var explicitRetry = await CreateAsync("timeout@example.com", "Timeout!Pass123");
        Assert.AreEqual(AdministrativeUserOperationState.Created, explicitRetry.State);
    }

    // Cancelamento enquanto a conexão espera o lock propaga ao chamador e não deixa credencial persistida.
    [TestMethod]
    public async Task CancellationWhileWaitingForLockRollsBackAndReleasesScope()
    {
        await using var blockerConnection = await OpenTestConnectionAsync();
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        await AcquireAdministrativeLockAsync(blockerConnection, blockerTransaction);
        using var cancellation = new CancellationTokenSource();

        var cancelledOperation = CreateAsync(
            "cancelled@example.com",
            "Cancelled!Pass123",
            provider: null,
            cancellationToken: cancellation.Token);
        Assert.IsTrue(await WaitForAdvisoryLockWaiterAsync(blockerConnection, TimeSpan.FromSeconds(10)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledOperation);
        Assert.AreEqual(0, await CountUsersAsync());

        await blockerTransaction.CommitAsync();
        var explicitRetry = await CreateAsync("cancelled@example.com", "Cancelled!Pass123");
        Assert.AreEqual(AdministrativeUserOperationState.Created, explicitRetry.State);
    }

    // Cada operação mantém um backend PostgreSQL distinto aberto antes do início simultâneo e do lock transacional.
    private async Task<ConcurrentCreationOutcome> CreateInIndependentScopeAsync(
        string email,
        string password,
        TaskCompletionSource<int> connectionReady,
        Task start)
    {
        using var scope = _serviceProvider!.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>();
        var context = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        await context.Database.OpenConnectionAsync();
        var backendPid = await ReadBackendPidAsync(context);
        connectionReady.SetResult(backendPid);
        await start;

        var result = await access.CreateAsync(email, password, CancellationToken.None);
        return new ConcurrentCreationOutcome(result, backendPid);
    }

    // O PID identifica a sessão PostgreSQL realmente ocupada por cada DbContext concorrente.
    private static async Task<int> ReadBackendPidAsync(FinoraDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // Cria pelo contrato Infrastructure e não imprime e-mail, senha ou mensagens Identity em falhas.
    private async Task<AdministrativeUserOperationResult> CreateAsync(string email, string password)
        => await CreateAsync(email, password, null, CancellationToken.None);

    // Permite que testes de falha usem provider/interceptor ou timeout interno sem expor opção ao ambiente de runtime.
    private async Task<AdministrativeUserOperationResult> CreateAsync(
        string email,
        string password,
        IServiceProvider? provider,
        CancellationToken cancellationToken = default)
    {
        using var scope = (provider ?? _serviceProvider!).CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>();
        return await access.CreateAsync(email, password, cancellationToken);
    }

    // Lê somente valores sintéticos para comparar hash, stamps e senha em um contexto independente.
    private async Task<CredentialSnapshot> ReadCredentialSnapshotAsync(
        string email,
        string passwordToCheck,
        string? secondPasswordToCheck = null,
        IServiceProvider? provider = null)
    {
        using var scope = (provider ?? _serviceProvider!).CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        return new CredentialSnapshot(
            user.Id,
            user.Email!,
            user.PasswordHash!,
            user.SecurityStamp!,
            user.ConcurrencyStamp!,
            await userManager.CheckPasswordAsync(user, passwordToCheck),
            secondPasswordToCheck is not null
                && await userManager.CheckPasswordAsync(user, secondPasswordToCheck));
    }

    // Conta usuários persistidos em uma scope nova para comprovar a unicidade observável no PostgreSQL.
    private async Task<int> CountUsersAsync(IServiceProvider? provider = null)
    {
        using var scope = (provider ?? _serviceProvider!).CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinoraDbContext>()
            .Users
            .AsNoTracking()
            .CountAsync();
    }

    // Compõe provider de teste com hooks locais; nenhum valor é carregado de configuração de runtime.
    private ServiceProvider BuildServiceProvider(
        ISaveChangesInterceptor? interceptor = null,
        TimeSpan? databaseOperationTimeout = null)
    {
        var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        if (interceptor is not null)
        {
            services.AddSingleton(interceptor);
        }

        services.AddFinoraAdministrativeIdentity(connectionString);
        if (databaseOperationTimeout is { } timeout)
        {
            // O construtor interno reduz o timeout somente neste provider descartável.
            services.AddScoped<IAdministrativeUserAccess>(serviceProvider => new AdministrativeUserAccessService(
                serviceProvider.GetRequiredService<FinoraDbContext>(),
                serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                timeout));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    // Abre uma sessão PostgreSQL apenas com a connection string que a barreira descartável validou.
    private static async Task<NpgsqlConnection> OpenTestConnectionAsync()
    {
        var connection = new NpgsqlConnection(DisposablePostgresTestConfiguration.ReadRequired().ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    // Usa a mesma chave fixa da Infrastructure para manter um bloqueio transacional durante o ensaio.
    private static async Task AcquireAdministrativeLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@lock_key)",
            connection,
            transaction);
        command.Parameters.AddWithValue("lock_key", 0x46494E4F5241L);
        await command.ExecuteScalarAsync();
    }

    // Observa a espera real pelo advisory lock antes de testar timeout/cancelamento.
    private static async Task<bool> WaitForAdvisoryLockWaiterAsync(
        NpgsqlConnection observerConnection,
        TimeSpan maximumWait)
    {
        var deadline = DateTime.UtcNow + maximumWait;
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("""
                SELECT COUNT(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND usename = current_user
                  AND wait_event_type = 'Lock'
                  AND wait_event = 'advisory';
                """, observerConnection);
            var waitingSessions = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (waitingSessions > 0)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return false;
    }

    // Mantém no teste somente campos necessários às comparações, sem exibir credenciais.
    private sealed record CredentialSnapshot(
        Guid UserId,
        string Email,
        string PasswordHash,
        string SecurityStamp,
        string ConcurrencyStamp,
        bool OriginalPasswordMatches,
        bool RepeatedPasswordMatches);

    // Mantém o resultado associado à sessão independente que executou cada criação concorrente.
    private sealed record ConcurrentCreationOutcome(
        AdministrativeUserOperationResult Result,
        int BackendPid);

    // Lança uma única falha após o INSERT ter sido salvo dentro da transação externa, antes do commit administrativo.
    private sealed class FailAfterFirstSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int _shouldFail = 1;

        public bool WasTriggered => Volatile.Read(ref _shouldFail) == 0;

        // A exceção posterior ao SaveChanges permite provar que a transação externa desfaz a gravação concluída.
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _shouldFail, 0) == 1)
            {
                throw new InvalidOperationException("falha sintética após SaveChanges");
            }

            return ValueTask.FromResult(result);
        }
    }
}
