using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita contrato, autorização e falhas da rota real sem consultar Pluggy ou usar dados financeiros reais.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlTransactionsApiTests
{
    private static readonly Guid OwnerConnectionId = Guid.Parse("3e70dfc9-b534-47d5-9bfd-f39fb38fe45e");
    private static readonly Guid ForeignConnectionId = Guid.Parse("472bda54-0e35-4af3-8e09-d8a0bcab493d");
    private static readonly Guid SecondOwnerConnectionId = Guid.Parse("39547c16-2fa0-4778-988f-dae1e97cc2d9");
    private static readonly Guid BrlCardId = Guid.Parse("f635c14f-d751-4d05-86f7-b3236d6c708a");
    private static readonly Guid UsdCardId = Guid.Parse("8f1ca96b-81b5-45be-92d2-7a79c341e457");
    private static readonly Guid EurCardId = Guid.Parse("673f9952-3faf-4131-a3c9-4f487aa23924");
    private static readonly Guid ForeignCardId = Guid.Parse("98df8497-66de-4278-a6a9-dfc74d67dcdc");
    private static readonly Guid UnknownMovementId = Guid.Parse("b12c2c64-7cce-4a08-b43d-31b8937bb95f");
    private static readonly Guid BillMovementId = Guid.Parse("32834118-28a1-44f5-921c-17ebdf73a465");
    private static readonly Guid PendingMovementId = Guid.Parse("f34b8cad-b35e-49dd-bb27-e7a42da5e73b");
    private static readonly Guid OwnerCategoryId = Guid.Parse("80d2ce5b-a130-45d2-9a3e-bad042c0828a");
    private static readonly Guid MissingCardId = Guid.Parse("6c24a182-e842-4bf0-ad8d-bdd478005927");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 23);

    // Valores individuais em sinais/moedas distintos permanecem allowlisted, sem enum numérico ou agregado na resposta.
    [TestMethod]
    public async Task TransactionsRouteReturnsPagedAllowlistedMovementDetails()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var userId = await fixture.SeedUserAsync();
        await SeedMovementGraphAsync(fixture.Context, userId);
        var token = await fixture.TokenAsync();

        using var response = await GetTransactionsAsync(fixture.Client, token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        CollectionAssert.AreEquivalent(
            new[] { "items", "page", "pageSize", "filteredCount", "coverage" },
            body.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.AreEqual(25, body.RootElement.GetProperty("pageSize").GetInt32());
        Assert.AreEqual(3, body.RootElement.GetProperty("filteredCount").GetInt32());
        Assert.AreEqual(3, body.RootElement.GetProperty("items").GetArrayLength());
        var coverage = body.RootElement.GetProperty("coverage");
        Assert.AreEqual(JsonValueKind.Null, coverage.GetProperty("lastSuccessfulImportAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, coverage.GetProperty("lastSuccessfulWindow").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, coverage.GetProperty("latestExecution").ValueKind);

        var unknown = body.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == UnknownMovementId);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "id", "cardId", "description", "amount", "currencyCode", "originalDate", "transactionDate",
                "dateReason", "nature", "natureReason", "situation", "situationReason", "installmentNumber",
                "totalInstallments", "categoryId", "note"
            },
            unknown.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(17.25m, unknown.GetProperty("amount").GetDecimal());
        Assert.AreEqual("BRL", unknown.GetProperty("currencyCode").GetString());
        Assert.AreEqual("2026-99-99T00:00:00Z", unknown.GetProperty("originalDate").GetString());
        Assert.AreEqual(JsonValueKind.Null, unknown.GetProperty("transactionDate").ValueKind);
        Assert.AreEqual("INVALID_DATE", unknown.GetProperty("dateReason").GetString());
        Assert.AreEqual(JsonValueKind.String, unknown.GetProperty("nature").ValueKind);
        Assert.AreEqual("Unknown", unknown.GetProperty("nature").GetString());
        Assert.AreEqual("UNKNOWN_OPERATION_TYPE", unknown.GetProperty("natureReason").GetString());
        Assert.AreEqual("Effective", unknown.GetProperty("situation").GetString());
        Assert.AreEqual(OwnerCategoryId, unknown.GetProperty("categoryId").GetGuid());
        Assert.AreEqual("nota sintética do titular", unknown.GetProperty("note").GetString());

        var bill = body.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == BillMovementId);
        Assert.AreEqual(-9.10m, bill.GetProperty("amount").GetDecimal());
        Assert.AreEqual("USD", bill.GetProperty("currencyCode").GetString());
        Assert.AreEqual("BillPayment", bill.GetProperty("nature").GetString());

        var pending = body.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == PendingMovementId);
        Assert.AreEqual("Fee", pending.GetProperty("nature").GetString());
        Assert.AreEqual("Pending", pending.GetProperty("situation").GetString());
        Assert.AreEqual("NOT_EFFECTIVE", pending.GetProperty("situationReason").GetString());
        // Ausência de anotação segue os campos nulos existentes; listar opções não inventa uma associação.
        Assert.AreEqual(JsonValueKind.Null, pending.GetProperty("categoryId").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, pending.GetProperty("note").ValueKind);

        Assert.IsFalse(json.Contains("external-transaction-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("external-account-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("PAGAMENTO", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("operationType", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("totalAmount", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("totalByCurrency", StringComparison.OrdinalIgnoreCase));
    }

    // A sessão é obrigatória; filtros malformados recebem 400 e o limitador vigente pode rejeitar uma leitura válida.
    [TestMethod]
    public async Task TransactionsRouteRequiresBearerValidatesFiltersAndAppliesProtectedRateLimit()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(
            apiConfiguration: RateLimitConfiguration());
        var userId = await fixture.SeedUserAsync();
        await AddConnectionAsync(fixture.Context, userId, OwnerConnectionId, "external-owner-item-sentinel");

        using var anonymous = await GetTransactionsAsync(fixture.Client);
        await PostgreSqlBearerTests.AssertChallengeAsync(anonymous);

        var token = await fixture.TokenAsync();
        using var invalid = await GetTransactionsAsync(fixture.Client, token, "?page=0");
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");

        // A primeira consulta válida alcança a rota; a segunda é rejeitada antes de executar o caso de uso.
        using var accepted = await GetTransactionsAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        using var limited = await GetTransactionsAsync(fixture.Client, token);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    // O cartão alheio e o ID inexistente compartilham exatamente o mesmo 404 sem ecoar o ID solicitado.
    [TestMethod]
    public async Task ForeignAndMissingCardFiltersReturnIndistinguishableNotFound()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var foreignUserId = await fixture.SeedUserAsync("other-owner@example.invalid");
        await AddConnectionAsync(fixture.Context, ownerId, OwnerConnectionId, "owner-item-sentinel");
        await AddConnectionAsync(fixture.Context, foreignUserId, ForeignConnectionId, "foreign-item-sentinel");
        fixture.Context.Set<CardAccount>().Add(CardAccount.Create(
            ForeignCardId, foreignUserId, ForeignConnectionId, "foreign-card-sentinel", "USD"));
        await fixture.Context.SaveChangesAsync();

        using var foreign = await GetTransactionsAsync(fixture.Client, token, $"?cardId={ForeignCardId:D}");
        using var missing = await GetTransactionsAsync(fixture.Client, token, $"?cardId={MissingCardId:D}");

        var foreignProblem = await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        var missingProblem = await AssertProblemAsync(missing, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        Assert.AreEqual(foreignProblem.GetProperty("title").GetString(), missingProblem.GetProperty("title").GetString());
        var foreignJson = await foreign.Content.ReadAsStringAsync();
        var missingJson = await missing.Content.ReadAsStringAsync();
        Assert.IsFalse(foreignJson.Contains(ForeignCardId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(missingJson.Contains(MissingCardId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(foreignJson.Contains("foreign-item-sentinel", StringComparison.Ordinal));
    }

    // A ausência de conexão resulta em 404; conexões próprias múltiplas resultam em 409 sem escolha implícita.
    [TestMethod]
    public async Task MissingAndAmbiguousOwnedConnectionsReturnSanitizedNotFoundAndConflict()
    {
        await using (var missingFixture = await PostgreSqlAuthenticationFixture.CreateAsync())
        {
            await missingFixture.SeedUserAsync();
            using var missingResponse = await GetTransactionsAsync(
                missingFixture.Client,
                await missingFixture.TokenAsync());
            await AssertProblemAsync(missingResponse, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        }

        await using (var ambiguousFixture = await PostgreSqlAuthenticationFixture.CreateAsync())
        {
            var userId = await ambiguousFixture.SeedUserAsync();
            await AddConnectionAsync(ambiguousFixture.Context, userId, OwnerConnectionId, "owner-first-item");
            await AddConnectionAsync(ambiguousFixture.Context, userId, SecondOwnerConnectionId, "owner-second-item");
            using var conflict = await GetTransactionsAsync(
                ambiguousFixture.Client,
                await ambiguousFixture.TokenAsync());
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "RESOURCE_CONFLICT");
        }
    }

    // A falha de banco tipada mapeia a 503; exceção imprevista mapeia a 500 sem mensagem no corpo ou nos logs.
    [TestMethod]
    public async Task TransactionsRouteSanitizesTypedUnavailableAndUnexpectedFailures()
    {
        var reader = new FaultingMovementReader();
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync(services =>
        {
            services.RemoveAll<ITransactionMovementReader>();
            services.AddSingleton<ITransactionMovementReader>(reader);
        });
        var userId = await fixture.SeedUserAsync();
        await AddConnectionAsync(fixture.Context, userId, OwnerConnectionId, "failure-test-item");
        var token = await fixture.TokenAsync();

        reader.Failure = new SyntheticDatabaseUnavailableException();
        using var unavailable = await GetTransactionsAsync(fixture.Client, token);
        await AssertProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE");

        reader.Failure = new InvalidOperationException("database-sentinel provider-sentinel");
        using var unexpected = await GetTransactionsAsync(fixture.Client, token);
        await AssertProblemAsync(unexpected, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.IsFalse(fixture.Factory.Logs.Text.Contains("database-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Factory.Logs.Text.Contains("provider-sentinel", StringComparison.Ordinal));
    }

    // Gera três movimentos sintéticos com moedas/sinais distintos e uma anotação pessoal permitida na projeção.
    private static async Task SeedMovementGraphAsync(FinoraDbContext context, Guid userId)
    {
        await AddConnectionAsync(context, userId, OwnerConnectionId, "external-owner-item-sentinel");
        context.Set<CardAccount>().AddRange(
            CardAccount.Create(BrlCardId, userId, OwnerConnectionId, "external-account-sentinel", "BRL"),
            CardAccount.Create(UsdCardId, userId, OwnerConnectionId, "external-account-usd", "USD"),
            CardAccount.Create(EurCardId, userId, OwnerConnectionId, "external-account-eur", "EUR"));
        context.Set<Category>().Add(
            Category.Create(OwnerCategoryId, userId, isSystem: false, "Categoria sintética", "categoria-sintetica"));
        await context.SaveChangesAsync();

        context.Set<FinancialTransaction>().AddRange(
            CreateMovement(
                UnknownMovementId, userId, BrlCardId, "external-account-sentinel", "external-transaction-sentinel",
                "movimento ambíguo sintético", 17.25m, "BRL", "2026-99-99T00:00:00Z", "DEBIT", "POSTED", "PAGAMENTO"),
            CreateMovement(
                BillMovementId, userId, UsdCardId, "external-account-usd", "external-bill-transaction",
                "pagamento de fatura sintético", -9.10m, "USD", "2026-11-22", "CREDIT", "POSTED", "PAGAMENTO_FATURA"),
            CreateMovement(
                PendingMovementId, userId, EurCardId, "external-account-eur", "external-pending-transaction",
                "tarifa pendente sintética", 4.25m, "EUR", "2026-11-21", "DEBIT", "PENDING", "TARIFA"));
        await context.SaveChangesAsync();

        context.Set<TransactionAnnotation>().Add(TransactionAnnotation.Create(
            UnknownMovementId,
            userId,
            OwnerCategoryId,
            userId,
            "nota sintética do titular",
            new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();
    }

    // A factory usa os mesmos normalizadores de domínio da importação, com argumentos inteiramente sintéticos.
    private static FinancialTransaction CreateMovement(
        Guid id,
        Guid userId,
        Guid cardId,
        string externalAccountId,
        string externalId,
        string description,
        decimal amount,
        string currency,
        string originalDate,
        string type,
        string status,
        string operationType)
    {
        var facts = new TransactionFacts(
            externalId,
            externalAccountId,
            description,
            amount,
            currency,
            originalDate,
            type,
            status,
            operationType);
        var normalized = TransactionNormalizer.Normalize(facts, ReferenceDate);
        return FinancialTransaction.Create(id, userId, cardId, normalized, "transactions-api-test-v1", ReferenceDate);
    }

    // Conexões permanecem em estado persistido e não inicializam serviço de sincronização na leitura.
    private static async Task AddConnectionAsync(
        FinoraDbContext context,
        Guid userId,
        Guid connectionId,
        string externalItemId)
    {
        context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(connectionId, userId, "pluggy", externalItemId));
        await context.SaveChangesAsync();
    }

    // Cada chamada cria cabeçalho Bearer local à requisição para permitir comparar os contratos HTTP.
    private static async Task<HttpResponseMessage> GetTransactionsAsync(
        HttpClient client,
        string? token = null,
        string query = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/transactions" + query);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    // A configuração de teste reduz a janela protegida sem alterar os defaults da API.
    private static Dictionary<string, string?> RateLimitConfiguration() => new()
    {
        ["RateLimiting:Protected:PermitLimit"] = "2",
        ["RateLimiting:Protected:WindowSeconds"] = "60"
    };

    // Erros usam somente campos estáveis do Problem Details e não contêm valores sintéticos da requisição.
    private static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(json);
        Assert.AreEqual((int)expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
        Assert.AreEqual(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(json.Contains("sentinel", StringComparison.OrdinalIgnoreCase));
        return problem.RootElement.Clone();
    }

    // O double injeta exceções controladas depois das validações reais e da resolução de titularidade/conexão.
    private sealed class FaultingMovementReader : ITransactionMovementReader
    {
        public Exception? Failure { get; set; }

        public Task<PagedTransactionMovementSummary> ReadAsync(
            ValidatedTransactionsQuery query,
            CancellationToken cancellationToken) => Failure is { } failure
                ? Task.FromException<PagedTransactionMovementSummary>(failure)
                : Task.FromResult(new PagedTransactionMovementSummary(
                    query.Page,
                    query.PageSize,
                    0,
                    Array.Empty<TransactionMovementSummary>(),
                    new TransactionImportCoverageSummary(null, null, null)));
    }

    // DbException representa uma falha de persistência tipada; sua mensagem adversarial não deve ser serializada.
    private sealed class SyntheticDatabaseUnavailableException : DbException
    {
        public SyntheticDatabaseUnavailableException() : base("database-sentinel") { }
    }
}
