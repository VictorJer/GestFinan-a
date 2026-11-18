using System.Net;
using Finora.Application.Authentication;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Indisponibilidade é exercitada depois de assinatura válida, preservando 401 quando o banco não é necessário.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlAuthenticationFailureTests
{
    // Schema ausente é falha operacional no login/consulta; token adulterado continua challenge 401 antes do banco.
    [TestMethod]
    public async Task MissingSchemaIs503ButInvalidSignatureRemains401()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("DROP TABLE \"AuthSessions\"");
        using var protectedResponse = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token);
        await AuthenticationAvailabilityTests.AssertProblemAsync(protectedResponse, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, protectedResponse.Headers.WwwAuthenticate.Count);
        using var invalid = await PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, PostgreSqlBearerTests.Tamper(token));
        await PostgreSqlBearerTests.AssertChallengeAsync(invalid);
        using var login = await fixture.LoginAsync();
        await AuthenticationAvailabilityTests.AssertProblemAsync(login, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }

    // Um segundo host com conexão indisponível não confunde a assinatura válida com sessão não encontrada ou senha errada.
    [TestMethod]
    public async Task UnavailableDatabaseAfterValidSignatureIs503WithoutProtectedData()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await using var unavailable = new AuthenticationApiFactory(AuthenticationAvailabilityTests.Configuration(key: fixture.Key), fixture.Clock);
        using var client = unavailable.CreateClient();
        using var response = await PostgreSqlAuthenticationFixture.ProtectedAsync(client, token);
        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, response.Headers.WwwAuthenticate.Count);
        using var invalid = await PostgreSqlAuthenticationFixture.ProtectedAsync(client, PostgreSqlBearerTests.Tamper(token));
        await PostgreSqlBearerTests.AssertChallengeAsync(invalid);
        Assert.AreEqual(0, unavailable.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
        Assert.IsFalse(unavailable.Logs.Text.Contains("sentinel", StringComparison.Ordinal));
    }

    // Uma leitura realmente bloqueada no PostgreSQL alcança timeout ou cancelamento sem executar a ação protegida.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SessionLookupTimeoutAndClientCancellationNeverReleaseProtectedData(bool cancelClient)
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
            services.AddScoped<IAuthenticationSessionReader>(provider => new AuthenticationSessionReader(
                provider.GetRequiredService<FinoraDbContext>(), provider.GetRequiredService<TimeProvider>())
            { OperationTimeout = cancelClient ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(1) }));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var held = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = held;
        command.CommandText = "LOCK TABLE \"AuthSessions\" IN ACCESS EXCLUSIVE MODE";
        await command.ExecuteNonQueryAsync();
        using var cancellation = new CancellationTokenSource();
        var pending = PostgreSqlAuthenticationFixture.ProtectedAsync(fixture.Client, token, cancellation.Token);
        await fixture.WaitForSessionReaderAsync();
        if (cancelClient)
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => { using var response = await pending; });
        }
        else
        {
            using var response = await pending;
            await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
            Assert.AreEqual(0, response.Headers.WwwAuthenticate.Count);
        }
        await held.RollbackAsync();
        Assert.AreEqual(0, fixture.Factory.Services.GetRequiredService<AuthenticationProbeCounter>().Calls);
    }
}
