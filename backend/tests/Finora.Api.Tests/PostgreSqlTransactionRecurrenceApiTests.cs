using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida rotas protegidas de recorrência com sessões reais e somente dados PostgreSQL sintéticos descartáveis.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlTransactionRecurrenceApiTests
{
    private static readonly Guid OwnerConnectionId = Guid.Parse("d41f13bf-f5d7-4f06-a849-39eb06bd0b12");
    private static readonly Guid OtherConnectionId = Guid.Parse("27ad559c-f868-41fd-9781-5d1275e64a0f");
    private static readonly Guid OwnerCardId = Guid.Parse("68f2077b-a900-43bb-8450-5a52b68d3ad4");
    private static readonly Guid OtherCardId = Guid.Parse("9f3dab2c-d7d2-41c1-960a-03b60e8982d2");
    private static readonly Guid[] OwnerTransactionIds =
    [
        Guid.Parse("dfd8f8b2-8b58-45b1-a731-196906a2ba01"),
        Guid.Parse("dfd8f8b2-8b58-45b1-a731-196906a2ba02"),
        Guid.Parse("dfd8f8b2-8b58-45b1-a731-196906a2ba03")
    ];
    private static readonly Guid[] OtherTransactionIds =
    [
        Guid.Parse("efb71b5a-c64d-4b39-970e-a7c317fc1a01"),
        Guid.Parse("efb71b5a-c64d-4b39-970e-a7c317fc1a02"),
        Guid.Parse("efb71b5a-c64d-4b39-970e-a7c317fc1a03")
    ];
    private static readonly Guid OwnerCandidateId = Guid.Parse("2cb3bb33-31d5-4273-8c9d-d9ab7497ac01");
    private static readonly Guid OtherCandidateId = Guid.Parse("33193804-383e-4a18-8385-50233ba758ea");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 25);
    private static readonly string[] CandidatePropertyNames =
    [
        "candidateId", "policyVersion", "occurrenceTransactionIds", "isCurrent", "decision", "decisionUpdatedAtUtc"
    ];
    private const string CandidateRoute = "/api/transactions/recurrence-candidates";

    // Ausência de Bearer e sessão revogada bloqueiam consulta, avaliação e decisão antes dos handlers.
    [TestMethod]
    public async Task ProtectedOperationsRequireAnActiveSession()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();

        using var missingSession = await SendAsync(fixture.Client, HttpMethod.Get, CandidateRoute);
        await AssertProblemAsync(missingSession, HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");

        var sessionId = new JsonWebToken(token).Id;
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AuthSessions\" SET \"RevokedAtUtc\"={fixture.Clock.GetUtcNow()} WHERE \"Id\"={sessionId}");

        using var revokedRead = await SendAsync(fixture.Client, HttpMethod.Get, CandidateRoute, token);
        await PostgreSqlBearerTests.AssertChallengeAsync(revokedRead);
        using var revokedEvaluation = await SendAsync(
            fixture.Client, HttpMethod.Post, CandidateRoute + "/evaluate", token);
        await PostgreSqlBearerTests.AssertChallengeAsync(revokedEvaluation);
        using var revokedDecision = await SendAsync(
            fixture.Client, HttpMethod.Put, $"{CandidateRoute}/{OwnerCandidateId:D}/decision", token, "{\"decision\":\"Rejected\"}");
        await PostgreSqlBearerTests.AssertChallengeAsync(revokedDecision);
        Assert.AreEqual(0, await fixture.Context.Set<RecurrenceCandidate>().CountAsync());
        Assert.AreEqual(0, await fixture.Context.Set<RecurrenceDecision>().CountAsync());
    }

    // GET inclui decisões inativas; POST reavalia o titular e conserva candidato/decisão sem expor fatos da origem.
    [TestMethod]
    public async Task ReadAndEvaluateReturnOnlyOwnedAllowlistedCandidates()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("other-recurrence@example.invalid");
        var graph = await SeedRecurrenceGraphAsync(fixture.Context, ownerId, otherUserId, fixture.Clock.GetUtcNow());

        using var listResponse = await SendAsync(fixture.Client, HttpMethod.Get, CandidateRoute, token);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = await ReadJsonAsync(listResponse);
        Assert.AreEqual(JsonValueKind.Array, listed.ValueKind);
        Assert.AreEqual(1, listed.GetArrayLength());
        var listedCandidate = listed[0];
        Assert.AreEqual(OwnerCandidateId, listedCandidate.GetProperty("candidateId").GetGuid());
        Assert.AreEqual(TransactionRecurrenceEvaluator.PolicyVersion, listedCandidate.GetProperty("policyVersion").GetString());
        Assert.IsFalse(listedCandidate.GetProperty("isCurrent").GetBoolean());
        Assert.AreEqual("Rejected", listedCandidate.GetProperty("decision").GetString());
        CollectionAssert.AreEquivalent(CandidatePropertyNames,
            listedCandidate.EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEqual(OwnerTransactionIds,
            listedCandidate.GetProperty("occurrenceTransactionIds").EnumerateArray()
                .Select(value => value.GetGuid()).ToArray());
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.IsFalse(listJson.Contains("userId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(listJson.Contains("description", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(listJson.Contains("amount", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(listJson.Contains("externalId", StringComparison.OrdinalIgnoreCase));

        using var evaluateResponse = await SendAsync(
            fixture.Client, HttpMethod.Post, CandidateRoute + "/evaluate", token);
        Assert.AreEqual(HttpStatusCode.OK, evaluateResponse.StatusCode);
        var evaluated = await ReadJsonAsync(evaluateResponse);
        Assert.AreEqual(1, evaluated.GetArrayLength());
        var evaluatedCandidate = evaluated[0];
        Assert.AreEqual(OwnerCandidateId, evaluatedCandidate.GetProperty("candidateId").GetGuid());
        Assert.IsTrue(evaluatedCandidate.GetProperty("isCurrent").GetBoolean());
        Assert.AreEqual("Rejected", evaluatedCandidate.GetProperty("decision").GetString());
        Assert.AreEqual(graph.OwnerDecisionUpdatedAtUtc,
            evaluatedCandidate.GetProperty("decisionUpdatedAtUtc").GetDateTimeOffset());
        Assert.AreEqual(2, await fixture.Context.Set<RecurrenceCandidate>().CountAsync());
        Assert.AreEqual(2, await fixture.Context.Set<RecurrenceDecision>().CountAsync());

        // OpenAPI publica o corpo allowlisted e as respostas das rotas sem sugerir campos financeiros.
        using var openApiResponse = await fixture.Client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, openApiResponse.StatusCode);
        using var openApiDocument = JsonDocument.Parse(await openApiResponse.Content.ReadAsStringAsync());
        var decisionOperation = openApiDocument.RootElement.GetProperty("paths")
            .GetProperty("/api/transactions/recurrence-candidates/{candidateId}/decision")
            .GetProperty("put");
        var requestSchema = ResolveSchema(openApiDocument.RootElement,
            decisionOperation.GetProperty("requestBody").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"));
        CollectionAssert.AreEqual(new[] { "decision" },
            requestSchema.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "Subscription", "Installment", "IndependentPurchases", "Rejected" },
            requestSchema.GetProperty("properties").GetProperty("decision").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
        var candidateResponseSchema = ResolveSchema(openApiDocument.RootElement,
            decisionOperation.GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.IsFalse(candidateResponseSchema.GetProperty("properties").EnumerateObject()
            .Any(property => property.Name is "amount" or "description" or "userId"));
    }

    // PUT aceita somente decisões allowlisted e retorna 404 idêntico para candidato alheio ou inexistente.
    [TestMethod]
    public async Task DecisionEndpointValidatesCorrectionsAndHidesCrossOwnerCandidates()
    {
        await using var fixture = await PostgreSqlAuthenticationFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var ownerId = await fixture.SeedUserAsync();
        var token = await fixture.TokenAsync();
        var otherUserId = await fixture.SeedUserAsync("decision-other-recurrence@example.invalid");
        await SeedRecurrenceGraphAsync(fixture.Context, ownerId, otherUserId, fixture.Clock.GetUtcNow());

        using var rejected = await SendAsync(fixture.Client, HttpMethod.Put,
            $"{CandidateRoute}/{OwnerCandidateId:D}/decision", token, "{\"decision\":\"Rejected\"}");
        Assert.AreEqual(HttpStatusCode.OK, rejected.StatusCode);
        var rejectedBody = await ReadJsonAsync(rejected);
        Assert.AreEqual(OwnerCandidateId, rejectedBody.GetProperty("candidateId").GetGuid());
        Assert.AreEqual("Rejected", rejectedBody.GetProperty("decision").GetString());

        using var corrected = await SendAsync(fixture.Client, HttpMethod.Put,
            $"{CandidateRoute}/{OwnerCandidateId:D}/decision", token, "{\"decision\":\"Installment\"}");
        Assert.AreEqual(HttpStatusCode.OK, corrected.StatusCode);
        var correctedBody = await ReadJsonAsync(corrected);
        CollectionAssert.AreEquivalent(new[] { "candidateId", "decision", "decisionUpdatedAtUtc" },
            correctedBody.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("Installment", correctedBody.GetProperty("decision").GetString());
        var correctedJson = await corrected.Content.ReadAsStringAsync();
        Assert.IsFalse(correctedJson.Contains("userId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(correctedJson.Contains("amount", StringComparison.OrdinalIgnoreCase));

        using var foreign = await SendAsync(fixture.Client, HttpMethod.Put,
            $"{CandidateRoute}/{OtherCandidateId:D}/decision", token, "{\"decision\":\"Subscription\"}");
        var foreignProblem = await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        var missingId = Guid.NewGuid();
        using var missing = await SendAsync(fixture.Client, HttpMethod.Put,
            $"{CandidateRoute}/{missingId:D}/decision", token, "{\"decision\":\"Subscription\"}");
        var missingProblem = await AssertProblemAsync(missing, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        AssertEquivalentProblem(foreignProblem, missingProblem);
        Assert.IsFalse((await foreign.Content.ReadAsStringAsync()).Contains(OtherCandidateId.ToString("D"),
            StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse((await missing.Content.ReadAsStringAsync()).Contains(missingId.ToString("D"),
            StringComparison.OrdinalIgnoreCase));

        // JSON vazio, propriedades extras/duplicadas, valores fora da allowlist e rota malformada não persistem.
        var invalidBodies = new[]
        {
            "", "{}", "null", "[]", "{", "{\"decision\":null}", "{\"decision\":7}",
            "{\"decision\":\"unknown\"}", "{\"decision\":\"Subscription\",\"userId\":\"forbidden\"}",
            "{\"decision\":\"Rejected\",\"decision\":\"Subscription\"}"
        };
        foreach (var body in invalidBodies)
        {
            using var invalid = await SendAsync(fixture.Client, HttpMethod.Put,
                $"{CandidateRoute}/{OwnerCandidateId:D}/decision", token, body);
            await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");
        }

        using var malformedCandidate = await SendAsync(fixture.Client, HttpMethod.Put,
            $"{CandidateRoute}/not-a-guid/decision", token, "{\"decision\":\"Rejected\"}");
        await AssertProblemAsync(malformedCandidate, HttpStatusCode.BadRequest, "REQUEST_INVALID_INPUT");
        fixture.Context.ChangeTracker.Clear();
        Assert.AreEqual(RecurrenceDecisionKind.Installment,
            await fixture.Context.Set<RecurrenceDecision>().Where(decision => decision.CandidateId == OwnerCandidateId)
                .Select(decision => decision.Kind).SingleAsync());
        Assert.AreEqual(RecurrenceDecisionKind.Rejected,
            await fixture.Context.Set<RecurrenceDecision>().Where(decision => decision.CandidateId == OtherCandidateId)
                .Select(decision => decision.Kind).SingleAsync());
    }

    // O grafo inclui três movimentos efetivos por titular e candidatos inativos para provar isolamento e reativação.
    private static async Task<SyntheticRecurrenceGraph> SeedRecurrenceGraphAsync(
        FinoraDbContext context,
        Guid ownerId,
        Guid otherUserId,
        DateTimeOffset nowUtc)
    {
        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(OwnerConnectionId, ownerId, "pluggy", "api-synthetic-owner-item"),
            FinancialConnection.Create(OtherConnectionId, otherUserId, "pluggy", "api-synthetic-other-item"));
        await context.SaveChangesAsync();
        context.Set<FinancialConnectionItemIdentity>().AddRange(
            FinancialConnectionItemIdentity.CreateActive(OwnerConnectionId, ownerId, "pluggy", "api-synthetic-owner-item"),
            FinancialConnectionItemIdentity.CreateActive(OtherConnectionId, otherUserId, "pluggy", "api-synthetic-other-item"));
        await context.SaveChangesAsync();

        context.Set<CardAccount>().AddRange(
            CardAccount.Create(OwnerCardId, ownerId, OwnerConnectionId, "api-synthetic-owner-card", "BRL"),
            CardAccount.Create(OtherCardId, otherUserId, OtherConnectionId, "api-synthetic-other-card", "BRL"));
        await context.SaveChangesAsync();

        context.Set<FinancialTransaction>().AddRange(
            CreateTransactions(ownerId, OwnerCardId, "api-synthetic-owner-card", "owner")
                .Concat(CreateTransactions(otherUserId, OtherCardId, "api-synthetic-other-card", "other")));
        await context.SaveChangesAsync();

        // Datas anteriores ao relógio HTTP tornam a reavaliação POST uma transição observável de inativo para atual.
        var createdAtUtc = nowUtc.AddMinutes(-10);
        var lastEvaluatedAtUtc = nowUtc.AddMinutes(-9);
        var ownerCandidate = RecurrenceCandidate.Create(
            OwnerCandidateId, ownerId, TransactionRecurrenceEvaluator.PolicyVersion,
            OwnerTransactionIds[0], OwnerTransactionIds[1], OwnerTransactionIds[2], createdAtUtc);
        var otherCandidate = RecurrenceCandidate.Create(
            OtherCandidateId, otherUserId, TransactionRecurrenceEvaluator.PolicyVersion,
            OtherTransactionIds[0], OtherTransactionIds[1], OtherTransactionIds[2], createdAtUtc);
        ownerCandidate.RecordEvaluation(false, lastEvaluatedAtUtc);
        otherCandidate.RecordEvaluation(false, lastEvaluatedAtUtc);
        var ownerDecisionUpdatedAtUtc = nowUtc.AddMinutes(-8);
        context.Set<RecurrenceCandidate>().AddRange(ownerCandidate, otherCandidate);
        context.Set<RecurrenceDecision>().AddRange(
            RecurrenceDecision.Create(OwnerCandidateId, ownerId, RecurrenceDecisionKind.Rejected, ownerDecisionUpdatedAtUtc),
            RecurrenceDecision.Create(OtherCandidateId, otherUserId, RecurrenceDecisionKind.Rejected, ownerDecisionUpdatedAtUtc));
        await context.SaveChangesAsync();

        return new SyntheticRecurrenceGraph(ownerDecisionUpdatedAtUtc);
    }

    // Movimentos mensais compartilham descrição/valor, mas cada titular tem IDs de conta e transação independentes.
    private static FinancialTransaction[] CreateTransactions(
        Guid userId,
        Guid cardId,
        string externalAccountId,
        string ownerPrefix)
    {
        var ids = ownerPrefix == "owner" ? OwnerTransactionIds : OtherTransactionIds;
        var externalIds = new[] { $"api-{ownerPrefix}-sep", $"api-{ownerPrefix}-oct", $"api-{ownerPrefix}-nov" };
        var dates = new[] { "2026-09-05", "2026-10-06", "2026-11-07" };
        return ids.Select((id, index) =>
        {
            var facts = new TransactionFacts(
                externalIds[index], externalAccountId, "Plano sintético 8472", 100m,
                "BRL", dates[index], "DEBIT", "POSTED", "COMPRA");
            var normalization = TransactionNormalizer.Normalize(
                facts, ReferenceDate, positivePurchaseRuleConfirmed: true);
            return FinancialTransaction.Create(
                id, userId, cardId, normalization, "recurrence-api-test-v1", ReferenceDate);
        }).ToArray();
    }

    // Requests independentes exercitam roteamento, autorização e parsing pelo host ASP.NET Core.
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? token = null,
        string? jsonBody = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    // O clone permite validar o envelope depois que o JsonDocument temporário for descartado.
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    // Erros HTTP publicam somente status/código/título fixos e um traceId sem reproduzir entrada ou exceção.
    private static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.AreEqual((int)expectedStatus, body.GetProperty("status").GetInt32());
        Assert.AreEqual(expectedCode, body.GetProperty("code").GetString());
        Assert.IsFalse(body.TryGetProperty("detail", out _));
        return body;
    }

    // Propriedade cruzada e recurso inexistente precisam compartilhar a mesma resposta observável.
    private static void AssertEquivalentProblem(JsonElement left, JsonElement right)
    {
        Assert.AreEqual(left.GetProperty("status").GetInt32(), right.GetProperty("status").GetInt32());
        Assert.AreEqual(left.GetProperty("code").GetString(), right.GetProperty("code").GetString());
        Assert.AreEqual(left.GetProperty("title").GetString(), right.GetProperty("title").GetString());
    }

    // Referências locais do OpenAPI são resolvidas para conferir o contrato materializado, não apenas o path.
    private static JsonElement ResolveSchema(JsonElement document, JsonElement schema)
    {
        while (schema.TryGetProperty("$ref", out var reference))
        {
            foreach (var part in reference.GetString()![2..].Split('/'))
            {
                document = document.GetProperty(part);
            }

            schema = document;
        }

        return schema;
    }

    // O resultado documenta o instante inicial da rejeição que POST deve preservar durante reavaliação.
    private sealed record SyntheticRecurrenceGraph(DateTimeOffset OwnerDecisionUpdatedAtUtc);
}
