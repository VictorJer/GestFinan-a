using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Os cenários limpam o schema descartável e usam gravações concorrentes com contextos EF independentes.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialUniquenessTests
{
    private static readonly Guid UserOneId = Guid.Parse("3654cb83-dfef-47a6-8808-685e24b0ed2e");
    private static readonly Guid UserTwoId = Guid.Parse("fb48f5fd-cc09-4ca0-a9b6-8766068575dd");
    private static readonly Guid ConnectionOneId = Guid.Parse("0b38d58a-b530-4dbb-9dc4-f0b0452bac35");
    private static readonly Guid ConnectionTwoId = Guid.Parse("b403a3ad-c245-438f-a874-abfd3874dbd0");
    private static readonly Guid CardOneId = Guid.Parse("c0d149e7-81c8-4326-a29b-320595c87fd6");
    private static readonly Guid CardTwoId = Guid.Parse("6de818e8-7d84-4851-9d5b-ce720ec83a83");
    private static readonly Guid TransactionOneId = Guid.Parse("237d6527-46c9-459a-9956-e889b151500c");
    private static readonly Guid TransactionTwoId = Guid.Parse("90a741f5-f0cd-471c-8de0-2c6ad672bbd3");
    private static readonly Guid TransactionForConcurrentAnnotationId = Guid.Parse("05ba70e7-5bb1-49ac-8b86-a82426f48b47");
    private static readonly Guid UserOneCategoryId = Guid.Parse("e89a2ad8-7d2e-40ad-8621-499775f45290");
    private static readonly Guid UserTwoCategoryId = Guid.Parse("736497fb-bda1-4799-8ed7-c016cc88aafd");
    private static readonly Guid SystemCategoryId = Guid.Parse("77977474-2cdf-4d42-b51d-41d31e812f6e");
    private static readonly Guid AnnotationId = Guid.Parse("77d196e2-6e9f-4880-905e-d3ab61136d39");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private const string SharedTransactionExternalId = "same-external-transaction";
    private const string SharedCategoryNormalizedName = "same-normalized-name";

    // Exercita conflitos sequenciais e garante que identidade externa não seja inferida pelo conteúdo semelhante.
    [TestMethod]
    public async Task PostgreSqlEnforcesSequentialUniquenessWithoutMergingDistinctExternalIds()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedValidGraphAsync(fixture.Context);

        // A mesma chave externa não pode produzir outra conexão, cartão ou movimento com UUID interno diferente.
        await AssertSequentialUniqueViolationAsync(context =>
            context.Set<FinancialConnection>().Add(FinancialConnection.Create(
                Guid.NewGuid(), UserOneId, "pluggy", "item-primary")));
        await AssertSequentialUniqueViolationAsync(context =>
            context.Set<CardAccount>().Add(CardAccount.Create(
                Guid.NewGuid(), UserOneId, ConnectionOneId, "card-primary", "BRL")));
        await AssertSequentialUniqueViolationAsync(context =>
            context.Set<FinancialTransaction>().Add(CreateTransaction(
                Guid.NewGuid(), UserOneId, CardOneId, "card-primary", SharedTransactionExternalId)));
        await AssertSequentialUniqueViolationAsync(context =>
            context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
                TransactionOneId, UserOneId, null, null, "segunda anotação", CreatedAtUtc.AddMinutes(1))));
        await AssertSequentialUniqueViolationAsync(context =>
            context.Set<Category>().Add(Category.Create(
                Guid.NewGuid(),
                UserOneId,
                isSystem: false,
                "Nome repetido no escopo",
                SharedCategoryNormalizedName)));

        // Variações de caixa e espaços são preservadas como IDs opacos distintos, sem trim ou comparação insensível.
        var connectionIds = new[] { "OpaqueItem", "opaqueitem", " OpaqueItem " };
        var cardExternalIds = new[] { "OpaqueCard", "opaquecard", " OpaqueCard " };
        var transactionExternalIds = new[] { "OpaqueTransaction", "opaquetransaction", " OpaqueTransaction " };
        var transactionIds = transactionExternalIds.Select(_ => Guid.NewGuid()).ToArray();

        await using (var context = CreateContext())
        {
            context.Set<FinancialConnection>().AddRange(connectionIds.Select(externalId =>
                FinancialConnection.Create(Guid.NewGuid(), UserOneId, "pluggy", externalId)));
            await context.SaveChangesAsync();

            context.Set<CardAccount>().AddRange(cardExternalIds.Select(externalId =>
                CardAccount.Create(Guid.NewGuid(), UserOneId, ConnectionOneId, externalId, "BRL")));
            await context.SaveChangesAsync();

            context.Set<FinancialTransaction>().AddRange(transactionExternalIds.Select((externalId, index) =>
                CreateTransaction(transactionIds[index], UserOneId, CardOneId, "card-primary", externalId)));
            await context.SaveChangesAsync();
        }

        // Categoria de mesmo nome normalizado pode existir em escopos distintos, e o mesmo movimento externo em cartões distintos.
        await using (var context = CreateContext())
        {
            Assert.AreEqual(3, await context.Set<Category>().CountAsync(category =>
                category.NormalizedName == SharedCategoryNormalizedName));
            Assert.AreEqual(2, await context.Set<FinancialTransaction>().CountAsync(transaction =>
                transaction.ExternalId == SharedTransactionExternalId));

            var connections = await context.Set<FinancialConnection>()
                .Where(connection => connection.ExternalItemId == "OpaqueItem"
                    || connection.ExternalItemId == "opaqueitem"
                    || connection.ExternalItemId == " OpaqueItem ")
                .Select(connection => connection.ExternalItemId)
                .ToArrayAsync();
            CollectionAssert.AreEquivalent(connectionIds, connections);

            var cards = await context.Set<CardAccount>()
                .Where(account => account.ExternalId == "OpaqueCard"
                    || account.ExternalId == "opaquecard"
                    || account.ExternalId == " OpaqueCard ")
                .Select(account => account.ExternalId)
                .ToArrayAsync();
            CollectionAssert.AreEquivalent(cardExternalIds, cards);

            var transactions = await context.Set<FinancialTransaction>()
                .Where(transaction => transactionIds.Contains(transaction.Id))
                .OrderBy(transaction => transaction.Id)
                .ToArrayAsync();
            Assert.AreEqual(3, transactions.Length);
            CollectionAssert.AreEquivalent(transactionExternalIds, transactions.Select(transaction => transaction.ExternalId).ToArray());
            Assert.AreEqual(1, transactions.Select(transaction => transaction.Description).Distinct().Count());
            Assert.AreEqual(1, transactions.Select(transaction => transaction.Amount).Distinct().Count());
            Assert.AreEqual(1, transactions.Select(transaction => transaction.OriginalDate).Distinct().Count());

            var sameExternalIdAcrossCards = await context.Set<FinancialTransaction>()
                .Where(transaction => transaction.ExternalId == SharedTransactionExternalId)
                .OrderBy(transaction => transaction.CardAccountId)
                .ToArrayAsync();
            Assert.AreEqual(2, sameExternalIdAcrossCards.Length);
            Assert.AreNotEqual(sameExternalIdAcrossCards[0].Id, sameExternalIdAcrossCards[1].Id);
            CollectionAssert.AreEquivalent(
                new[] { CardOneId, CardTwoId },
                sameExternalIdAcrossCards.Select(transaction => transaction.CardAccountId).ToArray());
        }

        // Contexto novo confirma que cada conflito deixou exatamente a linha original para sua chave única.
        await using (var context = CreateContext())
        {
            Assert.AreEqual(1, await context.Set<FinancialConnection>().CountAsync(connection =>
                connection.UserId == UserOneId
                && connection.Provider == "pluggy"
                && connection.ExternalItemId == "item-primary"));
            Assert.AreEqual(1, await context.Set<CardAccount>().CountAsync(account =>
                account.ConnectionId == ConnectionOneId && account.ExternalId == "card-primary"));
            Assert.AreEqual(1, await context.Set<FinancialTransaction>().CountAsync(transaction =>
                transaction.CardAccountId == CardOneId && transaction.ExternalId == SharedTransactionExternalId));
            Assert.AreEqual(1, await context.Set<TransactionAnnotation>().CountAsync(annotation =>
                annotation.TransactionId == TransactionOneId));
            Assert.AreEqual(1, await context.Set<Category>().CountAsync(category =>
                category.ScopeId == UserOneId && category.NormalizedName == SharedCategoryNormalizedName));
        }
    }

    // Corridas iniciadas após barreira tentam a mesma chave em contextos independentes e deixam um único vencedor.
    [TestMethod]
    public async Task PostgreSqlEnforcesConcurrentUniquenessForAllFinancialKeys()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedValidGraphAsync(fixture.Context);

        await Task.WhenAll(
            AssertOneConcurrentWinnerAsync(
                (context, id) => context.Set<FinancialConnection>().Add(
                    FinancialConnection.Create(id, UserOneId, "pluggy", "race-item")),
                context => context.Set<FinancialConnection>().CountAsync(connection =>
                    connection.UserId == UserOneId
                    && connection.Provider == "pluggy"
                    && connection.ExternalItemId == "race-item")),
            AssertOneConcurrentWinnerAsync(
                (context, id) => context.Set<CardAccount>().Add(
                    CardAccount.Create(id, UserOneId, ConnectionOneId, "race-card", "BRL")),
                context => context.Set<CardAccount>().CountAsync(account =>
                    account.ConnectionId == ConnectionOneId && account.ExternalId == "race-card")),
            AssertOneConcurrentWinnerAsync(
                (context, id) => context.Set<FinancialTransaction>().Add(
                    CreateTransaction(id, UserOneId, CardOneId, "card-primary", "race-transaction")),
                context => context.Set<FinancialTransaction>().CountAsync(transaction =>
                    transaction.CardAccountId == CardOneId && transaction.ExternalId == "race-transaction")),
            AssertOneConcurrentWinnerAsync(
                (context, _) => context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
                    TransactionForConcurrentAnnotationId,
                    UserOneId,
                    null,
                    null,
                    "anotação concorrente",
                    CreatedAtUtc)),
                context => context.Set<TransactionAnnotation>().CountAsync(annotation =>
                    annotation.TransactionId == TransactionForConcurrentAnnotationId)),
            AssertOneConcurrentWinnerAsync(
                (context, id) => context.Set<Category>().Add(Category.Create(
                    id,
                    UserOneId,
                    isSystem: false,
                    "Categoria concorrente",
                    "race-normalized-name")),
                context => context.Set<Category>().CountAsync(category =>
                    category.ScopeId == UserOneId && category.NormalizedName == "race-normalized-name")));
    }

    // Pais mínimos são gravados em etapas para que cada teste de unicidade alcance o índice que está exercitando.
    private static async Task SeedValidGraphAsync(FinoraDbContext context)
    {
        context.Users.AddRange(new ApplicationUser(UserOneId), new ApplicationUser(UserTwoId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(ConnectionOneId, UserOneId, "pluggy", "item-primary"),
            FinancialConnection.Create(ConnectionTwoId, UserOneId, "pluggy", "item-secondary"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().AddRange(
            CardAccount.Create(CardOneId, UserOneId, ConnectionOneId, "card-primary", "BRL"),
            CardAccount.Create(CardTwoId, UserOneId, ConnectionOneId, "card-secondary", "USD"));
        await context.SaveChangesAsync();

        context.Set<FinancialTransaction>().AddRange(
            CreateTransaction(TransactionOneId, UserOneId, CardOneId, "card-primary", SharedTransactionExternalId),
            CreateTransaction(TransactionTwoId, UserOneId, CardTwoId, "card-secondary", SharedTransactionExternalId),
            CreateTransaction(
                TransactionForConcurrentAnnotationId,
                UserOneId,
                CardTwoId,
                "card-secondary",
                "transaction-without-annotation"));
        await context.SaveChangesAsync();

        context.Set<Category>().AddRange(
            Category.Create(
                UserOneCategoryId,
                UserOneId,
                isSystem: false,
                "Categoria do titular um",
                SharedCategoryNormalizedName),
            Category.Create(
                UserTwoCategoryId,
                UserTwoId,
                isSystem: false,
                "Categoria do titular dois",
                SharedCategoryNormalizedName),
            Category.Create(
                SystemCategoryId,
                null,
                isSystem: true,
                "Categoria do sistema",
                SharedCategoryNormalizedName));
        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            TransactionOneId,
            UserOneId,
            UserOneCategoryId,
            UserOneId,
            "anotação inicial",
            CreatedAtUtc));
        await context.SaveChangesAsync();
    }

    // O helper exige SQLSTATE de unicidade real; conflitos de FK/check ou erro de conexão não contam como sucesso.
    private static async Task AssertSequentialUniqueViolationAsync(Action<FinoraDbContext> addEntity)
    {
        await using var context = CreateContext();
        addEntity(context);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = FindPostgresException(exception);

        Assert.IsNotNull(postgresException, exception.ToString());
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, postgresException!.SqlState);
    }

    // Ambos os contextos só enviam INSERT depois da barreira, garantindo disputa simultânea pelo mesmo índice único.
    private static async Task AssertOneConcurrentWinnerAsync(
        Action<FinoraDbContext, Guid> addEntity,
        Func<FinoraDbContext, Task<int>> countMatching)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = TryInsertConcurrentlyAsync(Guid.NewGuid(), addEntity, ready);
        var second = TryInsertConcurrentlyAsync(Guid.NewGuid(), addEntity, ready);
        var results = await Task.WhenAll(first, second);

        Assert.AreEqual(1, results.Count(inserted => inserted), "Uma gravação concorrente deve vencer o índice único.");
        await using var verificationContext = CreateContext();
        Assert.AreEqual(1, await countMatching(verificationContext));
    }

    // Cada tentativa possui contexto/conexão EF próprios e classifica como perdedor somente a violação 23505.
    private static async Task<bool> TryInsertConcurrentlyAsync(
        Guid id,
        Action<FinoraDbContext, Guid> addEntity,
        TaskCompletionSource ready)
    {
        await using var context = CreateContext();
        addEntity(context, id);
        ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));

        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exception)
        {
            var postgresException = FindPostgresException(exception);
            Assert.IsNotNull(postgresException, exception.ToString());
            Assert.AreEqual(PostgresErrorCodes.UniqueViolation, postgresException!.SqlState);
            return false;
        }
    }

    // Resolve a exceção do provider sem confundir o wrapper DbUpdateException com o código SQLSTATE esperado.
    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }

    // Reutiliza a mesma configuração de destino dedicada e validada antes de qualquer conexão PostgreSQL.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, settings.ConnectionString);
        return new FinoraDbContext(optionsBuilder.Options);
    }

    // Os dados sintéticos conservam descrição, valor e data idênticos para demonstrar que IDs distintos não se fundem.
    private static FinancialTransaction CreateTransaction(
        Guid id,
        Guid userId,
        Guid cardAccountId,
        string externalAccountId,
        string externalId)
    {
        var facts = new TransactionFacts(
            externalId,
            externalAccountId,
            "conteúdo sintético coincidente",
            12.3456m,
            "BRL",
            "2026-11-17",
            "DEBIT",
            "POSTED",
            "TARIFA");
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);

        return FinancialTransaction.Create(
            id,
            userId,
            cardAccountId,
            normalization,
            "uniqueness-test-v1",
            ReferenceDate);
    }
}
