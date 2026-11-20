using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Finora.Api.AdministrativeUsers;
using Finora.Application.Authentication;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Coordena host HTTP real e banco descartável; a barreira é verificada antes de resetar ou conectar.
internal sealed class PostgreSqlAuthenticationFixture : IAsyncDisposable
{
    private readonly DisposablePostgresMigrationFixture _database;
    private readonly Dictionary<string, string?> _apiConfiguration;
    internal AuthenticationApiFactory Factory { get; }
    internal HttpClient Client { get; }
    internal AuthenticationTestClock Clock { get; }
    internal string Key { get; }
    internal string ConnectionString { get; }
    internal FinoraDbContext Context => _database.Context;

    // A chave é criada por fixture, nunca lida de um segredo operacional ou persistida no banco.
    private PostgreSqlAuthenticationFixture(DisposablePostgresMigrationFixture database, string connection,
        Action<IServiceCollection>? configure, Dictionary<string, string?>? apiConfiguration)
    {
        _database = database;
        ConnectionString = connection;
        _apiConfiguration = apiConfiguration ?? new Dictionary<string, string?>();
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        Clock = new AuthenticationTestClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Factory = NewFactory(configure: configure);
        Client = Factory.CreateClient();
    }

    // Schema é aplicado explicitamente pela fixture, sem auto-migration na aplicação.
    internal static async Task<PostgreSqlAuthenticationFixture> CreateAsync(Action<IServiceCollection>? configure = null,
        Dictionary<string, string?>? apiConfiguration = null)
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var database = DisposablePostgresMigrationFixture.CreateValidated();
        await database.ResetToEmptySchemaAsync();
        await database.Context.Database.MigrateAsync(PostgreSqlAuthenticationSessionTests.MigrationId);
        return new PostgreSqlAuthenticationFixture(database, settings.ConnectionString, configure, apiConfiguration);
    }

    // Hosts adicionais preservam a mesma base e relógio para testar reinício/rotação sem dados fictícios de sessão.
    internal AuthenticationApiFactory NewFactory(string? key = null, Action<IServiceCollection>? configure = null,
        Dictionary<string, string?>? apiConfiguration = null)
    {
        var configuration = AuthenticationAvailabilityTests.Configuration(ConnectionString, key ?? Key);
        foreach (var entry in _apiConfiguration) { configuration[entry.Key] = entry.Value; }
        if (apiConfiguration is not null)
        {
            foreach (var entry in apiConfiguration) { configuration[entry.Key] = entry.Value; }
        }
        return new AuthenticationApiFactory(configuration, Clock, configure);
    }

    // Toda conta é provisionada com UserManager real; dados de fixtures são exclusivamente sintéticos.
    internal async Task<Guid> SeedUserAsync(string email = "owner@example.invalid", string password = " Password!123 ", bool lockoutEnabled = true)
    {
        using var scope = Factory.Services.CreateScope();
        var user = new ApplicationUser { UserName = email, Email = email, LockoutEnabled = lockoutEnabled };
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await manager.CreateAsync(user, password);
        Assert.IsTrue(result.Succeeded, "Provisionamento sintético deve respeitar a política Identity.");
        // CreateAsync habilita lockout pela política; a fixture reproduz explicitamente o estado legado quando solicitado.
        if (!lockoutEnabled) { Assert.IsTrue((await manager.SetLockoutEnabledAsync(user, false)).Succeeded); }
        return user.Id;
    }

    // Cada operação HTTP cria um escopo real e encaminha o cancelamento até o caso de uso.
    internal Task<HttpResponseMessage> LoginAsync(string email = "owner@example.invalid", string password = " Password!123 ",
        CancellationToken cancellationToken = default) => Client.PostAsJsonAsync("/api/auth/login", new { email, password }, cancellationToken);

    // O helper lê o token somente em memória, sem incluí-lo em mensagens de asserção ou logs.
    internal async Task<string> TokenAsync()
    {
        using var response = await LoginAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Bearer", body.RootElement.GetProperty("tokenType").GetString());
        Assert.AreEqual(1800, body.RootElement.GetProperty("expiresIn").GetInt32());
        Assert.IsFalse(body.RootElement.TryGetProperty("refreshToken", out _));
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    // Headers são por requisição, evitando interferência entre chamadas concorrentes.
    internal static async Task<HttpResponseMessage> ProtectedAsync(HttpClient client, string? token = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_tests/fallback-authentication-probe");
        if (token is not null) { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); }
        return await client.SendAsync(request, cancellationToken);
    }

    // As leituras de conferência não reaproveitam entidades rastreadas pelo host ou pela criação da fixture.
    internal async Task<ApplicationUser> ReadUserAsync(Guid id)
    {
        Context.ChangeTracker.Clear();
        return await Context.Users.AsNoTracking().SingleAsync(user => user.Id == id);
    }

    // O provider administrativo é separado do HTTP, preservando o provider efêmero do reset e o protocolo comum.
    internal ServiceProvider AdministrativeProvider(TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (clock is not null)
        {
            services.AddSingleton<TimeProvider>(clock);
        }
        services.AddAdministrativeUserIdentity(ConnectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Mantém o lock real aberto até DisposeAsync para observar espera, timeout e ordem entre processos/scopes.
    internal async Task<NpgsqlTransaction> HoldIdentityLockAsync(NpgsqlConnection connection)
    {
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        command.Parameters.AddWithValue("key", IdentityMutationLock.Key);
        await command.ExecuteScalarAsync();
        return transaction;
    }

    // A barreira observa espera no PostgreSQL; o pequeno polling não é usado para presumir conclusão de uma operação.
    internal async Task WaitForAdvisoryWaitersAsync(int minimum)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await Context.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype='advisory' AND NOT granted AND database=(SELECT oid FROM pg_database WHERE datname=current_database())")
            .SingleAsync(deadline.Token) < minimum)
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    // Observa a espera por leitura da tabela real para acionar cancelamento/timeout sem depender de sleeps arbitrários.
    internal async Task WaitForSessionReaderAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await Context.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM pg_locks l JOIN pg_class c ON c.oid=l.relation WHERE NOT l.granted AND c.relname='AuthSessions' AND l.database=(SELECT oid FROM pg_database WHERE datname=current_database())")
            .SingleAsync(deadline.Token) == 0)
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    // Libera recursos de teste sem executar operações destrutivas fora da fixture autorizada.
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _database.DisposeAsync();
    }
}
