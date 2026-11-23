using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Finora.Api.Authentication;
using Finora.Application.Authentication;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Messaging;
using Finora.Infrastructure.Messaging.Contracts;
using Finora.Infrastructure.Persistence;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita a rota autenticada com o PostgreSQL descartável e transporte in-memory, sem iniciar RabbitMQ ou Pluggy.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunAcceptanceApiTests
{
    // A rota retorna 202 apenas depois do commit e uma repetição pela mesma chave responde com o mesmo identificador.
    [TestMethod]
    public async Task HttpAcceptanceCommitsRunAndOutboxBeforeReturning202AndReplaysSameRun()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);

        using var firstResponse = await fixture.PostSyncAsync("http-acceptance-key");
        Assert.AreEqual(HttpStatusCode.Accepted, firstResponse.StatusCode);
        var firstId = await ReadAcceptedIdAsync(firstResponse);
        Assert.AreEqual(1, fixture.CommitEvidence.CommittedAcceptanceCount);
        Assert.IsTrue(fixture.CommitEvidence.Snapshots.Any(snapshot => snapshot.SyncRuns == 1 && snapshot.OutboxMessages == 1));

        var delivered = await fixture.Receipt.Message.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(firstId, delivered.Message.SyncRunId);
        Assert.AreEqual(firstId, delivered.MessageId);

        using var replayResponse = await fixture.PostSyncAsync("http-acceptance-key");
        Assert.AreEqual(HttpStatusCode.Accepted, replayResponse.StatusCode);
        Assert.AreEqual(firstId, await ReadAcceptedIdAsync(replayResponse));
        Assert.AreEqual(1, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
    }

    // Entrada com importação recente responde not_required sem execução, outbox ou consulta externa.
    [TestMethod]
    public async Task OnEntryWithRecentSuccessReturnsNotRequiredWithoutPersistingAcceptance()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(
            connectionCount: 1,
            lastSuccessfulImportAtUtc: DateTimeOffset.UtcNow.AddHours(-1));

        using var response = await fixture.PostOnEntryAsync("http-entry-recent-key");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("not_required", body.RootElement.GetProperty("decision").GetString());
        Assert.IsFalse(body.RootElement.TryGetProperty("syncRunId", out _));
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, fixture.CommitEvidence.CommittedAcceptanceCount);
    }

    // Entrada antiga aceita após commit; uma intenção nova durante a execução ativa recebe 409 sem duplicar o par.
    [TestMethod]
    public async Task OnEntryAcceptsThenReturnsActiveConflictForAnotherKey()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);

        using var accepted = await fixture.PostOnEntryAsync("http-entry-accepted-key");
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode);
        var acceptedId = await ReadAcceptedIdAsync(accepted);
        Assert.AreEqual(1, fixture.CommitEvidence.CommittedAcceptanceCount);
        var delivered = await fixture.Receipt.Message.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(acceptedId, delivered.Message.SyncRunId);

        using var active = await fixture.PostOnEntryAsync("http-entry-active-key");
        Assert.AreEqual(HttpStatusCode.Conflict, active.StatusCode);
        using var activeBody = JsonDocument.Parse(await active.Content.ReadAsStringAsync());
        Assert.AreEqual("SYNC_RUN_ALREADY_ACTIVE", activeBody.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(acceptedId, activeBody.RootElement.GetProperty("syncRunId").GetGuid());
        Assert.AreEqual(1, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(1, fixture.CommitEvidence.CommittedAcceptanceCount);
    }

    // O bloqueio por cooldown publica o prazo e Retry-After sem aceitar nem enfileirar uma nova intenção.
    [TestMethod]
    public async Task OnEntryCooldownReturns429AndNextAllowedAtWithoutCreatingRunOrOutbox()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);
        using var first = await fixture.PostSyncAsync("http-cooldown-original-key");
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        var originalId = await ReadAcceptedIdAsync(first);
        var firstCreatedAtUtc = await fixture.Database.Context.Set<SyncRun>().AsNoTracking()
            .Where(run => run.Id == originalId)
            .Select(run => run.CreatedAtUtc)
            .SingleAsync();
        await MarkRunFailedAsync(
            fixture.Database.Context,
            originalId,
            SyncRunFailureCodes.ProviderUnavailable);

        using var blocked = await fixture.PostOnEntryAsync("http-cooldown-second-key");

        Assert.AreEqual(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.IsTrue(blocked.Headers.TryGetValues("Retry-After", out var retryValues));
        Assert.IsTrue(int.TryParse(retryValues!.Single(), NumberStyles.None, CultureInfo.InvariantCulture, out var retrySeconds));
        Assert.IsTrue(retrySeconds is >= 0 and <= 300);
        using var problem = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.AreEqual("SYNC_RUN_MINIMUM_INTERVAL_ACTIVE", problem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(
            firstCreatedAtUtc.AddMinutes(5),
            problem.RootElement.GetProperty("nextAllowedAtUtc").GetDateTimeOffset());
        Assert.AreEqual(1, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(1, fixture.CommitEvidence.CommittedAcceptanceCount);
    }

    // AUTH_REQUIRED retorna orientação sanitizada, sem dizer que o acesso foi renovado ou iniciar outra execução.
    [TestMethod]
    public async Task OnEntryAfterAuthorizationFailureRequestsReauthorizationWithoutRepeating()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);
        using var first = await fixture.PostSyncAsync("http-auth-required-original-key");
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        var originalId = await ReadAcceptedIdAsync(first);
        await MarkRunFailedAsync(
            fixture.Database.Context,
            originalId,
            SyncRunFailureCodes.AuthenticationRequired);

        using var response = await fixture.PostOnEntryAsync("http-auth-required-entry-key");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("reauthorization_required", body.RootElement.GetProperty("decision").GetString());
        StringAssert.Contains(body.RootElement.GetProperty("guidance").GetString()!, "Renove o acesso");
        Assert.IsFalse(body.RootElement.TryGetProperty("syncRunId", out _));
        Assert.AreEqual(1, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(1, fixture.CommitEvidence.CommittedAcceptanceCount);
    }

    // Falha ao consultar tabela persistente é indisponibilidade sanitizada, nunca uma aceitação HTTP 202.
    [TestMethod]
    public async Task MissingSyncRunTableReturnsUnavailableWithoutAcceptingEntry()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);
        await fixture.Database.Context.Database.ExecuteSqlRawAsync("DROP TABLE \"SyncRun\" CASCADE;");

        using var response = await fixture.PostOnEntryAsync("http-database-unavailable-key");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("DEPENDENCY_UNAVAILABLE", problem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, fixture.CommitEvidence.CommittedAcceptanceCount);
    }

    // Estado sem importação distingue ausência de sucesso e de tentativa sem criar trabalho ou obrigação.
    [TestMethod]
    public async Task SyncStateWithoutHistoryReportsUpdateNeededAndUnknownSource()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);

        using var response = await fixture.GetSyncStateAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(body.RootElement.GetProperty("updateNeeded").GetBoolean());
        Assert.IsFalse(body.RootElement.GetProperty("reauthorizationRequired").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("lastSuccessfulImportAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("sourceUpdatedAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("lastAttemptAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("nextAllowedAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("latestExecution").ValueKind);
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
    }

    // Um sucesso recente evita atualização, e a leitura preserva o horário de origem desconhecido como null.
    [TestMethod]
    public async Task SyncStateWithRecentSuccessReportsNoUpdateWithoutInventingSourceTime()
    {
        var lastSuccess = DateTimeOffset.UtcNow.AddHours(-1);
        await using var fixture = await AcceptanceApiFixture.CreateAsync(
            connectionCount: 1,
            lastSuccessfulImportAtUtc: lastSuccess);
        // Compara com o timestamp persistido, respeitando a precisão de microssegundos do PostgreSQL.
        var persistedLastSuccess = await fixture.Database.Context.Set<FinancialConnection>()
            .AsNoTracking()
            .Select(connection => connection.LastSuccessfulImportAtUtc)
            .SingleAsync();

        using var response = await fixture.GetSyncStateAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsFalse(body.RootElement.GetProperty("updateNeeded").GetBoolean());
        Assert.AreEqual(persistedLastSuccess, body.RootElement.GetProperty("lastSuccessfulImportAtUtc").GetDateTimeOffset());
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("sourceUpdatedAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("latestExecution").ValueKind);
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
    }

    // Falha AUTH_REQUIRED posterior não avança o último sucesso e a projeção preserva tentativa/cobertura desconhecida.
    [TestMethod]
    public async Task SyncStateAfterAuthorizationFailurePreservesSuccessAndExposesSanitizedRun()
    {
        var lastSuccess = DateTimeOffset.UtcNow.AddHours(-25);
        await using var fixture = await AcceptanceApiFixture.CreateAsync(
            connectionCount: 1,
            lastSuccessfulImportAtUtc: lastSuccess);
        // A leitura da projeção deve preservar o fato confirmado no banco, não os ticks excedentes do valor de entrada.
        var persistedLastSuccess = await fixture.Database.Context.Set<FinancialConnection>()
            .AsNoTracking()
            .Select(connection => connection.LastSuccessfulImportAtUtc)
            .SingleAsync();
        using var accepted = await fixture.PostSyncAsync("http-state-auth-required-key");
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode);
        var syncRunId = await ReadAcceptedIdAsync(accepted);
        var createdAtUtc = await fixture.Database.Context.Set<SyncRun>().AsNoTracking()
            .Where(run => run.Id == syncRunId)
            .Select(run => run.CreatedAtUtc)
            .SingleAsync();
        await MarkRunFailedAsync(
            fixture.Database.Context,
            syncRunId,
            SyncRunFailureCodes.AuthenticationRequired);

        using var response = await fixture.GetSyncStateAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(body.RootElement.GetProperty("updateNeeded").GetBoolean());
        Assert.IsTrue(body.RootElement.GetProperty("reauthorizationRequired").GetBoolean());
        Assert.AreEqual(persistedLastSuccess, body.RootElement.GetProperty("lastSuccessfulImportAtUtc").GetDateTimeOffset());
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("sourceUpdatedAtUtc").ValueKind);
        Assert.AreEqual(createdAtUtc, body.RootElement.GetProperty("lastAttemptAtUtc").GetDateTimeOffset());
        Assert.AreEqual(createdAtUtc.AddMinutes(5), body.RootElement.GetProperty("nextAllowedAtUtc").GetDateTimeOffset());
        var execution = body.RootElement.GetProperty("latestExecution");
        Assert.AreEqual(syncRunId, execution.GetProperty("syncRunId").GetGuid());
        Assert.AreEqual("Failed", execution.GetProperty("state").GetString());
        Assert.AreEqual("Unknown", execution.GetProperty("coverageState").GetString());
        Assert.AreEqual(SyncRunFailureCodes.AuthenticationRequired, execution.GetProperty("failureCode").GetString());
        foreach (var sensitiveProperty in new[] { "userId", "connectionId", "externalItemId", "idempotencyKeyHash" })
        {
            Assert.IsFalse(body.RootElement.ToString().Contains(sensitiveProperty, StringComparison.OrdinalIgnoreCase));
        }
        // Valores privados reais não podem sair no DTO nem nos logs, mesmo quando a execução expõe AUTH_REQUIRED.
        var connection = await fixture.Database.Context.Set<FinancialConnection>().AsNoTracking().SingleAsync();
        var acceptedRun = await fixture.Database.Context.Set<SyncRun>().AsNoTracking()
            .SingleAsync(run => run.Id == syncRunId);
        var sensitiveValues = new[]
        {
            connection.UserId.ToString("D"),
            connection.Id.ToString("D"),
            connection.ExternalItemId,
            acceptedRun.IdempotencyKeyHash!,
            "http-state-auth-required-key"
        };
        var responseJson = body.RootElement.ToString();
        foreach (var sensitiveValue in sensitiveValues)
        {
            Assert.IsFalse(responseJson.Contains(sensitiveValue, StringComparison.Ordinal));
            Assert.IsFalse(fixture.Logs.Contains(sensitiveValue, StringComparison.Ordinal));
        }
        Assert.AreEqual(1, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
    }

    // Um titular alheio recebe o mesmo 404 genérico, enquanto o dono autentica; a rota também exige Bearer válido.
    [TestMethod]
    public async Task SyncStateIsOwnedAndRequiresAuthentication()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(
            connectionCount: 1,
            lastSuccessfulImportAtUtc: DateTimeOffset.UtcNow.AddHours(-1));

        using var ownerResponse = await fixture.GetSyncStateAsync();
        Assert.AreEqual(HttpStatusCode.OK, ownerResponse.StatusCode);
        using var ownerBody = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync());
        Assert.IsFalse(ownerBody.RootElement.GetProperty("lastSuccessfulImportAtUtc").ValueKind == JsonValueKind.Null);

        using var otherOwnerResponse = await fixture.GetSyncStateAsync(fixture.OtherToken);
        await AuthenticationAvailabilityTests.AssertProblemAsync(otherOwnerResponse, HttpStatusCode.NotFound);
        using var otherOwnerProblem = JsonDocument.Parse(await otherOwnerResponse.Content.ReadAsStringAsync());
        Assert.IsFalse(otherOwnerProblem.RootElement.TryGetProperty("lastSuccessfulImportAtUtc", out _));
        Assert.IsFalse(otherOwnerProblem.RootElement.TryGetProperty("latestExecution", out _));

        using var anonymousResponse = await fixture.GetSyncStateAsync(authenticated: false);
        await AuthenticationAvailabilityTests.AssertProblemAsync(anonymousResponse, HttpStatusCode.Unauthorized);
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
    }

    // A ausência de conexão própria retorna 404 sanitizado sem criar execução ou obrigação de transporte.
    [TestMethod]
    public async Task SyncStateWithoutConnectionReturnsNotFoundWithoutCreatingWork()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 0);

        using var response = await fixture.GetSyncStateAsync();

        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.NotFound);
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
    }

    // Mais de uma conexão própria é ambígua; a leitura falha fechada sem selecionar ou expor uma delas.
    [TestMethod]
    public async Task SyncStateWithAmbiguousConnectionsReturnsSanitizedConflict()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 2);

        using var response = await fixture.GetSyncStateAsync();

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("RESOURCE_CONFLICT", problem.RootElement.GetProperty("code").GetString());
        foreach (var sensitiveProperty in new[] { "connectionId", "externalItemId", "userId" })
        {
            Assert.IsFalse(problem.RootElement.TryGetProperty(sensitiveProperty, out _));
        }
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
    }

    // A consulta de estado e a consulta de execução continuam somente leitura quando o broker não está configurado.
    [TestMethod]
    public async Task SyncStateAndExecutionStatusRemainReadableWithoutBrokerAndDoNotCreateWork()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(
            connectionCount: 1,
            configureInMemoryOutbox: false);

        using var state = await fixture.GetSyncStateAsync();
        Assert.AreEqual(HttpStatusCode.OK, state.StatusCode);
        using var missingExecution = await fixture.GetSyncRunAsync(Guid.NewGuid(), fixture.Token);
        await AuthenticationAvailabilityTests.AssertProblemAsync(missingExecution, HttpStatusCode.NotFound);
        using var repeatedState = await fixture.GetSyncStateAsync();
        Assert.AreEqual(HttpStatusCode.OK, repeatedState.StatusCode);

        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, fixture.CommitEvidence.CommittedAcceptanceCount);
    }

    // Falha antes do commit físico é reportada conservadoramente como indeterminada; retry pela chave converge após rollback.
    [TestMethod]
    public async Task CommitFailureBeforePhysicalCommitReturnsIndeterminateAndRetryCreatesOnePair()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);
        fixture.CommitEvidence.FailNextCommit();

        using var failedResponse = await fixture.PostSyncAsync("http-rollback-key");
        await AssertAcceptanceIndeterminateAsync(failedResponse);
        Assert.IsTrue(fixture.CommitEvidence.Snapshots.Any(snapshot => snapshot.SyncRuns == 1 && snapshot.OutboxMessages == 1));
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxState>().AsNoTracking().CountAsync());

        using var retryResponse = await fixture.PostSyncAsync("http-rollback-key");
        Assert.AreEqual(HttpStatusCode.Accepted, retryResponse.StatusCode);
        var retryId = await ReadAcceptedIdAsync(retryResponse);
        Assert.AreEqual(1, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(1, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.AreEqual(2, fixture.CommitEvidence.Snapshots.Count(snapshot => snapshot.SyncRuns == 1 && snapshot.OutboxMessages == 1));
        var delivered = await fixture.Receipt.Message.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(retryId, delivered.Message.SyncRunId);
        Assert.AreEqual(1, fixture.Receipt.MessageCount);
    }

    // Perda simulada após o commit físico retorna estado indeterminado; replay recupera o par já confirmado sem duplicá-lo.
    [TestMethod]
    public async Task LostCommitConfirmationAfterPhysicalCommitReturnsIndeterminateAndReplayReusesPair()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);
        fixture.CommitEvidence.LoseNextCommitConfirmation();

        using var uncertainResponse = await fixture.PostSyncAsync("http-lost-confirmation-key");
        await AssertAcceptanceIndeterminateAsync(uncertainResponse);
        Assert.AreEqual(1, fixture.CommitEvidence.LostConfirmationCount);
        Assert.AreEqual(1, fixture.CommitEvidence.Snapshots.Count(snapshot => snapshot.SyncRuns == 1 && snapshot.OutboxMessages == 1));

        var persistedId = await fixture.Database.Context.Set<SyncRun>()
            .AsNoTracking()
            .Select(run => run.Id)
            .SingleAsync();
        var delivered = await fixture.Receipt.Message.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(persistedId, delivered.Message.SyncRunId);
        Assert.AreEqual(1, fixture.Receipt.MessageCount);
        await WaitForOutboxDrainAsync(fixture.Database.Context);
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());

        using var retryResponse = await fixture.PostSyncAsync("http-lost-confirmation-key");
        Assert.AreEqual(HttpStatusCode.Accepted, retryResponse.StatusCode);
        Assert.AreEqual(persistedId, await ReadAcceptedIdAsync(retryResponse));
        Assert.AreEqual(1, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.AreEqual(1, fixture.CommitEvidence.Snapshots.Count(snapshot => snapshot.SyncRuns == 1 && snapshot.OutboxMessages == 1));
        Assert.AreEqual(1, fixture.Receipt.MessageCount);
    }

    // Intenções distintas que disputam a mesma conexão são serializadas no PostgreSQL e a perdedora recebe referência segura.
    [TestMethod]
    public async Task ConcurrentDistinctIntentionsCreateOneRunAndReturnActiveConflict()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);

        var responses = await Task.WhenAll(
            fixture.PostSyncAsync("concurrent-intent-a"),
            fixture.PostSyncAsync("concurrent-intent-b"));
        try
        {
            var accepted = responses.Single(response => response.StatusCode == HttpStatusCode.Accepted);
            var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            var acceptedId = await ReadAcceptedIdAsync(accepted);
            using var conflictBody = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
            Assert.AreEqual("SYNC_RUN_ALREADY_ACTIVE", conflictBody.RootElement.GetProperty("code").GetString());
            Assert.AreEqual(acceptedId, conflictBody.RootElement.GetProperty("syncRunId").GetGuid());
            Assert.IsFalse(conflictBody.RootElement.TryGetProperty("userId", out _));
            Assert.IsFalse(conflictBody.RootElement.TryGetProperty("connectionId", out _));
            Assert.AreEqual(1, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());

            var delivered = await fixture.Receipt.Message.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(acceptedId, delivered.Message.SyncRunId);
            Assert.AreEqual(1, fixture.Receipt.MessageCount);
            Assert.AreEqual(1, fixture.CommitEvidence.Snapshots.Count(snapshot => snapshot.SyncRuns == 1 && snapshot.OutboxMessages == 1));
        }
        finally
        {
            foreach (var response in responses) { response.Dispose(); }
        }
    }

    // O titular consulta a projeção mínima; titular alheio e ID inexistente recebem o mesmo 404, e a rota exige autenticação.
    [TestMethod]
    public async Task StatusEndpointReturnsSanitizedOwnedRunAndHidesOtherOwners()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1);
        using var accepted = await fixture.PostSyncAsync("status-owner-key");
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode);
        var syncRunId = await ReadAcceptedIdAsync(accepted);

        // Agenda uma repetição na mesma execução para provar que o próximo instante é observável pelo titular.
        var acceptedRun = await fixture.Database.Context.Set<SyncRun>().AsNoTracking()
            .SingleAsync(run => run.Id == syncRunId);
        var acquired = await new SyncRunLeaseStore(fixture.Database.Context).AcquireAsync(
            syncRunId, acceptedRun.CreatedAtUtc, CancellationToken.None);
        Assert.AreEqual(SyncRunLeaseAcquisitionStatus.Acquired, acquired.Status);
        var retryAtUtc = acceptedRun.CreatedAtUtc.AddMinutes(2);
        Assert.IsTrue(await new SyncRunLeaseStore(fixture.Database.Context).ScheduleRetryAsync(
            acquired.Grant!,
            acceptedRun.CreatedAtUtc.AddSeconds(30),
            retryAtUtc,
            SyncRunFailureCodes.RateLimited,
            pagesRead: 1,
            receivedCount: 4,
            CancellationToken.None));

        using var ownStatus = await fixture.GetSyncRunAsync(syncRunId, fixture.Token);
        Assert.AreEqual(HttpStatusCode.OK, ownStatus.StatusCode);
        using var ownBody = JsonDocument.Parse(await ownStatus.Content.ReadAsStringAsync());
        Assert.AreEqual(syncRunId, ownBody.RootElement.GetProperty("syncRunId").GetGuid());
        Assert.AreEqual("Pending", ownBody.RootElement.GetProperty("state").GetString());
        Assert.IsTrue(ownBody.RootElement.GetProperty("requestedToExclusive").ValueKind == JsonValueKind.String);
        Assert.AreEqual(retryAtUtc, ownBody.RootElement.GetProperty("nextAttemptAtUtc").GetDateTimeOffset());
        foreach (var sensitiveProperty in new[] { "userId", "connectionId", "idempotencyKeyHash", "requestFingerprint" })
        {
            Assert.IsFalse(ownBody.RootElement.TryGetProperty(sensitiveProperty, out _), sensitiveProperty);
        }
        Assert.IsFalse((await ownStatus.Content.ReadAsStringAsync()).Contains("status-owner-key", StringComparison.Ordinal));

        using var otherStatus = await fixture.GetSyncRunAsync(syncRunId, fixture.OtherToken);
        using var missingStatus = await fixture.GetSyncRunAsync(Guid.NewGuid(), fixture.OtherToken);
        await AuthenticationAvailabilityTests.AssertProblemAsync(otherStatus, HttpStatusCode.NotFound);
        await AuthenticationAvailabilityTests.AssertProblemAsync(missingStatus, HttpStatusCode.NotFound);
        using var otherProblem = JsonDocument.Parse(await otherStatus.Content.ReadAsStringAsync());
        using var missingProblem = JsonDocument.Parse(await missingStatus.Content.ReadAsStringAsync());
        Assert.AreEqual(otherProblem.RootElement.GetProperty("code").GetString(), missingProblem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(otherProblem.RootElement.GetProperty("title").GetString(), missingProblem.RootElement.GetProperty("title").GetString());

        using var anonymousStatus = await fixture.GetSyncRunAsync(syncRunId, null);
        await AuthenticationAvailabilityTests.AssertProblemAsync(anonymousStatus, HttpStatusCode.Unauthorized);
    }

    // Mais de uma conexão própria não é resolvida por ordem/recência e não cria execução nem obrigação.
    [TestMethod]
    public async Task AmbiguousCurrentConnectionIsRejectedWithoutPersistingAcceptance()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 2);

        using var response = await fixture.PostSyncAsync("ambiguous-current-key");
        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.Conflict);
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
    }

    // Sem configuração explícita do broker, o endpoint falha fechado sem criar SyncRun ou obrigação de transporte.
    [TestMethod]
    public async Task MissingTransportConfigurationReturnsUnavailableWithoutPersistingAcceptance()
    {
        await using var fixture = await AcceptanceApiFixture.CreateAsync(connectionCount: 1, configureInMemoryOutbox: false);

        using var missingKeyResponse = await fixture.PostSyncAsync(null);
        await AuthenticationAvailabilityTests.AssertProblemAsync(missingKeyResponse, HttpStatusCode.BadRequest);

        using var missingEntryKeyResponse = await fixture.PostOnEntryAsync(null);
        await AuthenticationAvailabilityTests.AssertProblemAsync(missingEntryKeyResponse, HttpStatusCode.BadRequest);

        using var response = await fixture.PostSyncAsync("transport-not-configured-key");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("DEPENDENCY_UNAVAILABLE", problem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());

        using var entryResponse = await fixture.PostOnEntryAsync("entry-transport-not-configured-key");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, entryResponse.StatusCode);
        Assert.AreEqual(0, await fixture.Database.Context.Set<SyncRun>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, await fixture.Database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
    }

    // O DTO publicado contém somente a referência persistida, sem alegar entrega, estado final ou sucesso financeiro.
    private static async Task<Guid> ReadAcceptedIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, document.RootElement.EnumerateObject().Count());
        return document.RootElement.GetProperty("syncRunId").GetGuid();
    }

    // Conclui a tentativa sintética para liberar a política de nova intenção sem simular sucesso do provedor.
    private static async Task MarkRunFailedAsync(FinoraDbContext context, Guid syncRunId, string failureCode)
    {
        var run = await context.Set<SyncRun>().SingleAsync(value => value.Id == syncRunId);
        Assert.IsTrue(run.TryBeginAttempt(run.CreatedAtUtc));
        Assert.IsTrue(run.TryCompleteAttemptFailure(
            1,
            run.CreatedAtUtc.AddSeconds(30),
            failureCode,
            pagesRead: 0,
            receivedCount: 0));
        await context.SaveChangesAsync();
    }

    // O consumer registra a recepção antes de o transport confirmar e remover a mensagem da outbox.
    private static async Task WaitForOutboxDrainAsync(FinoraDbContext context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await context.Set<OutboxMessage>().AsNoTracking().AnyAsync(timeout.Token))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }

    // O envelope instrui retry estável sem revelar a exceção ou classificar ausência observada como rollback.
    private static async Task AssertAcceptanceIndeterminateAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.AreEqual((int)HttpStatusCode.ServiceUnavailable, problem.RootElement.GetProperty("status").GetInt32());
        Assert.AreEqual("ACCEPTANCE_RESULT_INDETERMINATE", problem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(
            "O resultado da aceitação é indeterminado. Repita a solicitação usando a mesma chave Idempotency-Key.",
            problem.RootElement.GetProperty("title").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.IsFalse(body.Contains("synthetic", StringComparison.OrdinalIgnoreCase));
    }

    // Coordena host HTTP real, usuário sintético e schema criado somente no banco PostgreSQL descartável allowlisted.
    private sealed class AcceptanceApiFixture : IAsyncDisposable
    {
        private readonly AuthenticationApiFactory _factory;
        private readonly HttpClient _client;
        private readonly string _token;
        private readonly string _otherToken;

        private AcceptanceApiFixture(
            DisposablePostgresMigrationFixture database,
            AuthenticationApiFactory factory,
            HttpClient client,
            string token,
            string otherToken,
            SyncRunExecutionReceipt receipt,
            AcceptanceCommitEvidenceInterceptor commitEvidence)
        {
            Database = database;
            _factory = factory;
            _client = client;
            _token = token;
            _otherToken = otherToken;
            Receipt = receipt;
            CommitEvidence = commitEvidence;
        }

        internal DisposablePostgresMigrationFixture Database { get; }
        internal string Token => _token;
        internal string OtherToken => _otherToken;
        internal SyncRunExecutionReceipt Receipt { get; }
        internal AcceptanceCommitEvidenceInterceptor CommitEvidence { get; }
        internal string Logs => _factory.Logs.Text;

        // A fixture aplica apenas o modelo atual em schema descartável e substitui RabbitMQ por transporte in-memory.
        internal static async Task<AcceptanceApiFixture> CreateAsync(
            int connectionCount,
            bool configureInMemoryOutbox = true,
            DateTimeOffset? lastSuccessfulImportAtUtc = null)
        {
            var database = DisposablePostgresMigrationFixture.CreateValidated();
            await database.ResetToEmptySchemaAsync();
            Assert.IsTrue(await database.Context.Database.EnsureCreatedAsync());
            // Reutiliza a configuração validada da fixture; Npgsql pode ocultar a senha ao expor a conexão aberta.
            var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
            var receipt = new SyncRunExecutionReceipt();
            var commitEvidence = new AcceptanceCommitEvidenceInterceptor();
            var configuration = AuthenticationAvailabilityTests.Configuration(
                connectionString,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var factory = new AuthenticationApiFactory(configuration, configure: services =>
            {
                services.AddSingleton<IDbTransactionInterceptor>(commitEvidence);
                services.AddSingleton(receipt);
                if (configureInMemoryOutbox)
                {
                    services.AddFinoraSyncRunExecutionOutbox(registration =>
                    {
                        registration.AddConsumer<SyncRunExecutionCaptureConsumer>();
                        registration.UsingInMemory((context, bus) =>
                            bus.ReceiveEndpoint(SyncRunExecutionTopology.QueueName, endpoint =>
                                endpoint.ConfigureConsumer<SyncRunExecutionCaptureConsumer>(context)));
                    });
                    services.Replace(ServiceDescriptor.Singleton(new SyncRunAcceptanceAvailability(true)));
                }
            });
            var client = factory.CreateClient();

            try
            {
                using var scope = factory.Services.CreateScope();
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser
                {
                    UserName = "sync-api-owner@example.invalid",
                    Email = "sync-api-owner@example.invalid"
                };
                Assert.IsTrue((await manager.CreateAsync(user, " Password!123 ")).Succeeded);

                var connectionIds = Enumerable.Range(0, connectionCount).Select(_ => Guid.NewGuid()).ToArray();
                database.Context.Set<FinancialConnection>().AddRange(connectionIds.Select((id, index) =>
                    FinancialConnection.Create(
                        id,
                        user.Id,
                        "pluggy",
                        $"synthetic-api-item-{index}",
                        lastSuccessfulImportAtUtc: lastSuccessfulImportAtUtc)));
                await database.Context.SaveChangesAsync();

                using var login = await client.PostAsJsonAsync("/api/auth/login", new
                {
                    email = "sync-api-owner@example.invalid",
                    password = " Password!123 "
                });
                Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
                using var loginDocument = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                var token = loginDocument.RootElement.GetProperty("accessToken").GetString()!;

                // O login atual exige um titular local único; a identidade adversária é criada depois da emissão e recebe sessão sintética válida.
                var otherUser = new ApplicationUser
                {
                    UserName = "sync-api-other-owner@example.invalid",
                    Email = "sync-api-other-owner@example.invalid"
                };
                Assert.IsTrue((await manager.CreateAsync(otherUser, " Password!123 ")).Succeeded);
                var otherSessionCreated = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                var otherSession = AuthSession.Create(
                    AuthSession.GenerateId(), otherUser.Id, otherSessionCreated, otherSessionCreated.AddMinutes(30));
                database.Context.Add(otherSession);
                await database.Context.SaveChangesAsync();
                var otherToken = factory.Services.GetRequiredService<IAccessTokenIssuer>()
                    .Issue(otherUser.Id, otherSession.Id, otherSession.CreatedAtUtc, otherSession.ExpiresAtUtc)
                    .AccessToken;
                return new AcceptanceApiFixture(database, factory, client, token, otherToken, receipt, commitEvidence);
            }
            catch
            {
                client.Dispose();
                await factory.DisposeAsync();
                await database.DisposeAsync();
                throw;
            }
        }

        // Cada request recebe a chave em header próprio e a sessão real emitida pelo host de teste.
        internal async Task<HttpResponseMessage> PostSyncAsync(string? idempotencyKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/connections/current/sync");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            if (idempotencyKey is not null) { request.Headers.Add("Idempotency-Key", idempotencyKey); }
            return await _client.SendAsync(request);
        }

        // Entrada e pedido manual usam a mesma chave opaca, mas alcançam rotas e fingerprints de intenção distintos.
        internal async Task<HttpResponseMessage> PostOnEntryAsync(string? idempotencyKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/connections/current/sync/on-entry");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            if (idempotencyKey is not null) { request.Headers.Add("Idempotency-Key", idempotencyKey); }
            return await _client.SendAsync(request);
        }

        // A mesma rota de status pode ser invocada como titular, outro titular ou anônimo sem compartilhar cabeçalhos.
        internal Task<HttpResponseMessage> GetSyncRunAsync(Guid syncRunId, string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sync-runs/{syncRunId:D}");
            if (token is not null) { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); }
            return _client.SendAsync(request);
        }

        // A leitura permite variar o principal nos testes, sem aceitar seletor de conexão na rota.
        internal Task<HttpResponseMessage> GetSyncStateAsync(string? token = null, bool authenticated = true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/connections/current/sync-state");
            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? _token);
            }

            return _client.SendAsync(request);
        }

        // A fixture fecha recursos do host antes do contexto do schema; não apaga tabelas nem remove volumes.
        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _factory.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    // Observa o par antes do commit e simula falha pré-commit ou perda da confirmação após o commit físico.
    private sealed class AcceptanceCommitEvidenceInterceptor : DbTransactionInterceptor
    {
        private readonly ConcurrentQueue<CommitSnapshot> _snapshots = new();
        private int _failNextCommit;
        private int _loseNextConfirmation;
        private int _lostConfirmationCount;
        private int _committedAcceptanceCount;

        internal IReadOnlyCollection<CommitSnapshot> Snapshots => _snapshots.ToArray();
        internal int LostConfirmationCount => Volatile.Read(ref _lostConfirmationCount);
        internal int CommittedAcceptanceCount => Volatile.Read(ref _committedAcceptanceCount);

        // Arma uma falha no callback anterior ao commit físico do provider.
        internal void FailNextCommit() => Interlocked.Exchange(ref _failNextCommit, 1);

        // Arma uma falha após o provider informar commit físico, simulando confirmação perdida pela aplicação.
        internal void LoseNextCommitConfirmation() => Interlocked.Exchange(ref _loseNextConfirmation, 1);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context)
            {
                _snapshots.Enqueue(new CommitSnapshot(
                    context.ChangeTracker.Entries<SyncRun>().Count(),
                    context.ChangeTracker.Entries<OutboxMessage>().Count()));
            }

            return Interlocked.Exchange(ref _failNextCommit, 0) == 1
                ? ValueTask.FromException<InterceptionResult>(new InvalidOperationException("Falha sintética antes do commit."))
                : ValueTask.FromResult(result);
        }

        // A exceção só é lançada no callback posterior ao commit do provider, modelando confirmação perdida.
        public override Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            // Conta apenas commits que ainda exibem o par acompanhado pela transação de aceitação.
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<SyncRun>().Any()
                && context.ChangeTracker.Entries<OutboxMessage>().Any())
            {
                Interlocked.Increment(ref _committedAcceptanceCount);
            }

            if (Interlocked.Exchange(ref _loseNextConfirmation, 0) != 1)
            {
                return Task.CompletedTask;
            }

            Interlocked.Increment(ref _lostConfirmationCount);
            return Task.FromException(new InvalidOperationException("synthetic lost commit confirmation"));
        }
    }

    // A evidência guarda somente cardinalidades locais, sem copiar chaves, IDs ou payloads para diagnósticos.
    private sealed record CommitSnapshot(int SyncRuns, int OutboxMessages);
}
