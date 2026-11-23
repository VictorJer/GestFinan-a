using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Messaging;
using Finora.Infrastructure.Messaging.Contracts;
using Finora.Infrastructure.Persistence;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita a API e o transporte RabbitMQ reais com PostgreSQL descartável e leitor financeiro sintético.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlRabbitMqSyncRunIntegrationTests
{
    private const int ManagementPort = 55672;
    private const string ExecutionErrorQueueName = "finora-sync-execution_error";

    // Valida aceitação, concorrência, redelivery, retry/restart, privacidade e entrega somente após handoff persistido.
    [TestMethod]
    public async Task RabbitMqApiFlowPersistsBeforeAckAndRecoversSameRunAcrossRestart()
    {
        // Nenhum schema ou mensagem é alterado se o vhost dedicado já contiver obrigações desconhecidas.
        var broker = MqP01BrokerSettings.ReadRequired();
        using var management = CreateManagementClient(broker);
        // O reset e o schema atual são limitados ao PostgreSQL descartável validado em loopback.
        var databaseSettings = DisposablePostgresTestConfiguration.ReadRequired();
        await using var database = DisposablePostgresMigrationFixture.CreateValidated();
        await database.Context.Database.EnsureCreatedAsync();
        var initialQueues = await ReadQueuesAsync(management, broker.Vhost);
        var pendingRuns = await database.Context.Set<SyncRun>().AsNoTracking()
            .Where(run => run.State == SyncRunState.Pending || run.State == SyncRunState.Running)
            .ToArrayAsync();
        var pendingOutboxCount = await database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync();
        if (initialQueues.Any(queue => queue.Ready > 0 || queue.Unacknowledged > 0)
            || pendingRuns.Length > 0 || pendingOutboxCount > 0)
        {
            // Só retoma o resíduo sintético da tentativa de teste anterior; qualquer outra obrigação bloqueia reset.
            await RecoverInterruptedSyntheticRunAsync(database, management, broker, databaseSettings.ConnectionString,
                initialQueues, pendingRuns, pendingOutboxCount);
            initialQueues = await ReadQueuesAsync(management, broker.Vhost);
            pendingRuns = await database.Context.Set<SyncRun>().AsNoTracking()
                .Where(run => run.State == SyncRunState.Pending || run.State == SyncRunState.Running)
                .ToArrayAsync();
            pendingOutboxCount = await database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync();
        }
        Assert.IsFalse(initialQueues.Any(queue => queue.Ready > 0 || queue.Unacknowledged > 0 || queue.Consumers > 0),
            "O vhost dedicado contém obrigações ou consumers desconhecidos; a prova não pode continuar.");
        Assert.AreEqual(0, pendingRuns.Length, "O banco descartável contém SyncRun não terminal.");
        Assert.AreEqual(0, pendingOutboxCount, "O banco descartável contém obrigação de outbox pendente.");

        await database.ResetToEmptySchemaAsync();
        Assert.IsTrue(await database.Context.Database.EnsureCreatedAsync());

        // A fila recebe somente os resultados sintéticos definidos para redelivery e retomada por retry.
        var reader = new SyntheticRabbitImportReader();
        reader.EnqueueCancellationUntilShutdown();
        reader.EnqueueSuccess();
        reader.EnqueueFailure(TransactionImportFailureCode.ProviderTimeout);
        reader.EnqueueFailure(TransactionImportFailureCode.ProviderTimeout);
        reader.EnqueueSuccess();
        reader.EnqueueFailure(TransactionImportFailureCode.RateLimited, TimeSpan.FromMinutes(15));
        reader.EnqueueFailure(TransactionImportFailureCode.RateLimited, TimeSpan.FromMinutes(20));

        // Hosts reiniciados reutilizam chave JWT, configuração de transporte e a mesma fixture sintética.
        var configuration = CreateApiConfiguration(databaseSettings.ConnectionString, broker);
        RabbitMqApiHost? host = await RabbitMqApiHost.StartAsync(configuration, reader);
        try
        {
            var (userId, connectionId) = await SeedOwnerAndConnectionAsync(host, database);
            var token = await LoginAsync(host, "rabbit-integration-owner@example.invalid", " Password!123 ");

            // A primeira entrega fica bloqueada após adquirir lease para medir ack, concorrência e renovação reais.
            using var acceptance = await PostSyncAsync(host, token, "rabbit-integration-primary-key");
            Assert.AreEqual(HttpStatusCode.Accepted, acceptance.StatusCode);
            var firstRunId = await ReadAcceptedIdAsync(acceptance);
            await reader.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var running = await WaitForRunAsync(database, firstRunId, run => run.State == SyncRunState.Running);
            var initialLease = await ReadLeaseAsync(database, connectionId);
            Assert.AreEqual(firstRunId, initialLease.SyncRunId);
            Assert.AreEqual(1, initialLease.AttemptNumber);
            Assert.IsTrue(running.AttemptDeadlineAtUtc > running.AttemptStartedAtUtc);
            Assert.IsTrue(running.AttemptDeadlineAtUtc - running.AttemptStartedAtUtc <= TimeSpan.FromMinutes(10));
            Assert.IsTrue(initialLease.ExpiresAtUtc - initialLease.AcquiredAtUtc >= TimeSpan.FromSeconds(119));
            Assert.IsTrue(initialLease.ExpiresAtUtc - initialLease.AcquiredAtUtc <= TimeSpan.FromSeconds(121));

            // O mesmo pedido reproduz o ID; uma chave distinta conflita enquanto a tentativa real está em posse.
            using var replay = await PostSyncAsync(host, token, "rabbit-integration-primary-key");
            Assert.AreEqual(HttpStatusCode.Accepted, replay.StatusCode);
            Assert.AreEqual(firstRunId, await ReadAcceptedIdAsync(replay));
            using var conflict = await PostSyncAsync(host, token, "rabbit-integration-competing-key");
            Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
            var conflictBody = await conflict.Content.ReadAsStringAsync();
            Assert.IsFalse(conflictBody.Contains("synthetic-rabbit-item", StringComparison.Ordinal));

            // A fila real mostra a entrega não reconhecida enquanto a coleta ainda não foi confirmada.
            var blockedQueue = await WaitForQueueAsync(management, broker.Vhost,
                queue => queue.Name == SyncRunExecutionTopology.QueueName && queue.Unacknowledged == 1,
                TimeSpan.FromSeconds(20));
            Assert.AreEqual(1, blockedQueue.Consumers);

            // Espera a renovação configurada de 30 s e confirma extensão da lease de 120 s no PostgreSQL.
            var renewal = await WaitForLeaseRenewalAsync(database, connectionId, initialLease,
                TimeSpan.FromSeconds(45));
            Assert.IsTrue(renewal.ExpiresAtUtc > initialLease.AcquiredAtUtc.AddSeconds(130));

            // O leitor sintético falha por cancelamento enquanto o bus está saudável; o consumer deve preservar a entrega.
            reader.CancelFirstReadForTest();
            await reader.FirstReadCancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var interrupted = await ReadRunAsync(database, firstRunId);
            Assert.AreEqual(SyncRunState.Running, interrupted.State);
            Assert.AreEqual(1, interrupted.AttemptNumber);
            Assert.IsNull(interrupted.FinishedAtUtc);
            Assert.AreEqual(0, await database.Context.Set<FinancialTransaction>().AsNoTracking().CountAsync());

            // Aguarda uma única entrega faulted sem unacked; o consumer principal permanece ativo até o stop explícito.
            await WaitForFaultedDeliveryAsync(management, broker.Vhost, TimeSpan.FromSeconds(60));
            await host.DisposeAsync();
            host = null;
            var shutdownQueues = await WaitForShutdownDeliveryAsync(management, broker.Vhost, TimeSpan.FromSeconds(30));

            // A retomada só é autorizada após correlacionar o erro durável e a run sintética, sem purgar a fila.
            await RecoverInterruptedSyntheticRunAsync(database, management, broker, databaseSettings.ConnectionString,
                shutdownQueues, [interrupted], pendingOutboxCount: 0, existingReader: reader);

            // Um novo host confirma o estado terminal recuperado sem iniciar uma segunda tentativa financeira.
            host = await RabbitMqApiHost.StartAsync(configuration, reader);
            var firstSucceeded = await WaitForRunAsync(database, firstRunId,
                run => run.State == SyncRunState.Succeeded && run.AttemptNumber == 2);
            Assert.AreEqual(firstRunId, firstSucceeded.Id);
            Assert.AreEqual(2, reader.CallCount);
            var firstAttemptDuration = firstSucceeded.FinishedAtUtc!.Value - firstSucceeded.AttemptStartedAtUtc!.Value;
            Assert.IsTrue(firstAttemptDuration < TimeSpan.FromMinutes(10));
            Assert.AreEqual(1, await database.Context.Set<SyncRun>().AsNoTracking().CountAsync(run => run.Id == firstRunId));
            Assert.AreEqual(1, await database.Context.Set<CardAccount>().AsNoTracking().CountAsync());
            Assert.AreEqual(1, await database.Context.Set<FinancialTransaction>().AsNoTracking().CountAsync());
            await WaitForQueueAsync(management, broker.Vhost,
                queue => queue.Name == SyncRunExecutionTopology.QueueName
                    && queue.Ready == 0 && queue.Unacknowledged == 0,
                TimeSpan.FromSeconds(20));

            // O status HTTP confirma resultado sanitizado e continua autenticável com a mesma sessão após restart.
            using var status = await GetSyncRunAsync(host, token, firstRunId);
            Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
            using (var statusDocument = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
            {
                Assert.AreEqual(firstRunId, statusDocument.RootElement.GetProperty("syncRunId").GetGuid());
                Assert.AreEqual("Succeeded", statusDocument.RootElement.GetProperty("state").GetString());
                Assert.IsFalse(statusDocument.RootElement.TryGetProperty("userId", out _));
                Assert.IsFalse(statusDocument.RootElement.TryGetProperty("connectionId", out _));
                Assert.IsFalse(statusDocument.RootElement.TryGetProperty("idempotencyKeyHash", out _));
            }

            // Reenvio pelo bus real testa a duplicata terminal; ela deve ser reconhecida sem nova importação financeira.
            var ackBeforeDuplicate = (await ReadQueueAsync(management, broker.Vhost,
                SyncRunExecutionTopology.QueueName)).Acknowledgements;
            var bus = host.Factory.Services.GetRequiredService<IBus>();
            var endpoint = await bus.GetSendEndpoint(SyncRunExecutionTopology.QueueAddress);
            var duplicate = ExecuteSyncRunV1Contract.Create(firstRunId, DateTimeOffset.UtcNow);
            await endpoint.Send(duplicate, sendContext =>
            {
                sendContext.MessageId = firstRunId;
                sendContext.CorrelationId = firstRunId;
                sendContext.Durable = true;
            });
            var duplicateQueue = await WaitForQueueAsync(management, broker.Vhost,
                queue => queue.Name == SyncRunExecutionTopology.QueueName
                    && queue.Acknowledgements > ackBeforeDuplicate
                    && queue.Ready == 0 && queue.Unacknowledged == 0,
                TimeSpan.FromSeconds(20));
            Assert.IsTrue(duplicateQueue.Acknowledgements > ackBeforeDuplicate);
            Assert.AreEqual(2, reader.CallCount);
            using var terminalReplay = await PostSyncAsync(host, token, "rabbit-integration-primary-key");
            Assert.AreEqual(HttpStatusCode.Accepted, terminalReplay.StatusCode);
            Assert.AreEqual(firstRunId, await ReadAcceptedIdAsync(terminalReplay));

            // Primeira falha transitória confirma handoff persistente e backoff de 1 min antes do ack RabbitMQ.
            using var retryAcceptance = await PostSyncAsync(host, token, "rabbit-integration-retry-key");
            Assert.AreEqual(HttpStatusCode.Accepted, retryAcceptance.StatusCode);
            var retryRunId = await ReadAcceptedIdAsync(retryAcceptance);
            var retryOne = await WaitForRunAsync(database, retryRunId,
                run => run.State == SyncRunState.Pending && run.AttemptNumber == 1 && run.NextAttemptAtUtc is not null);
            AssertBackoffIsFuture(retryOne, TimeSpan.FromMinutes(1));
            await AssertQueueIdleAsync(management, broker.Vhost);

            // Reinício durante o backoff futuro não antecipa a tentativa nem publica outra mensagem.
            await host.DisposeAsync();
            host = null;
            host = await RabbitMqApiHost.StartAsync(configuration, reader);
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.AreEqual(3, reader.CallCount);
            await AssertQueueIdleAsync(management, broker.Vhost);
            await host.DisposeAsync();
            host = null;

            // O relógio de espera é acelerado somente no banco descartável; o restart real reavalia a mesma SyncRun.
            await MarkRetryDueAsync(database, retryRunId);
            host = await RabbitMqApiHost.StartAsync(configuration, reader);
            var retryTwo = await WaitForRunAsync(database, retryRunId,
                run => run.State == SyncRunState.Pending && run.AttemptNumber == 2 && run.NextAttemptAtUtc is not null);
            AssertBackoffIsFuture(retryTwo, TimeSpan.FromMinutes(5));
            await AssertQueueIdleAsync(management, broker.Vhost);

            // Outro restart no backoff de 5 min não executa trabalho antes do instante persistido.
            await host.DisposeAsync();
            host = null;
            host = await RabbitMqApiHost.StartAsync(configuration, reader);
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.AreEqual(4, reader.CallCount);
            await AssertQueueIdleAsync(management, broker.Vhost);
            await host.DisposeAsync();
            host = null;

            // Terceira tentativa ocorre somente após marcar o prazo vencido no database de integração.
            await MarkRetryDueAsync(database, retryRunId);
            host = await RabbitMqApiHost.StartAsync(configuration, reader);
            var retrySucceeded = await WaitForRunAsync(database, retryRunId,
                run => run.State == SyncRunState.Succeeded && run.AttemptNumber == 3);
            Assert.AreEqual(retryRunId, retrySucceeded.Id);
            Assert.AreEqual(5, reader.CallCount);
            await AssertQueueIdleAsync(management, broker.Vhost);

            // Retry-After de 15 min é persistido sem espera ocupada; um prazo maior bloqueia retry automático.
            using var rateLimitAcceptance = await PostSyncAsync(host, token, "rabbit-integration-rate-limit-key");
            Assert.AreEqual(HttpStatusCode.Accepted, rateLimitAcceptance.StatusCode);
            var rateLimitRunId = await ReadAcceptedIdAsync(rateLimitAcceptance);
            var fifteenMinuteWait = await WaitForRunAsync(database, rateLimitRunId,
                run => run.State == SyncRunState.Pending
                    && run.AttemptNumber == 1
                    && run.FailureCode == SyncRunFailureCodes.RateLimited
                    && run.NextAttemptAtUtc is not null);
            AssertRetryAfterIsFuture(fifteenMinuteWait, TimeSpan.FromMinutes(15));
            await AssertQueueIdleAsync(management, broker.Vhost);

            // API reiniciada durante Retry-After de 15 min não chama novamente a origem nem emite entrega antecipada.
            await host.DisposeAsync();
            host = null;
            host = await RabbitMqApiHost.StartAsync(configuration, reader);
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.AreEqual(6, reader.CallCount);
            await AssertQueueIdleAsync(management, broker.Vhost);
            await host.DisposeAsync();
            host = null;

            // O prazo de 15 min é vencido virtualmente; Retry-After acima do teto termina a mesma execução.
            await MarkRetryDueAsync(database, rateLimitRunId);
            host = await RabbitMqApiHost.StartAsync(configuration, reader);
            var rateLimitTerminal = await WaitForRunAsync(database, rateLimitRunId,
                run => run.State == SyncRunState.Failed && run.AttemptNumber == 2);
            AssertRetryAfterIsFuture(rateLimitTerminal, TimeSpan.FromMinutes(20));
            Assert.AreEqual(rateLimitRunId, rateLimitTerminal.Id);
            Assert.AreEqual(7, reader.CallCount);
            await AssertQueueIdleAsync(management, broker.Vhost);

            // O limite configurado é 11 min; a prova usa prazo reduzido e uma leitura sintética não cooperativa.
            Assert.AreEqual(TimeSpan.FromMinutes(11), SyncRunExecutionWatchdogOptions.Default.DeliveryDeadline);
            var normalHostLogs = host.Factory.Logs;
            await host.DisposeAsync();
            host = null;
            var watchdogReader = new SyntheticRabbitImportReader();
            watchdogReader.EnqueueIgnoringCancellation();
            var watchdogDeadline = TimeSpan.FromSeconds(5);
            host = await RabbitMqApiHost.StartAsync(configuration, watchdogReader, watchdogDeadline);
            using var watchdogAcceptance = await PostSyncAsync(host, token, "rabbit-integration-watchdog-key");
            Assert.AreEqual(HttpStatusCode.Accepted, watchdogAcceptance.StatusCode);
            var watchdogRunId = await ReadAcceptedIdAsync(watchdogAcceptance);
            await watchdogReader.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var watchdogErrorQueue = await WaitForQueueAsync(management, broker.Vhost,
                queue => queue.Name == ExecutionErrorQueueName && queue.Ready == 1
                    && queue.Unacknowledged == 0 && queue.Consumers == 0,
                TimeSpan.FromSeconds(20));
            Assert.AreEqual(1, watchdogErrorQueue.Ready);
            var watchdogRun = await ReadRunAsync(database, watchdogRunId);
            Assert.AreEqual(SyncRunState.Running, watchdogRun.State);
            Assert.AreEqual(1, watchdogRun.AttemptNumber);
            Assert.IsNull(watchdogRun.FinishedAtUtc);
            Assert.IsNull(watchdogRun.FailureCode);

            // Libera a leitura após o limite; o token cancelado precisa impedir qualquer promoção tardia.
            watchdogReader.ReleaseIgnoringCancellationRead();
            await watchdogReader.WatchdogReadCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            var afterWatchdog = await ReadRunAsync(database, watchdogRunId);
            Assert.AreEqual(SyncRunState.Running, afterWatchdog.State);
            Assert.AreEqual(1, await database.Context.Set<FinancialTransaction>().AsNoTracking().CountAsync());
            var watchdogHostLogs = host.Factory.Logs;
            await host.DisposeAsync();
            host = null;
            var watchdogQueues = await WaitForShutdownDeliveryAsync(
                management, broker.Vhost, TimeSpan.FromSeconds(30));
            watchdogReader.EnqueueSuccess();
            await RecoverInterruptedSyntheticRunAsync(
                database, management, broker, databaseSettings.ConnectionString,
                watchdogQueues, [afterWatchdog], pendingOutboxCount: 0, existingReader: watchdogReader);
            Assert.AreEqual(2, watchdogReader.CallCount);
            Assert.IsFalse(normalHostLogs.Text.Contains("synthetic-rabbit-item", StringComparison.Ordinal));
            Assert.IsFalse(watchdogHostLogs.Text.Contains("synthetic-rabbit-item", StringComparison.Ordinal));

            // Nenhuma obrigação não terminal pode permanecer antes do encerramento do host de teste.
            database.Context.ChangeTracker.Clear();
            Assert.AreEqual(0, await database.Context.Set<SyncRun>()
                .AsNoTracking().CountAsync(run => run.State == SyncRunState.Pending || run.State == SyncRunState.Running));
            Assert.AreEqual(0, await database.Context.Set<OutboxMessage>().AsNoTracking().CountAsync());
            Assert.AreEqual(0, await database.Context.Set<SyncRunLease>().AsNoTracking()
                .CountAsync(lease => lease.ExpiresAtUtc > DateTimeOffset.UtcNow));
        }
        finally
        {
            // O host é encerrado sem purge; filas, mensagens e o volume MQ-P01 nunca são removidos pelo teste.
            if (host is not null) { await host.DisposeAsync(); }
        }

        // Confirma ausência de mensagens ready/unacked e de consumers após o encerramento, sem limpar o vhost.
        var finalQueues = await ReadQueuesAsync(management, broker.Vhost);
        Assert.IsFalse(finalQueues.Any(queue => queue.Ready > 0 || queue.Unacknowledged > 0 || queue.Consumers > 0));
    }

    // A composição usa o destino AMQP de teste e credenciais do arquivo ignorado, sem fallback de host.
    private static Dictionary<string, string?> CreateApiConfiguration(string connectionString, MqP01BrokerSettings broker)
    {
        var configuration = AuthenticationAvailabilityTests.Configuration(connectionString);
        configuration["Messaging:RabbitMq:HostAddress"] = $"rabbitmq://127.0.0.1:{broker.Port}/{broker.Vhost}";
        configuration["Messaging:RabbitMq:Username"] = broker.Username;
        configuration["Messaging:RabbitMq:Password"] = broker.Password;
        configuration["Messaging:RabbitMq:UseTls"] = "false";
        return configuration;
    }

    // Resgata somente o registro marcado por ID sintético após uma falha anterior; não purga nem consome dados alheios.
    private static async Task RecoverInterruptedSyntheticRunAsync(
        DisposablePostgresMigrationFixture database,
        HttpClient management,
        MqP01BrokerSettings broker,
        string connectionString,
        IReadOnlyList<RabbitQueueState> queues,
        IReadOnlyList<SyncRun> pendingRuns,
        int pendingOutboxCount,
        SyntheticRabbitImportReader? existingReader = null)
    {
        Assert.AreEqual(1, pendingRuns.Count, "A recuperação encontrou mais de uma execução pendente.");
        var run = pendingRuns.Single();
        var interruptedAttempt = run.State == SyncRunState.Running;
        var duePendingAttempt = run.State == SyncRunState.Pending
            && run.NextAttemptAtUtc is { } nextAttemptAtUtc
            && nextAttemptAtUtc <= DateTimeOffset.UtcNow;
        Assert.IsTrue(interruptedAttempt || duePendingAttempt,
            "A única obrigação sintética não é uma tentativa interrompida ou um retry já elegível.");
        var connection = await database.Context.Set<FinancialConnection>().AsNoTracking()
            .SingleAsync(value => value.Id == run.ConnectionId);
        Assert.AreEqual("synthetic-rabbit-item", connection.ExternalItemId);
        Assert.AreEqual(0, pendingOutboxCount);
        Assert.IsTrue(queues.Where(queue => queue.Name is not SyncRunExecutionTopology.QueueName
                and not ExecutionErrorQueueName)
            .All(queue => queue.Ready == 0 && queue.Unacknowledged == 0 && queue.Consumers == 0));
        var targetQueue = queues.Single(queue => queue.Name == SyncRunExecutionTopology.QueueName);
        var errorQueue = queues.SingleOrDefault(queue => queue.Name == ExecutionErrorQueueName);
        var errorQueueEmpty = errorQueue is null or { Ready: 0, Unacknowledged: 0, Consumers: 0 };
        var hasRetainedError = errorQueue is { Ready: 1, Unacknowledged: 0, Consumers: 0 };
        Assert.IsTrue(errorQueueEmpty || hasRetainedError,
            "A fila de erro contém quantidade/consumidores incompatíveis com a única obrigação sintética.");
        Assert.AreEqual(0, targetQueue.Unacknowledged);
        Assert.AreEqual(0, targetQueue.Consumers);
        if (interruptedAttempt)
        {
            Assert.IsTrue(targetQueue.Ready is 0 or 1);
            Assert.AreEqual(1, targetQueue.Ready + (hasRetainedError ? errorQueue!.Ready : 0),
                "A tentativa interrompida não corresponde a uma única entrega durável identificável.");
        }
        else
        {
            Assert.AreEqual(0, targetQueue.Ready);
            Assert.IsTrue(errorQueueEmpty,
                "Um retry devido sem mensagem principal/outbox tem obrigação externa desconhecida.");
            var lease = await database.Context.Set<SyncRunLease>().AsNoTracking()
                .SingleAsync(value => value.ConnectionId == run.ConnectionId);
            Assert.IsTrue(lease.ExpiresAtUtc <= DateTimeOffset.UtcNow,
                "A execução Pending ainda possui lease vigente e não deve ser retomada pelo preflight.");
        }
        if (hasRetainedError)
        {
            // Inspeciona e recoloca a mensagem antes da recuperação; o payload precisa apontar para a mesma SyncRun.
            Assert.AreEqual(run.Id, await ReadErrorMessageIdAsync(
                management, broker.Vhost, ExecutionErrorQueueName, requeue: true));
        }

        // A lease expira somente para que o reconciliador publique a mesma execução sintética pela outbox.
        if (interruptedAttempt)
        {
            // Somente a tentativa Running sintética é expirada para acionar o reconciliador após shutdown.
            await database.Context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"SyncRunLease\" SET \"ExpiresAtUtc\" = CURRENT_TIMESTAMP - interval '1 second' WHERE \"SyncRunId\" = {run.Id}");
        }
        var reader = existingReader ?? new SyntheticRabbitImportReader();
        var nextRetryExceedsApprovedLimit = !interruptedAttempt
            && run.FailureCode == SyncRunFailureCodes.RateLimited;
        if (existingReader is null)
        {
            if (nextRetryExceedsApprovedLimit)
            {
                // Preserva o cenário sintético de Retry-After: um segundo prazo acima de 15 min terminaliza a mesma run.
                reader.EnqueueFailure(TransactionImportFailureCode.RateLimited, TimeSpan.FromMinutes(20));
            }
            else
            {
                reader.EnqueueSuccess();
            }
        }
        var configuration = CreateApiConfiguration(connectionString, broker);
        var recoveryHost = await RabbitMqApiHost.StartAsync(configuration, reader);
        try
        {
            var expectedState = nextRetryExceedsApprovedLimit ? SyncRunState.Failed : SyncRunState.Succeeded;
            var recovered = await WaitForRunAsync(database, run.Id,
                value => value.State == expectedState && value.AttemptNumber == run.AttemptNumber + 1);
            Assert.AreEqual(
                nextRetryExceedsApprovedLimit ? SyncRunFailureCodes.RateLimited : null,
                recovered.FailureCode);
            if (existingReader is null) { Assert.AreEqual(1, reader.CallCount); }
            await AssertQueueIdleAsync(management, broker.Vhost);
            if (hasRetainedError)
            {
                // Só confirma a cópia faulted após sucesso terminal persistido e nova conferência do ID sintético.
                Assert.AreEqual(run.Id, await ReadErrorMessageIdAsync(
                    management, broker.Vhost, ExecutionErrorQueueName, requeue: true));
                Assert.AreEqual(run.Id, await ReadErrorMessageIdAsync(
                    management, broker.Vhost, ExecutionErrorQueueName, requeue: false));
            }
        }
        finally
        {
            await recoveryHost.DisposeAsync();
        }
        await WaitForQueueAsync(management, broker.Vhost,
            queue => queue.Name == SyncRunExecutionTopology.QueueName
                && queue.Consumers == 0 && queue.Ready == 0 && queue.Unacknowledged == 0,
            TimeSpan.FromSeconds(20));
        if (hasRetainedError)
        {
            await WaitForQueueAsync(management, broker.Vhost,
                queue => queue.Name == ExecutionErrorQueueName
                    && queue.Consumers == 0 && queue.Ready == 0 && queue.Unacknowledged == 0,
                TimeSpan.FromSeconds(20));
        }
    }

    // Cria um titular local e uma única conexão sintética para que o endpoint current aceite a intenção.
    private static async Task<(Guid UserId, Guid ConnectionId)> SeedOwnerAndConnectionAsync(
        RabbitMqApiHost host,
        DisposablePostgresMigrationFixture database)
    {
        using var scope = host.Factory.Services.CreateScope();
        var email = "rabbit-integration-owner@example.invalid";
        var user = new ApplicationUser { UserName = email, Email = email };
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.IsTrue((await manager.CreateAsync(user, " Password!123 ")).Succeeded);
        var connectionId = Guid.NewGuid();
        database.Context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(connectionId, user.Id, "pluggy", "synthetic-rabbit-item"));
        // A promoção financeira exige identidade ativa e permanente do item além da conexão principal.
        database.Context.Set<FinancialConnectionItemIdentity>().Add(
            FinancialConnectionItemIdentity.CreateActive(
                connectionId, user.Id, "pluggy", "synthetic-rabbit-item"));
        await database.Context.SaveChangesAsync();
        return (user.Id, connectionId);
    }

    // Login é real contra PostgreSQL descartável; o token é reutilizado após reinício com a mesma chave sintética.
    private static async Task<string> LoginAsync(RabbitMqApiHost host, string email, string password)
    {
        using var response = await host.Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    // Cada pedido fornece Bearer e chave idempotente na requisição, evitando estado compartilhado do HttpClient.
    private static async Task<HttpResponseMessage> PostSyncAsync(RabbitMqApiHost host, string token, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/connections/current/sync");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await host.Client.SendAsync(request);
    }

    // A resposta de aceitação contém somente o identificador persistido da execução.
    private static async Task<Guid> ReadAcceptedIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, document.RootElement.EnumerateObject().Count());
        return document.RootElement.GetProperty("syncRunId").GetGuid();
    }

    // Consulta de status usa a sessão do titular e não coloca IDs financeiros no query string.
    private static async Task<HttpResponseMessage> GetSyncRunAsync(RabbitMqApiHost host, string token, Guid syncRunId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sync-runs/{syncRunId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await host.Client.SendAsync(request);
    }

    // Management é usado somente em HTTP no bind loopback 55672; o transporte AMQP permanece na porta 55673.
    private static HttpClient CreateManagementClient(MqP01BrokerSettings broker)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{ManagementPort}/") };
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{broker.Username}:{broker.Password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    // A inspeção lê apenas nome e contadores de mensagens do vhost allowlisted, nunca payloads.
    private static async Task<IReadOnlyList<RabbitQueueState>> ReadQueuesAsync(HttpClient management, string vhost)
    {
        using var response = await management.GetAsync($"api/queues/{Uri.EscapeDataString(vhost)}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().Select(queue => new RabbitQueueState(
            queue.GetProperty("name").GetString()!,
            ReadInt(queue, "messages_ready"),
            ReadInt(queue, "messages_unacknowledged"),
            ReadInt(queue, "consumers"),
            ReadAckCount(queue))).ToArray();
    }

    // Espera uma única entrega confirmada pelo broker sem confundir consumer ativo com mensagem ainda unacked.
    private static async Task WaitForFaultedDeliveryAsync(
        HttpClient management,
        string vhost,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            var queues = await ReadQueuesAsync(management, vhost);
            var execution = queues.Single(queue => queue.Name == SyncRunExecutionTopology.QueueName);
            var error = queues.SingleOrDefault(queue => queue.Name == ExecutionErrorQueueName);
            if (execution.Unacknowledged == 0 && (error is null || error.Unacknowledged == 0)
                && execution.Ready + (error?.Ready ?? 0) == 1)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellation.Token);
        }
    }

    // Aguarda o broker terminar o stop do consumer e deixar uma única entrega pronta ou quarantined, sem unacked.
    private static async Task<IReadOnlyList<RabbitQueueState>> WaitForShutdownDeliveryAsync(
        HttpClient management,
        string vhost,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            var queues = await ReadQueuesAsync(management, vhost);
            var execution = queues.Single(queue => queue.Name == SyncRunExecutionTopology.QueueName);
            var error = queues.SingleOrDefault(queue => queue.Name == ExecutionErrorQueueName);
            var errorReady = error?.Ready ?? 0;
            if (execution.Consumers == 0 && execution.Unacknowledged == 0
                && (error is null || (error.Consumers == 0 && error.Unacknowledged == 0))
                && execution.Ready + errorReady == 1)
            {
                return queues;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellation.Token);
        }
    }

    // Leitura individual falha fechada se a fila da aplicação não existir no vhost esperado.
    private static async Task<RabbitQueueState> ReadQueueAsync(HttpClient management, string vhost, string queueName) =>
        (await ReadQueuesAsync(management, vhost)).Single(queue => queue.Name == queueName);

    // Management só recoloca a mensagem de erro ou a confirma depois da SyncRun sintética estar terminal.
    private static async Task<Guid> ReadErrorMessageIdAsync(
        HttpClient management,
        string vhost,
        string queueName,
        bool requeue)
    {
        var endpoint = $"api/queues/{Uri.EscapeDataString(vhost)}/{Uri.EscapeDataString(queueName)}/get";
        var request = new
        {
            count = 1,
            ackmode = requeue ? "ack_requeue_true" : "ack_requeue_false",
            encoding = "auto",
            truncate = 50_000
        };
        using var response = await management.PostAsJsonAsync(endpoint, request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var messages = document.RootElement.EnumerateArray().ToArray();
        Assert.AreEqual(1, messages.Length);
        var payload = messages[0].GetProperty("payload").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(payload));
        using var envelope = JsonDocument.Parse(payload!);
        var message = envelope.RootElement.GetProperty("message");
        Assert.AreEqual(ExecuteSyncRunV1Contract.CurrentVersion,
            message.GetProperty("contractVersion").GetInt32());
        var syncRunId = message.GetProperty("syncRunId").GetGuid();
        Assert.IsNotNull(message.GetProperty("emittedAtUtc").GetString());
        return syncRunId;
    }

    // Polling limitado acompanha estados publicados pelo broker, sem consumir, ack ou alterar mensagens.
    private static async Task<RabbitQueueState> WaitForQueueAsync(
        HttpClient management,
        string vhost,
        Func<RabbitQueueState, bool> condition,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            var queues = await ReadQueuesAsync(management, vhost);
            var match = queues.FirstOrDefault(condition);
            if (match is not null) { return match; }
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellation.Token);
        }
    }

    // A fila operacional fica sem mensagem pronta/em processamento entre handoffs e reinícios.
    private static async Task AssertQueueIdleAsync(HttpClient management, string vhost)
    {
        var queue = await WaitForQueueAsync(management, vhost,
            value => value.Name == SyncRunExecutionTopology.QueueName
                && value.Ready == 0 && value.Unacknowledged == 0,
            TimeSpan.FromSeconds(20));
        Assert.AreEqual(0, queue.Ready);
        Assert.AreEqual(0, queue.Unacknowledged);
    }

    // Aguarda estado persistido por leitura nova, sem inferir commit pelo retorno do publisher ou pelo log.
    private static async Task<SyncRun> WaitForRunAsync(
        DisposablePostgresMigrationFixture database,
        Guid syncRunId,
        Func<SyncRun, bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            database.Context.ChangeTracker.Clear();
            var run = await database.Context.Set<SyncRun>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == syncRunId, cancellation.Token);
            if (run is not null && condition(run)) { return run; }
            if (run is { State: SyncRunState.Succeeded or SyncRunState.Failed })
            {
                // Falha terminal inesperada deve expor cedo somente o código fechado, não virar timeout de conexão.
                Assert.Fail($"A execução terminou antes da condição esperada: {run.State}/{run.FailureCode ?? "none"}.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellation.Token);
        }
    }

    // Evita reutilizar entidade rastreada ao conferir o estado após shutdown ou redelivery.
    private static async Task<SyncRun> ReadRunAsync(DisposablePostgresMigrationFixture database, Guid syncRunId)
    {
        database.Context.ChangeTracker.Clear();
        return await database.Context.Set<SyncRun>().AsNoTracking().SingleAsync(run => run.Id == syncRunId);
    }

    // A leitura isolada observa a posse sem reutilizar estado rastreado pelo contexto de teste.
    private static async Task<SyncRunLease> ReadLeaseAsync(
        DisposablePostgresMigrationFixture database,
        Guid connectionId)
    {
        database.Context.ChangeTracker.Clear();
        return await database.Context.Set<SyncRunLease>().AsNoTracking()
            .SingleAsync(lease => lease.ConnectionId == connectionId);
    }

    // A renovação real deve estender a expiração original enquanto o leitor continua bloqueado.
    private static async Task<SyncRunLease> WaitForLeaseRenewalAsync(
        DisposablePostgresMigrationFixture database,
        Guid connectionId,
        SyncRunLease original,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            database.Context.ChangeTracker.Clear();
            var lease = await database.Context.Set<SyncRunLease>().AsNoTracking()
                .SingleAsync(value => value.ConnectionId == connectionId, cancellation.Token);
            if (lease.ExpiresAtUtc > original.AcquiredAtUtc.AddSeconds(130)) { return lease; }
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellation.Token);
        }
    }

    // O relógio real só é aguardado até a lease artificialmente expirada se tornar elegível para takeover.
    private static async Task WaitForLeaseExpiredAsync(
        DisposablePostgresMigrationFixture database,
        Guid connectionId,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            database.Context.ChangeTracker.Clear();
            var lease = await database.Context.Set<SyncRunLease>().AsNoTracking()
                .SingleAsync(value => value.ConnectionId == connectionId, cancellation.Token);
            if (lease.ExpiresAtUtc <= DateTimeOffset.UtcNow) { return; }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellation.Token);
        }
    }

    // O prazo de retry é avançado apenas no banco temporário para testar retomada sem aguardar minutos reais.
    private static Task<int> MarkRetryDueAsync(DisposablePostgresMigrationFixture database, Guid syncRunId) =>
        database.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SyncRun\" SET \"NextAttemptAtUtc\" = CURRENT_TIMESTAMP - interval '1 second' WHERE \"Id\" = {syncRunId}");

    // Compara backoff com o relógio observado sem encurtar ou alterar a política de produção.
    private static void AssertBackoffIsFuture(SyncRun run, TimeSpan expected)
    {
        Assert.IsNotNull(run.NextAttemptAtUtc);
        var remaining = run.NextAttemptAtUtc.Value - DateTimeOffset.UtcNow;
        Assert.IsTrue(remaining > expected - TimeSpan.FromSeconds(20));
        Assert.IsTrue(remaining <= expected + TimeSpan.FromSeconds(5));
    }

    // O instante Retry-After permanece original e impede nova execução até ser explicitamente vencido no teste.
    private static void AssertRetryAfterIsFuture(SyncRun run, TimeSpan expected)
    {
        Assert.IsNotNull(run.NextAttemptAtUtc);
        DateTimeOffset? observationAnchorUtc = run.State == SyncRunState.Pending
            ? run.CreatedAtUtc
            : run.FinishedAtUtc;
        Assert.IsNotNull(observationAnchorUtc);
        // A criação ou conclusão aproxima o instante da resposta sintética sem depender da hora do processo de teste.
        var persistedDelay = run.NextAttemptAtUtc.Value - observationAnchorUtc.Value;
        Assert.IsTrue(persistedDelay > expected - TimeSpan.FromMinutes(2),
            $"O prazo persistido foi encurtado: {persistedDelay}.");
        Assert.IsTrue(persistedDelay < expected + TimeSpan.FromMinutes(2),
            $"O prazo persistido excedeu a janela do cenário: {persistedDelay}.");
        Assert.IsTrue(run.NextAttemptAtUtc > DateTimeOffset.UtcNow,
            "O Retry-After persistido já venceu antes do cenário de espera.");
    }

    // Converte contadores opcionais de JSON sem incluir a resposta completa em falhas de teste.
    private static int ReadInt(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.TryGetInt32(out var result) ? result : 0;

    // Métrica ack é cumulativa por fila e confirma que a duplicata terminal foi processada pelo consumer real.
    private static long ReadAckCount(JsonElement queue)
    {
        if (!queue.TryGetProperty("message_stats", out var stats)) { return 0; }
        return stats.TryGetProperty("ack", out var ack) && ack.TryGetInt64(out var result) ? result : 0;
    }

    private sealed record RabbitQueueState(
        string Name,
        int Ready,
        int Unacknowledged,
        int Consumers,
        long Acknowledgements);

    // O host inicia/dispose a API e o bus reais; apenas o leitor de dados externos é substituído por síntese local.
    private sealed class RabbitMqApiHost(AuthenticationApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        private int _disposed;
        internal AuthenticationApiFactory Factory { get; } = factory;
        internal HttpClient Client { get; } = client;

        // A composição completa começa antes de qualquer request e falha o teste se liveness não responder.
        internal static async Task<RabbitMqApiHost> StartAsync(
            Dictionary<string, string?> configuration,
            SyntheticRabbitImportReader reader,
            TimeSpan? deliveryWatchdogDeadline = null)
        {
            // LogContext usa AsyncLocal; descartar o logger do WebApplicationFactory anterior quebrava hosts reiniciados.
            LogContext.Current = null;
            var factory = new AuthenticationApiFactory(configuration, configure: services =>
            {
                services.RemoveAll<ITransactionImportReader>();
                services.AddSingleton<ITransactionImportReader>(reader);
                services.RemoveAll<SyncRunExecutionWatchdogOptions>();
                services.AddSingleton(new SyncRunExecutionWatchdogOptions(
                    deliveryWatchdogDeadline ?? SyncRunExecutionWatchdogOptions.Default.DeliveryDeadline));
            });
            var client = factory.CreateClient();
            try
            {
                using var health = await client.GetAsync("/health");
                Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
                return new RabbitMqApiHost(factory, client);
            }
            catch
            {
                client.Dispose();
                await factory.DisposeAsync();
                throw;
            }
        }

        // O encerramento espera hosted services/consumer e nunca remove fila, vhost ou volume.
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    // Resultados sintéticos permitem reproduzir shutdown, retries e sucesso sem credenciais ou chamadas Pluggy.
    private sealed class SyntheticRabbitImportReader : ITransactionImportReader
    {
        private readonly Queue<ImportOutcome> _outcomes = new();
        private readonly object _gate = new();
        private readonly CancellationTokenSource _firstReadTestCancellation = new();
        private int _calls;
        internal int CallCount => Volatile.Read(ref _calls);
        internal TaskCompletionSource FirstReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FirstReadCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource WatchdogReadCancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseIgnoringCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // A ordem dos resultados é fixa e espelha os cenários do mesmo teste integrado.
        internal void EnqueueCancellationUntilShutdown() => Enqueue(new ImportOutcome(WaitForCancellation: true));
        internal void EnqueueIgnoringCancellation() => Enqueue(new ImportOutcome(IgnoreCancellation: true));
        internal void EnqueueSuccess() => Enqueue(new ImportOutcome());
        internal void EnqueueFailure(TransactionImportFailureCode failure, TimeSpan? retryAfter = null) =>
            Enqueue(new ImportOutcome(Failure: failure, RetryAfter: retryAfter));

        // O teste cancela apenas a leitura sintética antes do shutdown do host para observar o fluxo de erro do broker.
        internal void CancelFirstReadForTest() => _firstReadTestCancellation.Cancel();

        // A leitura deliberadamente ignora o token até o teste liberá-la, verificando fencing após o watchdog.
        internal void ReleaseIgnoringCancellationRead() => _releaseIgnoringCancellation.TrySetResult();

        // A fila concorrente é consumida sob lock porque várias tentativas compartilham o leitor nos restarts.
        private void Enqueue(ImportOutcome outcome)
        {
            lock (_gate) { _outcomes.Enqueue(outcome); }
        }

        // O adapter valida a identidade sintética, devolve falhas contratadas ou snapshot financeiro estável.
        public async Task<TransactionImportReadResult> ReadAsync(
            string externalItemId,
            DateOnly dateFrom,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(externalItemId, "synthetic-rabbit-item", StringComparison.Ordinal))
            {
                return TransactionImportReadResult.Failed(TransactionImportFailureCode.ConnectionUnavailable);
            }

            var call = Interlocked.Increment(ref _calls);
            ImportOutcome outcome;
            lock (_gate)
            {
                if (_outcomes.Count == 0)
                {
                    return TransactionImportReadResult.Failed(TransactionImportFailureCode.InvalidResponse);
                }
                outcome = _outcomes.Dequeue();
            }

            if (call == 1) { FirstReadStarted.TrySetResult(); }
            if (outcome.WaitForCancellation)
            {
                using var localCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _firstReadTestCancellation.Token);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, localCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    FirstReadCancelled.TrySetResult();
                    throw;
                }
            }
            else if (outcome.IgnoreCancellation)
            {
                await _releaseIgnoringCancellation.Task;
                try { cancellationToken.ThrowIfCancellationRequested(); }
                catch (OperationCanceledException)
                {
                    WatchdogReadCancellationObserved.TrySetResult();
                    throw;
                }
            }

            if (outcome.Failure is { } failure)
            {
                DateTimeOffset? retryAfterAtUtc = outcome.RetryAfter is { } delay
                    ? DateTimeOffset.UtcNow.Add(delay)
                    : null;
                return TransactionImportReadResult.Failed(
                    failure, pagesRead: 1, receivedCount: 0, retryAfterAtUtc: retryAfterAtUtc);
            }

            var observedDate = dateFrom.AddDays(1);
            var snapshot = new TransactionImportSnapshot(
                [new TransactionImportAccount("synthetic-rabbit-account", "CREDIT", "CREDIT_CARD", "BRL")],
                [new PluggyTransactionData(
                    "synthetic-rabbit-transaction", "synthetic-rabbit-account", "Lançamento sintético", 12.34m,
                    "BRL", observedDate.ToString("yyyy-MM-dd"), "DEBIT", "POSTED", "TARIFA")],
                dateFrom,
                PaginationCompleted: true,
                PagesRead: 1,
                ReceivedCount: 1,
                ObservedFrom: observedDate,
                ObservedTo: observedDate);
            return new TransactionImportReadResult(snapshot, null);
        }

        private sealed record ImportOutcome(
            bool WaitForCancellation = false,
            bool IgnoreCancellation = false,
            TransactionImportFailureCode? Failure = null,
            TimeSpan? RetryAfter = null);
    }
}
