using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Api.Tests;

// Exercita identidade/upsert e anotação em PostgreSQL exclusivamente no banco descartável validado.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialTransactionImportIdentityTests
{
    private static readonly Guid UserId = Guid.Parse("042017e8-81c3-46d3-9970-c952f50be3e6");
    private static readonly Guid ConnectionId = Guid.Parse("f19086fc-87e4-42bd-9a9b-7d5cc0d5944d");
    private static readonly Guid CardId = Guid.Parse("3842a32e-bbb4-4514-921b-d29a7043af7e");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 20);
    private static readonly DateTimeOffset UpdatedAtUtc = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    // Repetição é idempotente, IDs diferentes não colidem e fatos mutáveis preservam UUID/anotação sem excluir ausentes.
    [TestMethod]
    public async Task RepeatedImportUpdatesSourceFactsAndPreservesIdentityAnnotationAndMissingRows()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedConnectionAsync(fixture.Context);

        var originalOne = Facts("external-one", "2026-11-18", 15.25m, "POSTED", "TARIFA", "DEBIT");
        var originalTwo = Facts("external-two", "2026-11-18", 15.25m, "POSTED", "TARIFA", "DEBIT");
        await ApplySnapshotAsync([originalOne, originalTwo]);
        await ApplySnapshotAsync([originalOne, originalTwo]);

        Guid firstInternalId;
        await using (var annotate = CreateContext())
        {
            var transaction = await annotate.Set<FinancialTransaction>()
                .SingleAsync(value => value.ExternalId == originalOne.ExternalId);
            firstInternalId = transaction.Id;
            annotate.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
                transaction.Id, UserId, null, null, "anotação pessoal", UpdatedAtUtc));
            await annotate.SaveChangesAsync();
        }

        var pending = Facts("external-one", "2026-11-19", 17.50m, "PENDING", "TARIFA", "DEBIT");
        await ApplySnapshotAsync([pending]);
        var refund = Facts("external-one", "2026-11-19", -17.50m, "POSTED", "ESTORNO", "CREDIT");
        await ApplySnapshotAsync([refund]);

        await using var verify = CreateContext();
        var transactions = await verify.Set<FinancialTransaction>().OrderBy(value => value.ExternalId).ToArrayAsync();
        Assert.AreEqual(2, transactions.Length);
        Assert.AreEqual(2, transactions.Select(value => value.ExternalId).Distinct(StringComparer.Ordinal).Count());
        var updated = transactions.Single(value => value.ExternalId == "external-one");
        Assert.AreEqual(firstInternalId, updated.Id);
        Assert.AreEqual(refund.Amount, updated.Amount);
        Assert.AreEqual(refund.Status, updated.Status);
        Assert.AreEqual(TransactionNature.Refund, updated.Nature);
        Assert.AreEqual(TransactionSituation.Effective, updated.Situation);
        Assert.IsTrue(transactions.Any(value => value.ExternalId == "external-two"),
            "A linha ausente da coleta posterior não deve ser excluída.");
        var annotation = await verify.Set<TransactionAnnotation>().SingleAsync(value => value.TransactionId == firstInternalId);
        Assert.AreEqual("anotação pessoal", annotation.Note);
    }

    // Cria vínculo titular/conexão/cartão antes de qualquer snapshot importado.
    private static async Task SeedConnectionAsync(FinoraDbContext context)
    {
        context.Users.Add(new ApplicationUser(UserId));
        await context.SaveChangesAsync();
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId, UserId, "pluggy", "synthetic-item"));
        await context.SaveChangesAsync();
        context.Set<CardAccount>().Add(CardAccount.Create(
            CardId, UserId, ConnectionId, "synthetic-account", "BRL"));
        await context.SaveChangesAsync();
    }

    // Aplica o conjunto recebido por chave externa contextual, sem apagar o que não veio nesta leitura.
    private static async Task ApplySnapshotAsync(IReadOnlyList<TransactionFacts> facts)
    {
        await using var context = CreateContext();
        foreach (var source in facts)
        {
            var existing = await context.Set<FinancialTransaction>().SingleOrDefaultAsync(value =>
                value.CardAccountId == CardId && value.ExternalId == source.ExternalId);
            var normalization = TransactionNormalizer.Normalize(source, ReferenceDate, positivePurchaseRuleConfirmed: false);
            if (existing is null)
            {
                context.Set<FinancialTransaction>().Add(FinancialTransaction.Create(
                    Guid.NewGuid(), UserId, CardId, normalization, "transaction-normalizer-v1", ReferenceDate));
            }
            else
            {
                existing.UpdateSourceFacts(normalization, "transaction-normalizer-v1", ReferenceDate);
            }
        }

        await context.SaveChangesAsync();
    }

    // Abre contexto somente pela connection string dedicada, revalidada antes da conexão.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, settings.ConnectionString);
        return new FinoraDbContext(options.Options);
    }

    // Usa conteúdo igual com identidades externas distintas para provar que deduplicação não usa valor/data/descrição.
    private static TransactionFacts Facts(
        string id,
        string date,
        decimal amount,
        string status,
        string operation,
        string type) => new(
            id, "synthetic-account", "descrição sintética igual", amount, "BRL", date,
            type, status, operation);
}
