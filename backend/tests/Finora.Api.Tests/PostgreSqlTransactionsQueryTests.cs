using System.Globalization;
using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confirma no PostgreSQL que o filtro de cartão não revela se um ID está ausente ou pertence a outro titular.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlTransactionsQueryTests
{
    private static readonly Guid OwnerId = Guid.Parse("c8353882-a00c-4213-801b-23f0485b3b03");
    private static readonly Guid OtherUserId = Guid.Parse("e3c20710-0b4b-4703-9914-116aa29b5489");
    private static readonly Guid OwnerConnectionId = Guid.Parse("1c65e203-c4fe-4f53-9a63-f46d452eaac6");
    private static readonly Guid OtherConnectionId = Guid.Parse("8ed9514a-1561-451b-9c13-4ca4b7e2a0ef");
    private static readonly Guid OwnerCardId = Guid.Parse("f68a8842-4e67-458f-9868-d108a54136e7");
    private static readonly Guid OtherCardId = Guid.Parse("f9952da0-929b-4c48-ad02-38372f67c7ae");
    private static readonly Guid OwnerSecondConnectionId = Guid.Parse("488b33bd-c0d0-48b5-a4ba-d4258bf1ad45");
    private static readonly Guid OwnerSecondCardId = Guid.Parse("2612b61d-60c6-4b1e-a1ef-493e69c330ee");
    private static readonly Guid SameConnectionOtherCardId = Guid.Parse("766da44a-784a-46aa-9d9e-42c2ca826f31");
    private static readonly Guid OwnerCategoryId = Guid.Parse("8c620338-9ca8-41b6-9ac5-dc5e76501387");
    private static readonly Guid OwnerOtherCategoryId = Guid.Parse("29271f15-615d-4402-816d-4fb3393dbfca");
    private static readonly Guid OtherCategoryId = Guid.Parse("cf69c46f-aa4d-4336-9d75-d81e75d11a8e");
    private static readonly Guid UnknownPendingDateTransactionId = Guid.Parse("17e32e76-a258-46d4-b524-dfbce100551b");
    private static readonly Guid PendingTransactionId = Guid.Parse("563c4c7f-fd98-4148-9a59-d78e4967e315");
    private static readonly Guid OtherConnectionTransactionId = Guid.Parse("8738ab2b-1b38-4a9a-a0bb-489fab4628f9");
    private static readonly Guid OtherOwnerTransactionId = Guid.Parse("0fbec09e-843a-4415-a420-57a1d93d0301");
    private static readonly Guid TieLowTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid TieHighTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000102");
    private static readonly Guid FromBoundaryTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000103");
    private static readonly Guid ToExclusiveBoundaryTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000104");
    private static readonly Guid SameConnectionOtherCardTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000105");
    private static readonly Guid OtherCategoryTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000106");
    private static readonly Guid EffectiveTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000107");
    private static readonly Guid LiteralSearchDecoyTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000108");
    private static readonly Guid NullDateLowTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000109");
    private static readonly Guid NullDateHighTransactionId = Guid.Parse("00000000-0000-0000-0000-000000000110");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 23);
    private static readonly DateTimeOffset PromotionStartedAtUtc = new(2026, 11, 23, 13, 0, 0, TimeSpan.Zero);

    // Cartão próprio é aceito; cartão alheio e ID inexistente retornam o mesmo estado sem filtro validado.
    [TestMethod]
    public async Task ForeignAndMissingCardFiltersAreIndistinguishable()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedTwoOwnersAsync(fixture.Context);

        var handler = new GetTransactionsQueryHandler(
            new SyncRunCurrentConnectionReader(fixture.Context),
            new TransactionCardOwnershipReader(fixture.Context),
            new TransactionMovementReader(fixture.Context));
        var owned = await handler.Handle(
            new GetTransactionsQuery(OwnerId, CardId: OwnerCardId.ToString("D")),
            CancellationToken.None);
        var foreign = await handler.Handle(
            new GetTransactionsQuery(OwnerId, CardId: OtherCardId.ToString("D")),
            CancellationToken.None);
        var missing = await handler.Handle(
            new GetTransactionsQuery(OwnerId, CardId: Guid.Parse("2dc9e58e-24f0-48d7-9403-ff0a833b4ac5").ToString("D")),
            CancellationToken.None);

        Assert.AreEqual(TransactionsQueryStatus.Ready, owned.Status);
        Assert.AreEqual(OwnerCardId, owned.Query?.CardId);
        Assert.AreEqual(TransactionsQueryStatus.CardNotFound, foreign.Status);
        Assert.AreEqual(foreign.Status, missing.Status);
        Assert.IsNull(foreign.Query);
        Assert.IsNull(missing.Query);
    }

    // A projeção preserva estados ambíguos e opcionais, limitando movimentos e anotações ao titular/conexão pedidos.
    [TestMethod]
    public async Task MovementReaderProjectsConservativeDetailsAndExcludesOtherOwnersAndConnections()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedMovementGraphAsync(fixture.Context);

        var reader = new TransactionMovementReader(fixture.Context);
        var result = await reader.ReadAsync(
            new ValidatedTransactionsQuery(OwnerId, OwnerConnectionId, null, null, null, null, null, null, 1, 25),
            CancellationToken.None);
        var movements = result.Items;

        Assert.AreEqual(2, movements.Count);
        CollectionAssert.AreEquivalent(
            new[] { UnknownPendingDateTransactionId, PendingTransactionId },
            movements.Select(movement => movement.Id).ToArray());

        // A ambiguidade de natureza e data permanece explícita junto à anotação pessoal já persistida.
        var ambiguous = movements.Single(movement => movement.Id == UnknownPendingDateTransactionId);
        Assert.AreEqual(OwnerCardId, ambiguous.CardId);
        Assert.AreEqual("movimento ambíguo sintético", ambiguous.Description);
        Assert.AreEqual(17.2500m, ambiguous.Amount);
        Assert.AreEqual("BRL", ambiguous.CurrencyCode);
        Assert.AreEqual("2026-99-99T00:00:00Z", ambiguous.OriginalDate);
        Assert.IsNull(ambiguous.TransactionDate);
        Assert.AreEqual(TransactionExclusionReason.InvalidDate, ambiguous.DateReason);
        Assert.AreEqual(TransactionNature.Unknown, ambiguous.Nature);
        Assert.AreEqual(TransactionExclusionReason.UnknownOperationType, ambiguous.NatureReason);
        Assert.AreEqual(TransactionSituation.Effective, ambiguous.Situation);
        Assert.AreEqual(OwnerCategoryId, ambiguous.CategoryId);
        Assert.AreEqual("nota pessoal do titular", ambiguous.Note);
        Assert.IsNull(ambiguous.InstallmentNumber);
        Assert.IsNull(ambiguous.TotalInstallments);

        // PENDING continua separado da natureza e não ganha categoria, anotação ou parcela por inferência.
        var pending = movements.Single(movement => movement.Id == PendingTransactionId);
        Assert.AreEqual(TransactionNature.Fee, pending.Nature);
        Assert.AreEqual(TransactionSituation.Pending, pending.Situation);
        Assert.AreEqual(TransactionExclusionReason.NotEffective, pending.SituationReason);
        Assert.AreEqual(new DateOnly(2026, 11, 20), pending.TransactionDate);
        Assert.IsNull(pending.InstallmentNumber);
        Assert.IsNull(pending.TotalInstallments);
        Assert.IsNull(pending.CategoryId);
        Assert.IsNull(pending.Note);
    }

    // Combina os filtros validados, trata curingas/escape como texto e ordena datas empatadas/nulas sem entrada SQL livre.
    [TestMethod]
    public async Task MovementReaderCombinesLiteralFiltersAndOrdersDatesDeterministically()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedFilterGraphAsync(fixture.Context);

        var reader = new TransactionMovementReader(fixture.Context);
        var intersection = await reader.ReadAsync(
            CreateValidatedQuery(
                cardId: OwnerCardId,
                categoryId: OwnerCategoryId,
                situation: TransactionSituation.Pending,
                from: "2026-11-19",
                toExclusive: "2026-11-21",
                search: "compra 50%_ \\abc"),
            CancellationToken.None);
        CollectionAssert.AreEqual(
            new[] { TieHighTransactionId, TieLowTransactionId, FromBoundaryTransactionId },
            intersection.Items.Select(movement => movement.Id).ToArray());

        // Sem data, dias conhecidos vêm primeiro; empates e datas nulas usam ID interno decrescente.
        var ordered = await reader.ReadAsync(
            CreateValidatedQuery(cardId: OwnerCardId),
            CancellationToken.None);
        CollectionAssert.AreEqual(
            new[]
            {
                ToExclusiveBoundaryTransactionId,
                LiteralSearchDecoyTransactionId,
                EffectiveTransactionId,
                OtherCategoryTransactionId,
                TieHighTransactionId,
                TieLowTransactionId,
                FromBoundaryTransactionId,
                NullDateHighTransactionId,
                NullDateLowTransactionId
            },
            ordered.Items.Select(movement => movement.Id).ToArray());
    }

    // Contagem precede a paginação, repete-se entre páginas e permanece correta numa página além do fim.
    [TestMethod]
    public async Task MovementReaderCountsBeforePaginationAndReturnsEmptyPageWithRealCount()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedFilterGraphAsync(fixture.Context);

        var reader = new TransactionMovementReader(fixture.Context);
        var first = await reader.ReadAsync(
            CreateValidatedQuery(cardId: OwnerCardId, page: 1, pageSize: 3),
            CancellationToken.None);
        var second = await reader.ReadAsync(
            CreateValidatedQuery(cardId: OwnerCardId, page: 2, pageSize: 3),
            CancellationToken.None);
        var pastEnd = await reader.ReadAsync(
            CreateValidatedQuery(cardId: OwnerCardId, page: 4, pageSize: 3),
            CancellationToken.None);

        Assert.AreEqual(9, first.FilteredCount);
        Assert.AreEqual(first.FilteredCount, second.FilteredCount);
        Assert.AreEqual(first.FilteredCount, pastEnd.FilteredCount);
        Assert.AreEqual(1, first.Page);
        Assert.AreEqual(3, first.PageSize);
        Assert.AreEqual(3, first.Items.Count);
        CollectionAssert.AreEqual(
            new[] { ToExclusiveBoundaryTransactionId, LiteralSearchDecoyTransactionId, EffectiveTransactionId },
            first.Items.Select(movement => movement.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { OtherCategoryTransactionId, TieHighTransactionId, TieLowTransactionId },
            second.Items.Select(movement => movement.Id).ToArray());
        Assert.AreEqual(4, pastEnd.Page);
        Assert.AreEqual(0, pastEnd.Items.Count);
    }

    // A leitura RepeatableRead deve ignorar promoção confirmada depois da contagem, sem misturar página e total.
    [TestMethod]
    public async Task MovementReaderKeepsCountAndPageOnSnapshotDuringConcurrentPromotion()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync();
        await SeedFilterGraphAsync(fixture.Context, includeConnectionIdentity: true);

        var interceptor = new PromoteAfterCountCommandInterceptor(PromoteConcurrentSnapshotAsync);
        await using var readContext = CreateContext(interceptor);
        var result = await new TransactionMovementReader(readContext).ReadAsync(
            CreateValidatedQuery(cardId: OwnerCardId, page: 1, pageSize: 3),
            CancellationToken.None);

        Assert.IsTrue(interceptor.WasTriggered, "A promoção precisa ocorrer após a contagem dentro da leitura.");
        Assert.AreEqual(9, result.FilteredCount);
        CollectionAssert.AreEqual(
            new[] { ToExclusiveBoundaryTransactionId, LiteralSearchDecoyTransactionId, EffectiveTransactionId },
            result.Items.Select(movement => movement.Id).ToArray());

        // Um escopo novo vê o commit da promoção, enquanto a resposta anterior conserva o snapshot inicial.
        await using var verification = CreateContext();
        Assert.AreEqual(10, await verification.Set<FinancialTransaction>()
            .CountAsync(transaction => transaction.UserId == OwnerId && transaction.CardAccountId == OwnerCardId));
        Assert.AreEqual(1, await verification.Set<FinancialTransaction>()
            .CountAsync(transaction => transaction.ExternalId == "concurrent-promotion-transaction"));
    }

    // Semeia movimentos anotados do titular, de outra conexão e de outro usuário para provar ambos os limites no SQL.
    private static async Task SeedMovementGraphAsync(FinoraDbContext context)
    {
        context.Users.AddRange(new ApplicationUser(OwnerId), new ApplicationUser(OtherUserId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(OwnerConnectionId, OwnerId, "pluggy", "owner-movement-item"),
            FinancialConnection.Create(OwnerSecondConnectionId, OwnerId, "pluggy", "owner-second-movement-item"),
            FinancialConnection.Create(OtherConnectionId, OtherUserId, "pluggy", "other-movement-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().AddRange(
            CardAccount.Create(OwnerCardId, OwnerId, OwnerConnectionId, "owner-movement-card", "BRL"),
            CardAccount.Create(OwnerSecondCardId, OwnerId, OwnerSecondConnectionId, "owner-second-movement-card", "BRL"),
            CardAccount.Create(OtherCardId, OtherUserId, OtherConnectionId, "other-movement-card", "USD"));
        context.Set<Category>().AddRange(
            Category.Create(OwnerCategoryId, OwnerId, isSystem: false, "Categoria própria", "categoria-propria"),
            Category.Create(OtherCategoryId, OtherUserId, isSystem: false, "Categoria alheia", "categoria-alheia"));
        await context.SaveChangesAsync();

        context.Set<FinancialTransaction>().AddRange(
            CreateMovement(
                UnknownPendingDateTransactionId,
                OwnerId,
                OwnerCardId,
                "owner-movement-card",
                "unknown-pending-date",
                "movimento ambíguo sintético",
                "2026-99-99T00:00:00Z",
                "POSTED",
                "PAGAMENTO"),
            CreateMovement(
                PendingTransactionId,
                OwnerId,
                OwnerCardId,
                "owner-movement-card",
                "pending-transaction",
                "tarifa pendente sintética",
                "2026-11-20",
                "PENDING",
                "TARIFA"),
            CreateMovement(
                OtherConnectionTransactionId,
                OwnerId,
                OwnerSecondCardId,
                "owner-second-movement-card",
                "other-connection-transaction",
                "movimento de outra conexão",
                "2026-11-19",
                "POSTED",
                "TARIFA"),
            CreateMovement(
                OtherOwnerTransactionId,
                OtherUserId,
                OtherCardId,
                "other-movement-card",
                "other-owner-transaction",
                "movimento de outro titular",
                "2026-11-18",
                "POSTED",
                "TARIFA"));
        await context.SaveChangesAsync();

        context.Set<TransactionAnnotation>().AddRange(
            TransactionAnnotation.Create(
                UnknownPendingDateTransactionId,
                OwnerId,
                OwnerCategoryId,
                OwnerId,
                "nota pessoal do titular",
                new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)),
            TransactionAnnotation.Create(
                OtherConnectionTransactionId,
                OwnerId,
                OwnerCategoryId,
                OwnerId,
                "nota de outra conexão",
                new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)),
            TransactionAnnotation.Create(
                OtherOwnerTransactionId,
                OtherUserId,
                OtherCategoryId,
                OtherUserId,
                "nota de outro titular",
                new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();
    }

    // Cria registros que diferem em uma condição por vez para comprovar interseção, limites e desempates.
    private static async Task SeedFilterGraphAsync(
        FinoraDbContext context,
        bool includeConnectionIdentity = false)
    {
        context.Users.Add(new ApplicationUser(OwnerId));
        await context.SaveChangesAsync();

        context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(OwnerConnectionId, OwnerId, "pluggy", "owner-filter-item"));
        await context.SaveChangesAsync();
        if (includeConnectionIdentity)
        {
            context.Set<FinancialConnectionItemIdentity>().Add(FinancialConnectionItemIdentity.CreateActive(
                OwnerConnectionId, OwnerId, "pluggy", "owner-filter-item"));
            await context.SaveChangesAsync();
        }

        // Dois cartões e duas categorias próprias permitem provar os filtros sem usar identidades de terceiros.
        context.Set<CardAccount>().AddRange(
            CardAccount.Create(OwnerCardId, OwnerId, OwnerConnectionId, "owner-filter-card", "BRL"),
            CardAccount.Create(SameConnectionOtherCardId, OwnerId, OwnerConnectionId, "owner-other-filter-card", "BRL"));
        context.Set<Category>().AddRange(
            Category.Create(OwnerCategoryId, OwnerId, isSystem: false, "Categoria filtrada", "categoria-filtrada"),
            Category.Create(OwnerOtherCategoryId, OwnerId, isSystem: false, "Outra categoria", "outra-categoria"));
        await context.SaveChangesAsync();

        // Descrições coincidem por caixa e substring; o decoy só seria aceito se % e _ virassem curingas.
        const string literalDescription = "Compra 50%_ \\AbC movimento sintético";
        context.Set<FinancialTransaction>().AddRange(
            CreateMovement(TieLowTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-tie-low", literalDescription, "2026-11-20", "PENDING", "TARIFA"),
            CreateMovement(TieHighTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-tie-high", literalDescription, "2026-11-20", "PENDING", "TARIFA"),
            CreateMovement(FromBoundaryTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-from", literalDescription, "2026-11-19", "PENDING", "TARIFA"),
            CreateMovement(ToExclusiveBoundaryTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-to-exclusive", literalDescription, "2026-11-21", "PENDING", "TARIFA"),
            CreateMovement(SameConnectionOtherCardTransactionId, OwnerId, SameConnectionOtherCardId, "owner-other-filter-card", "filter-other-card", literalDescription, "2026-11-20", "PENDING", "TARIFA"),
            CreateMovement(OtherCategoryTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-other-category", literalDescription, "2026-11-20", "PENDING", "TARIFA"),
            CreateMovement(EffectiveTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-effective", literalDescription, "2026-11-20", "POSTED", "TARIFA"),
            CreateMovement(LiteralSearchDecoyTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-decoy", "Compra 50XX \\AbC movimento sintético", "2026-11-20", "PENDING", "TARIFA"),
            CreateMovement(NullDateLowTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-null-date-low", literalDescription, "2026-99-99T00:00:00Z", "PENDING", "TARIFA"),
            CreateMovement(NullDateHighTransactionId, OwnerId, OwnerCardId, "owner-filter-card", "filter-null-date-high", literalDescription, "2026-99-99T00:00:00Z", "PENDING", "TARIFA"));
        await context.SaveChangesAsync();

        // Anotações próprias vinculam categoria e movimento; a categoria alternativa é válida, mas não satisfaz o filtro.
        var annotatedTransactions = new[]
        {
            TieLowTransactionId,
            TieHighTransactionId,
            FromBoundaryTransactionId,
            ToExclusiveBoundaryTransactionId,
            SameConnectionOtherCardTransactionId,
            EffectiveTransactionId,
            LiteralSearchDecoyTransactionId,
            NullDateLowTransactionId,
            NullDateHighTransactionId
        };
        context.Set<TransactionAnnotation>().AddRange(annotatedTransactions.Select(id =>
            TransactionAnnotation.Create(
                id,
                OwnerId,
                OwnerCategoryId,
                OwnerId,
                null,
                new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero))));
        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            OtherCategoryTransactionId,
            OwnerId,
            OwnerOtherCategoryId,
            OwnerId,
            null,
            new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();
    }

    // Constrói critérios somente após validar o mesmo formato e os limites usados pela Application.
    private static ValidatedTransactionsQuery CreateValidatedQuery(
        Guid? cardId = null,
        Guid? categoryId = null,
        TransactionSituation? situation = null,
        string? from = null,
        string? toExclusive = null,
        string? search = null,
        int page = 1,
        int pageSize = 25)
    {
        var request = new GetTransactionsQuery(
            OwnerId,
            CardId: cardId?.ToString("D"),
            CategoryId: categoryId?.ToString("D"),
            Situation: situation?.ToString(),
            From: from,
            ToExclusive: toExclusive,
            Search: search,
            Page: page.ToString(CultureInfo.InvariantCulture),
            PageSize: pageSize.ToString(CultureInfo.InvariantCulture));
        Assert.IsTrue(TransactionQueryFilterValidator.TryValidate(request, out var filters));
        Assert.IsNotNull(filters);

        return new ValidatedTransactionsQuery(
            filters.UserId,
            OwnerConnectionId,
            filters.CardId,
            filters.CategoryId,
            filters.Situation,
            filters.From,
            filters.ToExclusive,
            filters.Search,
            filters.Page,
            filters.PageSize);
    }

    // A promoção real de uma página sintética é disparada pelo interceptor após a primeira consulta COUNT.
    private static async Task PromoteConcurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var snapshot = new TransactionImportSnapshot(
            [new TransactionImportAccount("owner-filter-card", "CREDIT", "CREDIT_CARD", "BRL")],
            [new PluggyTransactionData(
                "concurrent-promotion-transaction",
                "owner-filter-card",
                "movimento promovido durante a leitura",
                21.7500m,
                "BRL",
                "2026-11-22",
                "DEBIT",
                "POSTED",
                "TARIFA")],
            new DateOnly(2026, 11, 1),
            PaginationCompleted: true,
            PagesRead: 1,
            ReceivedCount: 1,
            ObservedFrom: new DateOnly(2026, 11, 22),
            ObservedTo: new DateOnly(2026, 11, 22));
        var connection = new TransactionImportConnection(
            OwnerConnectionId, OwnerId, "pluggy", "owner-filter-item", null);
        var result = await new FinancialTransactionImportPromoter(context).PromoteAsync(
            connection,
            snapshot,
            ReferenceDate,
            PromotionStartedAtUtc,
            PromotionStartedAtUtc.AddMinutes(1),
            cancellationToken);
        Assert.IsTrue(result.Succeeded, result.FailureCode?.ToString());
    }

    // A factory normaliza fatos sintéticos sem alterar dados de origem nem interpretar campos externos adicionais.
    private static FinancialTransaction CreateMovement(
        Guid id,
        Guid userId,
        Guid cardId,
        string externalAccountId,
        string externalId,
        string description,
        string originalDate,
        string status,
        string operationType)
    {
        var facts = new TransactionFacts(
            externalId,
            externalAccountId,
            description,
            17.2500m,
            "BRL",
            originalDate,
            "DEBIT",
            status,
            operationType);
        var normalization = TransactionNormalizer.Normalize(facts, ReferenceDate);
        return FinancialTransaction.Create(
            id,
            userId,
            cardId,
            normalization,
            "transactions-query-test-v1",
            ReferenceDate);
    }

    // O reader usa somente identidades sintéticas coerentes com as FKs compostas da persistência.
    private static async Task SeedTwoOwnersAsync(FinoraDbContext context)
    {
        context.Users.AddRange(new ApplicationUser(OwnerId), new ApplicationUser(OtherUserId));
        await context.SaveChangesAsync();
        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(OwnerConnectionId, OwnerId, "pluggy", "owner-item"),
            FinancialConnection.Create(OtherConnectionId, OtherUserId, "pluggy", "other-item"));
        await context.SaveChangesAsync();
        context.Set<CardAccount>().AddRange(
            CardAccount.Create(OwnerCardId, OwnerId, OwnerConnectionId, "owner-card", "BRL"),
            CardAccount.Create(OtherCardId, OtherUserId, OtherConnectionId, "other-card", "USD"));
        await context.SaveChangesAsync();
    }

    // Contextos independentes permitem uma promoção concorrente sem compartilhar o DbContext da consulta.
    private static FinoraDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, settings.ConnectionString);
        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new FinoraDbContext(optionsBuilder.Options);
    }

    // O callback ocorre após o SELECT COUNT executar e antes de a consulta materializar os itens da página.
    private sealed class PromoteAfterCountCommandInterceptor(
        Func<CancellationToken, Task> promote) : DbCommandInterceptor
    {
        private int _triggered;

        public bool WasTriggered => Volatile.Read(ref _triggered) == 1;

        public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            System.Data.Common.DbCommand command,
            CommandExecutedEventData eventData,
            System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _triggered, 1) == 0)
            {
                await promote(cancellationToken);
            }

            return result;
        }
    }
}
