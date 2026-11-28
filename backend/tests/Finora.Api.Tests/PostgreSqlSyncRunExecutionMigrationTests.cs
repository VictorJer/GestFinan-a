using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida criação, upgrade e downgrade da migration somente no PostgreSQL descartável allowlisted.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlSyncRunExecutionMigrationTests
{
    private const string PreviousMigrationId = "20261120211916_AddFinancialConnectionItemIdentityHistory";
    private const string ExecutionMigrationId = "20261123000048_Fnr013SyncRunExecutionPersistence";
    private const string RecoveryMigrationId = "20261123013815_Fnr013SyncRunExecutionRecovery";

    // Schema vazio aplica a cadeia atual e cria restrições; o catálogo é semeado pelas migrations próprias.
    [TestMethod]
    public async Task FreshSchemaCreatesLeaseOutboxAndRetryConstraints()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();

        await fixture.Context.Database.MigrateAsync();

        CollectionAssert.AreEqual(
            new[]
            {
                PostgreSqlPersistenceTests.SmokeMigrationId,
                PostgreSqlPersistenceTests.CoreMigrationId,
                PostgreSqlAuthenticationSessionTests.MigrationId,
                PreviousMigrationId,
                ExecutionMigrationId,
                RecoveryMigrationId,
                "20261124043312_Fnr016InitialCategories", // A migração de dados FNR-016 estende a sequência atual.
                "20261126003137_Fnr038RecurrenceAssistance", // A migration cria o schema de recorrência.
                "20261128011938_AddInvestmentPortfolio" // A migration FNR-029 integra a cadeia aplicada a schema vazio.
            },
            (await fixture.Context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.AreEqual(0, await fixture.Context.Set<SyncRunLease>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Set<SyncRun>().CountAsync());

        var constraints = await fixture.Context.Database.SqlQueryRaw<string>(
                "SELECT conname AS \"Value\" FROM pg_constraint " +
                "WHERE conrelid IN ('\"SyncRun\"'::regclass, '\"SyncRunLease\"'::regclass)")
            .ToArrayAsync();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "CK_SyncRun_AttemptBudget",
                "CK_SyncRun_AttemptDeadline",
                "CK_SyncRun_NextAttemptNotBeforeAcceptance",
                "CK_SyncRunLeases_Attempt_Positive",
                "CK_SyncRunLeases_Fencing_Positive",
                "CK_SyncRunLeases_Expiration"
            },
            constraints);

        var indexes = await fixture.Context.Database.SqlQueryRaw<string>(
                "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' " +
                "AND tablename IN ('SyncRun', 'SyncRunLease')")
            .ToArrayAsync();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "IX_SyncRun_State_NextAttemptAtUtc",
                "IX_SyncRun_State_ExecutionMessageEnqueuedAtUtc",
                "UX_SyncRun_IdempotencyKeyHash",
                "IX_SyncRunLeases_SyncRunId_AttemptNumber"
            },
            indexes);
    }

    // Upgrade backfills defaults on a legacy execution; downgrade removes only new schema and preserves prior rows.
    [TestMethod]
    public async Task UpgradeAndDowngradePreserveLegacyConnectionAndSyncRun()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PreviousMigrationId);
        await SeedLegacyRowsAsync(fixture.Context);

        await fixture.Context.Database.MigrateAsync(RecoveryMigrationId);

        var upgraded = await fixture.Context.Set<SyncRun>().AsNoTracking().SingleAsync();
        Assert.AreEqual(1, await fixture.Context.Set<FinancialConnection>().CountAsync());
        Assert.AreEqual(0, upgraded.AttemptNumber);
        Assert.AreEqual(SyncRun.ApprovedAttemptLimit, upgraded.AttemptLimit);
        Assert.IsNull(upgraded.AttemptStartedAtUtc);
        Assert.IsNull(upgraded.AttemptDeadlineAtUtc);
        Assert.IsNull(upgraded.NextAttemptAtUtc);
        Assert.IsNull(upgraded.ExecutionMessageEnqueuedAtUtc);
        Assert.IsNull(upgraded.IdempotencyKeyHash);
        Assert.IsNull(upgraded.RequestFingerprint);
        Assert.AreEqual(2025, upgraded.RequestedFrom.Year);

        await fixture.Context.Database.MigrateAsync(PreviousMigrationId);

        CollectionAssert.AreEqual(new[] { PreviousMigrationId },
            (await fixture.Context.Database.GetAppliedMigrationsAsync()).TakeLast(1).ToArray());
        CollectionAssert.AreEqual(new[] { LegacySyncRunId }, await fixture.Context.Database
            .SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"SyncRun\"")
            .ToArrayAsync());
        Assert.AreEqual(1, await fixture.Context.Set<FinancialConnection>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Database.SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns " +
                "WHERE table_schema = 'public' AND table_name = 'SyncRun' AND column_name = 'AttemptNumber'")
            .CountAsync());
    }

    // Insere somente as colunas do schema anterior, comprovando defaults sem usar o modelo novo para escrita legada.
    private static async Task SeedLegacyRowsAsync(FinoraDbContext context)
    {
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser(userId)
        {
            UserName = "sync-migration@example.invalid",
            NormalizedUserName = "SYNC-MIGRATION@EXAMPLE.INVALID",
            Email = "sync-migration@example.invalid",
            NormalizedEmail = "SYNC-MIGRATION@EXAMPLE.INVALID"
        });
        context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(connectionId, userId, "pluggy", "synthetic-migration-item"));
        await context.SaveChangesAsync();

        // O SQL lista apenas campos existentes antes da migration, preservando a execução histórica durante o upgrade.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SyncRun" (
                "Id", "UserId", "ConnectionId", "CreatedAtUtc", "RequestedFrom", "State", "CoverageState")
            VALUES (
                {LegacySyncRunId}, {userId}, {connectionId}, {new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero)},
                {new DateOnly(2025, 11, 22)}, {nameof(SyncRunState.Pending)}, {nameof(SyncCoverageState.Unknown)})
            """);
    }

    private static readonly Guid LegacySyncRunId = Guid.Parse("be7fbba2-ef46-45d2-b605-731de16e27a9");
}
