using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Inspeciona metadados EF construídos com Npgsql sem abrir conexão nem aplicar migrations.
[TestClass]
public sealed class FinancialDataRelationalModelTests
{
    // Chaves alternativas sustentam FKs compostas que provam o titular e a conta externa relacionada.
    [TestMethod]
    public void FinancialEntitiesExposeTheApprovedPrincipalAndAlternateKeys()
    {
        using var context = CreateContext();
        var model = context.Model;
        var connection = model.FindEntityType(typeof(FinancialConnection))!;
        var account = model.FindEntityType(typeof(CardAccount))!;
        var transaction = model.FindEntityType(typeof(FinancialTransaction))!;
        var category = model.FindEntityType(typeof(Category))!;
        var annotation = model.FindEntityType(typeof(TransactionAnnotation))!;

        AssertKeyProperties(connection, isPrimary: true, "Id");
        AssertKeyProperties(connection, isPrimary: false, "Id", "UserId");
        AssertKeyProperties(account, isPrimary: true, "Id");
        AssertKeyProperties(account, isPrimary: false, "Id", "UserId", "ExternalId");
        AssertKeyProperties(transaction, isPrimary: true, "Id");
        AssertKeyProperties(transaction, isPrimary: false, "Id", "UserId");
        AssertKeyProperties(category, isPrimary: true, "Id");
        AssertKeyProperties(category, isPrimary: false, "Id", "ScopeId");
        AssertKeyProperties(annotation, isPrimary: true, "TransactionId");
    }

    // Cada aresta do grafo financeiro restringe titular e exclusão sem depender de filtro de consulta.
    [TestMethod]
    public void FinancialForeignKeysEnforceOwnershipAndRestrictDeletion()
    {
        using var context = CreateContext();
        var model = context.Model;

        AssertForeignKey(
            model.FindEntityType(typeof(FinancialConnection))!,
            typeof(ApplicationUser),
            new[] { "UserId" },
            new[] { "Id" },
            required: true);
        AssertForeignKey(
            model.FindEntityType(typeof(CardAccount))!,
            typeof(FinancialConnection),
            new[] { "ConnectionId", "UserId" },
            new[] { "Id", "UserId" },
            required: true);
        AssertForeignKey(
            model.FindEntityType(typeof(FinancialTransaction))!,
            typeof(CardAccount),
            new[] { "CardAccountId", "UserId", "ExternalAccountId" },
            new[] { "Id", "UserId", "ExternalId" },
            required: true);
        AssertForeignKey(
            model.FindEntityType(typeof(TransactionAnnotation))!,
            typeof(FinancialTransaction),
            new[] { "TransactionId", "UserId" },
            new[] { "Id", "UserId" },
            required: true);
        AssertForeignKey(
            model.FindEntityType(typeof(TransactionAnnotation))!,
            typeof(Category),
            new[] { "CategoryId", "CategoryScopeId" },
            new[] { "Id", "ScopeId" },
            required: false);
        AssertForeignKey(
            model.FindEntityType(typeof(SyncRun))!,
            typeof(FinancialConnection),
            new[] { "ConnectionId", "UserId" },
            new[] { "Id", "UserId" },
            required: true);
        AssertForeignKey(
            model.FindEntityType(typeof(Category))!,
            typeof(ApplicationUser),
            new[] { "UserId" },
            new[] { "Id" },
            required: false);
    }

