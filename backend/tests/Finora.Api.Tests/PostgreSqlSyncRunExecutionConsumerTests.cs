using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Messaging;
using Finora.Infrastructure.Messaging.Contracts;
using Finora.Infrastructure.Persistence;
using MediatR;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita consumer e caso de uso com transporte in-memory e dados sintéticos no PostgreSQL descartável.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunExecutionConsumerTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    // A fila entrega somente SyncRunId; o lease recupera owner, conexão, janela e tentativa da execução aceita.
    [TestMethod]
    public async Task ConsumerLoadsPersistedExecutionAndInvokesAttemptExecutorWithSameContext()
    {
        await using var fixture = await ExecutionConsumerFixture.CreateAsync();

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<SyncRunExecutionMessageSender>();
            await sender.SendAsync(fixture.SyncRunId, InitialTime, CancellationToken.None);
        }

        var execution = await fixture.Executor!.Execution.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(fixture.SyncRunId, execution.SyncRunId);
        Assert.AreEqual(fixture.UserId, execution.UserId);
        Assert.AreEqual(fixture.ConnectionId, execution.ConnectionId);
        Assert.AreEqual(1, execution.AttemptNumber);
        Assert.AreEqual(1L, execution.FencingToken);
        Assert.AreEqual(InitialTime, execution.AcquiredAtUtc);
        Assert.AreEqual(InitialTime.AddSeconds(120), execution.ExpiresAtUtc);
        Assert.AreEqual(new DateOnly(2025, 11, 23), execution.RequestedFrom);
        Assert.AreEqual(new DateOnly(2026, 11, 24), execution.RequestedToExclusive);
        Assert.AreEqual(InitialTime, execution.AttemptStartedAtUtc);
        Assert.AreEqual(InitialTime.AddMinutes(10), execution.AttemptDeadlineAtUtc);

        await using var verification = CreateContext(fixture.ConnectionString);
        var persisted = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(SyncRunState.Running, persisted.State);
        Assert.AreEqual(1, persisted.AttemptNumber);
        Assert.AreEqual(fixture.SyncRunId, persisted.Id);
        Assert.AreEqual(new DateOnly(2025, 11, 23), persisted.RequestedFrom);
        Assert.AreEqual(new DateOnly(2026, 11, 24), persisted.RequestedToExclusive);
        Assert.AreEqual(1, await verification.Set<SyncRunLease>().CountAsync());

        // Application e Domain são independentes do transporte; somente Infrastructure contém o adapter MassTransit.
        Assert.IsFalse(typeof(ExecuteSyncRunCommand).Assembly.GetReferencedAssemblies()
            .Any(assembly => assembly.Name?.StartsWith("MassTransit", StringComparison.Ordinal) == true));
        Assert.IsFalse(typeof(SyncRun).Assembly.GetReferencedAssemblies()
            .Any(assembly => assembly.Name?.StartsWith("MassTransit", StringComparison.Ordinal) == true));
        Assert.IsTrue(typeof(SyncRunExecutionConsumer).Assembly.GetReferencedAssemblies()
            .Any(assembly => assembly.Name?.StartsWith("MassTransit", StringComparison.Ordinal) == true));
    }

    // Redelivery enquanto o lease está vivo confirma somente a cópia concorrente; não inicia outra importação.
    [TestMethod]
    public async Task ActiveLeaseRedeliveryDoesNotStartASecondImport()
    {
        await using var fixture = await ExecutionConsumerFixture.CreateAsync();
        await using (var owner = CreateContext(fixture.ConnectionString))
        {
            var acquired = await new SyncRunLeaseStore(owner).AcquireAsync(
                fixture.SyncRunId, InitialTime, CancellationToken.None);
            Assert.AreEqual(SyncRunLeaseAcquisitionStatus.Acquired, acquired.Status);
        }

        await fixture.SendExecutionAsync();
        await fixture.LeaseProbe.FirstAcquisition.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(0, fixture.Executor!.ExecutionCount);
        await using var verification = CreateContext(fixture.ConnectionString);
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(fixture.SyncRunId, run.Id);
        Assert.AreEqual(SyncRunState.Running, run.State);
        Assert.AreEqual(1, run.AttemptNumber);
        Assert.AreEqual(1, await verification.Set<SyncRunLease>().CountAsync());
    }

    // Reenvio após sucesso commitado representa ack perdido: estado terminal vira no-op e preserva os mesmos fatos.
    [TestMethod]
    public async Task RedeliveryAfterCommittedSuccessDoesNotRepeatFinancialEffects()
    {
        await using var fixture = await ExecutionConsumerFixture.CreateAsync(useFinancialPipeline: true);
        await fixture.SendExecutionAsync();
        await WaitForSuccessfulRunAsync(fixture.ConnectionString, fixture.SyncRunId);

        await using (var beforeRedelivery = CreateContext(fixture.ConnectionString))
        {
            Assert.AreEqual(1, await beforeRedelivery.Set<SyncRun>().CountAsync());
            Assert.AreEqual(1, await beforeRedelivery.Set<CardAccount>().CountAsync());
            Assert.AreEqual(1, await beforeRedelivery.Set<FinancialTransaction>().CountAsync());
        }

        await fixture.SendExecutionAsync();
        await fixture.LeaseProbe.SecondAcquisition.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var verification = CreateContext(fixture.ConnectionString);
        var run = await verification.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(fixture.SyncRunId, run.Id);
        Assert.AreEqual(SyncRunState.Succeeded, run.State);
        Assert.AreEqual(1, await verification.Set<SyncRun>().CountAsync());
        Assert.AreEqual(1, await verification.Set<CardAccount>().CountAsync());
        Assert.AreEqual(1, await verification.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(2, fixture.LeaseProbe.AcquisitionCount);
    }

    // Aguarda o resultado terminal persistido, que só se torna visível após o commit financeiro e da SyncRun.
    private static async Task WaitForSuccessfulRunAsync(string connectionString, Guid syncRunId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var context = CreateContext(connectionString);
            var state = await context.Set<SyncRun>().AsNoTracking()
                .Where(run => run.Id == syncRunId)
                .Select(run => run.State)
                .SingleAsync();
            if (state == SyncRunState.Succeeded)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("A execução não concluiu a promoção dentro do limite do teste.");
    }

    // As opções EF usam exclusivamente a connection string que passou a validação da fixture descartável.
    private static FinoraDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, connectionString);
        return new FinoraDbContext(options.Options);
    }

    // Um relógio fixo permite conferir deadline/lease sem esperar tempo real ou iniciar provider financeiro.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // O relógio avança um segundo por leitura para separar aquisição, coleta e finalização da posse no cenário real.
    private sealed class IncrementingTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private int _readCount = -1;

        public override DateTimeOffset GetUtcNow() =>
            initial.AddSeconds(Interlocked.Increment(ref _readCount));
    }

    // O executor de teste recebe o contexto Application e não toca no importador HTTP nem em movimentos financeiros.
    private sealed class RecordingSyncRunAttemptExecutor : ISyncRunAttemptExecutor
    {
        private int _executionCount;
        internal TaskCompletionSource<SyncRunLeaseGrant> Execution { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ExecutionCount => Volatile.Read(ref _executionCount);

        public Task<TransactionImportResult> ExecuteAttemptAsync(
            SyncRunLeaseGrant execution,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executionCount);
            Execution.TrySetResult(execution);
            return Task.FromResult(new TransactionImportResult(true, null));
        }
    }

    // Leitor retorna exclusivamente snapshot sintético para exercitar a promoção sem qualquer chamada externa.
    private sealed class FixedSnapshotReader(TransactionImportSnapshot snapshot) : ITransactionImportReader
    {
        public Task<TransactionImportReadResult> ReadAsync(
            string externalItemId,
            DateOnly dateFrom,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TransactionImportReadResult(snapshot, null));
    }

    // Instrumenta aquisições para sincronizar a prova de redelivery sem sleeps ou depender de logs do broker.
    private sealed class LeaseStoreProbe
    {
        private int _acquisitionCount;
        internal int AcquisitionCount => Volatile.Read(ref _acquisitionCount);
        internal TaskCompletionSource FirstAcquisition { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SecondAcquisition { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void RecordAcquisition()
        {
            var count = Interlocked.Increment(ref _acquisitionCount);
            if (count == 1)
            {
                FirstAcquisition.TrySetResult();
            }
            else if (count == 2)
            {
                SecondAcquisition.TrySetResult();
            }
        }
    }

    // O wrapper delega a política real de lease e só publica um sinal de teste após a segunda aquisição.
    private sealed class ProbedLeaseStore(SyncRunLeaseStore inner, LeaseStoreProbe probe) : ISyncRunLeaseStore
    {
        public async Task<SyncRunLeaseAcquisitionResult> AcquireAsync(
            Guid syncRunId,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            var result = await inner.AcquireAsync(syncRunId, nowUtc, cancellationToken);
            probe.RecordAcquisition();
            return result;
        }

        public Task<SyncRunLeaseGrant?> RenewAsync(
            SyncRunLeaseGrant grant,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => inner.RenewAsync(grant, nowUtc, cancellationToken);

        public Task<bool> ScheduleRetryAsync(
            SyncRunLeaseGrant grant,
            DateTimeOffset nowUtc,
            DateTimeOffset nextAttemptAtUtc,
            string failureCode,
            int? pagesRead,
            int? receivedCount,
            CancellationToken cancellationToken) =>
            inner.ScheduleRetryAsync(
                grant, nowUtc, nextAttemptAtUtc, failureCode, pagesRead, receivedCount, cancellationToken);

        public Task<bool> CompleteFailureAsync(
            SyncRunLeaseGrant grant,
            DateTimeOffset finishedAtUtc,
            string failureCode,
            int? pagesRead,
            int? receivedCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken) =>
            inner.CompleteFailureAsync(
                grant, finishedAtUtc, failureCode, pagesRead, receivedCount, nextAttemptAtUtc, cancellationToken);
    }

    // Mantém host, fixture PostgreSQL, identidade e IDs sintéticos isolados durante o ensaio.
    private sealed class ExecutionConsumerFixture : IAsyncDisposable
    {
        private readonly DisposablePostgresMigrationFixture _database;

        private ExecutionConsumerFixture(
            DisposablePostgresMigrationFixture database,
            string connectionString,
            IHost host,
            RecordingSyncRunAttemptExecutor? executor,
            LeaseStoreProbe leaseProbe,
            Guid userId,
            Guid connectionId,
            Guid syncRunId)
        {
            _database = database;
            ConnectionString = connectionString;
            Host = host;
            Executor = executor;
            LeaseProbe = leaseProbe;
            UserId = userId;
            ConnectionId = connectionId;
            SyncRunId = syncRunId;
        }

        internal string ConnectionString { get; }
        internal IHost Host { get; }
        internal RecordingSyncRunAttemptExecutor? Executor { get; }
        internal LeaseStoreProbe LeaseProbe { get; }
        internal Guid UserId { get; }
        internal Guid ConnectionId { get; }
        internal Guid SyncRunId { get; }

        // Cria schema vazio, uma execução aceita e host local; nenhum endereço RabbitMQ/Pluggy é lido.
        internal static async Task<ExecutionConsumerFixture> CreateAsync(bool useFinancialPipeline = false)
        {
            var database = DisposablePostgresMigrationFixture.CreateValidated();
            await database.ResetToEmptySchemaAsync();
            Assert.IsTrue(await database.Context.Database.EnsureCreatedAsync());
            var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
            var userId = Guid.NewGuid();
            var connectionId = Guid.NewGuid();
            var syncRunId = Guid.NewGuid();
            database.Context.Users.Add(new ApplicationUser(userId)
            {
                UserName = "sync-consumer@example.invalid",
                NormalizedUserName = "SYNC-CONSUMER@EXAMPLE.INVALID",
                Email = "sync-consumer@example.invalid",
                NormalizedEmail = "SYNC-CONSUMER@EXAMPLE.INVALID"
            });
            database.Context.Set<FinancialConnection>().Add(
                FinancialConnection.Create(connectionId, userId, "pluggy", "synthetic-consumer-item"));
            database.Context.Set<FinancialConnectionItemIdentity>().Add(
                FinancialConnectionItemIdentity.CreateActive(
                    connectionId, userId, "pluggy", "synthetic-consumer-item"));
            database.Context.Set<SyncRun>().Add(SyncRun.Create(
                syncRunId,
                userId,
                connectionId,
                InitialTime,
                new DateOnly(2025, 11, 23),
                new DateOnly(2026, 11, 24),
                nextAttemptAtUtc: InitialTime));
            await database.Context.SaveChangesAsync();

            var executor = useFinancialPipeline ? null : new RecordingSyncRunAttemptExecutor();
            var leaseProbe = new LeaseStoreProbe();
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Services.AddDbContext<FinoraDbContext>(options =>
                FinoraDbContextConfiguration.Configure(options, connectionString));
            builder.Services.AddSingleton<TimeProvider>(useFinancialPipeline
                ? new IncrementingTimeProvider(InitialTime)
                : new FixedTimeProvider(InitialTime));
            builder.Services.AddFinoraSyncRunAcceptancePersistence();
            builder.Services.RemoveAll<ISyncRunLeaseStore>();
            builder.Services.AddSingleton(leaseProbe);
            builder.Services.AddScoped<ISyncRunLeaseStore>(provider => new ProbedLeaseStore(
                new SyncRunLeaseStore(provider.GetRequiredService<FinoraDbContext>()),
                provider.GetRequiredService<LeaseStoreProbe>()));
            builder.Services.AddMediatR(configuration =>
                configuration.RegisterServicesFromAssembly(typeof(ExecuteSyncRunCommand).Assembly));
            if (useFinancialPipeline)
            {
                var snapshot = new TransactionImportSnapshot(
                    [new TransactionImportAccount("synthetic-consumer-account", "CREDIT", "CREDIT_CARD", "BRL")],
                    [new PluggyTransactionData(
                        "synthetic-consumer-transaction", "synthetic-consumer-account", "Fixture sintética", 12.34m,
                        "BRL", "2026-11-18", "DEBIT", "POSTED", "TARIFA")],
                    new DateOnly(2025, 11, 23),
                    true,
                    PagesRead: 1,
                    ReceivedCount: 1,
                    ObservedFrom: new DateOnly(2026, 11, 18),
                    ObservedTo: new DateOnly(2026, 11, 18));
                builder.Services.AddScoped<ITransactionImportConnectionReader, TransactionImportConnectionReader>();
                builder.Services.AddSingleton<ITransactionImportReader>(new FixedSnapshotReader(snapshot));
                builder.Services.AddScoped<ITransactionImportPromoter, FinancialTransactionImportPromoter>();
                builder.Services.AddScoped<ImportTransactionsHandler>();
                builder.Services.AddScoped<ISyncRunAttemptExecutor>(provider =>
                    provider.GetRequiredService<ImportTransactionsHandler>());
            }
            else
            {
                builder.Services.AddSingleton(executor!);
                builder.Services.AddSingleton<ISyncRunAttemptExecutor>(executor!);
            }
            builder.Services.AddScoped<SyncRunExecutionMessageSender>();
            builder.Services.AddMassTransit(registration =>
            {
                registration.AddConsumer<SyncRunExecutionConsumer>();
                registration.UsingInMemory((context, bus) => bus.ReceiveEndpoint(
                    SyncRunExecutionTopology.QueueName,
                    endpoint =>
                    {
                        endpoint.PrefetchCount = SyncRunExecutionTopology.ConsumerPrefetchCount;
                        endpoint.ConcurrentMessageLimit = SyncRunExecutionTopology.ConsumerConcurrencyLimit;
                        endpoint.ConfigureConsumer<SyncRunExecutionConsumer>(context);
                    }));
            });

            var host = builder.Build();
            try
            {
                await host.StartAsync();
                return new ExecutionConsumerFixture(
                    database, connectionString, host, executor, leaseProbe, userId, connectionId, syncRunId);
            }
            catch
            {
                host.Dispose();
                await database.DisposeAsync();
                throw;
            }
        }

        // Envia novamente o mesmo ID e metadado estável pelo transporte in-memory para simular redelivery/ack perdido.
        internal async Task SendExecutionAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<SyncRunExecutionMessageSender>();
            await sender.SendAsync(SyncRunId, InitialTime, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync();
            Host.Dispose();
            await _database.DisposeAsync();
        }
    }
}
