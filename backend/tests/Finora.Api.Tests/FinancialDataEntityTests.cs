using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Verifica invariantes puras de persistência sem abrir conexão ou depender de EF/Identity.
[TestClass]
public sealed class FinancialDataEntityTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 11, 17);
    private static readonly Guid UserId = Guid.Parse("3c472e18-2c28-4bf1-90dc-f60c0e97ac12");
    private static readonly Guid ConnectionId = Guid.Parse("a63e12ba-9921-436c-96f1-2967dc4ab374");
    private static readonly Guid CardAccountId = Guid.Parse("a42cf987-2b5c-4768-9b61-1fbb8d5b6096");
    private static readonly Guid TransactionId = Guid.Parse("c78b8403-dc21-4269-a6b7-7d4538f78a36");
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    // Identificadores externos preservam caixa e espaços, permanecendo distintos dos UUIDs internos.
    [TestMethod]
    public void ConnectionAndCardPreserveOpaqueExternalIds()
    {
        var externalItemId = " Item/AbC ";
        var externalCardId = "Card-01 ";
        var connection = FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            externalItemId);
        var card = CardAccount.Create(
            CardAccountId,
            UserId,
            connection.Id,
            externalCardId,
            "USD",
            name: "Cartão fictício",
            lastFour: "1234");

        Assert.AreEqual(ConnectionId, connection.Id);
        Assert.AreEqual(UserId, connection.UserId);
        Assert.AreEqual(externalItemId, connection.ExternalItemId);
        Assert.AreEqual(CardAccountId, card.Id);
        Assert.AreEqual(connection.Id, card.ConnectionId);
        Assert.AreEqual(externalCardId, card.ExternalId);
        Assert.AreEqual("USD", card.CurrencyCode);
    }

    // UUIDs vazios, IDs obrigatórios vazios e instantes fora de UTC são rejeitados na criação.
    [TestMethod]
    public void RejectsMissingIdentityAndNonUtcConnectionInstants()
    {
        Assert.Throws<ArgumentException>(() => FinancialConnection.Create(
            Guid.Empty,
            UserId,
            "pluggy",
            "external-item"));
        Assert.Throws<ArgumentException>(() => FinancialConnection.Create(
            ConnectionId,
            Guid.Empty,
            "pluggy",
            "external-item"));
        Assert.Throws<ArgumentException>(() => FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            string.Empty));
        Assert.Throws<ArgumentException>(() => FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            "external-item",
            expiresAtUtc: new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(-3))));
        Assert.Throws<ArgumentException>(() => CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            string.Empty,
            "BRL"));
    }

    // O tamanho é contado em bytes UTF-8 e o limite do provedor é validado sem normalização.
    [TestMethod]
    public void EnforcesExternalIdentifierAndProviderCodeLimits()
    {
        var maximumUtf8Id = new string('é', 512);
        var excessiveUtf8Id = new string('é', 513);
        var maximumProviderCode = new string('p', 32);

        var connection = FinancialConnection.Create(
            ConnectionId,
            UserId,
            maximumProviderCode,
            maximumUtf8Id);

        Assert.AreEqual(maximumProviderCode, connection.Provider);
        Assert.AreEqual(maximumUtf8Id, connection.ExternalItemId);
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            excessiveUtf8Id));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialConnection.Create(
            ConnectionId,
            UserId,
            new string('p', 33),
            "external-item"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            excessiveUtf8Id,
            "BRL"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateTransaction(
            Normalize(Facts(externalId: excessiveUtf8Id))));
    }

    // O movimento mantém fatos de origem e razões distintas sem persistir elegibilidade financeira.
    [TestMethod]
    public void TransactionPreservesSourceAndIndependentInterpretationReasons()
    {
        var facts = Facts(
            externalId: " Transaction/ABC ",
            accountId: "account-01 ",
            operationType: "PAGAMENTO",
            type: "DEBIT",
            amount: 42.75m,
            status: "PENDING");
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);
        var transaction = FinancialTransaction.Create(
            Guid.Parse("8de2784b-e0fb-436d-956b-14c748a057ed"),
            UserId,
            CardAccountId,
            normalization,
            "transaction-normalizer-v1",
            ReferenceDate);

        Assert.AreEqual(facts.ExternalId, transaction.ExternalId);
        Assert.AreEqual(facts.AccountId, transaction.ExternalAccountId);
        Assert.AreEqual(facts.Description, transaction.Description);
        Assert.AreEqual(facts.Amount, transaction.Amount);
        Assert.AreEqual(facts.CurrencyCode, transaction.CurrencyCode);
        Assert.AreEqual(new DateOnly(2026, 11, 17), transaction.TransactionDate);
        Assert.AreEqual(TransactionNature.Unknown, transaction.Nature);
        Assert.AreEqual(TransactionExclusionReason.UnknownOperationType, transaction.NatureReason);
        Assert.AreEqual(TransactionSituation.Pending, transaction.Situation);
        Assert.AreEqual(TransactionExclusionReason.NotEffective, transaction.SituationReason);
        Assert.IsNull(transaction.DateReason);
        Assert.AreEqual("transaction-normalizer-v1", transaction.NormalizationRuleVersion);
        Assert.AreEqual(ReferenceDate, transaction.NormalizationReferenceDate);
    }

    // Todos os fatos obrigatórios são validados sem descartar datas ambíguas nem inventar normalização.
    [TestMethod]
    public void RejectsMissingRequiredFactsAndInconsistentNormalizationSnapshot()
    {
        var missingExternalId = Normalize(Facts(externalId: string.Empty));
        Assert.Throws<ArgumentException>(() => CreateTransaction(missingExternalId));

        var missingDescription = Normalize(Facts() with { Description = " " });
        Assert.Throws<ArgumentException>(() => CreateTransaction(missingDescription));

        var missingAccountId = Normalize(Facts(accountId: string.Empty));
        Assert.Throws<ArgumentException>(() => CreateTransaction(missingAccountId));

        var missingCurrency = Normalize(Facts() with { CurrencyCode = " " });
        Assert.Throws<ArgumentException>(() => CreateTransaction(missingCurrency));

        var missingOriginalDate = Normalize(Facts(originalDate: string.Empty));
        Assert.Throws<ArgumentException>(() => CreateTransaction(missingOriginalDate));

        var unknownWithoutReason = Normalize(Facts(operationType: "NEW_OPERATION")) with { NatureReason = null };
        Assert.Throws<ArgumentException>(() => CreateTransaction(unknownWithoutReason));

        Assert.Throws<ArgumentException>(() => FinancialConnection.Create(
            ConnectionId,
            UserId,
            " ",
            "external-item"));
        Assert.Throws<ArgumentException>(() => CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            "external-card",
            " "));

        var ambiguousDate = Normalize(Facts(originalDate: "2026-11-17T10:00:00"));
        var transaction = CreateTransaction(ambiguousDate);
        Assert.IsNull(transaction.TransactionDate);
        Assert.AreEqual(TransactionExclusionReason.MissingDateOffset, transaction.DateReason);
    }

    // Numeric(19,4) aceita extremos e zeros excedentes, mas rejeita overflow ou fração não representável.
    [TestMethod]
    public void EnforcesNumeric19Scale4WithoutSilentRounding()
    {
        const decimal maximum = 999_999_999_999_999.9999m;
        var positiveLimit = CreateTransaction(Normalize(Facts(amount: maximum)));
        var negativeLimit = CreateTransaction(Normalize(Facts(amount: -maximum)));
        var trailingZeroes = CreateTransaction(Normalize(Facts(amount: 1.23000m)));
        var card = CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            "external-card",
            "BRL",
            availableCreditLimit: maximum);

        Assert.AreEqual(maximum, positiveLimit.Amount);
        Assert.AreEqual(-maximum, negativeLimit.Amount);
        Assert.AreEqual(1.23000m, trailingZeroes.Amount);
        Assert.AreEqual(maximum, card.AvailableCreditLimit);
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateTransaction(
            Normalize(Facts(amount: maximum + 0.0001m))));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateTransaction(
            Normalize(Facts(amount: 1.00001m))));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardAccount.Create(
            CardAccountId,
            UserId,
            ConnectionId,
            "external-card",
            "BRL",
            totalCreditLimit: maximum + 0.0001m));
    }

    // Categoria de sistema e pessoal usam escopos técnicos distintos sem normalizar nomes recebidos.
    [TestMethod]
    public void CategoryPreservesProvidedNamesAndValidatesOwnershipScope()
    {
        var system = Category.Create(
            Guid.Parse("7236554c-a374-4dd5-a477-63001c974abd"),
            null,
            isSystem: true,
            name: "Mercado",
            normalizedName: "mercado-chave");
        var personal = Category.Create(
            Guid.Parse("13dbf0d4-09f9-4b2b-945d-c1a397449340"),
            UserId,
            isSystem: false,
            name: " Minha categoria ",
            normalizedName: "chave-fornecida");

        Assert.IsNull(system.UserId);
        Assert.IsTrue(system.IsSystem);
        Assert.AreEqual(Guid.Empty, system.ScopeId);
        Assert.AreEqual("mercado-chave", system.NormalizedName);
        Assert.AreEqual(UserId, personal.UserId);
        Assert.IsFalse(personal.IsSystem);
        Assert.AreEqual(UserId, personal.ScopeId);
        Assert.AreEqual(" Minha categoria ", personal.Name);
        Assert.AreEqual("chave-fornecida", personal.NormalizedName);

        Assert.Throws<ArgumentException>(() => Category.Create(
            Guid.NewGuid(), UserId, isSystem: true, "Categoria", "chave"));
        Assert.Throws<ArgumentException>(() => Category.Create(
            Guid.NewGuid(), null, isSystem: false, "Categoria", "chave"));
        Assert.Throws<ArgumentException>(() => Category.Create(
            Guid.NewGuid(), Guid.Empty, isSystem: false, "Categoria", "chave"));
        Assert.Throws<ArgumentException>(() => Category.Create(
            Guid.NewGuid(), UserId, isSystem: false, " ", "chave"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Category.Create(
            Guid.NewGuid(), UserId, isSystem: false, new string('n', 61), "chave"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Category.Create(
            Guid.NewGuid(), UserId, isSystem: false, "Categoria", new string('k', 61)));
    }

    // Anotação aceita 500 caracteres, usa null para ausência e preserva categoria quando a nota é limpa.
    [TestMethod]
    public void AnnotationEnforcesNoteLimitAndClearsNoteWithoutRemovingCategory()
    {
        var categoryId = Guid.Parse("476d0f85-2746-47a3-a043-42dab49ab60e");
        var note = new string('n', 500);
        var annotation = TransactionAnnotation.Create(
            TransactionId,
            UserId,
            categoryId,
            UserId,
            note,
            CreatedAtUtc);
        var cleared = annotation.ClearNote(CreatedAtUtc.AddMinutes(5));
        var noCategory = TransactionAnnotation.Create(
            TransactionId,
            UserId,
            null,
            null,
            null,
            CreatedAtUtc);

        Assert.AreEqual(500, annotation.Note!.Length);
        Assert.IsNull(cleared.Note);
        Assert.AreEqual(annotation.TransactionId, cleared.TransactionId);
        Assert.AreEqual(annotation.UserId, cleared.UserId);
        Assert.AreEqual(categoryId, cleared.CategoryId);
        Assert.AreEqual(UserId, cleared.CategoryScopeId);
        Assert.AreEqual(CreatedAtUtc.AddMinutes(5), cleared.UpdatedAtUtc);
        Assert.IsNull(noCategory.CategoryId);
        Assert.IsNull(noCategory.CategoryScopeId);
        Assert.IsNull(noCategory.Note);

        Assert.Throws<ArgumentOutOfRangeException>(() => annotation.WithNote(new string('n', 501), CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => TransactionAnnotation.Create(
            TransactionId, UserId, categoryId, null, null, CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => TransactionAnnotation.Create(
            TransactionId, UserId, null, Guid.Empty, null, CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => TransactionAnnotation.Create(
            TransactionId, UserId, categoryId, Guid.NewGuid(), null, CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => TransactionAnnotation.Create(
            TransactionId, UserId, Guid.Empty, Guid.Empty, null, CreatedAtUtc));
    }

    // SyncRun preserva null como desconhecido e valida intervalo, ordenação temporal e contadores.
    [TestMethod]
    public void SyncRunPreservesUnknownCountsAndValidatesIntervalsAndCounters()
    {
        var openInterval = CreateSyncRun();
        var bounded = CreateSyncRun(
            requestedToExclusive: new DateOnly(2026, 12, 1),
            pagesRead: 0,
            receivedCount: 0,
            uniqueCount: 0,
            persistedCount: 0,
            observedFrom: new DateOnly(2026, 11, 2),
            observedTo: new DateOnly(2026, 11, 4),
            coverageState: SyncCoverageState.Partial);

        Assert.AreEqual(SyncRunState.Pending, openInterval.State);
        Assert.AreEqual(SyncCoverageState.Unknown, openInterval.CoverageState);
        Assert.IsNull(openInterval.RequestedToExclusive);
        Assert.IsNull(openInterval.PagesRead);
        Assert.IsNull(openInterval.ReceivedCount);
        Assert.IsNull(openInterval.UniqueCount);
        Assert.IsNull(openInterval.PersistedCount);
        Assert.AreEqual(0, bounded.PagesRead);
        Assert.AreEqual(0, bounded.ReceivedCount);
        Assert.AreEqual(0, bounded.UniqueCount);
        Assert.AreEqual(0, bounded.PersistedCount);
        Assert.AreEqual(SyncCoverageState.Partial, bounded.CoverageState);

        Assert.Throws<ArgumentException>(() => CreateSyncRun(
            requestedToExclusive: new DateOnly(2026, 11, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSyncRun(pagesRead: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSyncRun(receivedCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSyncRun(uniqueCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSyncRun(persistedCount: -1));
        Assert.Throws<ArgumentException>(() => CreateSyncRun(
            observedFrom: new DateOnly(2026, 11, 5),
            observedTo: new DateOnly(2026, 11, 4)));
        Assert.Throws<ArgumentException>(() => CreateSyncRun(
            finishedAtUtc: CreatedAtUtc.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => CreateSyncRun(
            startedAtUtc: CreatedAtUtc.AddMinutes(2),
            finishedAtUtc: CreatedAtUtc.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => CreateSyncRun(
            createdAtUtc: CreatedAtUtc.ToOffset(TimeSpan.FromHours(-3))));
    }

    // Somente códigos aprovados atravessam a fronteira; uma mensagem externa é rejeitada sem eco.
    [TestMethod]
    [DataRow("PROVIDER_UNAVAILABLE")]
    [DataRow("RATE_LIMITED")]
    [DataRow("AUTH_REQUIRED")]
    [DataRow("INVALID_RESPONSE")]
    [DataRow("PROVIDER_TIMEOUT")]
    [DataRow("PERSISTENCE_FAILED")]
    [DataRow("CANCELLED")]
    [DataRow("UNKNOWN")]
    public void SyncRunAcceptsOnlyTheApprovedFailureCodeAllowlist(string failureCode)
    {
        var run = CreateSyncRun(state: SyncRunState.Failed, failureCode: failureCode);

        Assert.AreEqual(failureCode, run.FailureCode);
        Assert.IsTrue(SyncRunFailureCodes.IsAllowed(failureCode));

        var rejected = Assert.Throws<ArgumentException>(() => CreateSyncRun(
            state: SyncRunState.Failed,
            failureCode: "Bearer SECRET-provider-payload"));
        Assert.IsFalse(rejected.Message.Contains("SECRET", StringComparison.Ordinal));
    }

    // Criar até uma execução marcada como sucesso não altera a última importação da conexão.
    [TestMethod]
    public void SyncRunCreationDoesNotAdvanceConnectionSuccessTimestamp()
    {
        var lastSuccess = CreatedAtUtc.AddDays(-2);
        var connection = FinancialConnection.Create(
            ConnectionId,
            UserId,
            "pluggy",
            "external-item",
            lastSuccessfulImportAtUtc: lastSuccess);

        var run = CreateSyncRun(
            createdAtUtc: CreatedAtUtc,
            startedAtUtc: CreatedAtUtc.AddMinutes(1),
            finishedAtUtc: CreatedAtUtc.AddMinutes(2),
            state: SyncRunState.Succeeded,
            coverageState: SyncCoverageState.CompleteForRequestedWindow);

        Assert.AreEqual(SyncRunState.Succeeded, run.State);
        Assert.AreEqual(lastSuccess, connection.LastSuccessfulImportAtUtc);
    }

    // Registros FNR-013 guardam somente fingerprints SHA-256 e exigem o par; execuções históricas podem omitir ambos.
    [TestMethod]
    public void SyncRunRequiresBothValidIdempotencyFingerprints()
    {
        var keyHash = new string('A', 64);
        var requestFingerprint = new string('B', 64);
        var accepted = CreateSyncRun(
            idempotencyKeyHash: keyHash,
            requestFingerprint: requestFingerprint);
        var historical = CreateSyncRun();

        Assert.AreEqual(keyHash, accepted.IdempotencyKeyHash);
        Assert.AreEqual(requestFingerprint, accepted.RequestFingerprint);
        Assert.IsNull(historical.IdempotencyKeyHash);
        Assert.IsNull(historical.RequestFingerprint);
        Assert.Throws<ArgumentException>(() => CreateSyncRun(idempotencyKeyHash: keyHash));
        Assert.Throws<ArgumentException>(() => CreateSyncRun(requestFingerprint: requestFingerprint));
        Assert.Throws<ArgumentException>(() => CreateSyncRun(
            idempotencyKeyHash: new string('x', 63),
            requestFingerprint: requestFingerprint));
    }

    // Cada fixture usa dados sintéticos e regras já existentes, sem regras positivas de compra.
    private static TransactionFacts Facts(
        string externalId = "external-transaction",
        string accountId = "external-account",
        string description = "descrição fictícia",
        decimal amount = 12.3456m,
        string currencyCode = "BRL",
        string originalDate = "2026-11-17",
        string? type = "DEBIT",
        string? status = "POSTED",
        string? operationType = "TARIFA") => new(
            externalId,
            accountId,
            description,
            amount,
            currencyCode,
            originalDate,
            type,
            status,
            operationType);

    // Normaliza no mesmo dia de referência das fixtures, sem ativar a regra positiva de compra.
    private static TransactionNormalizationResult Normalize(TransactionFacts facts) =>
        TransactionNormalizer.Normalize(facts, ReferenceDate);

    // Usa sempre identidades internas fixas, distintas dos IDs externos das fixtures.
    private static FinancialTransaction CreateTransaction(TransactionNormalizationResult normalization) =>
        FinancialTransaction.Create(
            Guid.Parse("8de2784b-e0fb-436d-956b-14c748a057ed"),
            UserId,
            CardAccountId,
            normalization,
            "transaction-normalizer-v1",
            ReferenceDate);

    // Fixtures de SyncRun usam apenas identificadores, horários e janelas sintéticos UTC.
    private static SyncRun CreateSyncRun(
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? finishedAtUtc = null,
        DateOnly? requestedToExclusive = null,
        int? pagesRead = null,
        int? receivedCount = null,
        int? uniqueCount = null,
        int? persistedCount = null,
        DateOnly? observedFrom = null,
        DateOnly? observedTo = null,
        SyncRunState state = SyncRunState.Pending,
        SyncCoverageState coverageState = SyncCoverageState.Unknown,
        string? failureCode = null,
        string? idempotencyKeyHash = null,
        string? requestFingerprint = null) => SyncRun.Create(
            Guid.Parse("7df73e47-8f6e-441e-8900-1f2e71e0a4a6"),
            UserId,
            ConnectionId,
            createdAtUtc ?? CreatedAtUtc,
            new DateOnly(2026, 11, 1),
            requestedToExclusive,
            startedAtUtc,
            finishedAtUtc,
            state,
            pagesRead,
            receivedCount,
            uniqueCount,
            persistedCount,
            observedFrom,
            observedTo,
            coverageState,
            failureCode,
            idempotencyKeyHash,
            requestFingerprint);
}