    // CHECKs tornam nulos, titular vazio e escopo de categoria invariantes mesmo em SQL direto.
    [TestMethod]
    public void OwnershipAndCategoryScopeCheckConstraintsArePresent()
    {
        using var context = CreateContext();
        // CHECKs são metadados de migration e precisam ser lidos no modelo de design, não no modelo runtime otimizado.
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertCheckConstraint(
            model.FindEntityType(typeof(ApplicationUser))!,
            "CK_AspNetUsers_Id_NotEmpty",
            "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        AssertCheckConstraint(
            model.FindEntityType(typeof(FinancialConnection))!,
            "CK_FinancialConnection_Ids_NotEmpty",
            "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        AssertCheckConstraint(
            model.FindEntityType(typeof(CardAccount))!,
            "CK_CardAccount_Ids_NotEmpty",
            "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid",
            "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        AssertCheckConstraint(
            model.FindEntityType(typeof(FinancialTransaction))!,
            "CK_FinancialTransaction_Ids_NotEmpty",
            "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid",
            "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        AssertCheckConstraint(
            model.FindEntityType(typeof(SyncRun))!,
            "CK_SyncRun_Ids_NotEmpty",
            "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid",
            "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        AssertCheckConstraint(
            model.FindEntityType(typeof(Category))!,
            "CK_Category_Id_NotEmpty",
            "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        AssertCheckConstraint(
            model.FindEntityType(typeof(Category))!,
            "CK_Category_SystemOwnershipAndScope",
            "\"IsSystem\" = TRUE",
            "\"IsSystem\" = FALSE",
            "\"UserId\" IS NULL",
            "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid",
            "\"ScopeId\" = \"UserId\"",
            "\"ScopeId\" = '00000000-0000-0000-0000-000000000000'::uuid");
        AssertCheckConstraint(
            model.FindEntityType(typeof(TransactionAnnotation))!,
            "CK_TransactionAnnotation_CategoryAndScope",
            "\"CategoryId\" IS NULL",
            "\"CategoryScopeId\" IS NULL",
            "\"CategoryScopeId\" = \"UserId\"",
            "\"CategoryScopeId\" = '00000000-0000-0000-0000-000000000000'::uuid");

        var annotation = model.FindEntityType(typeof(TransactionAnnotation))!;
        Assert.IsTrue(annotation.FindProperty(nameof(TransactionAnnotation.CategoryId))!.IsNullable);
        Assert.IsTrue(annotation.FindProperty(nameof(TransactionAnnotation.CategoryScopeId))!.IsNullable);
    }

