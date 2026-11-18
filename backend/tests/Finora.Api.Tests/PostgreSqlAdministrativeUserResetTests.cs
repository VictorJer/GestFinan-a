using Finora.Api.AdministrativeUsers;
using Finora.Application.AdministrativeUsers;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Usa o PostgreSQL descartável validado para confirmar reset Identity e preservação de vínculos financeiros.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlAdministrativeUserResetTests
{
    private static readonly Guid UserId = Guid.Parse("c3044f54-fef4-44bb-90f8-efbf4c925c1c");
    private static readonly Guid ConnectionId = Guid.Parse("c6dcf020-1c81-4fd4-b06b-a0a89caa6d2b");
    private static readonly Guid AccountId = Guid.Parse("bd05f7b6-a6ed-4fb2-a959-5f723b4745ad");
    private static readonly Guid TransactionId = Guid.Parse("adce680e-6b4f-49c8-89b4-3cf7a9f25d2e");
    private static readonly Guid CategoryId = Guid.Parse("52279684-30ec-4b87-9c53-165ba8a98623");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);
    private static readonly DateTimeOffset UpdatedAtUtc = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private const string NormalizationVersion = "administrative-reset-test-v1";
    private const string Email = "reset-owner@example.com";
    private const string OldPassword = "Old!Password123";
    private const string NewPassword = "New!Password456";
    private const string ConcurrentPasswordOne = "Concurrent!One123";
    private const string ConcurrentPasswordTwo = "Concurrent!Two456";
    private const long AdministrativeLockKey = 0x46494E4F5241L;

    private DisposablePostgresMigrationFixture? _fixture;
    private ServiceProvider? _serviceProvider;

    // Aplica o schema aprovado explicitamente e registra Identity mais o provider efêmero do comando administrativo.
    [TestInitialize]
    public async Task InitializeAsync()
    {
        _fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await _fixture.ResetToEmptySchemaAsync();
        await _fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);

        var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdministrativeUserIdentity(connectionString);
        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    // Descarta primeiro os serviços Identity e depois a fixture, sem apagar dados fora do database descartável.
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

    // O reset usa token Identity em memória, troca credencial/stamp e preserva a identidade e todo o grafo financeiro.
    [TestMethod]
    public async Task ResetChangesCredentialAndSecurityStampWithoutChangingIdentityOrFinancialLinks()
    {
        var originalSecurityStamp = await SeedUserAndFinancialGraphAsync();
        AdministrativeUserOperationResult result;
        using (var scope = _serviceProvider!.CreateScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>();
            result = await access.ResetPasswordAsync(Email, UserId, NewPassword, CancellationToken.None);
        }

        Assert.AreEqual(AdministrativeUserOperationState.PasswordReset, result.State);
        await AssertResetCredentialAsync(originalSecurityStamp);
        await AssertFinancialGraphUnchangedAsync();
    }

    // Um e-mail válido sem titular resulta em not-found e não cria identidade nem credencial.
    [TestMethod]
    public async Task ResetForMissingUserReturnsNotFoundWithoutCreatingUser()
    {
        var result = await ResetAsync(Email, UserId, NewPassword);

        Assert.AreEqual(AdministrativeUserOperationState.NotFound, result.State);
        Assert.AreEqual(0, await CountUsersAsync());
    }

    // Um banco ambíguo fecha o reset sem escolher qual das identidades existentes alterar.
    [TestMethod]
    public async Task ResetWithMultipleUsersReturnsConflictWithoutChangingEitherCredential()
    {
        var first = await CreateIdentityUserAsync(Email, OldPassword, UserId);
        var second = await CreateIdentityUserAsync(
            "second-reset-owner@example.com",
            "Second!Password123",
            Guid.NewGuid());

        var result = await ResetAsync(Email, first.Id, NewPassword);
        var firstCredentials = await ReadCredentialSnapshotAsync(Email, OldPassword, NewPassword);
        var secondCredentials = await ReadCredentialSnapshotAsync(
            "second-reset-owner@example.com",
            "Second!Password123",
            NewPassword);

        Assert.AreEqual(AdministrativeUserOperationState.Conflict, result.State);
        Assert.IsTrue(firstCredentials.PasswordMatches[0]);
        Assert.IsFalse(firstCredentials.PasswordMatches[1]);
        Assert.IsTrue(secondCredentials.PasswordMatches[0]);
        Assert.AreEqual(2, await CountUsersAsync());
    }

    // Recriar o e-mail com outro UUID após a pré-consulta retorna conflito sem redefinir a identidade substituta.
    [TestMethod]
    public async Task ResetRejectsStalePreflightWhenEmailNowBelongsToAnotherUserId()
    {
        var original = await CreateIdentityUserAsync(Email, OldPassword, UserId);
        using (var scope = _serviceProvider!.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var deleted = await userManager.DeleteAsync(original);
            Assert.IsTrue(deleted.Succeeded);
        }

        var replacement = await CreateIdentityUserAsync(Email, "Replacement!Pass123", Guid.NewGuid());
        var result = await ResetAsync(Email, UserId, NewPassword);
        var credentials = await ReadCredentialSnapshotAsync(Email, "Replacement!Pass123", NewPassword);

        Assert.AreEqual(AdministrativeUserOperationState.Conflict, result.State);
        Assert.IsTrue(credentials.PasswordMatches[0]);
        Assert.IsFalse(credentials.PasswordMatches[1]);
        Assert.AreEqual(replacement.Id, credentials.UserId);
    }

    // A política Identity recusa senha fraca e mantém hash/security stamp e senha anterior intactos.
    [TestMethod]
    public async Task InvalidPasswordReturnsPolicyFailureWithoutChangingCredential()
    {
        var user = await CreateIdentityUserAsync(Email, OldPassword, UserId);
        var original = await ReadCredentialSnapshotAsync(Email, OldPassword, NewPassword);

        var result = await ResetAsync(Email, user.Id, "weak");
        var after = await ReadCredentialSnapshotAsync(Email, OldPassword, "weak");

        Assert.AreEqual(AdministrativeUserOperationState.PasswordPolicyViolation, result.State);
        Assert.IsTrue(after.PasswordMatches[0]);
        Assert.IsFalse(after.PasswordMatches[1]);
        Assert.AreEqual(original.PasswordHash, after.PasswordHash);
        Assert.AreEqual(original.SecurityStamp, after.SecurityStamp);
    }

    // Timeout aguardando o lock é operacional, não repete o reset e permite nova tentativa explícita após liberação.
    [TestMethod]
    public async Task LockWaitTimeoutReturnsFailureWithoutChangingCredentialOrRetrying()
    {
        await CreateIdentityUserAsync(Email, OldPassword, UserId);
        await using var blockerConnection = await OpenTestConnectionAsync();
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        await AcquireAdministrativeLockAsync(blockerConnection, blockerTransaction);
        await using var observerConnection = await OpenTestConnectionAsync();
        await using var shortTimeoutProvider = BuildServiceProvider(
            databaseOperationTimeout: TimeSpan.FromSeconds(3));

        var timedOperation = ResetAsync(Email, UserId, NewPassword, shortTimeoutProvider);
        Assert.IsTrue(await WaitForAdvisoryLockWaiterAsync(observerConnection, TimeSpan.FromSeconds(10)));
        var timedOut = await timedOperation.WaitAsync(TimeSpan.FromSeconds(5));
        var afterTimeout = await ReadCredentialSnapshotAsync(Email, OldPassword, NewPassword);

        Assert.AreEqual(AdministrativeUserOperationState.OperationalFailure, timedOut.State);
        Assert.IsTrue(afterTimeout.PasswordMatches[0]);
        Assert.IsFalse(afterTimeout.PasswordMatches[1]);

        await blockerTransaction.CommitAsync();
        var explicitRetry = await ResetAsync(Email, UserId, NewPassword);
        Assert.AreEqual(AdministrativeUserOperationState.PasswordReset, explicitRetry.State);
    }

    // Cancelamento durante espera pelo lock propaga ao chamador e não grava credencial antes da tentativa explícita.
    [TestMethod]
    public async Task CancellationWhileWaitingForLockLeavesCredentialUnchangedAndReleasesLock()
    {
        await CreateIdentityUserAsync(Email, OldPassword, UserId);
        await using var blockerConnection = await OpenTestConnectionAsync();
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        await AcquireAdministrativeLockAsync(blockerConnection, blockerTransaction);
        await using var observerConnection = await OpenTestConnectionAsync();
        using var cancellation = new CancellationTokenSource();

        var cancelledOperation = ResetAsync(
            Email,
            UserId,
            NewPassword,
            cancellationToken: cancellation.Token);
        Assert.IsTrue(await WaitForAdvisoryLockWaiterAsync(observerConnection, TimeSpan.FromSeconds(10)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledOperation);
        var afterCancellation = await ReadCredentialSnapshotAsync(Email, OldPassword, NewPassword);
        Assert.IsTrue(afterCancellation.PasswordMatches[0]);
        Assert.IsFalse(afterCancellation.PasswordMatches[1]);

        await blockerTransaction.CommitAsync();
        var explicitRetry = await ResetAsync(Email, UserId, NewPassword);
        Assert.AreEqual(AdministrativeUserOperationState.PasswordReset, explicitRetry.State);
    }

    // Dois escopos/conexões iniciados juntos só podem concluir uma ordem serial protegida pelo advisory lock.
    [TestMethod]
    public async Task ConcurrentResetsAreSerializedAndOnlyOneFinalPasswordIsValid()
    {
        await CreateIdentityUserAsync(Email, OldPassword, UserId);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstConnectionReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondConnectionReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(() => ResetInIndependentScopeAsync(
            ConcurrentPasswordOne, firstConnectionReady, start.Task));
        var secondTask = Task.Run(() => ResetInIndependentScopeAsync(
            ConcurrentPasswordTwo, secondConnectionReady, start.Task));
        var backendPids = await Task.WhenAll(firstConnectionReady.Task, secondConnectionReady.Task)
            .WaitAsync(TimeSpan.FromSeconds(15));
        start.SetResult(true);
        var outcomes = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(20));
        var states = outcomes.Select(outcome => outcome.Result.State).ToArray();
        var credentials = await ReadCredentialSnapshotAsync(
            Email,
            OldPassword,
            ConcurrentPasswordOne,
            ConcurrentPasswordTwo);

        Assert.AreNotEqual(backendPids[0], backendPids[1]);
        Assert.IsTrue(states.All(state => state == AdministrativeUserOperationState.PasswordReset)
            || states.Count(state => state == AdministrativeUserOperationState.PasswordReset) == 1
            && states.Count(state => state == AdministrativeUserOperationState.Conflict) == 1);
        Assert.IsFalse(credentials.PasswordMatches[0]);
        Assert.AreEqual(1, credentials.PasswordMatches.Skip(1).Count(matches => matches));
    }

    // Uma alteração de ConcurrencyStamp externa durante o save vira conflito Identity sem publicar senha parcial.
    [TestMethod]
    public async Task IdentityConcurrencyConflictDoesNotChangePasswordOrSecurityStamp()
    {
        var user = await CreateIdentityUserAsync(Email, OldPassword, UserId);
        var before = await ReadCredentialSnapshotAsync(Email, OldPassword, NewPassword);
        await using var provider = BuildServiceProvider(new ChangeConcurrencyStampBeforeSaveInterceptor(
            DisposablePostgresTestConfiguration.ReadRequired().ConnectionString,
            user.Id));

        var result = await ResetAsync(Email, user.Id, NewPassword, provider);
        var after = await ReadCredentialSnapshotAsync(Email, OldPassword, NewPassword);

        Assert.AreEqual(AdministrativeUserOperationState.Conflict, result.State);
        Assert.IsTrue(after.PasswordMatches[0]);
        Assert.IsFalse(after.PasswordMatches[1]);
        Assert.AreEqual(before.PasswordHash, after.PasswordHash);
        Assert.AreEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.AreNotEqual(before.ConcurrencyStamp, after.ConcurrencyStamp);
    }

    // Uma exceção após o SaveChanges é revertida pela transação externa e uma operação explícita posterior libera o lock.
    [TestMethod]
    public async Task FailureAfterIdentitySaveRollsBackAndAllowsAnExplicitResetRetry()
    {
        await CreateIdentityUserAsync(Email, OldPassword, UserId);
        var interceptor = new FailAfterFirstSaveChangesInterceptor();
        await using var provider = BuildServiceProvider(interceptor);

        var failed = await ResetAsync(Email, UserId, NewPassword, provider);
        var afterFailure = await ReadCredentialSnapshotAsync(Email, OldPassword, NewPassword);
        var retry = await ResetAsync(Email, UserId, NewPassword, provider);

        Assert.AreEqual(AdministrativeUserOperationState.OperationalFailure, failed.State);
        Assert.IsTrue(interceptor.WasTriggered);
        Assert.IsTrue(afterFailure.PasswordMatches[0]);
        Assert.IsFalse(afterFailure.PasswordMatches[1]);
        Assert.AreEqual(AdministrativeUserOperationState.PasswordReset, retry.State);
    }

    // Uma falha injetada após o commit real retorna falha operacional e não afirma que a senha anterior foi restaurada.
    [TestMethod]
    public async Task CommitAcknowledgementFailureReturnsOperationalFailureWithoutClaimingRollback()
    {
        await CreateIdentityUserAsync(Email, OldPassword, UserId);
        await using var provider = BuildServiceProvider(transactionInterceptor: new FailAfterCommitInterceptor());

        var result = await ResetAsync(Email, UserId, NewPassword, provider);
        var credentials = await ReadCredentialSnapshotAsync(Email, OldPassword, NewPassword);

        Assert.AreEqual(AdministrativeUserOperationState.OperationalFailure, result.State);
        Assert.IsFalse(credentials.PasswordMatches[0]);
        Assert.IsTrue(credentials.PasswordMatches[1]);
    }

    // Cria a conta pela API do Identity e semeia somente dados financeiros sintéticos associados ao mesmo titular.
    private async Task<string> SeedUserAndFinancialGraphAsync()
    {
        using var scope = _serviceProvider!.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(UserId)
        {
            UserName = Email,
            Email = Email
        };
        var identityResult = await userManager.CreateAsync(user, OldPassword);
        Assert.IsTrue(identityResult.Succeeded);

        var context = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            "administrative-reset-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().Add(CardAccount.Create(
            AccountId,
            UserId,
            ConnectionId,
            "administrative-reset-card",
            "BRL"));
        await context.SaveChangesAsync();

        context.Set<Category>().Add(Category.Create(
            CategoryId,
            UserId,
            isSystem: false,
            "Categoria sintética",
            "categoria-sintetica"));
        context.Set<FinancialTransaction>().Add(CreateFinancialTransaction());
        await context.SaveChangesAsync();

        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            TransactionId,
            UserId,
            CategoryId,
            UserId,
            "anotação sintética preservada",
            UpdatedAtUtc));
        await context.SaveChangesAsync();

        return user.SecurityStamp!;
    }

    // Confere em um escopo novo a troca de senha/stamp e a permanência do mesmo e-mail e UUID Identity.
    private async Task AssertResetCredentialAsync(string originalSecurityStamp)
    {
        using var scope = _serviceProvider!.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(UserId.ToString());

        Assert.IsNotNull(user);
        Assert.AreEqual(UserId, user.Id);
        Assert.AreEqual(Email, user.Email);
        Assert.AreEqual(Email.ToUpperInvariant(), user.NormalizedEmail);
        Assert.AreNotEqual(originalSecurityStamp, user.SecurityStamp);
        Assert.IsTrue(await userManager.CheckPasswordAsync(user, NewPassword));
        Assert.IsFalse(await userManager.CheckPasswordAsync(user, OldPassword));
    }

    // Lê o grafo financeiro sem tracking para provar que o reset não removeu nem reassociou registros do titular.
    private async Task AssertFinancialGraphUnchangedAsync()
    {
        using var scope = _serviceProvider!.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();

        var connection = await context.Set<FinancialConnection>().AsNoTracking().SingleAsync();
        Assert.AreEqual(ConnectionId, connection.Id);
        Assert.AreEqual(UserId, connection.UserId);

        var account = await context.Set<CardAccount>().AsNoTracking().SingleAsync();
        Assert.AreEqual(AccountId, account.Id);
        Assert.AreEqual(UserId, account.UserId);
        Assert.AreEqual(ConnectionId, account.ConnectionId);

        var transaction = await context.Set<FinancialTransaction>().AsNoTracking().SingleAsync();
        Assert.AreEqual(TransactionId, transaction.Id);
        Assert.AreEqual(UserId, transaction.UserId);
        Assert.AreEqual(AccountId, transaction.CardAccountId);
        Assert.AreEqual("reset-test-transaction", transaction.ExternalId);

        var annotation = await context.Set<TransactionAnnotation>().AsNoTracking().SingleAsync();
        Assert.AreEqual(TransactionId, annotation.TransactionId);
        Assert.AreEqual(UserId, annotation.UserId);
        Assert.AreEqual(CategoryId, annotation.CategoryId);
        Assert.AreEqual(UserId, annotation.CategoryScopeId);
        Assert.AreEqual("anotação sintética preservada", annotation.Note);
    }

    // Cria usuários sintéticos por UserManager para testar ausências, ambiguidades e identidades substituídas.
    private async Task<ApplicationUser> CreateIdentityUserAsync(string email, string password, Guid userId)
    {
        using var scope = _serviceProvider!.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(userId)
        {
            UserName = email,
            Email = email
        };
        var result = await userManager.CreateAsync(user, password);

        Assert.IsTrue(result.Succeeded);
        return user;
    }

    // Executa o contrato administrativo em escopo novo, preservando o cancellation token de cada cenário.
    private async Task<AdministrativeUserOperationResult> ResetAsync(
        string email,
        Guid expectedUserId,
        string password,
        IServiceProvider? provider = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = (provider ?? _serviceProvider!).CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>();
        return await access.ResetPasswordAsync(email, expectedUserId, password, cancellationToken);
    }

    // Fotografa hash/stamps e valida várias senhas por Identity sem expor credenciais em mensagens de teste.
    private async Task<CredentialSnapshot> ReadCredentialSnapshotAsync(string email, params string[] passwords)
    {
        using var scope = _serviceProvider!.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var passwordMatches = new bool[passwords.Length];
        for (var index = 0; index < passwords.Length; index++)
        {
            passwordMatches[index] = await userManager.CheckPasswordAsync(user, passwords[index]);
        }

        return new CredentialSnapshot(
            user.Id,
            user.Email!,
            user.PasswordHash!,
            user.SecurityStamp!,
            user.ConcurrencyStamp!,
            passwordMatches);
    }

    // Conta usuários com consulta sem tracking para confirmar que reset nunca provisiona um titular ausente.
    private async Task<int> CountUsersAsync(IServiceProvider? provider = null)
    {
        using var scope = (provider ?? _serviceProvider!).CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinoraDbContext>()
            .Users
            .AsNoTracking()
            .CountAsync();
    }

    // Mantém contextos e sessões PostgreSQL independentes abertos antes de iniciar os resets concorrentes.
    private async Task<ConcurrentResetOutcome> ResetInIndependentScopeAsync(
        string password,
        TaskCompletionSource<int> connectionReady,
        Task start)
    {
        using var scope = _serviceProvider!.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        await context.Database.OpenConnectionAsync();
        var backendPid = await ReadBackendPidAsync(context);
        connectionReady.SetResult(backendPid);
        await start;

        var access = scope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>();
        var result = await access.ResetPasswordAsync(Email, UserId, password, CancellationToken.None);
        return new ConcurrentResetOutcome(result, backendPid);
    }

    // Lê o PID no PostgreSQL para provar que cada reset concorrente usa uma conexão distinta.
    private static async Task<int> ReadBackendPidAsync(FinoraDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // Os bloqueios de timeout/cancelamento usam somente a connection string validada pela fixture descartável.
    private static async Task<NpgsqlConnection> OpenTestConnectionAsync()
    {
        var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    // Adquire em outra sessão a mesma chave transacional usada pela Infrastructure.
    private static async Task AcquireAdministrativeLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@lock_key)",
            connection,
            transaction);
        command.Parameters.AddWithValue("lock_key", AdministrativeLockKey);
        await command.ExecuteScalarAsync();
    }

    // Observa no servidor a espera pelo advisory lock antes de validar timeout ou cancelamento.
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
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) > 0)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return false;
    }

    // Injeta apenas as falhas/transações necessárias ao teste, mantendo Identity e Data Protection reais.
    private ServiceProvider BuildServiceProvider(
        ISaveChangesInterceptor? saveChangesInterceptor = null,
        IDbTransactionInterceptor? transactionInterceptor = null,
        TimeSpan? databaseOperationTimeout = null)
    {
        var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        if (saveChangesInterceptor is not null)
        {
            services.AddSingleton(saveChangesInterceptor);
        }

        if (transactionInterceptor is not null)
        {
            services.AddSingleton(transactionInterceptor);
        }

        services.AddAdministrativeUserIdentity(connectionString);
        if (databaseOperationTimeout is { } timeout)
        {
            // O timeout menor existe somente no provider descartável para exercitar a espera pelo lock.
            services.AddScoped<IAdministrativeUserAccess>(serviceProvider => new AdministrativeUserAccessService(
                serviceProvider.GetRequiredService<FinoraDbContext>(),
                serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                timeout));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    // Cria o movimento por fábricas do Domain para que o teste use constraints e interpretação válidas.
    private static FinancialTransaction CreateFinancialTransaction()
    {
        var facts = new TransactionFacts(
            "reset-test-transaction",
            "administrative-reset-card",
            "movimento sintético",
            12.3456m,
            "BRL",
            "2026-11-17",
            "DEBIT",
            "POSTED",
            "TARIFA");
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);
        return FinancialTransaction.Create(
            TransactionId,
            UserId,
            AccountId,
            normalization,
            NormalizationVersion,
            ReferenceDate);
    }

    // O resultado do reset concorrente permanece associado à sessão independente que o executou.
    private sealed record ConcurrentResetOutcome(
        AdministrativeUserOperationResult Result,
        int BackendPid);

    // Guarda somente identificadores/stamps e resultados booleanos, nunca a senha em uma representação textual.
    private sealed record CredentialSnapshot(
        Guid UserId,
        string Email,
        string PasswordHash,
        string SecurityStamp,
        string ConcurrencyStamp,
        bool[] PasswordMatches);

    // Força uma alteração concorrente real de ConcurrencyStamp antes do UPDATE Identity do reset.
    private sealed class ChangeConcurrencyStampBeforeSaveInterceptor(
        string connectionString,
        Guid userId) : SaveChangesInterceptor
    {
        private int _hasChanged;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hasChanged, 1) == 0)
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    "UPDATE \"AspNetUsers\" SET \"ConcurrencyStamp\" = @stamp WHERE \"Id\" = @id",
                    connection);
                command.Parameters.AddWithValue("stamp", Guid.NewGuid().ToString());
                command.Parameters.AddWithValue("id", userId);

                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("A alteração concorrente sintética não encontrou o usuário.");
                }
            }

            return result;
        }
    }

    // Lança uma única falha após o store Identity salvar dentro da transação externa, antes de seu commit.
    private sealed class FailAfterFirstSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int _shouldFail = 1;

        public bool WasTriggered => Volatile.Read(ref _shouldFail) == 0;

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

    // Lança depois do commit físico, simulando perda da confirmação sem alegar que a transação sofreu rollback.
    private sealed class FailAfterCommitInterceptor : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("falha sintética após commit real"));
    }
}
