using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using MassTransit.EntityFrameworkCoreIntegration;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var pluggyGuard = new UnexpectedPluggyReader();
        await using var factory = new AuthenticationApiFactory(configuration, configure: services =>
        {
            // A rota de leitura não deve alcançar nem mesmo o adapter externo substituído por este guard.
            services.RemoveAll<ITransactionImportReader>();
            services.AddSingleton<ITransactionImportReader>(pluggyGuard);
        });
        using var client = factory.CreateClient();
        AssertNoLoopbackListener(unavailableBrokerPort);

        // A liveness responde depois do startup completo do host, incluindo o ciclo real dos hosted services.
        using var health = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        Assert.AreEqual("{\"status\":\"ok\"}", await health.Content.ReadAsStringAsync());

        // O titular, a conexão e a SyncRun são exclusivamente sintéticos e existem antes da leitura HTTP.
        Guid syncRunId;
        Guid cardId;
        Guid movementId;
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
            cardId = Guid.NewGuid();
            movementId = Guid.NewGuid();
            database.Context.Set<FinancialConnection>().Add(
                FinancialConnection.Create(connectionId, user.Id, "pluggy", "synthetic-broker-offline-item"));
            var referenceDate = new DateOnly(2026, 11, 23);
            var facts = new TransactionFacts(
                "synthetic-broker-offline-transaction",
                "synthetic-broker-offline-account",
                "movimento sintético broker offline",
                12.50m,
                "BRL",
                "2026-11-22T12:00:00Z",
                "DEBIT",
                "POSTED",
                "TARIFA");
            var normalized = TransactionNormalizer.Normalize(facts, referenceDate);
            database.Context.Set<CardAccount>().Add(CardAccount.Create(
                cardId,
                user.Id,
                connectionId,
                "synthetic-broker-offline-account",
                "BRL",
                "Cartão sintético",
                "1234"));
            database.Context.Set<FinancialTransaction>().Add(FinancialTransaction.Create(
                movementId,
                user.Id,
                cardId,
                normalized,
                "broker-offline-read-test-v1",
                referenceDate));
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

        // O estado da conexão também permanece consultável enquanto o host tenta alcançar o broker indisponível.
        using var syncStateRequest = new HttpRequestMessage(HttpMethod.Get, "/api/connections/current/sync-state");
        syncStateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var syncStateResponse = await client.SendAsync(syncStateRequest);
        Assert.AreEqual(HttpStatusCode.OK, syncStateResponse.StatusCode);
        using var syncStateDocument = JsonDocument.Parse(await syncStateResponse.Content.ReadAsStringAsync());
        Assert.AreEqual(
            syncRunId,
            syncStateDocument.RootElement.GetProperty("latestExecution").GetProperty("syncRunId").GetGuid());

        // A linha do cartão persistido continua legível mesmo com RabbitMQ ausente e sob sessão real.
        var persistedRunIdsBeforeReads = await database.Context.Set<SyncRun>()
            .AsNoTracking()
            .OrderBy(run => run.Id)
            .Select(run => run.Id)
            .ToArrayAsync();
        var outboxCountBeforeReads = await database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync();
        using var cardsRequest = CreateAuthorizedGet("/api/cards", token!);
        using var cards = await client.SendAsync(cardsRequest);
        Assert.AreEqual(HttpStatusCode.OK, cards.StatusCode);
        using (var cardsDocument = JsonDocument.Parse(await cards.Content.ReadAsStringAsync()))
        {
            Assert.AreEqual(1, cardsDocument.RootElement.GetArrayLength());
            Assert.AreEqual(cardId, cardsDocument.RootElement[0].GetProperty("id").GetGuid());
        }

        // Filtros combinados e a página além do fim consultam apenas o snapshot, sem iniciar sincronização.
        using var firstPageRequest = CreateAuthorizedGet(
            $"/api/transactions?cardId={cardId:D}&search=offline&page=1&pageSize=1", token!);
        using var firstPage = await client.SendAsync(firstPageRequest);
        Assert.AreEqual(HttpStatusCode.OK, firstPage.StatusCode);
        using (var firstPageDocument = JsonDocument.Parse(await firstPage.Content.ReadAsStringAsync()))
        {
            Assert.AreEqual(1, firstPageDocument.RootElement.GetProperty("filteredCount").GetInt32());
            Assert.AreEqual(movementId, firstPageDocument.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        }

        using var nextPageRequest = CreateAuthorizedGet(
            $"/api/transactions?cardId={cardId:D}&search=offline&page=2&pageSize=1", token!);
        using var nextPage = await client.SendAsync(nextPageRequest);
        Assert.AreEqual(HttpStatusCode.OK, nextPage.StatusCode);
        using (var nextPageDocument = JsonDocument.Parse(await nextPage.Content.ReadAsStringAsync()))
        {
            Assert.AreEqual(1, nextPageDocument.RootElement.GetProperty("filteredCount").GetInt32());
            Assert.AreEqual(0, nextPageDocument.RootElement.GetProperty("items").GetArrayLength());
        }

        // A leitura preserva execuções/outbox já existentes e nunca invoca o adapter que representaria Pluggy.
        var persistedRunIdsAfterReads = await database.Context.Set<SyncRun>()
            .AsNoTracking()
            .OrderBy(run => run.Id)
            .Select(run => run.Id)
            .ToArrayAsync();
        CollectionAssert.AreEqual(persistedRunIdsBeforeReads, persistedRunIdsAfterReads);
        Assert.AreEqual(outboxCountBeforeReads,
            await database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, pluggyGuard.Calls);
        AssertNoLoopbackListener(unavailableBrokerPort);

        // A mesma verificação após tráfego autenticado preserva health como liveness, não como dependência do broker.
        using var healthAfterRequests = await client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, healthAfterRequests.StatusCode);
    }

    // Cada chamada envia seu próprio token e evita estado global de Authorization no client da fixture.
    private static HttpRequestMessage CreateAuthorizedGet(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
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

    // O guard falha se uma consulta HTTP passar a pedir coleta; nenhum HttpClient real da Pluggy é usado no teste.
    private sealed class UnexpectedPluggyReader : ITransactionImportReader
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<TransactionImportReadResult> ReadAsync(
            string externalItemId,
            DateOnly dateFrom,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("Leituras HTTP não podem invocar a coleta Pluggy.");
        }
    }
}
