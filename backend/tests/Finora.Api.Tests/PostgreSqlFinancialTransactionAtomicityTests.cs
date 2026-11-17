using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// A prova cobre somente atomicidade relacional de uma transação curta, sem chamadas externas ou sincronização ponta a ponta.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialTransactionAtomicityTests
{
    private static readonly Guid UserId = Guid.Parse("588bfa52-d564-46ad-82b0-255130df9183");
    private static readonly Guid ConnectionId = Guid.Parse("8ce11dbd-d073-480e-a9dc-4b5459fce449");
    private static readonly Guid CardAccountId = Guid.Parse("8af130dd-6e0d-482b-8c83-381e79e42983");
    private static readonly Guid ExistingTransactionId = Guid.Parse("56d4019d-a992-4104-8071-c871cf9f4917");
    private static readonly Guid ExistingCategoryId = Guid.Parse("277bf577-90b2-4ead-a608-c9d694c9039e");
    private static readonly Guid ExistingSyncRunId = Guid.Parse("6a1d30d9-1b79-4e8d-88de-6a55ecbe4710");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    // Uma primeira gravação bem-sucedida dentro da transação deve desaparecer junto do SyncRun quando a anotação falha.
    [TestMethod]
    public async Task RollbackRemovesEarlierWritesAndPreservesPreviouslyCommittedFinancialHistory()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedPreviouslyCommittedGraphAsync(fixture.Context);

        var attemptedTransactionId = Guid.NewGuid();
        var attemptedSyncRunId = Guid.NewGuid();
        var failedAnnotationId = attemptedTransactionId;
        var attemptedFacts = CreateFacts(
            "atomicity-new-transaction",
            "gravação que deverá ser revertida",
            42.1234m);

        // A primeira etapa persiste movimento e registro de sucesso somente dentro da transação explícita não confirmada.
        await using (var operationContext = CreateContext())
        await using (var transaction = await operationContext.Database.BeginTransactionAsync())
        {
            operationContext.Set<FinancialTransaction>().Add(CreateTransaction(attemptedTransactionId, attemptedFacts));
            operationContext.Set<SyncRun>().Add(CreateSucceededSyncRun(attemptedSyncRunId));
            await operationContext.SaveChangesAsync();

            Assert.AreEqual(1, await operationContext.Set<FinancialTransaction>()
                .CountAsync(value => value.Id == attemptedTransactionId));
            Assert.AreEqual(1, await operationContext.Set<SyncRun>()
                .CountAsync(value => value.Id == attemptedSyncRunId && value.State == SyncRunState.Succeeded));

            // Categoria inexistente provoca erro PostgreSQL posterior à gravação inicial, sem confundir validação local.
            operationContext.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
                failedAnnotationId,
                UserId,
                Guid.NewGuid(),
                UserId,
                "anotação inválida antes do rollback",
                CreatedAtUtc.AddMinutes(3)));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => operationContext.SaveChangesAsync());
            var postgresException = FindPostgresException(exception);
            Assert.IsNotNull(postgresException, exception.ToString());
            Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, postgresException!.SqlState);

            await transaction.RollbackAsync();
        }

        // Contexto independente prova rollback integral e preservação dos registros que já estavam confirmados.
        await using var verificationContext = CreateContext();
        Assert.AreEqual(0, await verificationContext.Set<FinancialTransaction>()
            .CountAsync(value => value.Id == attemptedTransactionId));
        Assert.AreEqual(0, await verificationContext.Set<TransactionAnnotation>()
            .CountAsync(value => value.TransactionId == failedAnnotationId));
        Assert.AreEqual(0, await verificationContext.Set<SyncRun>()
            .CountAsync(value => value.Id == attemptedSyncRunId));

        var existingTransaction = await verificationContext.Set<FinancialTransaction>()
            .SingleAsync(value => value.Id == ExistingTransactionId);
        Assert.AreEqual("existing-transaction", existingTransaction.ExternalId);
        Assert.AreEqual("movimento anteriormente confirmado", existingTransaction.Description);
        Assert.AreEqual(9.8765m, existingTransaction.Amount);
        Assert.AreEqual(TransactionNature.Fee, existingTransaction.Nature);
        Assert.AreEqual(new DateOnly(2026, 11, 17), existingTransaction.TransactionDate);
        Assert.AreEqual(1, await verificationContext.Set<Category>()
            .CountAsync(value => value.Id == ExistingCategoryId));
        Assert.AreEqual(1, await verificationContext.Set<TransactionAnnotation>()
            .CountAsync(value => value.TransactionId == ExistingTransactionId));
        var existingAnnotation = await verificationContext.Set<TransactionAnnotation>()
            .SingleAsync(value => value.TransactionId == ExistingTransactionId);
        Assert.AreEqual(ExistingCategoryId, existingAnnotation.CategoryId);
        Assert.AreEqual("anotação previamente confirmada", existingAnnotation.Note);

        var existingRun = await verificationContext.Set<SyncRun>()
            .SingleAsync(value => value.Id == ExistingSyncRunId);
        Assert.AreEqual(SyncRunState.Succeeded, existingRun.State);
        Assert.IsNull(await verificationContext.Set<FinancialConnection>()
            .Where(value => value.Id == ConnectionId)
            .Select(value => value.LastSuccessfulImportAtUtc)
            .SingleAsync());
    }

    // Os pais e histórico-base são confirmados antes da operação para separar rollback de perda de dados anteriores.
    private static async Task SeedPreviouslyCommittedGraphAsync(FinoraDbContext context)
    {
        context.Users.Add(new ApplicationUser(UserId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            "atomicity-test-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().Add(CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            "atomicity-test-card",
            "BRL"));
        await context.SaveChangesAsync();

        context.Set<Category>().Add(Category.Create(
            ExistingCategoryId,
            UserId,
            isSystem: false,
            "Categoria previamente confirmada",
            "categoria-atomicidade-confirmada"));
        context.Set<FinancialTransaction>().Add(CreateTransaction(
            ExistingTransactionId,
            CreateFacts("existing-transaction", "movimento anteriormente confirmado", 9.8765m)));
        context.Set<SyncRun>().Add(CreateSucceededSyncRun(ExistingSyncRunId));
        await context.SaveChangesAsync();

        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            ExistingTransactionId,
            UserId,
            ExistingCategoryId,
            UserId,
            "anotação previamente confirmada",
            CreatedAtUtc.AddMinutes(1)));
        await context.SaveChangesAsync();
    }

    // Valores de sucesso são puramente sintéticos e não atualizam automaticamente a conexão de origem.
    private static SyncRun CreateSucceededSyncRun(Guid id) => SyncRun.Create(
        id,
        UserId,
        ConnectionId,
        CreatedAtUtc,
        new DateOnly(2026, 11, 1),
        requestedToExclusive: new DateOnly(2026, 12, 1),
        startedAtUtc: CreatedAtUtc.AddMinutes(1),
        finishedAtUtc: CreatedAtUtc.AddMinutes(2),
        state: SyncRunState.Succeeded,
        pagesRead: 1,
        receivedCount: 1,
        uniqueCount: 1,
        persistedCount: 1,
        observedFrom: new DateOnly(2026, 11, 1),
        observedTo: ReferenceDate,
        coverageState: SyncCoverageState.CompleteForRequestedWindow);

    // O movimento segue as regras puras existentes e referencia o cartão e conta externa já semeados.
    private static FinancialTransaction CreateTransaction(Guid id, TransactionFacts facts)
    {
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);
        return FinancialTransaction.Create(
            id,
            UserId,
            CardAccountId,
            normalization,
            "atomicity-test-v1",
            ReferenceDate);
    }

    // Fatos artificiais mantêm o cenário independente de provedor, agregação e classificação de compra.
    private static TransactionFacts CreateFacts(string externalId, string description, decimal amount) => new(
        externalId,
        "atomicity-test-card",
        description,
        amount,
        "BRL",
        "2026-11-17",
        "DEBIT",
        "POSTED",
        "TARIFA");

    // Localiza o erro original do PostgreSQL dentro do wrapper de persistência do EF Core.
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

    // Cada contexto respeita a configuração dedicada que limita o alvo a loopback e ao database de integração.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, settings.ConnectionString);
        return new FinoraDbContext(optionsBuilder.Options);
    }
}
