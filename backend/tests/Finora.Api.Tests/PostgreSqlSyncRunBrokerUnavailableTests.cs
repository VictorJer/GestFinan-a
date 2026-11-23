using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita a composição HTTP e os hosted services reais com RabbitMQ ausente e PostgreSQL descartável.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunBrokerUnavailableTests
{
    // Startup, liveness, login e consulta protegida continuam disponíveis sem conexão AMQP.
    [TestMethod]
    public async Task UnavailableRabbitMqDoesNotBlockStartupAuthenticationOrExistingReads()
    {
        // Recusa qualquer execução se a porta de teste não representar indisponibilidade local observável.
        var unavailableBrokerPort = SelectUnavailableBrokerPort();

        // O reset e o schema são limitados pelo fixture ao database descartável em loopback.
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await using var database = DisposablePostgresMigrationFixture.CreateValidated();
        await database.ResetToEmptySchemaAsync();
        Assert.IsTrue(await database.Context.Database.EnsureCreatedAsync());

        // A configuração aponta somente à porta loopback sem listener e usa credenciais sintéticas.
        var configuration = AuthenticationAvailabilityTests.Configuration(settings.ConnectionString);
        configuration["Messaging:RabbitMq:HostAddress"] = $"rabbitmq://127.0.0.1:{unavailableBrokerPort}/finora-mq-p01";
        configuration["Messaging:RabbitMq:Username"] = "synthetic-offline-user";
        configuration["Messaging:RabbitMq:Password"] = "synthetic-offline-password";
        configuration["Messaging:RabbitMq:UseTls"] = "false";
        await using var factory = new AuthenticationApiFactory(configuration);
        using var client = factory.CreateClient();
        AssertNoLoopbackListener(unavailableBrokerPort);

        // A liveness responde depois do startup completo do host, incluindo o ciclo real dos hosted services.
        using var health = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        Assert.AreEqual("{\"status\":\"ok\"}", await health.Content.ReadAsStringAsync());

        // O titular, a conexão e a SyncRun são exclusivamente sintéticos e existem antes da leitura HTTP.
        Guid syncRunId;
        using (var scope = factory.Services.CreateScope())
        {
            var user = new ApplicationUser
            {
                UserName = "broker-offline-owner@example.invalid",
                Email = "broker-offline-owner@example.invalid"
            };
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.IsTrue((await userManager.CreateAsync(user, " Password!123 ")).Succeeded);

            var now = DateTimeOffset.UtcNow;
            var connectionId = Guid.NewGuid();
            syncRunId = Guid.NewGuid();
            database.Context.Set<FinancialConnection>().Add(
                FinancialConnection.Create(connectionId, user.Id, "pluggy", "synthetic-broker-offline-item"));
            database.Context.Set<SyncRun>().Add(SyncRun.Create(
                syncRunId,
                user.Id,
                connectionId,
                now,
                DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1),
                requestedToExclusive: DateOnly.FromDateTime(now.UtcDateTime).AddDays(1),
                startedAtUtc: now.AddSeconds(1),
                finishedAtUtc: now.AddSeconds(2),
                state: SyncRunState.Succeeded,
                coverageState: SyncCoverageState.CompleteForRequestedWindow));
            await database.Context.SaveChangesAsync();
        }

        // Login precisa validar usuário e sessão no PostgreSQL enquanto o transporte AMQP segue indisponível.
        using var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "broker-offline-owner@example.invalid",
            password = " Password!123 "
        });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        using var loginDocument = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginDocument.RootElement.GetProperty("accessToken").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));

        // A leitura protegida prova que autenticação, sessão e consulta de uma execução existente continuam funcionais.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sync-runs/{syncRunId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var status = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
        using var statusDocument = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.AreEqual(syncRunId, statusDocument.RootElement.GetProperty("syncRunId").GetGuid());
        Assert.AreEqual("Succeeded", statusDocument.RootElement.GetProperty("state").GetString());
        Assert.IsFalse(statusDocument.RootElement.TryGetProperty("userId", out _));
        Assert.IsFalse(statusDocument.RootElement.TryGetProperty("connectionId", out _));

        // A mesma verificação após tráfego autenticado preserva health como liveness, não como dependência do broker.
        using var healthAfterRequests = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, healthAfterRequests.StatusCode);
    }

    // Seleciona porta efêmera por bind local e confirma na tabela do SO que nenhum listener permaneceu nela.
    private static int SelectUnavailableBrokerPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        AssertNoLoopbackListener(port);
        return port;
    }

    // Considera listeners loopback e binds wildcard IPv4/IPv6 para não ignorar publicação local concorrente.
    private static void AssertNoLoopbackListener(int port)
    {
        var isListening = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint =>
            endpoint.Port == port
            && (IPAddress.IsLoopback(endpoint.Address)
                || endpoint.Address.Equals(IPAddress.Any)
                || endpoint.Address.Equals(IPAddress.IPv6Any)));
        Assert.IsFalse(isListening, "A porta local escolhida para o broker está atendida por um listener TCP.");
    }
}