    // Tipos de coluna preservam dinheiro, datas de calendário, instantes UTC e códigos estáveis sem conversão ambígua.
    [TestMethod]
    public void FinancialScalarsUseTheApprovedPostgresTypesAndCollations()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertColumnType(model, typeof(FinancialTransaction), nameof(FinancialTransaction.Amount), "numeric(19,4)");
        AssertColumnType(model, typeof(CardAccount), nameof(CardAccount.AvailableCreditLimit), "numeric(19,4)");
        AssertColumnType(model, typeof(CardAccount), nameof(CardAccount.TotalCreditLimit), "numeric(19,4)");
        AssertColumnType(model, typeof(FinancialTransaction), nameof(FinancialTransaction.TransactionDate), "date");
        AssertColumnType(model, typeof(FinancialTransaction), nameof(FinancialTransaction.NormalizationReferenceDate), "date");
        AssertColumnType(model, typeof(FinancialTransaction), nameof(FinancialTransaction.OriginalDate), "text");
        AssertColumnType(model, typeof(FinancialTransaction), nameof(FinancialTransaction.PurchaseDate), "text");
        AssertColumnType(model, typeof(FinancialTransaction), nameof(FinancialTransaction.Nature), "text");
        AssertColumnType(model, typeof(FinancialTransaction), nameof(FinancialTransaction.NatureReason), "text");
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.State), "text");
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.CoverageState), "text");
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.RequestedFrom), "date");
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.ObservedTo), "date");
        AssertColumnType(model, typeof(FinancialConnection), nameof(FinancialConnection.LastAttemptAtUtc), "timestamp with time zone");
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.CreatedAtUtc), "timestamp with time zone");
        AssertColumnType(model, typeof(TransactionAnnotation), nameof(TransactionAnnotation.UpdatedAtUtc), "timestamp with time zone");

        AssertPropertyCollation(model, typeof(FinancialConnection), nameof(FinancialConnection.Provider), "C");
        AssertPropertyCollation(model, typeof(FinancialConnection), nameof(FinancialConnection.ExternalItemId), "C");
        AssertPropertyCollation(model, typeof(CardAccount), nameof(CardAccount.ExternalId), "C");
        AssertPropertyCollation(model, typeof(FinancialTransaction), nameof(FinancialTransaction.ExternalId), "C");
        AssertPropertyCollation(model, typeof(FinancialTransaction), nameof(FinancialTransaction.ExternalAccountId), "C");
        AssertPropertyCollation(model, typeof(Category), nameof(Category.NormalizedName), "C");

        Assert.AreEqual(32, model.FindEntityType(typeof(FinancialConnection))!
            .FindProperty(nameof(FinancialConnection.Provider))!.GetMaxLength());
        Assert.AreEqual(60, model.FindEntityType(typeof(Category))!
            .FindProperty(nameof(Category.NormalizedName))!.GetMaxLength());
        Assert.AreEqual(500, model.FindEntityType(typeof(TransactionAnnotation))!
            .FindProperty(nameof(TransactionAnnotation.Note))!.GetMaxLength());
    }

    // Índices únicos guardam as identidades opacas; os índices de consulta e convenções cobrem prefixos sem duplicatas.
    [TestMethod]
    public void UniqueAndQueryIndexesMatchTheApprovedAccessPathsWithoutRedundantPrefixes()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var connection = model.FindEntityType(typeof(FinancialConnection))!;
        var transaction = model.FindEntityType(typeof(FinancialTransaction))!;
        var category = model.FindEntityType(typeof(Category))!;
        var annotation = model.FindEntityType(typeof(TransactionAnnotation))!;
        var syncRun = model.FindEntityType(typeof(SyncRun))!;

        AssertIndex(connection, isUnique: true, nameof(FinancialConnection.UserId), nameof(FinancialConnection.Provider), nameof(FinancialConnection.ExternalItemId));
        AssertIndex(connection, isUnique: false, nameof(FinancialConnection.UserId), allowMissing: true);
        AssertIndex(model.FindEntityType(typeof(CardAccount))!, isUnique: true,
            nameof(CardAccount.ConnectionId), nameof(CardAccount.ExternalId));
        AssertIndex(transaction, isUnique: true,
            nameof(FinancialTransaction.CardAccountId), nameof(FinancialTransaction.ExternalId));
        AssertIndex(transaction, isUnique: false,
            nameof(FinancialTransaction.CardAccountId), nameof(FinancialTransaction.TransactionDate), nameof(FinancialTransaction.Id));
        AssertIndex(category, isUnique: true, nameof(Category.ScopeId), nameof(Category.NormalizedName));
        AssertIndex(syncRun, isUnique: false, nameof(SyncRun.ConnectionId), nameof(SyncRun.State));
        AssertIndex(syncRun, isUnique: true, nameof(SyncRun.IdempotencyKeyHash));
        var idempotencyIndex = syncRun.GetIndexes().Single(index =>
            index.IsUnique && index.Properties.Single().Name == nameof(SyncRun.IdempotencyKeyHash));
        Assert.AreEqual("UX_SyncRun_IdempotencyKeyHash", idempotencyIndex.GetDatabaseName());
        Assert.AreEqual("\"IdempotencyKeyHash\" IS NOT NULL", idempotencyIndex.GetFilter());
        Assert.IsTrue(syncRun.FindProperty(nameof(SyncRun.IdempotencyKeyHash))!.IsNullable);

        var categoryFilterIndexes = annotation.GetIndexes()
            .Where(index => index.Properties[0].Name == nameof(TransactionAnnotation.CategoryId))
            .ToArray();
        Assert.AreEqual(1, categoryFilterIndexes.Length);
        Assert.AreEqual(nameof(TransactionAnnotation.CategoryId), categoryFilterIndexes[0].Properties[0].Name);

        // A FK composta retém índice convencional não único; o PK separado conserva a unicidade da anotação.
        AssertIndex(annotation, isUnique: false,
            nameof(TransactionAnnotation.TransactionId), nameof(TransactionAnnotation.UserId));
        AssertIndex(annotation, isUnique: true,
            nameof(TransactionAnnotation.TransactionId), allowMissing: true);
    }

    // CHECKs de tamanho contam a representação UTF-8 nas colunas preservadas integralmente.
    [TestMethod]
    public void ExternalIdentityColumnsHaveUtf8ByteConstraints()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertCheckConstraint(
            model.FindEntityType(typeof(FinancialConnection))!,
            "CK_FinancialConnection_ExternalItemId_MaxUtf8Bytes",
            "octet_length(convert_to(\"ExternalItemId\", 'UTF8')) <= 1024");
        AssertCheckConstraint(
            model.FindEntityType(typeof(CardAccount))!,
            "CK_CardAccount_ExternalId_MaxUtf8Bytes",
            "octet_length(convert_to(\"ExternalId\", 'UTF8')) <= 1024");
        AssertCheckConstraint(
            model.FindEntityType(typeof(FinancialTransaction))!,
            "CK_FinancialTransaction_ExternalId_MaxUtf8Bytes",
            "octet_length(convert_to(\"ExternalId\", 'UTF8')) <= 1024");
        AssertCheckConstraint(
            model.FindEntityType(typeof(FinancialTransaction))!,
            "CK_FinancialTransaction_ExternalAccountId_MaxUtf8Bytes",
            "octet_length(convert_to(\"ExternalAccountId\", 'UTF8')) <= 1024");
    }

    // O modelo protege gravações relacionais contra hash parcial e mantém o histórico sem chave compatível.
    [TestMethod]
    public void SyncRunRequiresIdempotencyHashAndRequestFingerprintAsAValidPair()
    {
        using var context = CreateContext();
        var syncRun = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SyncRun))!;

        AssertCheckConstraint(
            syncRun,
            "CK_SyncRun_IdempotencyFingerprintPair",
            "IdempotencyKeyHash\" IS NULL",
            "RequestFingerprint\" IS NULL",
            "{64}");
    }

    // A PK permite uma posse por conexão e FKs/checks prendem execução, titular, tentativa, expiração e geração.
    [TestMethod]
    public void SyncRunLeaseModelEnforcesSingleConnectionOwnershipAndPositiveFencing()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var lease = model.FindEntityType(typeof(SyncRunLease))!;
        var run = model.FindEntityType(typeof(SyncRun))!;

        AssertKeyProperties(lease, isPrimary: true, nameof(SyncRunLease.ConnectionId));
        AssertKeyProperties(run, isPrimary: false,
            nameof(SyncRun.Id), nameof(SyncRun.ConnectionId), nameof(SyncRun.UserId));
        AssertForeignKey(lease, typeof(FinancialConnection),
            [nameof(SyncRunLease.ConnectionId), nameof(SyncRunLease.UserId)],
            [nameof(FinancialConnection.Id), nameof(FinancialConnection.UserId)], required: true);
        AssertForeignKey(lease, typeof(SyncRun),
            [nameof(SyncRunLease.SyncRunId), nameof(SyncRunLease.ConnectionId), nameof(SyncRunLease.UserId)],
            [nameof(SyncRun.Id), nameof(SyncRun.ConnectionId), nameof(SyncRun.UserId)], required: true);
        AssertIndex(lease, isUnique: false,
            nameof(SyncRunLease.SyncRunId), nameof(SyncRunLease.AttemptNumber));
        AssertCheckConstraint(lease, "CK_SyncRunLeases_Attempt_Positive", "\"AttemptNumber\" > 0");
        AssertCheckConstraint(lease, "CK_SyncRunLeases_Fencing_Positive", "\"FencingToken\" > 0");
        AssertCheckConstraint(lease, "CK_SyncRunLeases_Expiration", "\"ExpiresAtUtc\" > \"AcquiredAtUtc\"");
        AssertColumnType(model, typeof(SyncRunLease), nameof(SyncRunLease.AcquiredAtUtc), "timestamp with time zone");
        AssertColumnType(model, typeof(SyncRunLease), nameof(SyncRunLease.ExpiresAtUtc), "timestamp with time zone");

        AssertIndex(run, isUnique: false, nameof(SyncRun.State), nameof(SyncRun.NextAttemptAtUtc));
        var retryIndex = run.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(SyncRun.State), nameof(SyncRun.NextAttemptAtUtc)]));
        Assert.AreEqual("\"NextAttemptAtUtc\" IS NOT NULL", retryIndex.GetFilter());
        AssertIndex(run, isUnique: false,
            nameof(SyncRun.State), nameof(SyncRun.ExecutionMessageEnqueuedAtUtc));
        var executionMessageIndex = run.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(SyncRun.State), nameof(SyncRun.ExecutionMessageEnqueuedAtUtc)]));
        Assert.AreEqual("\"ExecutionMessageEnqueuedAtUtc\" IS NULL", executionMessageIndex.GetFilter());
        AssertCheckConstraint(run, "CK_SyncRun_AttemptBudget", "\"AttemptNumber\" >= 0", "BETWEEN 1 AND 3");
        AssertCheckConstraint(run, "CK_SyncRun_AttemptDeadline", "\"AttemptDeadlineAtUtc\" > \"AttemptStartedAtUtc\"", "interval '10 minutes'");
        AssertCheckConstraint(run, "CK_SyncRun_NextAttemptNotBeforeAcceptance", "\"NextAttemptAtUtc\" >= \"CreatedAtUtc\"");
        Assert.AreEqual(0, run.FindProperty(nameof(SyncRun.AttemptNumber))!.GetDefaultValue());
        Assert.AreEqual(SyncRun.ApprovedAttemptLimit, run.FindProperty(nameof(SyncRun.AttemptLimit))!.GetDefaultValue());
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.AttemptStartedAtUtc), "timestamp with time zone");
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.AttemptDeadlineAtUtc), "timestamp with time zone");
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.NextAttemptAtUtc), "timestamp with time zone");
        AssertColumnType(model, typeof(SyncRun), nameof(SyncRun.ExecutionMessageEnqueuedAtUtc), "timestamp with time zone");
    }

    // As duas entidades técnicas do MassTransit pertencem ao mesmo modelo PostgreSQL da execução persistida.
    [TestMethod]
    public void FinoraContextIncludesTheEntityFrameworkBusOutboxTables()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.AreEqual("OutboxMessage", model.FindEntityType(typeof(OutboxMessage))!.GetTableName());
        Assert.AreEqual("OutboxState", model.FindEntityType(typeof(OutboxState))!.GetTableName());
    }

    // Opções Npgsql e Identity iguais às da factory constroem apenas o modelo, sem abrir o database sintético.
    private static FinoraDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(
            optionsBuilder,
            "Host=127.0.0.1;Database=finora_integration_test;Username=finora_test");
        return new FinoraDbContext(optionsBuilder.Options);
    }

    // Compara o tipo relacional configurado na property sem exigir schema materializado no banco.
    private static void AssertColumnType(IModel model, Type entityType, string propertyName, string expected)
    {
        var property = model.FindEntityType(entityType)!.FindProperty(propertyName)!;
        Assert.AreEqual(expected, property.GetColumnType(), $"Tipo incorreto em {entityType.Name}.{propertyName}.");
    }

    // Collation C mantém a comparação ordinal e sensível à caixa nas identidades indexadas.
    private static void AssertPropertyCollation(IModel model, Type entityType, string propertyName, string expected)
    {
        var property = model.FindEntityType(entityType)!.FindProperty(propertyName)!;
        Assert.AreEqual(expected, property.GetCollation(), $"Collation incorreta em {entityType.Name}.{propertyName}.");
    }

    // Verifica propriedade/ordem e unicidade do índice; allowMissing documenta o prefixo coberto por outro índice.
    private static void AssertIndex(
        IEntityType entityType,
        bool isUnique,
        string propertyName,
        bool allowMissing = false) =>
        AssertIndex(entityType, isUnique, new[] { propertyName }, allowMissing);

    private static void AssertIndex(IEntityType entityType, bool isUnique, params string[] propertyNames)
    {
        AssertIndex(entityType, isUnique, propertyNames, allowMissing: false);
    }

    private static void AssertIndex(
        IEntityType entityType,
        bool isUnique,
        string[] propertyNames,
        bool allowMissing)
    {
        var index = entityType.GetIndexes().SingleOrDefault(candidate =>
            candidate.IsUnique == isUnique
            && candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));

        if (allowMissing)
        {
            Assert.IsNull(index, $"Índice {string.Join(",", propertyNames)} seria redundante em {entityType.Name}.");
            return;
        }

        Assert.IsNotNull(index, $"Índice {(isUnique ? "único" : "de consulta")} ausente em {entityType.Name}.");
    }

    // Compara a composição e a ordem das propriedades de PK/AK para detectar FKs incompatíveis.
    private static void AssertKeyProperties(IEntityType entityType, bool isPrimary, params string[] names)
    {
        var key = entityType.GetKeys().SingleOrDefault(candidate =>
            candidate.IsPrimaryKey() == isPrimary
            && candidate.Properties.Select(property => property.Name).SequenceEqual(names));

        Assert.IsNotNull(key, $"Chave {(isPrimary ? "primária" : "alternativa")} ausente em {entityType.Name}.");
    }

    // Valida as propriedades dependentes/principais e impede Cascade nas relações financeiras.
    private static void AssertForeignKey(
        IEntityType dependent,
        Type principalType,
        string[] foreignKeyNames,
        string[] principalKeyNames,
        bool required)
    {
        var foreignKey = dependent.GetForeignKeys().SingleOrDefault(candidate =>
            candidate.PrincipalEntityType.ClrType == principalType
            && candidate.Properties.Select(property => property.Name).SequenceEqual(foreignKeyNames));

        Assert.IsNotNull(foreignKey, $"FK para {principalType.Name} ausente em {dependent.Name}.");
        CollectionAssert.AreEqual(
            principalKeyNames,
            foreignKey.PrincipalKey.Properties.Select(property => property.Name).ToArray());
        Assert.AreEqual(required, foreignKey.IsRequired);
        Assert.AreEqual(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    // Verifica expressões SQL pelo nome estável da constraint sem exigir conexão PostgreSQL.
    private static void AssertCheckConstraint(IEntityType entityType, string name, params string[] fragments)
    {
        var constraint = entityType.GetCheckConstraints().SingleOrDefault(candidate => candidate.Name == name);
        Assert.IsNotNull(constraint, $"CHECK {name} ausente em {entityType.Name}.");

        foreach (var fragment in fragments)
        {
            StringAssert.Contains(constraint.Sql, fragment);
        }
    }
}
