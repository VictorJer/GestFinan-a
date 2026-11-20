using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finora.Application.AdministrativeUsers;
using Finora.Application.Authentication;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Cada teste usa HTTP/Identity reais e reinicia somente o schema do PostgreSQL descartável validado.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlLoginTests
{
    // A 429 após o lockout não avalia a senha correta nem muda o estado persistido da conta.
    [TestMethod]
    public async Task LoginRateLimitRejectsBeforeCredentialEvaluationWithoutChangingLockout()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var id = await fixture.SeedUserAsync();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var rejectedCredentials = await fixture.LoginAsync(password: "wrong-sentinel");
            Assert.AreEqual(HttpStatusCode.Unauthorized, rejectedCredentials.StatusCode);
            Assert.IsTrue(rejectedCredentials.Headers.CacheControl?.NoStore);
        }

        var beforeLimit = await fixture.ReadUserAsync(id);
        // Identity zera a contagem ao ativar o lockout; a barreira a preservar é o prazo já persistido.
        Assert.AreEqual(0, beforeLimit.AccessFailedCount);
        Assert.IsNotNull(beforeLimit.LockoutEnd);

        using var validCredentials = await fixture.LoginAsync();
        Assert.AreEqual(HttpStatusCode.TooManyRequests, validCredentials.StatusCode);
        Assert.IsTrue(validCredentials.Headers.CacheControl?.NoStore);
        using var problem = JsonDocument.Parse(await validCredentials.Content.ReadAsStringAsync());
        Assert.AreEqual("RATE_LIMITED", problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(problem.RootElement.TryGetProperty("accessToken", out _));

        var afterLimit = await fixture.ReadUserAsync(id);
        Assert.AreEqual(beforeLimit.AccessFailedCount, afterLimit.AccessFailedCount);
        Assert.AreEqual(beforeLimit.LockoutEnd, afterLimit.LockoutEnd);
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // Conta desconhecida, senha errada e bloqueio usam o mesmo contrato; estado ambíguo não seleciona um titular.
    [TestMethod]
    public async Task UnknownAccountAndWrongPasswordAreGenericButMultipleUsersAreUnavailable()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        using var absent = await fixture.LoginAsync();
        await AuthenticationAvailabilityTests.AssertProblemAsync(absent, HttpStatusCode.Unauthorized);
        Assert.AreEqual(0, await fixture.Context.Users.CountAsync());
        var id = await fixture.SeedUserAsync();
        using var wrong = await fixture.LoginAsync(password: "wrong-sentinel");
        await AuthenticationAvailabilityTests.AssertProblemAsync(wrong, HttpStatusCode.Unauthorized);
        using var unknown = await fixture.LoginAsync(email: "unknown-sentinel@example.invalid");
        await AuthenticationAvailabilityTests.AssertProblemAsync(unknown, HttpStatusCode.Unauthorized);
        Assert.AreEqual(1, (await fixture.ReadUserAsync(id)).AccessFailedCount);
        await fixture.SeedUserAsync("second@example.invalid");
        using var ambiguous = await fixture.LoginAsync();
        await AuthenticationAvailabilityTests.AssertProblemAsync(ambiguous, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // Legado ganha lockout transacional; cinco falhas persistem entre scopes/hosts e tentativas bloqueadas não estendem prazo.
    [TestMethod]
    public async Task FiveFailuresPersistAcrossHostsAndLockoutExpiresWithoutExtension()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var id = await fixture.SeedUserAsync(lockoutEnabled: false);
        Assert.IsFalse((await fixture.ReadUserAsync(id)).LockoutEnabled);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await fixture.LoginAsync(password: "wrong-sentinel");
            await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized);
            var snapshot = await fixture.ReadUserAsync(id);
            Assert.IsTrue(snapshot.LockoutEnabled);
            if (attempt < 5) { Assert.AreEqual(attempt, snapshot.AccessFailedCount); }
        }
        var locked = await fixture.ReadUserAsync(id);
        Assert.IsNotNull(locked.LockoutEnd);
        Assert.IsTrue(locked.LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(4));
        Assert.IsTrue(locked.LockoutEnd <= DateTimeOffset.UtcNow.AddMinutes(5));
        using var blocked = await fixture.LoginAsync();
        await AuthenticationAvailabilityTests.AssertProblemAsync(blocked, HttpStatusCode.Unauthorized);
        await using var restarted = fixture.NewFactory();
        using var client = restarted.CreateClient();
        using var blockedAfterRestart = await client.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.invalid", password = " Password!123 " });
        Assert.AreEqual(HttpStatusCode.Unauthorized, blockedAfterRestart.StatusCode);
        Assert.AreEqual(locked.LockoutEnd, (await fixture.ReadUserAsync(id)).LockoutEnd);
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"LockoutEnd\"={DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE \"Id\"={id}");
        using var recovered = await fixture.LoginAsync();
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode);
        Assert.AreEqual(0, (await fixture.ReadUserAsync(id)).AccessFailedCount);
    }

    // Normaliza somente e-mail; dois logins válidos geram sessões distintas e sucesso limpa falhas anteriores.
    [TestMethod]
    public async Task ExactPasswordAndNormalizedEmailProduceDistinctCommittedSessions()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var id = await fixture.SeedUserAsync();
        using var trimmedPassword = await fixture.LoginAsync(password: "Password!123");
        Assert.AreEqual(HttpStatusCode.Unauthorized, trimmedPassword.StatusCode);
        using var normalized = await fixture.LoginAsync(email: "  OWNER@EXAMPLE.INVALID  ");
        Assert.AreEqual(HttpStatusCode.OK, normalized.StatusCode);
        Assert.AreEqual(1, await fixture.Context.Set<AuthSession>().CountAsync());
        Assert.AreEqual(0, (await fixture.ReadUserAsync(id)).AccessFailedCount);
        var token = await fixture.TokenAsync();
        Assert.AreEqual(2, await fixture.Context.Set<AuthSession>().CountAsync());
        Assert.AreEqual(2, await fixture.Context.Set<AuthSession>().Select(session => session.Id).Distinct().CountAsync());
        using var access = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.OK, access.StatusCode);
        using var identity = JsonDocument.Parse(await access.Content.ReadAsStringAsync());
        Assert.AreEqual(id.ToString("D"), identity.RootElement.GetProperty("subject").GetString());
    }

    // Requisições concorrentes têm scopes independentes e não perdem incrementos sob o lock cooperativo.
    [TestMethod]
    public async Task FiveConcurrentFailuresLoseNoIncrement()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var id = await fixture.SeedUserAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 5).Select(async _ =>
        {
            await gate.Task;
            using var response = await fixture.LoginAsync(password: "wrong-sentinel");
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }).ToArray();
        gate.SetResult();
        await Task.WhenAll(attempts);
        Assert.IsTrue((await fixture.ReadUserAsync(id)).LockoutEnd > DateTimeOffset.UtcNow);
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // Falhar ao persistir o contador nunca vira 401, e rollback preserva a credencial/contagem anterior.
    [TestMethod]
    public async Task CounterWriteFailureIs503AndDoesNotPublishInvalidCredentialResult()
    {
        var fault = new SaveFault(sessionOnly: false);
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services => services.AddSingleton<ISaveChangesInterceptor>(fault));
        var id = await fixture.SeedUserAsync();
        fault.Armed = true;
        using var response = await fixture.LoginAsync(password: "wrong-sentinel");
        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, (await fixture.ReadUserAsync(id)).AccessFailedCount);
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // Falha após gravação da sessão desfaz também reset do contador na transação externa.
    [TestMethod]
    public async Task SessionWriteFailureRollsBackSessionAndCounterReset()
    {
        var fault = new SaveFault(sessionOnly: true);
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services => services.AddSingleton<ISaveChangesInterceptor>(fault));
        var id = await fixture.SeedUserAsync();
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"AccessFailedCount\"=3 WHERE \"Id\"={id}");
        fault.Armed = true;
        using var response = await fixture.LoginAsync();
        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(3, (await fixture.ReadUserAsync(id)).AccessFailedCount);
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // Assinatura falha antes do commit; exceções inesperadas ficam sanitizadas sem token nem sessão publicada.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SigningFailureDoesNotCommitAndUnexpectedFailureIsSanitized(bool unexpected)
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.Replace(ServiceDescriptor.Singleton<IAccessTokenIssuer>(new FailingIssuer(unexpected))));
        var id = await fixture.SeedUserAsync();
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"AccessFailedCount\"=3 WHERE \"Id\"={id}");
        using var response = await fixture.LoginAsync();
        await AuthenticationAvailabilityTests.AssertProblemAsync(response,
            unexpected ? HttpStatusCode.InternalServerError : HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(3, (await fixture.ReadUserAsync(id)).AccessFailedCount);
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
        Assert.IsFalse(fixture.Factory.Logs.Text.Contains("exception-sentinel", StringComparison.Ordinal));
    }

    // A sessão pode ter sido confirmada mesmo sem confirmação ao cliente; o fluxo não entrega token nem repete commit.
    [TestMethod]
    public async Task UncertainCommitReturns503WithoutClaimingRollback()
    {
        var fault = new CommitFault();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services => services.AddSingleton<IDbTransactionInterceptor>(fault));
        await fixture.SeedUserAsync();
        fault.Armed = true;
        using var response = await fixture.LoginAsync();
        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(1, fault.Calls);
        Assert.AreEqual(1, await fixture.Context.Set<AuthSession>().CountAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsFalse(body.RootElement.TryGetProperty("accessToken", out _));
    }

    // Cancelamento durante espera observada no banco não cria sessão nem altera contador.
    [TestMethod]
    public async Task ClientCancellationWhileWaitingForLockPublishesNothing()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var id = await fixture.SeedUserAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.LoginAsync(cancellationToken: cancellation.Token);
        await fixture.WaitForAdvisoryWaitersAsync(1);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => { using var response = await pending; });
        await held.RollbackAsync();
        Assert.AreEqual(0, (await fixture.ReadUserAsync(id)).AccessFailedCount);
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // O timeout interno curto testa o mesmo caminho de falha fechada sem enfraquecer o limite de produção.
    [TestMethod]
    public async Task LockWaitTimeoutReturns503WithoutRetryOrPartialSession()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationLogin>(provider => new IdentityLoginService(
                provider.GetRequiredService<FinoraDbContext>(), provider.GetRequiredService<UserManager<ApplicationUser>>(),
                provider.GetRequiredService<IPasswordHasher<ApplicationUser>>(), provider.GetRequiredService<SyntheticCredential>(),
                provider.GetRequiredService<IAccessTokenIssuer>(), provider.GetRequiredService<TimeProvider>())
            { OperationTimeout = TimeSpan.FromSeconds(1) }));
        await fixture.SeedUserAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);
        var pending = fixture.LoginAsync();
        await fixture.WaitForAdvisoryWaitersAsync(1);
        using var response = await pending;
        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        await held.RollbackAsync();
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // Login posterior ao reset descarta inclusive um usuário rastreado antes do lock e revalida a credencial nova.
    [TestMethod]
    public async Task ResetQueuedBeforeLoginPreventsUsingStaleTrackedPassword()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var id = await fixture.SeedUserAsync();
        await using var administrative = fixture.AdministrativeProvider();
        using var resetScope = administrative.CreateScope();
        using var loginScope = fixture.Factory.Services.CreateScope();
        var context = loginScope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        await context.Users.SingleAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);
        var reset = resetScope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>()
            .ResetPasswordAsync("owner@example.invalid", id, "New!Password456", CancellationToken.None);
        await fixture.WaitForAdvisoryWaitersAsync(1);
        var login = loginScope.ServiceProvider.GetRequiredService<IAuthenticationLogin>()
            .LoginAsync("owner@example.invalid", " Password!123 ", CancellationToken.None);
        await fixture.WaitForAdvisoryWaitersAsync(2);
        await held.CommitAsync();
        Assert.AreEqual(AdministrativeUserOperationState.PasswordReset, (await reset).State);
        Assert.AreEqual(LoginState.InvalidCredentials, (await login).State);
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
        using var fresh = await fixture.LoginAsync(password: "New!Password456");
        Assert.AreEqual(HttpStatusCode.OK, fresh.StatusCode);
        Assert.AreEqual(1, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // O token do login só retorna após o reset que aguardava sua transação já ter revogado a sessão confirmada.
    [TestMethod]
    public async Task LoginQueuedBeforeResetReturnsLateTokenThatResetHasAlreadyRevoked()
    {
        var commitGate = new GateAfterLoginCommitInterceptor();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddSingleton<IDbTransactionInterceptor>(commitGate));
        var id = await fixture.SeedUserAsync();
        await using var administrative = fixture.AdministrativeProvider();
        using var resetScope = administrative.CreateScope();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);
        commitGate.Armed = true;
        var login = fixture.LoginAsync();
        Task<AdministrativeUserOperationResult>? reset = null;
        try
        {
            await fixture.WaitForAdvisoryWaitersAsync(1);
            reset = resetScope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>()
                .ResetPasswordAsync("owner@example.invalid", id, "New!Password456", CancellationToken.None);
            await fixture.WaitForAdvisoryWaitersAsync(2);
            await held.CommitAsync();

            // O commit físico libera o lock; o interceptor segura a resposta para o reset revogar antes da emissão do token.
            await commitGate.CommitObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(AdministrativeUserOperationState.PasswordReset,
                (await reset.WaitAsync(TimeSpan.FromSeconds(10))).State);
            fixture.Context.ChangeTracker.Clear();
            var sessionBeforeLoginResponse = await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync();
            Assert.IsNotNull(sessionBeforeLoginResponse.RevokedAtUtc);
        }
        finally
        {
            commitGate.Release();
        }

        using var response = await login.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, await fixture.Context.Set<AuthSession>().CountAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var lateToken = body.RootElement.GetProperty("accessToken").GetString();
        using var protectedResponse = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, lateToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        Assert.AreEqual(1, commitGate.Calls);
    }

    // Ativa falha somente depois do provisionamento; SavedChanges prova rollback após escrita SQL real.
    private sealed class SaveFault(bool sessionOnly) : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && (!sessionOnly || eventData.Context!.ChangeTracker.Entries<AuthSession>().Any()))
            {
                throw new DbUpdateException("persistence-exception-sentinel");
            }
            return ValueTask.FromResult(result);
        }
    }

    // Lança após commit físico real para exercitar confirmação incerta, não um rollback simulado.
    private sealed class CommitFault : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal int Calls { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) { return Task.CompletedTask; }
            Calls++;
            return Task.FromException(new NpgsqlException("commit-exception-sentinel"));
        }
    }

    // Pausa somente a publicação da resposta depois do commit SQL, deixando a operação concorrente revogar o estado.
    private sealed class GateAfterLoginCommitInterceptor : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _commitObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Armed { get; set; }
        internal int Calls { get; private set; }
        internal TaskCompletionSource CommitObserved => _commitObserved;

        internal void Release() => _release.TrySetResult();

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) { return; }
            Calls++;
            _commitObserved.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    // Substitui somente assinatura para provar rollback/falha inesperada; os demais componentes continuam reais.
    private sealed class FailingIssuer(bool unexpected) : IAccessTokenIssuer
    {
        public PreparedAccessToken Issue(Guid userId, string sessionId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
        {
            if (unexpected) { throw new InvalidOperationException("signing-exception-sentinel"); }
            throw new AuthenticationUnavailableException();
        }
    }
}
