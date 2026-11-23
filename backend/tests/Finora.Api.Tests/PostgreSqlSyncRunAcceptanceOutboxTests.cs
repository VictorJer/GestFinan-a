using System.Security.Cryptography;
using System.Text;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Messaging;
using Finora.Infrastructure.Persistence;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita somente o contexto e o database PostgreSQL descartável allowlisted; não inicia RabbitMQ nem usa dados reais.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunAcceptanceOutboxTests
{
    private static readonly Guid UserId = Guid.Parse("a71641fc-dc4c-45ba-aea5-51d373bb4053");
    private static readonly Guid ConnectionId = Guid.Parse("1a9ef6de-73db-4e79-99e9-dddd69991a6b");
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    // Uma falha durante CommitAsync é classificada como indeterminada e o banco descartável prova ausência do par.
    [TestMethod]
    public async Task FailedCommitLeavesNoAcceptedRunOrOutboxMessage()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        var interceptor = new FailBeforeCommitInterceptor();
        using var host = CreateHost(settings.ConnectionString, interceptor);
        await SeedConnectionAsync(host);

        var candidate = CreateCandidate(Guid.NewGuid(), "rollback-key");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISyncRunAcceptanceStore>();
            interceptor.FailNextCommit();
            await Assert.ThrowsExactlyAsync<SyncRunAcceptanceIndeterminateException>(
                () => store.AcceptOrGetAsync(candidate, CancellationToken.None));
        }

        await using var verification = host.Services.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.AreEqual(0, await dbContext.Set<SyncRun>().CountAsync());
        Assert.AreEqual(0, await dbContext.Set<OutboxMessage>().CountAsync());
    }

    // Commit grava o par na mesma transação e replay da chave não acrescenta segunda execução ou mensagem.
    [TestMethod]
    public async Task CommitPersistsRunAndOutboxTogetherAndReplayDoesNotDuplicateEither()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await ResetDisposableDatabaseAsync();
        using var host = CreateHost(settings.ConnectionString, new FailBeforeCommitInterceptor());
        await SeedConnectionAsync(host);
        var firstCandidate = CreateCandidate(Guid.NewGuid(), "committed-key");
        SyncRunAcceptanceResult first;
        SyncRunAcceptanceResult replay;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISyncRunAcceptanceStore>();
            first = await store.AcceptOrGetAsync(firstCandidate, CancellationToken.None);
            replay = await store.AcceptOrGetAsync(CreateCandidate(Guid.NewGuid(), "committed-key"), CancellationToken.None);
        }

        await using var verification = host.Services.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.AreEqual(SyncRunAcceptanceStatus.Accepted, first.Status);
        Assert.AreEqual(SyncRunAcceptanceStatus.Replayed, replay.Status);
        Assert.AreEqual(first.SyncRunId, replay.SyncRunId);
        Assert.AreEqual(1, await dbContext.Set<SyncRun>().CountAsync());
        Assert.AreEqual(1, await dbContext.Set<OutboxMessage>().CountAsync());
        Assert.AreEqual(CreatedAtUtc, (await dbContext.Set<SyncRun>().AsNoTracking().SingleAsync())
            .ExecutionMessageEnqueuedAtUtc);
    }

    // A composição usa transporte in-memory sem iniciar seu host; a bus outbox grava pelo DbContext scoped.
    private static IHost CreateHost(string connectionString, FailBeforeCommitInterceptor interceptor)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<FinoraDbContext>(options =>
        {
            FinoraDbContextConfiguration.Configure(options, connectionString);
            options.AddInterceptors(interceptor);
        });
        builder.Services.AddFinoraSyncRunAcceptancePersistence();
        builder.Services.AddFinoraSyncRunExecutionOutbox(registration =>
            registration.UsingInMemory((_, configurator) =>
                configurator.ReceiveEndpoint(SyncRunExecutionTopology.QueueName, _ => { })));
        return builder.Build();
    }

    // A fixture revalida host/database antes do reset; o schema é efêmero e não compartilha volume persistente.
    private static async Task ResetDisposableDatabaseAsync()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
    }

    // Titular e conexão sintéticos satisfazem as FKs exigidas antes de aceitar uma execução de teste.
    private static async Task SeedConnectionAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        Assert.IsTrue(await dbContext.Database.EnsureCreatedAsync());
        dbContext.Users.Add(new ApplicationUser(UserId)
        {
            UserName = "sync-outbox@example.invalid",
            NormalizedUserName = "SYNC-OUTBOX@EXAMPLE.INVALID",
            Email = "sync-outbox@example.invalid",
            NormalizedEmail = "SYNC-OUTBOX@EXAMPLE.INVALID"
        });
        dbContext.Set<FinancialConnection>().Add(
            FinancialConnection.Create(ConnectionId, UserId, "pluggy", "synthetic-outbox-item"));
        await dbContext.SaveChangesAsync();
    }

    // A chave é armazenada como SHA-256 e o fingerprint acompanha o formato validado pelo domínio.
    private static SyncRun CreateCandidate(Guid runId, string idempotencyKey)
    {
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(idempotencyKey)));
        return SyncRun.Create(
            runId,
            UserId,
            ConnectionId,
            CreatedAtUtc,
            new DateOnly(2025, 11, 22),
            new DateOnly(2026, 11, 23),
            idempotencyKeyHash: keyHash,
            requestFingerprint: SyncRunIdempotency.CreateRequestFingerprint(ConnectionId));
    }

    // O interceptor injeta uma falha controlada uma única vez, depois de SaveChanges e antes do commit PostgreSQL.
    private sealed class FailBeforeCommitInterceptor : DbTransactionInterceptor
    {
        private int _failNextCommit;

        public void FailNextCommit() => Interlocked.Exchange(ref _failNextCommit, 1);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _failNextCommit, 0) == 1
                ? ValueTask.FromException<InterceptionResult>(new InvalidOperationException("synthetic pre-commit failure"))
                : ValueTask.FromResult(result);
    }
}
