using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Este teste compartilha o schema descartável com outros testes PostgreSQL e executa toda a matriz em série.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlFinancialIntegrityTests
{
    private static readonly Guid UserOneId = Guid.Parse("3c472e18-2c28-4bf1-90dc-f60c0e97ac12");
    private static readonly Guid UserTwoId = Guid.Parse("9c2b9477-4fae-4c6d-95cf-41a98c811f0f");
    private static readonly Guid MissingUserId = Guid.Parse("63a31210-7982-4e6a-95f7-33e0a6d0fb09");
    private static readonly Guid ConnectionOneId = Guid.Parse("a63e12ba-9921-436c-96f1-2967dc4ab374");
    private static readonly Guid ConnectionTwoId = Guid.Parse("c7dc81d1-b6e8-42bb-b18e-f0e1efb91fb2");
    private static readonly Guid CardOneId = Guid.Parse("a42cf987-2b5c-4768-9b61-1fbb8d5b6096");
    private static readonly Guid CardTwoId = Guid.Parse("736cd454-5391-4a80-81df-a5a92ef703ad");
    private static readonly Guid TransactionOneId = Guid.Parse("8de2784b-e0fb-436d-956b-14c748a057ed");
    private static readonly Guid TransactionTwoId = Guid.Parse("d36f9cd9-2442-43b0-bd67-905e33c3612a");
    private static readonly Guid TransactionThreeId = Guid.Parse("e42b9e4a-50e4-41a9-8b18-1e21ef76aa92");
    private static readonly Guid TransactionFourId = Guid.Parse("f273b4c7-01b1-4faa-889c-faa7ed54d587");
    private static readonly Guid SyncRunId = Guid.Parse("7df73e47-8f6e-441e-8900-1f2e71e0a4a6");
    private static readonly Guid SystemCategoryId = Guid.Parse("3091c1fb-b5fa-4c28-98ca-224d0bd0c27d");
    private static readonly Guid PersonalCategoryOneId = Guid.Parse("78039977-c6e4-4e1e-b610-a69a76ce7463");
    private static readonly Guid PersonalCategoryTwoId = Guid.Parse("98739e66-5248-46be-866f-7af1041b3ccd");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    // Verifica o grafo permitido, rejeições por EF/SQL direto e restrições de exclusão no PostgreSQL real descartável.
    [TestMethod]
    public async Task PostgreSqlEnforcesOwnershipCategoryAndReferencedParentIntegrity()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        // A prova usa SyncRun do modelo atual; a cadeia completa cria suas colunas no schema descartável.
        await fixture.Context.Database.MigrateAsync();
        await SeedValidGraphAsync(fixture.Context);
        await AssertValidCategoryScopesAsync();

        // As fábricas aceitam referências externas ao grafo; o SaveChanges precisa receber a rejeição do PostgreSQL.
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<FinancialConnection>().Add(FinancialConnection.Create(
                Guid.NewGuid(), MissingUserId, "pluggy", "missing-user-item")));
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<CardAccount>().Add(CardAccount.Create(
                Guid.NewGuid(), UserOneId, Guid.NewGuid(), "missing-connection-card", "BRL")));
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<CardAccount>().Add(CardAccount.Create(
                Guid.NewGuid(), UserTwoId, ConnectionOneId, "cross-owner-card", "BRL")));
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<SyncRun>().Add(CreateSyncRun(Guid.NewGuid(), UserTwoId, ConnectionOneId)));
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<FinancialTransaction>().Add(CreateTransaction(
                Guid.NewGuid(), UserOneId, CardOneId, "ef-wrong-account", "different-external-card")));
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
                Guid.NewGuid(), UserOneId, null, null, null, CreatedAtUtc)));
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
                TransactionFourId, UserTwoId, null, null, null, CreatedAtUtc)));
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
                TransactionFourId,
                UserOneId,
                PersonalCategoryTwoId,
                UserOneId,
                "categoria pessoal de outro titular",
                CreatedAtUtc)));
        await AssertEntityForeignKeyViolationAsync(context =>
            context.Set<Category>().Add(Category.Create(
                Guid.NewGuid(), MissingUserId, isSystem: false, "titular ausente", "titular-ausente")));

        // SQL parametrizado contorna as fábricas para provar as FKs e CHECKs diretamente no banco.
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FinancialConnection" ("Id", "UserId", "Provider", "ExternalItemId")
                VALUES ({Guid.NewGuid()}, {MissingUserId}, 'pluggy', {"raw-missing-user-item"});
                """));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Category" ("Id", "UserId", "IsSystem", "Name", "NormalizedName", "ScopeId")
                VALUES ({Guid.NewGuid()}, {MissingUserId}, FALSE, 'titular ausente', 'raw-titular-ausente', {MissingUserId});
                """));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.CheckViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Category" ("Id", "UserId", "IsSystem", "Name", "NormalizedName", "ScopeId")
                VALUES ({Guid.NewGuid()}, NULL, TRUE, 'sistema incoerente', 'raw-sistema-incoerente', {UserOneId});
                """));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.CheckViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Category" ("Id", "UserId", "IsSystem", "Name", "NormalizedName", "ScopeId")
                VALUES ({Guid.NewGuid()}, {UserOneId}, FALSE, 'pessoal incoerente', 'raw-pessoal-incoerente', {UserTwoId});
                """));

        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "CardAccount" ("Id", "UserId", "ConnectionId", "ExternalId", "CurrencyCode")
                VALUES ({Guid.NewGuid()}, {UserOneId}, {Guid.NewGuid()}, 'raw-missing-connection-card', 'BRL');
                """));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "CardAccount" ("Id", "UserId", "ConnectionId", "ExternalId", "CurrencyCode")
                VALUES ({Guid.NewGuid()}, {UserTwoId}, {ConnectionOneId}, 'raw-cross-owner-card', 'BRL');
                """));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "SyncRun" ("Id", "UserId", "ConnectionId", "CreatedAtUtc", "RequestedFrom", "State", "CoverageState")
                VALUES ({Guid.NewGuid()}, {UserOneId}, {Guid.NewGuid()}, {CreatedAtUtc}, {ReferenceDate}, 'Pending', 'Unknown');
                """));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "SyncRun" ("Id", "UserId", "ConnectionId", "CreatedAtUtc", "RequestedFrom", "State", "CoverageState")
                VALUES ({Guid.NewGuid()}, {UserTwoId}, {ConnectionOneId}, {CreatedAtUtc}, {ReferenceDate}, 'Pending', 'Unknown');
                """));

        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildTransactionInsert(
                Guid.NewGuid(), UserOneId, Guid.NewGuid(), "raw-missing-card", "missing-card")));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildTransactionInsert(
                Guid.NewGuid(), UserTwoId, CardOneId, "raw-cross-owner-transaction", "external-card-one")));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildTransactionInsert(
                Guid.NewGuid(), UserOneId, CardOneId, "raw-wrong-account-transaction", "different-external-card")));

        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildAnnotationInsert(
                Guid.NewGuid(), UserOneId, null, null)));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildAnnotationInsert(
                TransactionFourId, UserTwoId, null, null)));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildAnnotationInsert(
                TransactionFourId, UserOneId, Guid.NewGuid(), UserOneId)));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.ForeignKeyViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildAnnotationInsert(
                TransactionFourId, UserOneId, PersonalCategoryTwoId, UserOneId)));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.CheckViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildAnnotationInsert(
                TransactionFourId, UserOneId, PersonalCategoryTwoId, UserTwoId)));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.CheckViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildAnnotationInsert(
                TransactionFourId, UserOneId, null, UserOneId)));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.CheckViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync(BuildAnnotationInsert(
                TransactionFourId, UserOneId, SystemCategoryId, null)));

        // SQL também tenta criar o UUID reservado como usuário real; a CHECK do Identity deve rejeitá-lo.
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.CheckViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AspNetUsers" ("Id", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES ({Guid.Empty}, FALSE, FALSE, FALSE, FALSE, 0);
                """));

        // PostgreSQL reporta ON DELETE RESTRICT como restrict_violation (23001), distinto da FK de INSERT (23503).
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.RestrictViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = {UserOneId};"));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.RestrictViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"FinancialConnection\" WHERE \"Id\" = {ConnectionOneId};"));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.RestrictViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CardAccount\" WHERE \"Id\" = {CardOneId};"));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.RestrictViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"FinancialTransaction\" WHERE \"Id\" = {TransactionOneId};"));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.RestrictViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Category\" WHERE \"Id\" = {PersonalCategoryOneId};"));
        await AssertDirectSqlViolationAsync(PostgresErrorCodes.RestrictViolation, context =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Category\" WHERE \"Id\" = {SystemCategoryId};"));

        await AssertSeedGraphRemainsIntactAsync();
    }

    // Semeia pais válidos antes dos dependentes para que cada rejeição posterior isole a constraint pretendida.
    private static async Task SeedValidGraphAsync(FinoraDbContext context)
    {
        context.Users.AddRange(new ApplicationUser(UserOneId), new ApplicationUser(UserTwoId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(ConnectionOneId, UserOneId, "pluggy", "item-one"),
            FinancialConnection.Create(ConnectionTwoId, UserTwoId, "pluggy", "item-two"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().AddRange(
            CardAccount.Create(CardOneId, UserOneId, ConnectionOneId, "external-card-one", "BRL"),
            CardAccount.Create(CardTwoId, UserTwoId, ConnectionTwoId, "external-card-two", "USD"));
        await context.SaveChangesAsync();

        context.Set<FinancialTransaction>().AddRange(
            CreateTransaction(TransactionOneId, UserOneId, CardOneId, "transaction-one", "external-card-one"),
            CreateTransaction(TransactionTwoId, UserOneId, CardOneId, "transaction-two", "external-card-one"),
            CreateTransaction(TransactionThreeId, UserOneId, CardOneId, "transaction-three", "external-card-one"),
            CreateTransaction(TransactionFourId, UserOneId, CardOneId, "transaction-four", "external-card-one"));
        await context.SaveChangesAsync();

        context.Set<Category>().AddRange(
            Category.Create(SystemCategoryId, null, isSystem: true, "Sistema sintético", "sistema-sintetico"),
            Category.Create(PersonalCategoryOneId, UserOneId, isSystem: false, "Pessoal titular um", "pessoal-titular-um"),
            Category.Create(PersonalCategoryTwoId, UserTwoId, isSystem: false, "Pessoal titular dois", "pessoal-titular-dois"));
        context.Set<SyncRun>().Add(CreateSyncRun(SyncRunId, UserOneId, ConnectionOneId));
        await context.SaveChangesAsync();

        context.Set<TransactionAnnotation>().AddRange(
            TransactionAnnotation.Create(
                TransactionOneId, UserOneId, PersonalCategoryOneId, UserOneId, "nota pessoal sintética", CreatedAtUtc),
            TransactionAnnotation.Create(
                TransactionTwoId, UserOneId, SystemCategoryId, Guid.Empty, "nota de sistema sintética", CreatedAtUtc),
            TransactionAnnotation.Create(
                TransactionThreeId, UserOneId, null, null, null, CreatedAtUtc));
        await context.SaveChangesAsync();
    }

    // A leitura independente confirma que categorias do sistema, do próprio titular e ausência são associações válidas.
    private static async Task AssertValidCategoryScopesAsync()
    {
        await using var context = CreateContext();
        var personal = await context.Set<TransactionAnnotation>().SingleAsync(annotation =>
            annotation.TransactionId == TransactionOneId);
        var system = await context.Set<TransactionAnnotation>().SingleAsync(annotation =>
            annotation.TransactionId == TransactionTwoId);
        var absent = await context.Set<TransactionAnnotation>().SingleAsync(annotation =>
            annotation.TransactionId == TransactionThreeId);

        Assert.AreEqual(PersonalCategoryOneId, personal.CategoryId);
        Assert.AreEqual(UserOneId, personal.CategoryScopeId);
        Assert.AreEqual(SystemCategoryId, system.CategoryId);
        Assert.AreEqual(Guid.Empty, system.CategoryScopeId);
        Assert.IsNull(absent.CategoryId);
        Assert.IsNull(absent.CategoryScopeId);
    }

    // Cada entidade inválida usa contexto novo; o erro SQLSTATE 23503 comprova rejeição da FK no provider.
    private static async Task AssertEntityForeignKeyViolationAsync(Action<FinoraDbContext> addEntity)
    {
        await using var context = CreateContext();
        addEntity(context);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;

        Assert.IsNotNull(postgresException, exception.ToString());
        Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, postgresException!.SqlState);
    }

    // Executa SQL interpolado parametrizado em contexto isolado para comprovar constraints sem validação das fábricas.
    private static async Task AssertDirectSqlViolationAsync(
        string expectedSqlState,
        Func<FinoraDbContext, Task<int>> execute)
    {
        await using var context = CreateContext();
        Exception? caughtException = null;
        try
        {
            await execute(context);
        }
        catch (Exception exception)
        {
            caughtException = exception;
        }

        Assert.IsNotNull(caughtException, "O PostgreSQL deveria rejeitar a instrução inválida.");
        var postgresException = caughtException as PostgresException
            ?? caughtException!.InnerException as PostgresException;
        Assert.IsNotNull(postgresException, caughtException!.ToString());
        Assert.AreEqual(expectedSqlState, postgresException!.SqlState, postgresException.MessageText);
    }

    // Connection string é lida exclusivamente da variável dedicada e validada antes de construir cada contexto.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, settings.ConnectionString);
        return new FinoraDbContext(optionsBuilder.Options);
    }

    // As fixtures mantêm natureza conservadora e vínculo externo coerente, sem qualquer interpretação de compra.
    private static FinancialTransaction CreateTransaction(
        Guid id,
        Guid userId,
        Guid cardAccountId,
        string externalId,
        string externalAccountId)
    {
        var facts = new TransactionFacts(
            externalId,
            externalAccountId,
            "movimento sintético para integridade relacional",
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
            "integrity-test-v1",
            ReferenceDate);
    }

    // SyncRun sintético usa somente conexão/titular existentes ou o par inválido do cenário que será rejeitado.
    private static SyncRun CreateSyncRun(Guid id, Guid userId, Guid connectionId) => SyncRun.Create(
        id,
        userId,
        connectionId,
        CreatedAtUtc,
        new DateOnly(2026, 11, 1));

    // SQL parametrizado preenche apenas colunas obrigatórias, deixando a FK composta como regra sob teste.
    private static FormattableString BuildTransactionInsert(
        Guid id,
        Guid userId,
        Guid cardAccountId,
        string externalId,
        string externalAccountId) => $"""
        INSERT INTO "FinancialTransaction" (
            "Id", "UserId", "CardAccountId", "ExternalId", "ExternalAccountId", "Description",
            "Amount", "CurrencyCode", "OriginalDate", "Nature", "Situation",
            "NormalizationRuleVersion", "NormalizationReferenceDate")
        VALUES (
            {id}, {userId}, {cardAccountId}, {externalId}, {externalAccountId},
            {"movimento SQL sintético"}, {12.3456m}, {"BRL"}, {"2026-11-17"}, {"Fee"}, {"Effective"},
            {"integrity-test-v1"}, {ReferenceDate});
        """;

    // SQL parametrizado permite exercitar o par categoria/escopo sem passar pela validação do Domain.
    private static FormattableString BuildAnnotationInsert(
        Guid transactionId,
        Guid userId,
        Guid? categoryId,
        Guid? categoryScopeId) => $"""
        INSERT INTO "TransactionAnnotation" ("TransactionId", "UserId", "CategoryId", "CategoryScopeId", "UpdatedAtUtc")
        VALUES ({transactionId}, {userId}, {categoryId}, {categoryScopeId}, {CreatedAtUtc});
        """;

    // A consulta em outro contexto confirma que falhas de FK/DELETE não publicaram linhas nem apagaram histórico.
    private static async Task AssertSeedGraphRemainsIntactAsync()
    {
        await using var context = CreateContext();
        Assert.AreEqual(2, await context.Users.CountAsync());
        Assert.AreEqual(2, await context.Set<FinancialConnection>().CountAsync());
        Assert.AreEqual(2, await context.Set<CardAccount>().CountAsync());
        Assert.AreEqual(4, await context.Set<FinancialTransaction>().CountAsync());
        Assert.AreEqual(1, await context.Set<SyncRun>().CountAsync());
        // As migrations semeiam sete categorias; as três do cenário devem permanecer sem gravações inválidas adicionais.
        Assert.AreEqual(10, await context.Set<Category>().CountAsync());
        Assert.AreEqual(3, await context.Set<TransactionAnnotation>().CountAsync());
    }
}
