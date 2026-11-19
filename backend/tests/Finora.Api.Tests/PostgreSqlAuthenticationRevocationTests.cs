using Finora.Application.Authentication;
using Finora.Application.AdministrativeUsers;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace Finora.Api.Tests;

// Exercita o adaptador scoped e os updates condicionais no PostgreSQL descartável protegido pela fixture.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlAuthenticationRevocationTests
{
    private readonly FirstLogoutCommitGate _logoutCommitGate = new();
    private PostgreSqlAuthenticationFixture? _fixture;

    // A fixture valida o destino descartável e aplica as migrations explicitamente, sem usar banco runtime.
    [TestInitialize]
    public async Task InitializeAsync() => _fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
        services.AddSingleton<IDbTransactionInterceptor>(_logoutCommitGate));

    // Libera host e contexto de teste sem deixar container ou conexão aberta pela classe.
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_fixture is not null) { await _fixture.DisposeAsync(); }
    }

    // Logout altera somente a sessão-alvo e uma confirmação concorrente preserva o primeiro timestamp.
    [TestMethod]
    public async Task RevocationTargetsOneSessionAndPreservesFirstTimestamp()
    {
        var fixture = _fixture!;
        var userId = await fixture.SeedUserAsync();
        var createdAtUtc = fixture.Clock.GetUtcNow();
        var targeted = AuthSession.Create(AuthSession.GenerateId(), userId, createdAtUtc, createdAtUtc.AddMinutes(30));
        var other = AuthSession.Create(AuthSession.GenerateId(), userId, createdAtUtc, createdAtUtc.AddMinutes(30));
        fixture.Context.AddRange(targeted, other);
        await fixture.Context.SaveChangesAsync();

        LogoutResult first;
        LogoutResult replay;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var revoker = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionRevoker>();
            first = await revoker.RevokeAsync(Reference(targeted), CancellationToken.None);
        }

        fixture.Context.ChangeTracker.Clear();
        var originalTimestamp = await fixture.Context.Set<AuthSession>().AsNoTracking()
            .Where(session => session.Id == targeted.Id).Select(session => session.RevokedAtUtc).SingleAsync();
        Assert.IsNotNull(originalTimestamp);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var revoker = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionRevoker>();
            replay = await revoker.RevokeAsync(Reference(targeted), CancellationToken.None);
        }

        fixture.Context.ChangeTracker.Clear();
        var sessions = await fixture.Context.Set<AuthSession>().AsNoTracking().OrderBy(session => session.Id).ToArrayAsync();
        var persistedTarget = sessions.Single(session => session.Id == targeted.Id);
        var persistedOther = sessions.Single(session => session.Id == other.Id);

        Assert.AreEqual(LogoutState.Confirmed, first.State);
        Assert.AreEqual(LogoutState.Confirmed, replay.State);
        Assert.AreEqual(originalTimestamp, persistedTarget.RevokedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, persistedTarget.RevokedAtUtc!.Value.Offset);
        Assert.IsTrue(persistedTarget.RevokedAtUtc >= persistedTarget.CreatedAtUtc);
        Assert.IsNull(persistedOther.RevokedAtUtc);
    }

    // Uma referência incompatível ou linha ausente nunca produz confirmação nem atualiza outra sessão.
    [TestMethod]
    public async Task IncompatibleOrMissingSessionDoesNotReportConfirmation()
    {
        var fixture = _fixture!;
        var userId = await fixture.SeedUserAsync();
        var createdAtUtc = fixture.Clock.GetUtcNow();
        var persisted = AuthSession.Create(AuthSession.GenerateId(), userId, createdAtUtc, createdAtUtc.AddMinutes(30));
        fixture.Context.Add(persisted);
        await fixture.Context.SaveChangesAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var revoker = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionRevoker>();
        var incompatible = await revoker.RevokeAsync(
            new AuthenticationSessionReference(userId, persisted.Id, createdAtUtc, createdAtUtc.AddMinutes(31)),
            CancellationToken.None);
        var missing = await revoker.RevokeAsync(
            new AuthenticationSessionReference(userId, AuthSession.GenerateId(), createdAtUtc, createdAtUtc.AddMinutes(30)),
            CancellationToken.None);

        fixture.Context.ChangeTracker.Clear();
        var current = await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync();
        Assert.AreEqual(LogoutState.InvalidSession, incompatible.State);
        Assert.AreEqual(LogoutState.InvalidSession, missing.State);
        Assert.IsNull(current.RevokedAtUtc);
    }

    // Sem Bearer, JwtBearer desafia antes de consultar sessão ou resolver o caso de uso de logout.
    [TestMethod]
    public async Task LogoutWithoutBearerChallengesBeforeDatabaseAccess()
    {
        using var response = await _fixture!.Client.PostAsync("/api/auth/logout", content: null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
    }

    // Rejeições criptográficas/semânticas do logout conservam challenge 401 e nunca alteram a sessão.
    [TestMethod]
    public async Task LogoutRejectsInvalidExpiredRevokedMissingAndIncompatibleTokens()
    {
        var fixture = _fixture!;
        var userId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var jwt = new JsonWebToken(token);
        var revokedToken = await fixture.TokenAsync();
        var revokedJwt = new JsonWebToken(revokedToken);
        var original = PostgreSqlBearerTests.Payload(fixture, userId, jwt.Id);
        var incompatible = new Dictionary<string, object>(original)
        {
            ["exp"] = fixture.Clock.GetUtcNow().AddMinutes(29).ToUnixTimeSeconds()
        };
        var missing = PostgreSqlBearerTests.Payload(fixture, userId, AuthSession.GenerateId());
        var expired = new Dictionary<string, object>(original)
        {
            ["iat"] = fixture.Clock.GetUtcNow().AddMinutes(-30).ToUnixTimeSeconds(),
            ["exp"] = fixture.Clock.GetUtcNow().ToUnixTimeSeconds()
        };
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AuthSessions\" SET \"RevokedAtUtc\"={fixture.Clock.GetUtcNow()} WHERE \"Id\"={revokedJwt.Id}");

        foreach (var rejectedToken in new[]
        {
            PostgreSqlBearerTests.Tamper(token),
            PostgreSqlBearerTests.Sign(fixture.Key, expired),
            PostgreSqlBearerTests.Sign(fixture.Key, missing),
            PostgreSqlBearerTests.Sign(fixture.Key, incompatible),
            revokedToken
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rejectedToken);
            using var response = await fixture.Client.SendAsync(request);
            await PostgreSqlBearerTests.AssertChallengeAsync(response);
            Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        }

        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.Set<AuthSession>().AsNoTracking().ToArrayAsync();
        Assert.IsNull(persisted.Single(session => session.Id == jwt.Id).RevokedAtUtc);
        Assert.AreEqual(fixture.Clock.GetUtcNow(), persisted.Single(session => session.Id == revokedJwt.Id).RevokedAtUtc);
    }

    // Uma assinatura inválida é recusada antes de consultar uma tabela que já está indisponível.
    [TestMethod]
    public async Task TamperedLogoutTokenRemains401WhenSessionSchemaIsUnavailable()
    {
        var fixture = _fixture!;
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("DROP TABLE \"AuthSessions\"");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PostgreSqlBearerTests.Tamper(token));
        using var response = await fixture.Client.SendAsync(request);

        await PostgreSqlBearerTests.AssertChallengeAsync(response);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
    }

    // Falha de schema durante a autenticação do logout é operacional, não challenge de credencial incorreta.
    [TestMethod]
    public async Task LogoutReturnsSanitized503WhenSessionSchemaIsUnavailable()
    {
        var fixture = _fixture!;
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("DROP TABLE \"AuthSessions\"");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(request);

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, response.Headers.WwwAuthenticate.Count);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
    }

    // Timeout real na leitura da sessão é 503 sem challenge e não chega à mutação do logout.
    [TestMethod]
    public async Task LogoutSessionLookupTimeoutReturns503WithoutRevokingSession()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationSessionReader>(provider => new AuthenticationSessionReader(
                provider.GetRequiredService<FinoraDbContext>(), provider.GetRequiredService<TimeProvider>())
            { OperationTimeout = TimeSpan.FromSeconds(1) }));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var held = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = held;
        command.CommandText = "LOCK TABLE \"AuthSessions\" IN ACCESS EXCLUSIVE MODE";
        await command.ExecuteNonQueryAsync();
        var pending = SendLogoutAsync(fixture.Client, token);
        await fixture.WaitForSessionReaderAsync();

        using var response = await pending;

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, response.Headers.WwwAuthenticate.Count);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        await held.RollbackAsync();
        fixture.Context.ChangeTracker.Clear();
        var session = await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync();
        Assert.IsNull(session.RevokedAtUtc);
    }

    // Falha operacional na fronteira de commit ocorre após o UPDATE SQL, mas a transação ainda reverte a sessão.
    [TestMethod]
    public async Task LogoutFailureBeforeCommitRollsBackAndAllowsOnlyExplicitRetry()
    {
        var fault = new FailBeforeCommitInterceptor();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddSingleton<IDbTransactionInterceptor>(fault));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        fault.Armed = true;

        using var failed = await SendLogoutAsync(fixture.Client, token);

        await AuthenticationAvailabilityTests.AssertProblemAsync(failed, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, failed.Headers.WwwAuthenticate.Count);
        Assert.AreEqual(1, fault.Calls);
        fixture.Context.ChangeTracker.Clear();
        Assert.IsNull((await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync()).RevokedAtUtc);

        // A tentativa seguinte é uma nova requisição explícita, não retry automático do adaptador.
        fault.Armed = false;
        using var explicitRetry = await SendLogoutAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.NoContent, explicitRetry.StatusCode);
        Assert.AreEqual(1, fault.Calls);
        fixture.Context.ChangeTracker.Clear();
        Assert.IsNotNull((await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync()).RevokedAtUtc);
    }

    // A confirmação perdida após o commit físico retorna 503, mantém a revogação e não tenta novo commit.
    [TestMethod]
    public async Task LogoutLostCommitAcknowledgementReturns503WithRevocationPersistedWithoutRetry()
    {
        var fault = new FailAfterCommitInterceptor();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddSingleton<IDbTransactionInterceptor>(fault));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        fault.Armed = true;

        using var response = await SendLogoutAsync(fixture.Client, token);

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, response.Headers.WwwAuthenticate.Count);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        Assert.AreEqual(1, fault.Calls);
        fixture.Context.ChangeTracker.Clear();
        var session = await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync();
        Assert.IsNotNull(session.RevokedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, session.RevokedAtUtc!.Value.Offset);

        // O token agora é recusado na autenticação, sem novo commit nem retry interno.
        using var replay = await SendLogoutAsync(fixture.Client, token);
        await PostgreSqlBearerTests.AssertChallengeAsync(replay);
        Assert.AreEqual(1, fault.Calls);
    }

    // Cancelamento HTTP real enquanto o logout aguarda o advisory lock deixa a sessão ativa e libera nova operação.
    [TestMethod]
    public async Task ClientCancellationWhileWaitingForLogoutLockPublishesNothing()
    {
        var fixture = _fixture!;
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);
        using var cancellation = new CancellationTokenSource();
        var pending = SendLogoutAsync(fixture.Client, token, cancellation.Token);
        await fixture.WaitForAdvisoryWaitersAsync(1);
        cancellation.Cancel();

        try
        {
            using var unexpectedResponse = await pending;
            Assert.Fail($"O cancelamento do cliente não deveria produzir HTTP {unexpectedResponse.StatusCode}.");
        }
        catch (OperationCanceledException)
        {
            // A conexão HTTP foi cancelada pelo próprio teste antes de liberar o lock.
        }

        await held.RollbackAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.IsNull((await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync()).RevokedAtUtc);
        using var explicitRetry = await SendLogoutAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.NoContent, explicitRetry.StatusCode);
    }

    // O prazo de operação inclui a espera pelo lock; após o timeout a revogação segue ausente e retry é explícito.
    [TestMethod]
    public async Task LogoutOperationTimeoutIncludesLockWaitAndReleasesLock()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationSessionRevoker>(provider => new IdentityAuthenticationSessionRevoker(
                provider.GetRequiredService<FinoraDbContext>(), provider.GetRequiredService<TimeProvider>())
            { OperationTimeout = TimeSpan.FromMilliseconds(800) }));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);
        var pending = SendLogoutAsync(fixture.Client, token);
        await fixture.WaitForAdvisoryWaitersAsync(1);

        using var response = await pending;

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, response.Headers.WwwAuthenticate.Count);
        await held.RollbackAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.IsNull((await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync()).RevokedAtUtc);
        using var explicitRetry = await SendLogoutAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.NoContent, explicitRetry.StatusCode);
    }

    // Expiração durante a espera pelo lock transforma o pedido em 401 sem gravar revogação.
    [TestMethod]
    public async Task LogoutExpirationWhileWaitingForMutationLockRollsBackBeforeUpdate()
    {
        var fixture = _fixture!;
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var expiration = new DateTimeOffset(new JsonWebToken(token).ValidTo, TimeSpan.Zero);
        fixture.Clock.Set(expiration.AddSeconds(-1));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);
        var pending = SendLogoutAsync(fixture.Client, token);
        await fixture.WaitForAdvisoryWaitersAsync(1);
        fixture.Clock.Set(expiration);
        await held.RollbackAsync();

        using var response = await pending;

        await PostgreSqlBearerTests.AssertChallengeAsync(response);
        fixture.Context.ChangeTracker.Clear();
        Assert.IsNull((await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync()).RevokedAtUtc);
    }

    // Expiração acionada enquanto o UPDATE aguarda lock de linha é revalidada antes do commit e desfaz a escrita SQL.
    [TestMethod]
    public async Task LogoutExpirationDuringSessionUpdateRollsBackBeforeCommit()
    {
        var fixture = _fixture!;
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var sessionId = new JsonWebToken(token).Id;
        var expiration = new DateTimeOffset(new JsonWebToken(token).ValidTo, TimeSpan.Zero);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var held = await connection.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand(
            "SELECT 1 FROM \"AuthSessions\" WHERE \"Id\"=@session_id FOR UPDATE", connection, held))
        {
            lockRow.Parameters.AddWithValue("session_id", sessionId);
            Assert.IsNotNull(await lockRow.ExecuteScalarAsync());
        }

        var pending = SendLogoutAsync(fixture.Client, token);
        Assert.IsTrue(await WaitForTransactionLockWaiterAsync(fixture, TimeSpan.FromSeconds(10)));
        fixture.Clock.Set(expiration);
        await held.RollbackAsync();

        using var response = await pending;

        await PostgreSqlBearerTests.AssertChallengeAsync(response);
        fixture.Context.ChangeTracker.Clear();
        Assert.IsNull((await fixture.Context.Set<AuthSession>().AsNoTracking().SingleAsync()).RevokedAtUtc);
    }

    // Resultado operacional do caso de uso chega como 503 sem challenge depois de autenticação real.
    [TestMethod]
    public async Task LogoutMapsUnavailableRevokerTo503WithoutChallenge()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationSessionRevoker>(_ => new TestRevoker(
                new LogoutResult(LogoutState.Unavailable))));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(request);

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, response.Headers.WwwAuthenticate.Count);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        Assert.IsFalse(fixture.Factory.Logs.Text.Contains("sentinel", StringComparison.Ordinal));
    }

    // Exceção inesperada do adaptador é sanitizada em 500 e não é exposta nem como challenge nem no corpo.
    [TestMethod]
    public async Task LogoutSanitizesUnexpectedRevokerFailureAs500()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationSessionRevoker>(_ => new TestRevoker(
                new InvalidOperationException("unexpected-logout-sentinel"))));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(request);

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.AreEqual(0, response.Headers.WwwAuthenticate.Count);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        Assert.IsFalse(fixture.Factory.Logs.Text.Contains("unexpected-logout-sentinel", StringComparison.Ordinal));
    }

    // Respostas e captura completa de logs/scopes não revelam material sensível nas falhas de logout nem em commit incerto.
    [TestMethod]
    public async Task LogoutOperationalUnexpectedAndUncertainCommitFailuresDoNotLeakSentinels()
    {
        const string email = "logout-email-private-sentinel@example.invalid";
        const string password = "Logout-password-private-sentinel-9!";
        const string exception = "logout-exception-private-sentinel";

        // O adaptador inesperado recebe sentinelas deliberadamente; sua exceção não pode chegar ao corpo nem aos logs/scopes.
        await using (var unexpectedFixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationSessionRevoker>(_ => new TestRevoker(
                new InvalidOperationException(string.Join(' ', email, password, exception, "hash-private-sentinel")))))
            .ConfigureAwait(false))
        {
            var userId = await unexpectedFixture.SeedUserAsync(email, password);
            using var login = await unexpectedFixture.LoginAsync(email, password);
            using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            var token = loginBody.RootElement.GetProperty("accessToken").GetString()!;
            var hash = (await unexpectedFixture.ReadUserAsync(userId)).PasswordHash!;
            var sentinels = PrivacySentinels(unexpectedFixture, email, password, token, hash, exception);
            using var response = await SendLogoutAsync(unexpectedFixture.Client, token);
            await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.InternalServerError);
            Assert.IsTrue(response.Headers.CacheControl?.NoStore);
            await AssertNoLogoutLeaksAsync(unexpectedFixture, response, sentinels);
        }

        // Falha operacional tipada também mantém o contrato de resposta fixa e os canais livres de PII/segredos.
        await using (var unavailableFixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationSessionRevoker>(_ => new TestRevoker(
                new LogoutResult(LogoutState.Unavailable)))))
        {
            var userId = await unavailableFixture.SeedUserAsync(email, password);
            using var login = await unavailableFixture.LoginAsync(email, password);
            using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            var token = loginBody.RootElement.GetProperty("accessToken").GetString()!;
            var hash = (await unavailableFixture.ReadUserAsync(userId)).PasswordHash!;
            var sentinels = PrivacySentinels(unavailableFixture, email, password, token, hash, exception);
            using var response = await SendLogoutAsync(unavailableFixture.Client, token);
            await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
            Assert.IsTrue(response.Headers.CacheControl?.NoStore);
            await AssertNoLogoutLeaksAsync(unavailableFixture, response, sentinels);
        }

        // As duas falhas de transação representam rollback operacional e commit físico com confirmação perdida.
        foreach (var uncertainCommit in new[] { false, true })
        {
            var beforeCommit = new FailBeforeCommitInterceptor();
            var afterCommit = new FailAfterCommitInterceptor();
            IDbTransactionInterceptor interceptor = uncertainCommit ? afterCommit : beforeCommit;
            await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
                services.AddSingleton(interceptor));
            var userId = await fixture.SeedUserAsync(email, password);
            using var login = await fixture.LoginAsync(email, password);
            using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            var token = loginBody.RootElement.GetProperty("accessToken").GetString()!;
            var hash = (await fixture.ReadUserAsync(userId)).PasswordHash!;
            var sentinels = PrivacySentinels(fixture, email, password, token, hash, exception);
            beforeCommit.Armed = !uncertainCommit;
            afterCommit.Armed = uncertainCommit;
            using var response = await SendLogoutAsync(fixture.Client, token);
            await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
            Assert.IsTrue(response.Headers.CacheControl?.NoStore);
            await AssertNoLogoutLeaksAsync(fixture, response, sentinels);
        }
    }

    // Reúne valores sintéticos e segredos efetivos do host para inspecionar os canais sem imprimi-los.
    private static string[] PrivacySentinels(
        PostgreSqlAuthenticationFixture fixture, string email, string password, string token, string hash, string exception) =>
        [email, email.ToUpperInvariant(), password, token, "Bearer " + token,
            hash,
            fixture.ConnectionString, fixture.Key, exception, "hash-private-sentinel"];

    // Verifica corpo problem-details e captura integral, que inclui exceções e scopes do pipeline real.
    private static async Task AssertNoLogoutLeaksAsync(
        PostgreSqlAuthenticationFixture fixture, HttpResponseMessage response, IEnumerable<string> sentinels)
    {
        var body = await response.Content.ReadAsStringAsync();
        foreach (var sentinel in sentinels)
        {
            Assert.IsFalse(body.Contains(sentinel, StringComparison.Ordinal), "Resposta de logout refletiu material sensível.");
            Assert.IsFalse(fixture.Factory.Logs.Text.Contains(sentinel, StringComparison.Ordinal), "Logs/scopes de logout refletiram material sensível.");
        }
    }

    // O fluxo HTTP real prova logout autenticado, corpo/query ignorados e isolamento de sessões distintas.
    [TestMethod]
    public async Task LogoutRevokesOnlyBearerSessionAndRejectsItsNextRequest()
    {
        var fixture = _fixture!;
        await fixture.SeedUserAsync();
        var firstToken = await fixture.TokenAsync();
        var secondToken = await fixture.TokenAsync();
        using var firstAccess = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, firstToken);
        using var secondAccess = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, secondToken);
        Assert.AreEqual(HttpStatusCode.OK, firstAccess.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondAccess.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/auth/logout?userId={Guid.NewGuid():D}&sessionId=client-selected-session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstToken);
        request.Content = new StringContent(
            "{\"userId\":\"client-selected-user\",\"sessionId\":\"client-selected-session\"}",
            Encoding.UTF8,
            "application/json");
        using var logout = await fixture.Client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.AreEqual(0, (await logout.Content.ReadAsByteArrayAsync()).Length);
        Assert.IsTrue(logout.Headers.CacheControl?.NoStore);
        Assert.IsFalse(logout.Headers.Contains("Set-Cookie"));

        using var revokedAccess = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, firstToken);
        using var otherAccess = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, secondToken);
        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        replayRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstToken);
        using var replay = await fixture.Client.SendAsync(replayRequest);
        Assert.AreEqual(HttpStatusCode.Unauthorized, revokedAccess.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.AreEqual("Bearer", replay.Headers.WwwAuthenticate.Single().Scheme);
        Assert.IsTrue(replay.Headers.CacheControl?.NoStore);
        Assert.AreEqual(HttpStatusCode.OK, otherAccess.StatusCode);
    }

    // Duas requisições autenticadas antes da revogação serializam no lock e preservam o primeiro timestamp.
    [TestMethod]
    public async Task ConcurrentAdmittedLogoutsConfirmOneTargetWithoutRevokingAnotherSession()
    {
        var fixture = _fixture!;
        await fixture.SeedUserAsync();
        var firstToken = await fixture.TokenAsync();
        var secondToken = await fixture.TokenAsync();
        var firstSessionId = new JsonWebToken(firstToken).Id;
        var secondSessionId = new JsonWebToken(secondToken).Id;
        var firstRevocationTime = fixture.Clock.GetUtcNow().AddMinutes(1);
        fixture.Clock.Set(firstRevocationTime);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);

        _logoutCommitGate.Armed = true;
        Task<HttpResponseMessage>? firstLogout = null;
        Task<HttpResponseMessage>? secondLogout = null;
        try
        {
            firstLogout = SendLogoutAsync(fixture.Client, firstToken);
            secondLogout = SendLogoutAsync(fixture.Client, firstToken);
            await fixture.WaitForAdvisoryWaitersAsync(2);
            await held.RollbackAsync();
            await _logoutCommitGate.CommitObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Clock.Set(firstRevocationTime.AddMinutes(1));
        }
        finally
        {
            _logoutCommitGate.Release();
        }

        using var firstResponse = await firstLogout!.WaitAsync(TimeSpan.FromSeconds(10));
        using var secondResponse = await secondLogout!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, secondResponse.StatusCode);
        Assert.IsTrue(firstResponse.Headers.CacheControl?.NoStore);
        Assert.IsTrue(secondResponse.Headers.CacheControl?.NoStore);
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.Set<AuthSession>().AsNoTracking()
            .ToDictionaryAsync(session => session.Id);
        var firstTimestamp = persisted[firstSessionId].RevokedAtUtc;
        Assert.AreEqual(firstRevocationTime, firstTimestamp);
        Assert.AreEqual(1, _logoutCommitGate.Calls);
        Assert.IsNull(persisted[secondSessionId].RevokedAtUtc);

        using var activeOtherSession = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, secondToken);
        Assert.AreEqual(HttpStatusCode.OK, activeOtherSession.StatusCode);
        using var rejectedTarget = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, firstToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, rejectedTarget.StatusCode);
    }

    // Logout primeiro revoga somente A; o reset enfileirado depois revoga B sem sobrescrever o timestamp de A.
    [TestMethod]
    public async Task LogoutThenResetPreservesFirstTimestampAndResetRevokesRemainingSession()
    {
        var fixture = _fixture!;
        var userId = await fixture.SeedUserAsync();
        var firstToken = await fixture.TokenAsync();
        var secondToken = await fixture.TokenAsync();
        var firstSessionId = new JsonWebToken(firstToken).Id;
        var secondSessionId = new JsonWebToken(secondToken).Id;
        var logoutTime = fixture.Clock.GetUtcNow().AddSeconds(1);
        fixture.Clock.Set(logoutTime);
        var resetTime = logoutTime.AddMinutes(1);
        var resetClock = new AuthenticationTestClock(resetTime);
        await using var administrative = fixture.AdministrativeProvider(resetClock);
        using var resetScope = administrative.CreateScope();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);

        var logout = SendLogoutAsync(fixture.Client, firstToken);
        await fixture.WaitForAdvisoryWaitersAsync(1);
        var reset = resetScope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>()
            .ResetPasswordAsync("owner@example.invalid", userId, "New!Password456", CancellationToken.None);
        await fixture.WaitForAdvisoryWaitersAsync(2);
        await held.RollbackAsync();

        using var logoutResponse = await logout.WaitAsync(TimeSpan.FromSeconds(10));
        var resetResult = await reset.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.AreEqual(AdministrativeUserOperationState.PasswordReset, resetResult.State);
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.Set<AuthSession>().AsNoTracking().ToDictionaryAsync(session => session.Id);
        Assert.AreEqual(logoutTime, persisted[firstSessionId].RevokedAtUtc);
        Assert.AreEqual(resetTime, persisted[secondSessionId].RevokedAtUtc);
    }

    // Reset primeiro revoga ambas; logout já autenticado antes do commit confirma sem alterar a data existente.
    [TestMethod]
    public async Task ResetThenAdmittedLogoutDoesNotOverwriteRevocationsOrDeadlock()
    {
        var fixture = _fixture!;
        var userId = await fixture.SeedUserAsync();
        var firstToken = await fixture.TokenAsync();
        var secondToken = await fixture.TokenAsync();
        var firstSessionId = new JsonWebToken(firstToken).Id;
        var secondSessionId = new JsonWebToken(secondToken).Id;
        var resetTime = fixture.Clock.GetUtcNow().AddMinutes(2);
        var resetClock = new AuthenticationTestClock(resetTime);
        await using var administrative = fixture.AdministrativeProvider(resetClock);
        using var resetScope = administrative.CreateScope();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var held = await fixture.HoldIdentityLockAsync(connection);

        var reset = resetScope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>()
            .ResetPasswordAsync("owner@example.invalid", userId, "New!Password456", CancellationToken.None);
        await fixture.WaitForAdvisoryWaitersAsync(1);
        fixture.Clock.Set(resetTime.AddMinutes(-1));
        var logout = SendLogoutAsync(fixture.Client, firstToken);
        await fixture.WaitForAdvisoryWaitersAsync(2);
        await held.RollbackAsync();

        var resetResult = await reset.WaitAsync(TimeSpan.FromSeconds(10));
        using var logoutResponse = await logout.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(AdministrativeUserOperationState.PasswordReset, resetResult.State);
        Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.Set<AuthSession>().AsNoTracking().ToDictionaryAsync(session => session.Id);
        Assert.AreEqual(resetTime, persisted[firstSessionId].RevokedAtUtc);
        Assert.AreEqual(resetTime, persisted[secondSessionId].RevokedAtUtc);
        Assert.AreEqual(2, persisted.Values.Count(session => session.RevokedAtUtc is not null));
    }

    // Hosts distintos e um host recriado com o mesmo banco/chave observam revogações por logout e reset sem cache local.
    [TestMethod]
    public async Task LogoutAndResetRevocationsAreObservedOnOtherHostAndAfterRestart()
    {
        var fixture = _fixture!;
        var userId = await fixture.SeedUserAsync();
        var logoutToken = await fixture.TokenAsync();
        var resetToken = await fixture.TokenAsync();

        // O host secundário aceita os dois tokens antes das mutações, comprovando o compartilhamento de chave/configuração.
        {
            await using var otherHost = fixture.NewFactory(key: fixture.Key);
            using var otherClient = otherHost.CreateClient();
            var otherProbe = otherHost.Services.GetRequiredService<AuthenticationProbeCounter>();
            using var logoutTokenInitiallyAccepted = await PostgreSqlAuthenticationFixture.ProtectedAsync(otherClient, logoutToken);
            using var resetTokenInitiallyAccepted = await PostgreSqlAuthenticationFixture.ProtectedAsync(otherClient, resetToken);
            Assert.AreEqual(HttpStatusCode.OK, logoutTokenInitiallyAccepted.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, resetTokenInitiallyAccepted.StatusCode);
            Assert.AreEqual(2, otherProbe.Calls);

            using var logoutResponse = await SendLogoutAsync(fixture.Client, logoutToken);
            Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);
            using var logoutRevokedOnOtherHost = await PostgreSqlAuthenticationFixture.ProtectedAsync(otherClient, logoutToken);
            Assert.AreEqual(HttpStatusCode.Unauthorized, logoutRevokedOnOtherHost.StatusCode);
            Assert.AreEqual(2, otherProbe.Calls);

            await using var administrative = fixture.AdministrativeProvider();
            using var scope = administrative.CreateScope();
            var reset = await scope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>()
                .ResetPasswordAsync("owner@example.invalid", userId, "New!Password456", CancellationToken.None);
            Assert.AreEqual(AdministrativeUserOperationState.PasswordReset, reset.State);
            using var resetRevokedOnOtherHost = await PostgreSqlAuthenticationFixture.ProtectedAsync(otherClient, resetToken);
            Assert.AreEqual(HttpStatusCode.Unauthorized, resetRevokedOnOtherHost.StatusCode);
            Assert.AreEqual(2, otherProbe.Calls);
        }

        // Descartar e recriar o host simula reinicialização sem trocar banco nem chave de assinatura.
        await using var restartedHost = fixture.NewFactory(key: fixture.Key);
        using var restartedClient = restartedHost.CreateClient();
        var restartedProbe = restartedHost.Services.GetRequiredService<AuthenticationProbeCounter>();
        using var logoutTokenAfterRestart = await PostgreSqlAuthenticationFixture.ProtectedAsync(restartedClient, logoutToken);
        using var resetTokenAfterRestart = await PostgreSqlAuthenticationFixture.ProtectedAsync(restartedClient, resetToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, logoutTokenAfterRestart.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, resetTokenAfterRestart.StatusCode);
        Assert.AreEqual(0, restartedProbe.Calls);
    }

    // Uma ação já autenticada termina; somente chamadas protegidas novas passam pela validação revogatória persistida.
    [TestMethod]
    public async Task LogoutDoesNotCancelRequestWhoseAuthenticationAlreadyCompleted()
    {
        var fixture = _fixture!;
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var barrier = fixture.Factory.Services.GetRequiredService<AuthenticationProbeBarrier>();
        var entered = barrier.Arm();
        var inFlight = PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);

        try
        {
            await entered.WaitAsync(TimeSpan.FromSeconds(10));
            using var logoutResponse = await SendLogoutAsync(fixture.Client, token);
            Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);

            using var newRequest = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
            Assert.AreEqual(HttpStatusCode.Unauthorized, newRequest.StatusCode);
            Assert.AreEqual(1, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
        }
        finally
        {
            barrier.Release();
        }

        using var completedInFlight = await inFlight.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(HttpStatusCode.OK, completedInFlight.StatusCode);
        Assert.AreEqual(1, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // A referência carrega somente os campos persistidos necessários à conferência, sem token JWT.
    private static AuthenticationSessionReference Reference(AuthSession session) =>
        new(session.UserId, session.Id, session.CreatedAtUtc, session.ExpiresAtUtc);

    // Pausa o primeiro logout antes do commit para avançar o relógio e provar que o segundo não sobrescreve a data.
    private sealed class FirstLogoutCommitGate : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _commitObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Armed { get; set; }
        internal int Calls { get; private set; }
        internal TaskCompletionSource CommitObserved => _commitObserved;

        // A primeira confirmação é pausada enquanto o segundo logout segue bloqueado no advisory lock.
        internal void Release()
        {
            Armed = false;
            _release.TrySetResult();
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) { return result; }
            Calls++;
            _commitObserved.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    // Cria headers por requisição para verificar o challenge sem manter o Bearer no cliente compartilhado.
    private static Task<HttpResponseMessage> SendLogoutAsync(HttpClient client, string token,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return SendAndDisposeRequestAsync(client, request, cancellationToken);
    }

    // O conteúdo da resposta pertence ao teste; somente a request temporária é liberada após o envio.
    private static async Task<HttpResponseMessage> SendAndDisposeRequestAsync(HttpClient client,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request) { return await client.SendAsync(request, cancellationToken); }
    }

    // Observa a espera do UPDATE pela transação concorrente antes de avançar o relógio e liberar a linha.
    private static async Task<bool> WaitForTransactionLockWaiterAsync(
        PostgreSqlAuthenticationFixture fixture,
        TimeSpan maximumWait)
    {
        var deadline = DateTime.UtcNow + maximumWait;
        while (DateTime.UtcNow < deadline)
        {
            var waiters = await fixture.Context.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value"
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND usename = current_user
                  AND wait_event_type = 'Lock'
                  AND wait_event = 'transactionid'
                """).SingleAsync();
            if (waiters > 0) { return true; }
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return false;
    }

    // Permite exercitar somente o mapeamento HTTP posterior à autenticação com resultados/erros controlados.
    private sealed class TestRevoker : IAuthenticationSessionRevoker
    {
        private readonly LogoutResult? _result;
        private readonly Exception? _exception;

        public TestRevoker(LogoutResult result) => _result = result;
        public TestRevoker(Exception exception) => _exception = exception;

        public Task<LogoutResult> RevokeAsync(AuthenticationSessionReference reference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_exception is not null) { throw _exception; }
            return Task.FromResult(_result!);
        }
    }

    // Falha antes do commit delegado ao provider, após o UPDATE real e ainda dentro da transação aberta.
    private sealed class FailBeforeCommitInterceptor : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Calls { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) { return ValueTask.FromResult(result); }
            Calls++;
            return ValueTask.FromException<InterceptionResult>(new NpgsqlException("pre-commit-failure-sentinel"));
        }
    }

    // Injeta perda da resposta somente depois da confirmação física para distinguir commit incerto de rollback.
    private sealed class FailAfterCommitInterceptor : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Calls { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) { return Task.CompletedTask; }
            Calls++;
            return Task.FromException(new NpgsqlException("post-commit-acknowledgement-sentinel"));
        }
    }
}
