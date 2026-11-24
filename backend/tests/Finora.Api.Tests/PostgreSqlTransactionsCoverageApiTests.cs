using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confirma via HTTP que vazio persistido, cobertura parcial e promoção posterior não viram zero financeiro.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlTransactionsCoverageApiTests
{
    private static readonly Guid ConnectionId = Guid.Parse("27bd2c4b-5406-4f3b-98c7-84d775cc226b");
    private static readonly Guid FailedRunId = Guid.Parse("2c15183a-7cc0-44e4-9991-b8bd0c6b83ed");
    private static readonly string ExternalItemId = "coverage-api-item-synthetic";
    private static readonly string ExternalAccountId = "coverage-api-account-synthetic";
    private static readonly DateTimeOffset FailedStartedAtUtc = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ImportStartedAtUtc = new(2026, 11, 23, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ReferenceDate = new(2026, 11, 23);

    // Uma conexão sem movimentos e sem execução retorna lista vazia e cobertura explicitamente desconhecida.
    [TestMethod]
    public async Task EmptyPersistedResultReturnsUnknownCoverageWithoutFinancialZero()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        var userId = await fixture.SeedUserAsync();
        await AddConnectionAsync(fixture.Context, userId);
        var token = await fixture.TokenAsync();

        using var response = await GetTransactionsAsync(fixture.Client, token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertPagedResponseHasNoAggregate(body.RootElement);
        Assert.AreEqual(0, body.RootElement.GetProperty("filteredCount").GetInt32());
        Assert.AreEqual(0, body.RootElement.GetProperty("items").GetArrayLength());
        var coverage = body.RootElement.GetProperty("coverage");
        Assert.AreEqual(JsonValueKind.Null, coverage.GetProperty("lastSuccessfulImportAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, coverage.GetProperty("lastSuccessfulWindow").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, coverage.GetProperty("latestExecution").ValueKind);
    }

    // Falha após páginas recebidas continua parcial; promoção sintética posterior atualiza histórico e sucesso visível.
    [TestMethod]
    public async Task PartialFailureAndLaterPromotionRemainDistinctInHttpCoverage()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var userId = await fixture.SeedUserAsync();
        await AddConnectionAsync(fixture.Context, userId);
        fixture.Context.Set<FinancialConnectionItemIdentity>().Add(FinancialConnectionItemIdentity.CreateActive(
            ConnectionId, userId, "pluggy", ExternalItemId));
        fixture.Context.Set<SyncRun>().Add(SyncRun.Create(
            FailedRunId,
            userId,
            ConnectionId,
            FailedStartedAtUtc,
            new DateOnly(2026, 11, 1),
            requestedToExclusive: new DateOnly(2026, 12, 1),
            startedAtUtc: FailedStartedAtUtc,
            finishedAtUtc: FailedStartedAtUtc.AddMinutes(3),
            state: SyncRunState.Failed,
            pagesRead: 2,
            receivedCount: 17,
            coverageState: SyncCoverageState.Partial,
            failureCode: SyncRunFailureCodes.ProviderUnavailable));
        await fixture.Context.SaveChangesAsync();
        var token = await fixture.TokenAsync();

        using (var failedRead = await GetTransactionsAsync(fixture.Client, token))
        {
            Assert.AreEqual(HttpStatusCode.OK, failedRead.StatusCode);
            using var failedBody = JsonDocument.Parse(await failedRead.Content.ReadAsStringAsync());
            AssertPagedResponseHasNoAggregate(failedBody.RootElement);
            Assert.AreEqual(0, failedBody.RootElement.GetProperty("filteredCount").GetInt32());
            Assert.AreEqual(0, failedBody.RootElement.GetProperty("items").GetArrayLength());
            var coverage = failedBody.RootElement.GetProperty("coverage");
            Assert.AreEqual(JsonValueKind.Null, coverage.GetProperty("lastSuccessfulImportAtUtc").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, coverage.GetProperty("lastSuccessfulWindow").ValueKind);
            var latest = coverage.GetProperty("latestExecution");
            Assert.AreEqual("Failed", latest.GetProperty("state").GetString());
            Assert.AreEqual("Partial", latest.GetProperty("coverageState").GetString());
            Assert.AreEqual("PROVIDER_UNAVAILABLE", latest.GetProperty("failureCode").GetString());
        }

        // O promoter recebe somente payload sintético local e confirma movimento, SyncRun e sucesso no mesmo commit.
        var transaction = new PluggyTransactionData(
            "coverage-api-transaction-synthetic",
            ExternalAccountId,
            "movimento sintético após falha parcial",
            24.75m,
            "BRL",
            "2026-11-22",
            "DEBIT",
            "POSTED",
            "TARIFA");
        var snapshot = new TransactionImportSnapshot(
            [new TransactionImportAccount(ExternalAccountId, "CREDIT", "CREDIT_CARD", "BRL")],
            [transaction],
            new DateOnly(2026, 11, 1),
            PaginationCompleted: true,
            PagesRead: 4,
            ReceivedCount: 1,
            ObservedFrom: new DateOnly(2026, 11, 22),
            ObservedTo: new DateOnly(2026, 11, 22));
        var promotion = await new FinancialTransactionImportPromoter(fixture.Context).PromoteAsync(
            new TransactionImportConnection(ConnectionId, userId, "pluggy", ExternalItemId, null),
            snapshot,
            ReferenceDate,
            ImportStartedAtUtc,
            ImportStartedAtUtc.AddMinutes(1),
            CancellationToken.None);
        Assert.IsTrue(promotion.Succeeded, promotion.FailureCode?.ToString());

        using var updatedRead = await GetTransactionsAsync(fixture.Client, token);
        Assert.AreEqual(HttpStatusCode.OK, updatedRead.StatusCode);
        using var updatedBody = JsonDocument.Parse(await updatedRead.Content.ReadAsStringAsync());
        AssertPagedResponseHasNoAggregate(updatedBody.RootElement);
        Assert.AreEqual(1, updatedBody.RootElement.GetProperty("filteredCount").GetInt32());
        Assert.AreEqual(1, updatedBody.RootElement.GetProperty("items").GetArrayLength());
        var item = updatedBody.RootElement.GetProperty("items")[0];
        Assert.AreEqual(24.75m, item.GetProperty("amount").GetDecimal());
        Assert.AreEqual("BRL", item.GetProperty("currencyCode").GetString());
        var updatedCoverage = updatedBody.RootElement.GetProperty("coverage");
        Assert.AreEqual(ImportStartedAtUtc.AddMinutes(1),
            updatedCoverage.GetProperty("lastSuccessfulImportAtUtc").GetDateTimeOffset());
        var window = updatedCoverage.GetProperty("lastSuccessfulWindow");
        Assert.AreEqual("2026-11-01", window.GetProperty("requestedFrom").GetString());
        Assert.AreEqual(JsonValueKind.Null, window.GetProperty("requestedToExclusive").ValueKind);
        Assert.AreEqual("2026-11-22", window.GetProperty("observedFrom").GetString());
        Assert.AreEqual("CompleteForRequestedWindow", window.GetProperty("coverageState").GetString());
        var latestAfterPromotion = updatedCoverage.GetProperty("latestExecution");
        Assert.AreEqual("Succeeded", latestAfterPromotion.GetProperty("state").GetString());
        Assert.AreEqual("CompleteForRequestedWindow", latestAfterPromotion.GetProperty("coverageState").GetString());
        Assert.AreEqual(JsonValueKind.Null, latestAfterPromotion.GetProperty("failureCode").ValueKind);
    }

    // A identidade externa fica apenas na persistência; a leitura HTTP resolve titular e conexão próprios.
    private static async Task AddConnectionAsync(Finora.Infrastructure.Persistence.FinoraDbContext context, Guid userId)
    {
        context.Set<FinancialConnection>().Add(FinancialConnection.Create(
            ConnectionId, userId, "pluggy", ExternalItemId));
        await context.SaveChangesAsync();
    }

    // Cada chamada usa seu próprio Bearer e não coloca filtros ou dados financeiros em cabeçalhos compartilhados.
    private static async Task<HttpResponseMessage> GetTransactionsAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    // O esquema superior contém somente página, contagem/cobertura não monetárias e itens individuais.
    private static void AssertPagedResponseHasNoAggregate(JsonElement body)
    {
        CollectionAssert.AreEquivalent(
            new[] { "items", "page", "pageSize", "filteredCount", "coverage" },
            body.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.IsFalse(body.TryGetProperty("totalAmount", out _));
        Assert.IsFalse(body.TryGetProperty("totalByCurrency", out _));
    }
}
