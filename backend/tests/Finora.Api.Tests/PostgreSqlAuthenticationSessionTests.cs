using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// As provas de constraints/migration usam exclusivamente a fixture PostgreSQL descartável com barreira de destino.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlAuthenticationSessionTests
{
    internal const string MigrationId = "20261118173427_AddAuthenticationSessions";
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    // A tabela real preserva datas UTC, vínculo e minimização; o modelo não possui campo de token completo.
    [TestMethod]
    public async Task SessionsRoundTripWithOnlyApprovedColumns()
    {
        await using var fixture = await CreateDatabaseAsync();
        var context = fixture.Context;
        var user = await SeedUserAsync(context);
        var session = AuthSession.Create(AuthSession.GenerateId(), user.Id, Now, Now.AddMinutes(30));
        context.Add(session);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var loaded = await context.Set<AuthSession>().AsNoTracking().SingleAsync();
        Assert.AreEqual(session.Id, loaded.Id);
        Assert.AreEqual(user.Id, loaded.UserId);
        Assert.AreEqual(Now, loaded.CreatedAtUtc);
        Assert.AreEqual(Now.AddMinutes(30), loaded.ExpiresAtUtc);
        Assert.AreEqual(TimeSpan.Zero, loaded.CreatedAtUtc.Offset);
        Assert.IsNull(loaded.RevokedAtUtc);
        var columns = await context.Database.SqlQueryRaw<string>(
            "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema='public' AND table_name='AuthSessions'")
            .ToArrayAsync();
        CollectionAssert.AreEquivalent(new[] { "Id", "UserId", "CreatedAtUtc", "ExpiresAtUtc", "RevokedAtUtc" }, columns);
    }

    // SQL parametrizado contorna a fábrica deliberadamente para provar constraints no próprio PostgreSQL.
    [TestMethod]
    public async Task DatabaseRejectsDuplicateMissingOwnerAndInvalidDates()
    {
        await using var fixture = await CreateDatabaseAsync();
        var context = fixture.Context;
        var user = await SeedUserAsync(context);
        var id = AuthSession.GenerateId();
        await InsertAsync(context, id, user.Id, Now, Now.AddMinutes(30), null);
        await AssertSqlStateAsync(PostgresErrorCodes.UniqueViolation,
            () => InsertAsync(context, id, user.Id, Now, Now.AddMinutes(30), null));
        await AssertSqlStateAsync(PostgresErrorCodes.ForeignKeyViolation,
            () => InsertAsync(context, AuthSession.GenerateId(), Guid.NewGuid(), Now, Now.AddMinutes(30), null));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation,
            () => InsertAsync(context, AuthSession.GenerateId(), user.Id, Now, Now, null));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation,
            () => InsertAsync(context, AuthSession.GenerateId(), user.Id, Now, Now.AddMinutes(30), Now.AddSeconds(-1)));
        await AssertSqlStateAsync(PostgresErrorCodes.RestrictViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AspNetUsers\" WHERE \"Id\"={user.Id}"));
        await InsertAsync(context, AuthSession.GenerateId(), user.Id, Now, Now.AddMinutes(30), Now);
        Assert.AreEqual(2, await context.Set<AuthSession>().CountAsync());
    }

    // A barreira SaveChanges impede inclusive alteração rastreada inválida antes de executar SQL.
    [TestMethod]
    public async Task TrackedChangesCannotBypassSessionValidation()
    {
        await using var fixture = await CreateDatabaseAsync();
        var context = fixture.Context;
        var user = await SeedUserAsync(context);
        var session = AuthSession.Create(AuthSession.GenerateId(), user.Id, Now, Now.AddMinutes(30));
        context.Add(session);
        context.Entry(session).Property(item => item.Id).CurrentValue = "malformed";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        Assert.AreEqual(0, await context.Set<AuthSession>().CountAsync());
    }

    // Uma base realmente vazia recebe a sequência completa, sem seed e sem migration no startup da aplicação.
    [TestMethod]
    public async Task MigrationAppliesFromEmptyDatabase()
    {
        await using var fixture = await CreateDatabaseAsync();
        CollectionAssert.AreEqual(new[] { PostgreSqlPersistenceTests.SmokeMigrationId, PostgreSqlPersistenceTests.CoreMigrationId, MigrationId },
            (await fixture.Context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.AreEqual(0, await fixture.Context.Users.CountAsync());
        Assert.AreEqual(0, await fixture.Context.Set<AuthSession>().CountAsync());
    }

    // Upgrade/down/reaplicação preservam todas as linhas anteriores, incluindo credencial e grafo financeiro sintéticos.
    [TestMethod]
    public async Task IncrementalMigrationAndRollbackPreserveExistingUserAndFinancialGraph()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var context = fixture.Context;
        await context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        var user = await SeedUserAsync(context);
        await SeedGraphAsync(context, user.Id);
        var before = await ReadExistingRowsAsync(context);
        await context.Database.MigrateAsync(MigrationId);
        var afterUpgrade = await ReadExistingRowsAsync(context);
        Assert.IsTrue(before.SequenceEqual(afterUpgrade), "Upgrade deve preservar os dados anteriores.");
        await InsertAsync(context, AuthSession.GenerateId(), user.Id, Now, Now.AddMinutes(30), null);
        await context.GetService<IMigrator>().MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        Assert.IsFalse((await PostgreSqlPersistenceTests.ReadPublicTablesAsync(context)).Contains("AuthSessions"));
        var afterDown = await ReadExistingRowsAsync(context);
        Assert.IsTrue(before.SequenceEqual(afterDown), "Down deve remover somente sessões.");
        await context.Database.MigrateAsync(MigrationId);
        Assert.AreEqual(0, await context.Set<AuthSession>().CountAsync());
        var afterReapplication = await ReadExistingRowsAsync(context);
        Assert.IsTrue(before.SequenceEqual(afterReapplication), "Reaplicação deve preservar o grafo.");
    }

    // A criação da fixture valida host/database antes de qualquer operação destrutiva e aplica schema explicitamente.
    private static async Task<DisposablePostgresMigrationFixture> CreateDatabaseAsync()
    {
        var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(MigrationId);
        return fixture;
    }

    // Conta sintética com hash Identity permite verificar que a migration não altera a credencial existente.
    private static async Task<ApplicationUser> SeedUserAsync(FinoraDbContext context)
    {
        var user = new ApplicationUser { UserName = "session@example.invalid", Email = "session@example.invalid" };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, "Synthetic!Password123");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    // Compõe um grafo financeiro representativo pelas fábricas reais, sem payload externo ou dados pessoais.
    private static async Task SeedGraphAsync(FinoraDbContext context, Guid userId)
    {
        var connectionId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        context.Add(FinancialConnection.Create(connectionId, userId, "pluggy", "session-migration-item"));
        context.Add(CardAccount.Create(cardId, userId, connectionId, "session-migration-card", "BRL"));
        context.Add(Category.Create(categoryId, userId, false, "Sintética", "sintetica"));
        var date = new DateOnly(2026, 11, 18);
        var facts = new TransactionFacts("session-migration-movement", "session-migration-card", "sintético",
            12.3456m, "BRL", "2026-11-18", "DEBIT", "POSTED", "TARIFA");
        context.Add(FinancialTransaction.Create(transactionId, userId, cardId,
            TransactionNormalizer.Normalize(facts, date), "session-migration-test", date));
        context.Add(TransactionAnnotation.Create(transactionId, userId, categoryId, userId, "sintética", Now));
        await context.SaveChangesAsync();
    }

    // Compara conteúdo integral em memória sem incluir hashes/stamps em mensagens de asserção.
    private static async Task<string[]> ReadExistingRowsAsync(FinoraDbContext context)
    {
        var rows = new List<string>();
        foreach (var table in new[] { "AspNetUsers", "FinancialConnection", "CardAccount", "Category", "FinancialTransaction", "TransactionAnnotation" })
        {
            // Identificadores vêm exclusivamente da allowlist literal acima, nunca de entrada externa.
            var sql = "SELECT row_to_json(t)::text AS \"Value\" FROM \"" + table + "\" t";
            var data = await context.Database.SqlQueryRaw<string>(sql).ToArrayAsync();
            rows.AddRange(data.Select(row => table + row).Order(StringComparer.Ordinal));
        }
        return rows.ToArray();
    }

    // Os valores são parâmetros, inclusive datas/revogação nula; nenhum input integra o texto SQL.
    private static Task<int> InsertAsync(FinoraDbContext context, string id, Guid userId,
        DateTimeOffset created, DateTimeOffset expires, DateTimeOffset? revoked) =>
        context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AuthSessions\" (\"Id\",\"UserId\",\"CreatedAtUtc\",\"ExpiresAtUtc\",\"RevokedAtUtc\") VALUES ({id},{userId},{created},{expires},{revoked})");

    // Interpreta apenas SQLSTATE estável, sem refletir mensagens do banco ou valores sintéticos nos logs.
    private static async Task AssertSqlStateAsync(string expected, Func<Task<int>> action)
    {
        var exception = await Assert.ThrowsExactlyAsync<PostgresException>(action);
        Assert.AreEqual(expected, exception.SqlState);
    }
}
