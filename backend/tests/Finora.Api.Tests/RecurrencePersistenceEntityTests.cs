using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida invariantes locais das entidades persistentes sem abrir banco ou substituir testes relacionais.
[TestClass]
public sealed class RecurrencePersistenceEntityTests
{
    private static readonly Guid UserId = Guid.Parse("ce469626-a63b-4625-bad5-c2e8309e0c90");
    private static readonly Guid CandidateId = Guid.Parse("d5a5d7e2-468c-4674-976a-4c1aaee8f521");
    private static readonly Guid FirstOccurrenceId = Guid.Parse("c600f085-a9a6-4ee0-9cad-1741447723bd");
    private static readonly Guid SecondOccurrenceId = Guid.Parse("087d2eb4-2a72-4beb-b599-88ed3a04b035");
    private static readonly Guid ThirdOccurrenceId = Guid.Parse("c4168721-d888-45cb-b116-79d2bf6c8185");
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    // A identidade do candidato permanece estável enquanto reavaliação altera somente vigência e instante.
    [TestMethod]
    public void CandidateReevaluationPreservesPolicyAndOccurrenceIdentity()
    {
        var candidate = CreateCandidate();

        candidate.RecordEvaluation(isCurrent: false, EvaluatedAtUtc.AddMinutes(5));

        Assert.AreEqual(CandidateId, candidate.Id);
        Assert.AreEqual(UserId, candidate.UserId);
        Assert.AreEqual("recurrence-v1", candidate.PolicyVersion);
        Assert.AreEqual(FirstOccurrenceId, candidate.FirstOccurrenceTransactionId);
        Assert.AreEqual(SecondOccurrenceId, candidate.SecondOccurrenceTransactionId);
        Assert.AreEqual(ThirdOccurrenceId, candidate.ThirdOccurrenceTransactionId);
        Assert.IsFalse(candidate.IsCurrent);
        Assert.AreEqual(EvaluatedAtUtc.AddMinutes(5), candidate.LastEvaluatedAtUtc);
        Assert.AreEqual(EvaluatedAtUtc, candidate.CreatedAtUtc);
    }

    // Um trio precisa ter três IDs, política identificável e instantes coerentes antes do rastreamento.
    [TestMethod]
    public void CandidateRejectsInvalidIdentityVersionTrioAndTime()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RecurrenceCandidate.Create(
            Guid.Empty, UserId, "recurrence-v1", FirstOccurrenceId, SecondOccurrenceId, ThirdOccurrenceId, EvaluatedAtUtc));
        Assert.ThrowsExactly<ArgumentException>(() => RecurrenceCandidate.Create(
            CandidateId, UserId, " ", FirstOccurrenceId, SecondOccurrenceId, ThirdOccurrenceId, EvaluatedAtUtc));
        Assert.ThrowsExactly<ArgumentException>(() => RecurrenceCandidate.Create(
            CandidateId, UserId, new string('v', 65), FirstOccurrenceId, SecondOccurrenceId, ThirdOccurrenceId, EvaluatedAtUtc));
        Assert.ThrowsExactly<ArgumentException>(() => RecurrenceCandidate.Create(
            CandidateId, UserId, "recurrence-v1", FirstOccurrenceId, FirstOccurrenceId, ThirdOccurrenceId, EvaluatedAtUtc));
        Assert.ThrowsExactly<ArgumentException>(() => RecurrenceCandidate.Create(
            CandidateId, UserId, "recurrence-v1", FirstOccurrenceId, SecondOccurrenceId, ThirdOccurrenceId,
            EvaluatedAtUtc.ToOffset(TimeSpan.FromHours(1))));
    }

    // Uma decisão aceita somente as quatro escolhas aprovadas e um instante UTC.
    [TestMethod]
    public void DecisionRejectsMissingOwnerCandidateUnknownChoiceAndNonUtcTime()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RecurrenceDecision.Create(
            Guid.Empty, UserId, RecurrenceDecisionKind.Subscription, EvaluatedAtUtc));
        Assert.ThrowsExactly<ArgumentException>(() => RecurrenceDecision.Create(
            CandidateId, Guid.Empty, RecurrenceDecisionKind.Subscription, EvaluatedAtUtc));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RecurrenceDecision.Create(
            CandidateId, UserId, (RecurrenceDecisionKind)0, EvaluatedAtUtc));
        Assert.ThrowsExactly<ArgumentException>(() => RecurrenceDecision.Create(
            CandidateId, UserId, RecurrenceDecisionKind.Subscription, EvaluatedAtUtc.ToOffset(TimeSpan.FromHours(1))));
    }

    // Correção substitui a escolha vigente por ação explícita, sem modificar candidato ou movimentos.
    [TestMethod]
    public void ExplicitDecisionCorrectionChangesOnlyTheCurrentChoice()
    {
        var decision = RecurrenceDecision.Create(
            CandidateId, UserId, RecurrenceDecisionKind.Subscription, EvaluatedAtUtc);

        decision.ChangeKind(RecurrenceDecisionKind.IndependentPurchases, EvaluatedAtUtc.AddMinutes(2));

        Assert.AreEqual(CandidateId, decision.CandidateId);
        Assert.AreEqual(UserId, decision.UserId);
        Assert.AreEqual(RecurrenceDecisionKind.IndependentPurchases, decision.Kind);
        Assert.AreEqual(EvaluatedAtUtc.AddMinutes(2), decision.UpdatedAtUtc);
    }

    // Usa IDs sintéticos estáveis para provar invariantes sem qualquer histórico bancário real.
    private static RecurrenceCandidate CreateCandidate() => RecurrenceCandidate.Create(
        CandidateId,
        UserId,
        "recurrence-v1",
        FirstOccurrenceId,
        SecondOccurrenceId,
        ThirdOccurrenceId,
        EvaluatedAtUtc);
}
