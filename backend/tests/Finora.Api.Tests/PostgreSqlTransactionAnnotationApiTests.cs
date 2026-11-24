using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Valida o contrato HTTP do PATCH em host real, com sessão PostgreSQL descartável e handler isolado.
[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlTransactionAnnotationApiTests
{
    private static readonly Guid MovementId = Guid.Parse("c55a2ea3-d89c-45f5-aa7a-a8141b1b0001");
    private static readonly Guid CategoryId = Guid.Parse("8e6f8998-a941-4e12-a963-60b529a0ab01");
    private static readonly Guid MovementConnectionId = Guid.Parse("1ac2d213-8b7f-4c30-baa6-8a3d78b10001");
    private static readonly Guid OtherConnectionId = Guid.Parse("da515ea6-412c-4b26-90d9-a476f1b10001");
    private static readonly Guid OwnerCardId = Guid.Parse("591dc009-6e5e-4e0b-a93e-023f0b1b0001");
    private static readonly Guid OtherCardId = Guid.Parse("cc687988-b6c7-42ae-900c-45dd21b10001");
    private static readonly Guid OtherMovementId = Guid.Parse("14f69c8f-1e35-4ced-8bd5-61a628b10001");
    private static readonly Guid OtherCategoryId = Guid.Parse("f82029f7-f36c-4411-82d8-ae4023b10001");
    private static readonly Guid MissingCategoryId = Guid.Parse("324b9ee6-16ca-4ccf-9d24-5084dcb10001");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 24);

    // O caminho feliz confirma presença dos campos, titular derivado da sessão e projeção HTTP allowlist.
    [TestMethod]
    public async Task PatchReturnsOnlyPersistedAnnotationFields()
    {
        var observed = new CapturedAnnotationCommand();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(ConfigureHandler(observed));
        var userId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();

        using var response = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{CategoryId:D}\",\"note\":\"nota sintética\"}}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        CollectionAssert.AreEquivalent(new[] { "categoryId", "note" },
            body.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(CategoryId, body.RootElement.GetProperty("categoryId").GetGuid());
        Assert.AreEqual("nota sintética", body.RootElement.GetProperty("note").GetString());
        Assert.AreEqual(1, observed.CallCount);
        Assert.AreEqual(userId, observed.Command!.UserId);
        Assert.AreEqual(MovementId, observed.Command.TransactionId);
        Assert.IsTrue(observed.Command.HasCategoryId);
        Assert.AreEqual(CategoryId, observed.Command.CategoryId);
        Assert.IsTrue(observed.Command.HasNote);
        Assert.AreEqual("nota sintética", observed.Command.Note);
        Assert.IsFalse(json.Contains("userId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("amount", StringComparison.OrdinalIgnoreCase));
    }

    // Omissão e null chegam distintos à Application; null/string vazia limpam somente o campo presente.
    [TestMethod]
    public async Task PatchPreservesOmittedFieldsAndRepresentsIndependentClears()
    {
        var observed = new CapturedAnnotationCommand();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(ConfigureHandler(observed));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();

        using var omitted = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"mantida\"}");
        Assert.AreEqual(HttpStatusCode.OK, omitted.StatusCode);
        Assert.IsFalse(observed.Command!.HasCategoryId);
        Assert.IsTrue(observed.Command.HasNote);

        using var clearCategory = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"categoryId\":null}");
        Assert.AreEqual(HttpStatusCode.OK, clearCategory.StatusCode);
        Assert.IsTrue(observed.Command!.HasCategoryId);
        Assert.IsNull(observed.Command.CategoryId);
        Assert.IsFalse(observed.Command.HasNote);

        using var clearNote = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":null}");
        Assert.AreEqual(HttpStatusCode.OK, clearNote.StatusCode);
        Assert.IsTrue(observed.Command!.HasNote);
        Assert.IsNull(observed.Command.Note);

        using var clearEmptyNote = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"\"}");
        Assert.AreEqual(HttpStatusCode.OK, clearEmptyNote.StatusCode);
        Assert.IsTrue(observed.Command!.HasNote);
        Assert.IsNull(observed.Command.Note);
    }

    // Corpos vazios, extras, duplicados, tipos/UUIDs inválidos e observações acima do limite não despacham comandos.
    [TestMethod]
    public async Task PatchRejectsBodiesOutsideTheStrictContract()
    {
        var observed = new CapturedAnnotationCommand();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(ConfigureHandler(observed));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var invalidBodies = new[]
        {
            "", "{}", "null", "[]", "{", "{\"categoryId\":\"not-a-uuid\"}",
            "{\"categoryId\":\"00000000-0000-0000-0000-000000000000\"}",
            "{\"categoryId\":7}", "{\"note\":7}", "{\"note\":true}",
            "{\"note\":\"" + new string('x', 501) + "\"}",
            "{\"categoryId\":null,\"categoryId\":null}", "{\"note\":null,\"note\":null}",
            "{\"userId\":\"" + Guid.NewGuid().ToString("D") + "\"}",
            "{\"categoryId\":null,\"description\":\"origem\"}"
        };

        foreach (var json in invalidBodies)
        {
            using var response = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), json);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");
        }

        using var malformedPath = await SendPatchAsync(fixture.Client, token, "not-a-uuid", "{\"note\":\"x\"}");
        await AssertProblemAsync(malformedPath, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");
        using var emptyPath = await SendPatchAsync(fixture.Client, token, Guid.Empty.ToString("D"), "{\"note\":\"x\"}");
        await AssertProblemAsync(emptyPath, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");
        Assert.AreEqual(0, observed.CallCount);
    }

    // O limite inclusivo de 500 caracteres é preservado exatamente e valores maiores já foram recusados acima.
    [TestMethod]
    public async Task PatchAcceptsNoteAtExactlyFiveHundredCharacters()
    {
        var observed = new CapturedAnnotationCommand();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(ConfigureHandler(observed));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var note = new string('n', 500);

        using var response = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            JsonSerializer.Serialize(new { note }));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(note, observed.Command!.Note);
    }

    // A sessão revogada recebe challenge Bearer antes do command e não alcança o handler de edição.
    [TestMethod]
    public async Task PatchRejectsRevokedSessionBeforeDispatch()
    {
        var observed = new CapturedAnnotationCommand();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(ConfigureHandler(observed));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var sessionId = new JsonWebToken(token).Id;
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AuthSessions\" SET \"RevokedAtUtc\"={fixture.Clock.GetUtcNow()} WHERE \"Id\"={sessionId}");

        using var response = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"x\"}");

        await PostgreSqlBearerTests.AssertChallengeAsync(response);
        Assert.AreEqual(0, observed.CallCount);
    }

    // Ausência/alheamento de movimento ou categoria mapeiam ao mesmo 404 fixo sem ecoar identificadores.
    [TestMethod]
    public async Task PatchMapsMissingMovementAndCategoryToIndistinguishableNotFound()
    {
        var observed = new CapturedAnnotationCommand();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(ConfigureHandler(observed));
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();

        observed.ResultStatus = UpdateTransactionAnnotationStatus.TransactionNotFound;
        using var missingMovement = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"x\"}");
        var movementProblem = await AssertProblemAsync(missingMovement, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        observed.ResultStatus = UpdateTransactionAnnotationStatus.CategoryNotFound;
        using var unavailableCategory = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{CategoryId:D}\"}}");
        var categoryProblem = await AssertProblemAsync(unavailableCategory, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        // TDD §11 fixa status, código e título; type/instance/details não integram o contrato comum publicado.
        Assert.AreEqual(movementProblem.GetProperty("status").GetInt32(), categoryProblem.GetProperty("status").GetInt32());
        Assert.AreEqual(movementProblem.GetProperty("code").GetString(), categoryProblem.GetProperty("code").GetString());
        Assert.AreEqual(movementProblem.GetProperty("title").GetString(), categoryProblem.GetProperty("title").GetString());
        Assert.IsFalse((await missingMovement.Content.ReadAsStringAsync()).Contains(MovementId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse((await unavailableCategory.Content.ReadAsStringAsync()).Contains(CategoryId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    // O adapter real distingue movimento próprio de alheio/ausente sem revelar titularidade nem gravar no caso negado.
    [TestMethod]
    public async Task RealStoreAuthorizesOnlyOwnedMovementsAndHidesMissingResources()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("other-annotation@example.invalid");
        await SeedMovementGraphAsync(fixture.Context, ownerId, otherUserId);

        using var ownMovement = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"nota própria\"}");
        Assert.AreEqual(HttpStatusCode.OK, ownMovement.StatusCode);

        using var otherMovement = await SendPatchAsync(fixture.Client, token, OtherMovementId.ToString("D"), "{\"note\":\"não autorizado\"}");
        var otherProblem = await AssertProblemAsync(otherMovement, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        using var absentMovement = await SendPatchAsync(fixture.Client, token, Guid.NewGuid().ToString("D"), "{\"note\":\"não existe\"}");
        var absentProblem = await AssertProblemAsync(absentMovement, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        AssertEquivalentProblem(otherProblem, absentProblem);
        Assert.IsFalse((await otherMovement.Content.ReadAsStringAsync()).Contains(OtherMovementId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, await fixture.Context.Set<TransactionAnnotation>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Set<SyncRun>().CountAsync());
        Assert.AreNotEqual(ownerId, otherUserId);
    }

    // Categorias próprias e de sistema são aceitas; alheias e ausentes compartilham 404 e não alteram a anotação.
    [TestMethod]
    public async Task RealStoreValidatesCategoryScopeAndPreservesIndependentFields()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("other-category-annotation@example.invalid");
        await SeedMovementGraphAsync(fixture.Context, ownerId, otherUserId);

        using var personal = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{CategoryId:D}\",\"note\":\"observação persistida\"}}");
        Assert.AreEqual(HttpStatusCode.OK, personal.StatusCode);
        using var system = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{SystemCategoryCatalog.Categories[0].Id:D}\"}}");
        Assert.AreEqual(HttpStatusCode.OK, system.StatusCode);
        using var clearCategory = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"categoryId\":null}");
        Assert.AreEqual(HttpStatusCode.OK, clearCategory.StatusCode);

        using var foreign = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{OtherCategoryId:D}\"}}");
        var foreignProblem = await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        using var missing = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{MissingCategoryId:D}\"}}");
        var missingProblem = await AssertProblemAsync(missing, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        AssertEquivalentProblem(foreignProblem, missingProblem);
        fixture.Context.ChangeTracker.Clear();
        var annotation = await fixture.Context.Set<TransactionAnnotation>().SingleAsync();
        Assert.IsNull(annotation.CategoryId);
        Assert.IsNull(annotation.CategoryScopeId);
        Assert.AreEqual("observação persistida", annotation.Note);

        using var restoreSystemCategory = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{SystemCategoryCatalog.Categories[0].Id:D}\"}}");
        Assert.AreEqual(HttpStatusCode.OK, restoreSystemCategory.StatusCode);
        using var clearNote = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":null}");
        Assert.AreEqual(HttpStatusCode.OK, clearNote.StatusCode);
        fixture.Context.ChangeTracker.Clear();
        annotation = await fixture.Context.Set<TransactionAnnotation>().SingleAsync();
        Assert.AreEqual(SystemCategoryCatalog.Categories[0].Id, annotation.CategoryId);
        Assert.AreEqual(Guid.Empty, annotation.CategoryScopeId);
        Assert.IsNull(annotation.Note);
        Assert.AreEqual(0, await fixture.Context.Set<SyncRun>().CountAsync());
    }

    // O limite protegido bloqueia a segunda escrita antes do handler e mantém o primeiro commit intacto.
    [TestMethod]
    public async Task PatchUsesProtectedRateLimitAndDoesNotApplyLimitedEdit()
    {
        var rateLimit = new Dictionary<string, string?>
        {
            ["RateLimiting:Protected:PermitLimit"] = "1",
            ["RateLimiting:Protected:WindowSeconds"] = "60"
        };
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(apiConfiguration: rateLimit);
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("rate-limit-other@example.invalid");
        await SeedMovementGraphAsync(fixture.Context, ownerId, otherUserId);

        using var accepted = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"primeira edição\"}");
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        using var limited = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"edição bloqueada\"}");
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "RATE_LIMITED");

        fixture.Context.ChangeTracker.Clear();
        var annotation = await fixture.Context.Set<TransactionAnnotation>().SingleAsync();
        Assert.AreEqual("primeira edição", annotation.Note);
        Assert.AreEqual(0, await fixture.Context.Set<SyncRun>().CountAsync());
    }

    // O adapter usa o PK/FK composto já aplicado; o PostgreSQL atual rejeita duplicidade e movimento sem pai.
    [TestMethod]
    public async Task RealStoreKeepsExistingAnnotationPrimaryAndForeignKeys()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("constraints-other@example.invalid");
        await SeedMovementGraphAsync(fixture.Context, ownerId, otherUserId);

        using var created = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"anotação única\"}");
        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(async () =>
            await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TransactionAnnotation"
                    ("TransactionId", "UserId", "CategoryId", "CategoryScopeId", "Note", "UpdatedAtUtc")
                VALUES ({MovementId}, {ownerId}, NULL, NULL, 'duplicada', {fixture.Clock.GetUtcNow()});
                """));
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);

        fixture.Context.ChangeTracker.Clear();
        fixture.Context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            Guid.NewGuid(), ownerId, null, null, "sem movimento pai", fixture.Clock.GetUtcNow()));
        var missingParent = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
        Assert.IsInstanceOfType<PostgresException>(missingParent.InnerException);
        Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)missingParent.InnerException!).SqlState);
    }

    // Duas primeiras escritas ficam em fila no lock PostgreSQL e a segunda aplica sua alteração sobre o commit anterior.
    [TestMethod]
    public async Task ConcurrentFirstWritesSerializeAndPreserveIndependentFields()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("concurrent-other@example.invalid");
        await SeedMovementGraphAsync(fixture.Context, ownerId, otherUserId);
        var (lockConnection, blocker) = await HoldMovementRowLockAsync(fixture.ConnectionString);
        await using var ownedLockConnection = lockConnection;
        await using var ownedBlocker = blocker;

        var categoryWrite = SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), $"{{\"categoryId\":\"{CategoryId:D}\"}}");
        await WaitForMovementLockWaiterCountAsync(fixture, expected: 1);
        var noteWrite = SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"concorrente\"}");
        await WaitForMovementLockWaiterCountAsync(fixture, expected: 2);

        await ownedBlocker.CommitAsync();
        using var categoryResponse = await categoryWrite;
        using var noteResponse = await noteWrite;
        Assert.AreEqual(HttpStatusCode.OK, categoryResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, noteResponse.StatusCode);
        fixture.Context.ChangeTracker.Clear();
        var annotation = await fixture.Context.Set<TransactionAnnotation>().SingleAsync();
        Assert.AreEqual(CategoryId, annotation.CategoryId);
        Assert.AreEqual(ownerId, annotation.CategoryScopeId);
        Assert.AreEqual("concorrente", annotation.Note);
        Assert.AreEqual(1, await fixture.Context.Set<TransactionAnnotation>().CountAsync());
    }

    // Cancelamento durante espera do lock remove o waiter; falha no SaveChanges também reverte a transação inteira.
    [TestMethod]
    public async Task CancellationAndDatabaseConstraintFailureLeaveNoPartialAnnotation()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("rollback-other@example.invalid");
        await SeedMovementGraphAsync(fixture.Context, ownerId, otherUserId);
        var (lockConnection, blocker) = await HoldMovementRowLockAsync(fixture.ConnectionString);
        await using var ownedLockConnection = lockConnection;
        await using var ownedBlocker = blocker;
        using var cancellation = new CancellationTokenSource();
        var cancelledWrite = SendPatchAsync(
            fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"cancelada\"}", cancellation.Token);
        await WaitForMovementLockWaiterCountAsync(fixture, expected: 1);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelledWrite);
        await WaitForMovementLockWaiterCountAsync(fixture, expected: 0);
        await ownedBlocker.RollbackAsync();
        Assert.AreEqual(0, await fixture.Context.Set<TransactionAnnotation>().CountAsync());

        const string constraintName = "CK_Test_Annotation_RollbackSentinel";
        await fixture.Context.Database.ExecuteSqlRawAsync($"ALTER TABLE \"TransactionAnnotation\" ADD CONSTRAINT \"{constraintName}\" CHECK (\"Note\" <> 'rollback-sentinel')");
        try
        {
            using var rejected = await SendPatchAsync(
                fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"rollback-sentinel\"}");
            await AssertProblemAsync(rejected, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE");
            Assert.AreEqual(0, await fixture.Context.Set<TransactionAnnotation>().CountAsync());
        }
        finally
        {
            await fixture.Context.Database.ExecuteSqlRawAsync($"ALTER TABLE \"TransactionAnnotation\" DROP CONSTRAINT IF EXISTS \"{constraintName}\"");
        }
    }

    // A promoção bloqueia primeiro a conexão; a edição não bloqueia essa mesma linha e evita inversão/deadlock.
    [TestMethod]
    public async Task AnnotationEditCompletesWhilePromotionConnectionLockIsHeld()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("lock-order-other@example.invalid");
        await SeedMovementGraphAsync(fixture.Context, ownerId, otherUserId);
        var (lockConnection, blocker) = await HoldConnectionRowLockAsync(fixture.ConnectionString, MovementConnectionId);
        await using var ownedLockConnection = lockConnection;
        await using var ownedBlocker = blocker;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await SendPatchAsync(
            fixture.Client, token, MovementId.ToString("D"), "{\"note\":\"sem lock da conexão\"}", deadline.Token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        await ownedBlocker.RollbackAsync();
        Assert.AreEqual(1, await fixture.Context.Set<TransactionAnnotation>().CountAsync());
    }

    // Leitura, troca/limpeza e promoção/reconexão sintéticas refletem a anotação sem criar totais nem chamar provedor.
    [TestMethod]
    public async Task ReadsReflectEditsAndPreserveAnnotationsAcrossSyntheticReimportAndReconnection()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("query-other@example.invalid");
        await SeedMovementGraphAsync(fixture.Context, ownerId, otherUserId);
        var systemCategoryId = SystemCategoryCatalog.Categories[0].Id;

        using var firstEdit = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{CategoryId:D}\",\"note\":\"anotação persistente\"}}");
        Assert.AreEqual(HttpStatusCode.OK, firstEdit.StatusCode);
        var personalPage = await ReadTransactionsPageAsync(fixture.Client, token, $"?categoryId={CategoryId:D}");
        Assert.AreEqual(1, personalPage.GetProperty("filteredCount").GetInt32());
        var personalMovement = personalPage.GetProperty("items")[0];
        Assert.AreEqual(MovementId, personalMovement.GetProperty("id").GetGuid());
        Assert.AreEqual(CategoryId, personalMovement.GetProperty("categoryId").GetGuid());
        Assert.AreEqual("anotação persistente", personalMovement.GetProperty("note").GetString());
        Assert.AreEqual(0, await fixture.Context.Set<SyncRun>().CountAsync());

        using var changeCategory = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"),
            $"{{\"categoryId\":\"{systemCategoryId:D}\"}}");
        Assert.AreEqual(HttpStatusCode.OK, changeCategory.StatusCode);
        var oldFilter = await ReadTransactionsPageAsync(fixture.Client, token, $"?categoryId={CategoryId:D}");
        var systemFilter = await ReadTransactionsPageAsync(fixture.Client, token, $"?categoryId={systemCategoryId:D}");
        Assert.AreEqual(0, oldFilter.GetProperty("filteredCount").GetInt32());
        Assert.AreEqual(1, systemFilter.GetProperty("filteredCount").GetInt32());
        Assert.AreEqual("anotação persistente", systemFilter.GetProperty("items")[0].GetProperty("note").GetString());
        Assert.AreEqual(0, await fixture.Context.Set<SyncRun>().CountAsync());

        fixture.Context.ChangeTracker.Clear();
        var firstPromotion = await PromoteSyntheticSnapshotAsync(
            fixture.Context,
            ownerId,
            new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 24, 12, 1, 0, TimeSpan.Zero),
            "descrição após reimportação sintética",
            87.65m);
        Assert.IsTrue(firstPromotion.Succeeded);
        fixture.Context.ChangeTracker.Clear();
        var afterImport = await ReadTransactionsPageAsync(fixture.Client, token, $"?categoryId={systemCategoryId:D}");
        var importedMovement = afterImport.GetProperty("items")[0];
        Assert.AreEqual(1, afterImport.GetProperty("filteredCount").GetInt32());
        Assert.AreEqual(MovementId, importedMovement.GetProperty("id").GetGuid());
        Assert.AreEqual("descrição após reimportação sintética", importedMovement.GetProperty("description").GetString());
        Assert.AreEqual(87.65m, importedMovement.GetProperty("amount").GetDecimal());
        Assert.AreEqual(systemCategoryId, importedMovement.GetProperty("categoryId").GetGuid());
        Assert.AreEqual("anotação persistente", importedMovement.GetProperty("note").GetString());
        Assert.IsFalse(importedMovement.TryGetProperty("externalId", out _));
        Assert.IsFalse(importedMovement.TryGetProperty("userId", out _));
        Assert.AreEqual(1, await fixture.Context.Set<SyncRun>().CountAsync());

        fixture.Context.ChangeTracker.Clear();
        var reconnection = await PromoteSyntheticSnapshotAsync(
            fixture.Context,
            ownerId,
            new DateTimeOffset(2026, 11, 24, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 24, 14, 1, 0, TimeSpan.Zero),
            "descrição após reconexão sintética",
            45.67m,
            "synthetic-owner-item-reconnected");
        Assert.IsTrue(reconnection.Succeeded);
        fixture.Context.ChangeTracker.Clear();
        var afterReconnection = await ReadTransactionsPageAsync(fixture.Client, token, $"?categoryId={systemCategoryId:D}");
        var reconnectedMovement = afterReconnection.GetProperty("items")[0];
        Assert.AreEqual(MovementId, reconnectedMovement.GetProperty("id").GetGuid());
        Assert.AreEqual("descrição após reconexão sintética", reconnectedMovement.GetProperty("description").GetString());
        Assert.AreEqual(45.67m, reconnectedMovement.GetProperty("amount").GetDecimal());
        Assert.AreEqual(systemCategoryId, reconnectedMovement.GetProperty("categoryId").GetGuid());
        Assert.AreEqual("anotação persistente", reconnectedMovement.GetProperty("note").GetString());
        Assert.AreEqual(2, await fixture.Context.Set<SyncRun>().CountAsync());

        using var clearCategory = await SendPatchAsync(fixture.Client, token, MovementId.ToString("D"), "{\"categoryId\":null}");
        Assert.AreEqual(HttpStatusCode.OK, clearCategory.StatusCode);
        Assert.AreEqual(2, await fixture.Context.Set<SyncRun>().CountAsync());
        var clearedFilter = await ReadTransactionsPageAsync(fixture.Client, token, $"?categoryId={systemCategoryId:D}");
        var unfiltered = await ReadTransactionsPageAsync(fixture.Client, token, string.Empty);
        Assert.AreEqual(0, clearedFilter.GetProperty("filteredCount").GetInt32());
        Assert.AreEqual(1, unfiltered.GetProperty("filteredCount").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, unfiltered.GetProperty("items")[0].GetProperty("categoryId").ValueKind);
        Assert.AreEqual("anotação persistente", unfiltered.GetProperty("items")[0].GetProperty("note").GetString());
        CollectionAssert.AreEquivalent(new[] { "items", "page", "pageSize", "filteredCount", "coverage" },
            unfiltered.EnumerateObject().Select(property => property.Name).ToArray());
    }

    // Insere somente grafo financeiro sintético e categorias pessoais para testar os predicados reais do adapter.
    private static async Task SeedMovementGraphAsync(FinoraDbContext context, Guid ownerId, Guid otherUserId)
    {
        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(MovementConnectionId, ownerId, "pluggy", "synthetic-owner-item"),
            FinancialConnection.Create(OtherConnectionId, otherUserId, "pluggy", "synthetic-other-item"));
        await context.SaveChangesAsync();

        // A identidade externa ativa habilita somente a promoção sintética local, sem criar cliente ou chamada de provedor.
        context.Set<FinancialConnectionItemIdentity>().AddRange(
            FinancialConnectionItemIdentity.CreateActive(MovementConnectionId, ownerId, "pluggy", "synthetic-owner-item"),
            FinancialConnectionItemIdentity.CreateActive(OtherConnectionId, otherUserId, "pluggy", "synthetic-other-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().AddRange(
            CardAccount.Create(OwnerCardId, ownerId, MovementConnectionId, "synthetic-owner-card", "BRL"),
            CardAccount.Create(OtherCardId, otherUserId, OtherConnectionId, "synthetic-other-card", "BRL"));
        context.Set<Category>().AddRange(
            Category.Create(CategoryId, ownerId, isSystem: false, "Categoria do titular", "categoria-do-titular"),
            Category.Create(OtherCategoryId, otherUserId, isSystem: false, "Categoria alheia", "categoria-alheia"));
        await context.SaveChangesAsync();

        context.Set<FinancialTransaction>().AddRange(
            CreateMovement(MovementId, ownerId, OwnerCardId, "synthetic-owner-card", "synthetic-owner-transaction"),
            CreateMovement(OtherMovementId, otherUserId, OtherCardId, "synthetic-other-card", "synthetic-other-transaction"));
        await context.SaveChangesAsync();
    }

    // Cria movimentos com os normalizadores de domínio existentes; nenhum dado de origem real é utilizado.
    private static FinancialTransaction CreateMovement(
        Guid id,
        Guid userId,
        Guid cardId,
        string externalAccountId,
        string externalId)
    {
        var facts = new TransactionFacts(
            externalId,
            externalAccountId,
            "descrição sintética de teste",
            12.34m,
            "BRL",
            "2026-11-24",
            "DEBIT",
            "POSTED",
            "TARIFA");
        var normalized = TransactionNormalizer.Normalize(facts, ReferenceDate);
        return FinancialTransaction.Create(id, userId, cardId, normalized, "annotation-test-v1", ReferenceDate);
    }

    // O mesmo snapshot sintético atualiza a identidade interna; o argumento opcional exercita reconexão conservadora.
    private static async Task<TransactionImportPromotionResult> PromoteSyntheticSnapshotAsync(
        FinoraDbContext context,
        Guid userId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        string description,
        decimal amount,
        string? replacementExternalItemId = null)
    {
        var connection = await context.Set<FinancialConnection>().AsNoTracking()
            .SingleAsync(value => value.Id == MovementConnectionId && value.UserId == userId);
        var importConnection = new TransactionImportConnection(
            connection.Id,
            connection.UserId,
            connection.Provider,
            connection.ExternalItemId,
            connection.LastSuccessfulImportAtUtc);
        var snapshot = new TransactionImportSnapshot(
            [new TransactionImportAccount("synthetic-owner-card", "CREDIT", "CREDIT_CARD", "BRL")],
            [new PluggyTransactionData(
                "synthetic-owner-transaction", "synthetic-owner-card", description, amount,
                "BRL", "2026-11-23", "DEBIT", "POSTED", "TARIFA")],
            new DateOnly(2025, 11, 24),
            PaginationCompleted: true,
            PagesRead: 1,
            ReceivedCount: 1);
        var promoter = new FinancialTransactionImportPromoter(context);
        return replacementExternalItemId is null
            ? await promoter.PromoteAsync(
                importConnection,
                snapshot,
                ReferenceDate,
                startedAtUtc,
                finishedAtUtc,
                CancellationToken.None)
            : await promoter.PromoteReconnectionAsync(
                importConnection,
                replacementExternalItemId,
                snapshot,
                ReferenceDate,
                startedAtUtc,
                finishedAtUtc,
                CancellationToken.None);
    }

    // A leitura HTTP deixa explícito que editar e filtrar movimentos não inicia importação.
    private static async Task<JsonElement> ReadTransactionsPageAsync(HttpClient client, string token, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/transactions" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    // Cada request usa seu próprio Bearer e corpo JSON para exercitar roteamento, autenticação e parsing HTTP.
    private static async Task<HttpResponseMessage> SendPatchAsync(
        HttpClient client,
        string token,
        string movementId,
        string json,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/transactions/{movementId}/annotation");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.SendAsync(request, cancellationToken);
    }

    // Mantém um lock PostgreSQL real em uma linha até que o cenário concorrente libere a transação.
    private static Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> HoldMovementRowLockAsync(
        string connectionString) => HoldRowLockAsync(
        connectionString,
        "SELECT \"Id\" FROM \"FinancialTransaction\" WHERE \"Id\" = @row_id FOR UPDATE",
        MovementId);

    // Simula somente a primeira aquisição feita pela promoção para detectar dependência invertida na edição.
    private static Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> HoldConnectionRowLockAsync(
        string connectionString,
        Guid connectionId) => HoldRowLockAsync(
        connectionString,
        "SELECT \"Id\" FROM \"FinancialConnection\" WHERE \"Id\" = @row_id FOR UPDATE",
        connectionId);

    // Uma conexão independente mantém a transação aberta; o comando de teste não usa sleeps como barreira.
    private static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> HoldRowLockAsync(
        string connectionString,
        string commandText,
        Guid rowId)
    {
        var connection = new NpgsqlConnection(connectionString);
        NpgsqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync();
            transaction = await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            command.Parameters.AddWithValue("row_id", rowId);
            _ = await command.ExecuteScalarAsync();
            return (connection, transaction);
        }
        catch
        {
            if (transaction is not null) { await transaction.DisposeAsync(); }
            await connection.DisposeAsync();
            throw;
        }
    }

    // A barreira observa wait_event PostgreSQL da consulta FOR UPDATE, com deadline para falhar sem travar a suíte.
    private static async Task WaitForMovementLockWaiterCountAsync(
        PostgreSqlAuthenticationFixture fixture,
        int expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var waiters = await fixture.Context.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value"
                FROM pg_stat_activity
                WHERE datname = current_database()
                    AND wait_event_type = 'Lock'
                    AND query LIKE '%FOR UPDATE OF movement%'
                """).SingleAsync(deadline.Token);
            if (waiters == expected) { return; }
            await Task.Delay(10, deadline.Token);
        }
    }

    // O fake deixa os testes HTTP focados no binding e na allowlist; a lógica de negócio será testada separadamente.
    private static Action<IServiceCollection> ConfigureHandler(CapturedAnnotationCommand captured) => services =>
    {
        services.RemoveAll<IRequestHandler<UpdateTransactionAnnotationCommand, UpdateTransactionAnnotationResult>>();
        services.AddScoped<IRequestHandler<UpdateTransactionAnnotationCommand, UpdateTransactionAnnotationResult>>(
            _ => new CapturingAnnotationHandler(captured));
    };

    // O estado capturado comprova que a fronteira preserva presença e valor sem receber titular do cliente.
    private sealed class CapturedAnnotationCommand
    {
        public int CallCount { get; private set; }
        public UpdateTransactionAnnotationCommand? Command { get; private set; }
        public UpdateTransactionAnnotationStatus ResultStatus { get; set; } = UpdateTransactionAnnotationStatus.Updated;

        public UpdateTransactionAnnotationResult Handle(UpdateTransactionAnnotationCommand command)
        {
            CallCount++;
            Command = command;
            return new(ResultStatus, command.CategoryId,
                command.HasNote ? command.Note : "nota previamente preservada");
        }
    }

    // O double retorna somente os campos da allowlist e não acessa entidades financeiras.
    private sealed class CapturingAnnotationHandler(CapturedAnnotationCommand captured)
        : IRequestHandler<UpdateTransactionAnnotationCommand, UpdateTransactionAnnotationResult>
    {
        public Task<UpdateTransactionAnnotationResult> Handle(
            UpdateTransactionAnnotationCommand request,
            CancellationToken cancellationToken) => Task.FromResult(captured.Handle(request));
    }

    // O envelope de erro fixa status/código e impede ecoar entradas inválidas ou sentinelas de dados.
    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        Assert.AreEqual((int)status, body.RootElement.GetProperty("status").GetInt32());
        Assert.AreEqual(code, body.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(json.Contains("sentinel", StringComparison.OrdinalIgnoreCase));
        return body.RootElement.Clone();
    }

    // A comparação cobre somente os campos estáveis do contrato comum de erros, sem presumir type ou instance.
    private static void AssertEquivalentProblem(JsonElement expected, JsonElement actual)
    {
        Assert.AreEqual(expected.GetProperty("status").GetInt32(), actual.GetProperty("status").GetInt32());
        Assert.AreEqual(expected.GetProperty("code").GetString(), actual.GetProperty("code").GetString());
        Assert.AreEqual(expected.GetProperty("title").GetString(), actual.GetProperty("title").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(actual.GetProperty("title").GetString()));
    }
}
