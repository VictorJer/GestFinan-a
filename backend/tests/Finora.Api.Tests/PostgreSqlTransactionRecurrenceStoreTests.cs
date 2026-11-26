using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita importação, reconexão, candidatos e decisões somente no PostgreSQL descartável.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlTransactionRecurrenceStoreTests
{
    private static readonly Guid UserId = Guid.Parse("7134e5c8-0aa6-4f29-b2d8-3041a1981901");
    private static readonly Guid ConnectionId = Guid.Parse("f0791105-aa3f-44de-a08e-a61547d7648d");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 25);
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    // IDs externos iguais mantêm os movimentos internos apesar de mudanças de descrição/valor e troca do item.
    [TestMethod]
    public async Task ReimportsSourceFactUpdatesAndReconnectionPreserveCandidateAndCorrectedDecision()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync();
        fixture.Context.Users.Add(new ApplicationUser(UserId));
        fixture.Context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(ConnectionId, UserId, "pluggy", "synthetic-old-item"));
        fixture.Context.Set<FinancialConnectionItemIdentity>().Add(FinancialConnectionItemIdentity.CreateActive(
            ConnectionId, UserId, "pluggy", "synthetic-old-item"));
        await fixture.Context.SaveChangesAsync();

        // A primeira coleta cria três ocorrências sintéticas que atendem à política aprovada.
        var originalSnapshot = Snapshot("Nuvem Fictícia 8472", 100m, 100m, 100m);
        var firstImport = await PromoteAsync("synthetic-old-item", originalSnapshot, StartedAtUtc);
        Assert.IsTrue(firstImport.Succeeded, firstImport.FailureCode?.ToString());
        var firstEvaluation = await EvaluateAndSynchronizeAsync(StartedAtUtc.AddMinutes(2));
        var originalCandidate = firstEvaluation.Single();
        Assert.AreEqual(TransactionRecurrenceEvaluator.PolicyVersion, originalCandidate.PolicyVersion);
        Assert.AreEqual(3, originalCandidate.OccurrenceTransactionIds.Count);

        // Rejeição, confirmação e correção explícitas substituem somente a decisão vigente do candidato.
        var rejectedAtUtc = StartedAtUtc.AddMinutes(3);
        var confirmedAtUtc = StartedAtUtc.AddMinutes(4);
        var correctedAtUtc = StartedAtUtc.AddMinutes(5);
        await SaveAndAssertDecisionAsync(originalCandidate.Id, RecurrenceDecisionKind.Rejected, rejectedAtUtc);
        await SaveAndAssertDecisionAsync(originalCandidate.Id, RecurrenceDecisionKind.Subscription, confirmedAtUtc);
        await SaveAndAssertDecisionAsync(
            originalCandidate.Id, RecurrenceDecisionKind.IndependentPurchases, correctedAtUtc);

        // A repetição idêntica da origem não duplica transações/candidato nem restaura uma escolha anterior.
        var repeatedImport = await PromoteAsync(
            "synthetic-old-item", originalSnapshot, StartedAtUtc.AddMinutes(10));
        Assert.IsTrue(repeatedImport.Succeeded, repeatedImport.FailureCode?.ToString());
        var afterRepeatedImport = await EvaluateAndSynchronizeAsync(StartedAtUtc.AddMinutes(12));
        AssertCandidateAndDecision(originalCandidate, afterRepeatedImport.Single(), RecurrenceDecisionKind.IndependentPurchases,
            correctedAtUtc);

        // A origem pode corrigir texto e valores; a identidade do candidato continua baseada nos IDs internos.
        var updatedSnapshot = Snapshot("Plano Sintético 9321", 100.50m, 100.60m, 100.40m);
        var updatedImport = await PromoteAsync(
            "synthetic-old-item", updatedSnapshot, StartedAtUtc.AddMinutes(20));
        Assert.IsTrue(updatedImport.Succeeded, updatedImport.FailureCode?.ToString());
        var updatedMovements = await ReadTransactionsAsync();
        Assert.AreEqual(3, updatedMovements.Count);
        Assert.AreEqual(3, updatedMovements.Select(movement => movement.Id).Distinct().Count());
        Assert.IsTrue(updatedMovements.All(movement => movement.Description == "Plano Sintético 9321"));
        Assert.IsTrue(updatedMovements.All(movement => movement.Amount is >= 100.40m and <= 100.60m));
        var afterFactUpdate = await EvaluateAndSynchronizeAsync(StartedAtUtc.AddMinutes(22));
        AssertCandidateAndDecision(originalCandidate, afterFactUpdate.Single(),
            RecurrenceDecisionKind.IndependentPurchases, correctedAtUtc);

        // A reconexão comprovada reaproveita cartão e IDs de transação, sem remover candidato ou decisão.
        var reconnectionStartedAtUtc = StartedAtUtc.AddMinutes(30);
        var reconnection = await PromoteReconnectionAsync(
            "synthetic-old-item", "synthetic-new-item", updatedSnapshot, reconnectionStartedAtUtc);
        Assert.IsTrue(reconnection.Succeeded, reconnection.FailureCode?.ToString());
        var reconnectedMovements = await ReadTransactionsAsync();
        Assert.AreEqual(3, reconnectedMovements.Count);
        CollectionAssert.AreEquivalent(
            updatedMovements.Select(movement => movement.Id).ToArray(),
            reconnectedMovements.Select(movement => movement.Id).ToArray());

        await using var verification = CreateContext();
        Assert.AreEqual("synthetic-new-item", await verification.Set<FinancialConnection>()
            .Where(connection => connection.Id == ConnectionId)
            .Select(connection => connection.ExternalItemId)
            .SingleAsync());
        var afterReconnection = await EvaluateAndSynchronizeAsync(reconnectionStartedAtUtc.AddMinutes(2));
        AssertCandidateAndDecision(originalCandidate, afterReconnection.Single(),
            RecurrenceDecisionKind.IndependentPurchases, correctedAtUtc);
        Assert.AreEqual(1, await verification.Set<RecurrenceCandidate>().CountAsync());
        Assert.AreEqual(1, await verification.Set<RecurrenceDecision>().CountAsync());
    }

    // Uma política nova ou não suportada não reclassifica decisões guardadas em candidatos históricos.
    [TestMethod]
    public async Task ReEvaluationKeepsHistoricalPolicyDecisionAndRejectsUnavailableVersion()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync();
        fixture.Context.Users.Add(new ApplicationUser(UserId));
        fixture.Context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(ConnectionId, UserId, "pluggy", "synthetic-old-item"));
        fixture.Context.Set<FinancialConnectionItemIdentity>().Add(FinancialConnectionItemIdentity.CreateActive(
            ConnectionId, UserId, "pluggy", "synthetic-old-item"));
        await fixture.Context.SaveChangesAsync();

        // O trio observado é compartilhado pelas versões histórica e atual sem compartilhar suas decisões.
        var snapshot = Snapshot("Nuvem Fictícia 8472", 100m, 100m, 100m);
        var imported = await PromoteAsync("synthetic-old-item", snapshot, StartedAtUtc);
        Assert.IsTrue(imported.Succeeded, imported.FailureCode?.ToString());
        var movements = await ReadTransactionsAsync();
        var evaluated = TransactionRecurrenceEvaluator.Evaluate(movements).Single();
        var historicalCandidateId = Guid.NewGuid();
        var historicalDecisionAtUtc = StartedAtUtc.AddMinutes(3);

        // Semear uma decisão histórica reproduz a atualização de política sem fingir que v0 é hoje suportada.
        await using (var seedContext = CreateContext())
        {
            seedContext.Set<RecurrenceCandidate>().Add(RecurrenceCandidate.Create(
                historicalCandidateId,
                UserId,
                "recurrence-v0",
                evaluated.OccurrenceTransactionIds[0],
                evaluated.OccurrenceTransactionIds[1],
                evaluated.OccurrenceTransactionIds[2],
                StartedAtUtc.AddMinutes(2)));
            seedContext.Set<RecurrenceDecision>().Add(RecurrenceDecision.Create(
                historicalCandidateId,
                UserId,
                RecurrenceDecisionKind.Subscription,
                historicalDecisionAtUtc));
            await seedContext.SaveChangesAsync();
        }

        // A avaliação v1 cria identidade separada mesmo quando os três movimentos são os mesmos.
        var afterCurrentEvaluation = await EvaluateAndSynchronizeAsync(StartedAtUtc.AddMinutes(4));
        Assert.AreEqual(2, afterCurrentEvaluation.Count);
        var currentCandidate = afterCurrentEvaluation.Single(candidate =>
            candidate.PolicyVersion == TransactionRecurrenceEvaluator.PolicyVersion);
        Assert.AreNotEqual(historicalCandidateId, currentCandidate.Id);
        await SaveAndAssertDecisionAsync(
            currentCandidate.Id, RecurrenceDecisionKind.Rejected, StartedAtUtc.AddMinutes(5));

        // A reavaliação sem trio desativa v1, mas mantém a escolha corrigível e não toca a versão histórica.
        await SynchronizeCandidatesAsync(
            TransactionRecurrenceEvaluator.PolicyVersion,
            Array.Empty<TransactionRecurrenceCandidate>(),
            StartedAtUtc.AddMinutes(6));

        // Limites ainda não implementados falham fechados antes de gravar um candidato ou decisão.
        var unavailableCandidate = evaluated with { PolicyVersion = "recurrence-v2" };
        await using (var unsupportedContext = CreateContext())
        {
            var store = new TransactionRecurrenceStore(unsupportedContext);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SynchronizeCandidatesAsync(
                UserId,
                "recurrence-v2",
                [unavailableCandidate],
                StartedAtUtc.AddMinutes(7),
                CancellationToken.None));
        }

        // A consulta final prova que nenhuma reavaliação mudou as escolhas explícitas de qualquer versão.
        await using var verification = CreateContext();
        var finalCandidates = await new TransactionRecurrenceStore(verification)
            .ReadCandidatesAsync(UserId, CancellationToken.None);
        Assert.AreEqual(2, finalCandidates.Count);
        var historical = finalCandidates.Single(candidate => candidate.Id == historicalCandidateId);
        var current = finalCandidates.Single(candidate => candidate.Id == currentCandidate.Id);
        Assert.IsTrue(historical.IsCurrent);
        Assert.AreEqual(RecurrenceDecisionKind.Subscription, historical.Decision);
        Assert.AreEqual(historicalDecisionAtUtc, historical.DecisionUpdatedAtUtc);
        Assert.IsFalse(current.IsCurrent);
        Assert.AreEqual(RecurrenceDecisionKind.Rejected, current.Decision);
        Assert.AreEqual(StartedAtUtc.AddMinutes(5), current.DecisionUpdatedAtUtc);
    }

    // Cada escolha é lida novamente do banco para comprovar rejeição e correções explícitas em sequência.
    private static async Task SaveAndAssertDecisionAsync(
        Guid candidateId,
        RecurrenceDecisionKind kind,
        DateTimeOffset updatedAtUtc)
    {
        await using var context = CreateContext();
        var store = new TransactionRecurrenceStore(context);
        var status = await store.SaveDecisionAsync(UserId, candidateId, kind, updatedAtUtc, CancellationToken.None);
        Assert.AreEqual(RecurrenceDecisionPersistenceStatus.Updated, status);
        var persisted = (await store.ReadCandidatesAsync(UserId, CancellationToken.None))
            .Single(candidate => candidate.Id == candidateId);
        Assert.AreEqual(kind, persisted.Decision);
        Assert.AreEqual(updatedAtUtc, persisted.DecisionUpdatedAtUtc);
    }

    // Isola a política reavaliada para que candidatos de versões históricas permaneçam fora do upsert atual.
    private static async Task SynchronizeCandidatesAsync(
        string policyVersion,
        IReadOnlyList<TransactionRecurrenceCandidate> evaluated,
        DateTimeOffset evaluatedAtUtc)
    {
        await using var context = CreateContext();
        await new TransactionRecurrenceStore(context).SynchronizeCandidatesAsync(
            UserId, policyVersion, evaluated, evaluatedAtUtc, CancellationToken.None);
    }

    // A avaliação real lê os movimentos persistidos e sincroniza o resultado pelo trio de IDs estáveis.
    private static async Task<IReadOnlyList<PersistedRecurrenceCandidate>> EvaluateAndSynchronizeAsync(
        DateTimeOffset evaluatedAtUtc)
    {
        await using var context = CreateContext();
        var store = new TransactionRecurrenceStore(context);
        var movements = await store.ReadTransactionsAsync(UserId, CancellationToken.None);
        var evaluated = TransactionRecurrenceEvaluator.Evaluate(movements);
        await store.SynchronizeCandidatesAsync(
            UserId,
            TransactionRecurrenceEvaluator.PolicyVersion,
            evaluated,
            evaluatedAtUtc,
            CancellationToken.None);
        return await store.ReadCandidatesAsync(UserId, CancellationToken.None);
    }

    // A projeção deve conservar chave, vigência e instante da última decisão após cada sincronização.
    private static void AssertCandidateAndDecision(
        PersistedRecurrenceCandidate expected,
        PersistedRecurrenceCandidate actual,
        RecurrenceDecisionKind expectedDecision,
        DateTimeOffset expectedDecisionUpdatedAtUtc)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.PolicyVersion, actual.PolicyVersion);
        CollectionAssert.AreEqual(expected.OccurrenceTransactionIds.ToArray(), actual.OccurrenceTransactionIds.ToArray());
        Assert.IsTrue(actual.IsCurrent);
        Assert.AreEqual(expectedDecision, actual.Decision);
        Assert.AreEqual(expectedDecisionUpdatedAtUtc, actual.DecisionUpdatedAtUtc);
    }

    // A promoção usa um contexto novo por importação para simular requisições independentes da aplicação.
    private static async Task<TransactionImportPromotionResult> PromoteAsync(
        string externalItemId,
        TransactionImportSnapshot snapshot,
        DateTimeOffset startedAtUtc)
    {
        await using var context = CreateContext();
        return await new FinancialTransactionImportPromoter(context).PromoteAsync(
            new TransactionImportConnection(ConnectionId, UserId, "pluggy", externalItemId, null),
            snapshot,
            ReferenceDate,
            startedAtUtc,
            startedAtUtc.AddMinutes(1),
            CancellationToken.None);
    }

    // A troca do item usa a operação de reconexão existente e não substitui a identidade interna do cartão.
    private static async Task<TransactionImportPromotionResult> PromoteReconnectionAsync(
        string currentExternalItemId,
        string replacementExternalItemId,
        TransactionImportSnapshot snapshot,
        DateTimeOffset startedAtUtc)
    {
        await using var context = CreateContext();
        return await new FinancialTransactionImportPromoter(context).PromoteReconnectionAsync(
            new TransactionImportConnection(ConnectionId, UserId, "pluggy", currentExternalItemId, null),
            replacementExternalItemId,
            snapshot,
            ReferenceDate,
            startedAtUtc,
            startedAtUtc.AddMinutes(1),
            CancellationToken.None);
    }

    // A consulta usa o mesmo port de titular da persistência, sem chavear por descrição ou valor.
    private static async Task<IReadOnlyList<FinancialTransaction>> ReadTransactionsAsync()
    {
        await using var context = CreateContext();
        return await new TransactionRecurrenceStore(context).ReadTransactionsAsync(UserId, CancellationToken.None);
    }

    // O snapshot contém identidades externas fixas e somente fatos fictícios que formam três meses consecutivos.
    private static TransactionImportSnapshot Snapshot(
        string description,
        decimal septemberAmount,
        decimal octoberAmount,
        decimal novemberAmount) => new(
        [new TransactionImportAccount("synthetic-account", "CREDIT", "CREDIT_CARD", "BRL")],
        [
            SyntheticTransaction("synthetic-september", description, septemberAmount, "2026-09-05"),
            SyntheticTransaction("synthetic-october", description, octoberAmount, "2026-10-06"),
            SyntheticTransaction("synthetic-november", description, novemberAmount, "2026-11-07")
        ],
        new DateOnly(2025, 11, 25),
        true,
        PagesRead: 1,
        ReceivedCount: 3);

    // Mantém os metadados de origem constantes para isolar as mudanças de descrição e valor da identidade.
    private static PluggyTransactionData SyntheticTransaction(
        string externalId,
        string description,
        decimal amount,
        string originalDate) => new(
        externalId,
        "synthetic-account",
        description,
        amount,
        "BRL",
        originalDate,
        "DEBIT",
        "POSTED",
        "COMPRA");

    // Contextos independentes aceitam somente a connection string já validada para o PostgreSQL descartável.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, settings.ConnectionString);
        return new FinoraDbContext(options.Options);
    }
}
