using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita somente atualizações de entidades persistidas, sem importar dados ou criar endpoints de aplicação.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialAnnotationUpdateTests
{
    private static readonly Guid UserId = Guid.Parse("f2651ecf-d22e-4d13-961e-21589fb919ef");
    private static readonly Guid ConnectionId = Guid.Parse("e8854a66-3a20-4c71-b10a-4a7887cbf3f1");
    private static readonly Guid CardAccountId = Guid.Parse("a37ab7b8-6831-4ad0-8a62-76674d33b1d2");
    private static readonly Guid TransactionId = Guid.Parse("4ed69034-dce6-4da2-a52a-1589a705b496");
    private static readonly Guid CategoryId = Guid.Parse("797a7f69-c7f7-47d5-bd13-5173ce7e0692");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);
    private static readonly DateTimeOffset InitialUpdatedAtUtc = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private const string NormalizationVersion = "controlled-update-v1";

    // A origem muda na mesma chave interna, enquanto a anotação pessoal continua separada e intacta.
    [TestMethod]
    public async Task SourceUpdatePreservesIdentityAndPersonalAnnotationThenAnnotationEditsLeaveFactsUntouched()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedInitialGraphAsync(fixture.Context);

        var updatedFacts = CreateFacts(
            amount: -7.6543m,
            currency: "USD",
            originalDate: "2026-11-18",
            type: "CREDIT",
            status: "PENDING",
            operationType: "CASHBACK",
            description: "origem controladamente atualizada");

        // O update reaproveita o UUID e o ID externo originais; nenhum registro de domínio é excluído/recriado.
        await using (var updateContext = CreateContext())
        {
            var tracked = await updateContext.Set<FinancialTransaction>()
                .SingleAsync(transaction => transaction.Id == TransactionId);
            var replacement = CreateTransaction(updatedFacts);
            updateContext.Entry(tracked).CurrentValues.SetValues(replacement);
            await updateContext.SaveChangesAsync();
        }

        // Nova leitura comprova que o snapshot externo mudou e a classificação foi recalculada sem tocar a anotação.
        await AssertUpdatedSourceAndOriginalAnnotationAsync(updatedFacts, "observação pessoal original");

        // WithNote altera somente organização pessoal; o movimento já atualizado permanece idêntico.
        await using (var editContext = CreateContext())
        {
            var tracked = await editContext.Set<TransactionAnnotation>()
                .SingleAsync(annotation => annotation.TransactionId == TransactionId);
            var edited = tracked.WithNote("observação pessoal editada", InitialUpdatedAtUtc.AddMinutes(1));
            editContext.Entry(tracked).CurrentValues.SetValues(edited);
            await editContext.SaveChangesAsync();
        }

        await AssertUpdatedSourceAndOriginalAnnotationAsync(updatedFacts, "observação pessoal editada");

        // ClearNote remove o texto, mas conserva categoria/titular e não altera nenhum fato classificado.
        await using (var clearContext = CreateContext())
        {
            var tracked = await clearContext.Set<TransactionAnnotation>()
                .SingleAsync(annotation => annotation.TransactionId == TransactionId);
            var cleared = tracked.ClearNote(InitialUpdatedAtUtc.AddMinutes(2));
            clearContext.Entry(tracked).CurrentValues.SetValues(cleared);
            await clearContext.SaveChangesAsync();
        }

        await AssertUpdatedSourceAndOriginalAnnotationAsync(updatedFacts, expectedNote: null);
    }

    // Semeia o grafo mínimo, categoria pessoal e uma anotação separada do snapshot de origem.
    private static async Task SeedInitialGraphAsync(FinoraDbContext context)
    {
        context.Users.Add(new ApplicationUser(UserId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            "controlled-update-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().Add(CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            "controlled-update-card",
            "BRL"));
        await context.SaveChangesAsync();

        context.Set<Category>().Add(Category.Create(
            CategoryId,
            UserId,
            isSystem: false,
            "Categoria pessoal do titular",
            "categoria-pessoal-titular"));
        context.Set<FinancialTransaction>().Add(CreateTransaction(CreateFacts()));
        await context.SaveChangesAsync();

        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            TransactionId,
            UserId,
            CategoryId,
            UserId,
            "observação pessoal original",
            InitialUpdatedAtUtc));
        await context.SaveChangesAsync();
    }

    // Confere por contexto independente o estado do movimento e da anotação depois de cada etapa de atualização.
    private static async Task AssertUpdatedSourceAndOriginalAnnotationAsync(
        TransactionFacts expectedFacts,
        string? expectedNote)
    {
        await using var context = CreateContext();
        var transactions = await context.Set<FinancialTransaction>()
            .Where(transaction => transaction.Id == TransactionId)
            .ToArrayAsync();
        Assert.AreEqual(1, transactions.Length, "A atualização deve preservar a identidade interna única.");
        var transaction = transactions[0];
        Assert.AreEqual(expectedFacts.ExternalId, transaction.ExternalId);
        Assert.AreEqual(expectedFacts.AccountId, transaction.ExternalAccountId);
        Assert.AreEqual(expectedFacts.Description, transaction.Description);
        Assert.AreEqual(expectedFacts.Amount, transaction.Amount);
        Assert.AreEqual(expectedFacts.CurrencyCode, transaction.CurrencyCode);
        Assert.AreEqual(expectedFacts.OriginalDate, transaction.OriginalDate);
        Assert.AreEqual(expectedFacts.Type, transaction.Type);
        Assert.AreEqual(expectedFacts.Status, transaction.Status);
        Assert.AreEqual(expectedFacts.OperationType, transaction.OperationType);
        Assert.AreEqual(new DateOnly(2026, 11, 18), transaction.TransactionDate);
        Assert.AreEqual(TransactionNature.Adjustment, transaction.Nature);
        Assert.IsNull(transaction.NatureReason);
        Assert.AreEqual(TransactionSituation.Pending, transaction.Situation);
        Assert.AreEqual(TransactionExclusionReason.NotEffective, transaction.SituationReason);
        Assert.IsNull(transaction.DateReason);
        Assert.AreEqual(ReferenceDate, transaction.NormalizationReferenceDate);
        Assert.AreEqual(NormalizationVersion, transaction.NormalizationRuleVersion);

        var annotation = await context.Set<TransactionAnnotation>()
            .SingleAsync(value => value.TransactionId == TransactionId);
        Assert.AreEqual(UserId, annotation.UserId);
        Assert.AreEqual(CategoryId, annotation.CategoryId);
        Assert.AreEqual(UserId, annotation.CategoryScopeId);
        Assert.AreEqual(expectedNote, annotation.Note);
    }

    // A criação aceita a mesma identidade para atualizar a linha e recalcula os campos interpretados da origem recebida.
    private static FinancialTransaction CreateTransaction(TransactionFacts facts)
    {
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);
        return FinancialTransaction.Create(
            TransactionId,
            UserId,
            CardAccountId,
            normalization,
            NormalizationVersion,
            ReferenceDate);
    }

    // Valores iniciais e atualizados têm o mesmo ID externo; somente os fatos de origem especificados mudam.
    private static TransactionFacts CreateFacts(
        decimal amount = 12.3456m,
        string currency = "BRL",
        string originalDate = "2026-11-17",
        string type = "DEBIT",
        string status = "POSTED",
        string operationType = "TARIFA",
        string description = "origem inicialmente persistida") => new(
            "stable-external-transaction",
            "controlled-update-card",
            description,
            amount,
            currency,
            originalDate,
            type,
            status,
            operationType);

    // A factory usa somente a connection string dedicada já validada contra host e database autorizados.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, settings.ConnectionString);
        return new FinoraDbContext(optionsBuilder.Options);
    }
}
