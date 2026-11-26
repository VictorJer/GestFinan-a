namespace Finora.Domain.FinancialData;

/// <summary>Interpretação explícita vigente escolhida pelo titular para um candidato.</summary>
public enum RecurrenceDecisionKind
{
    Subscription = 1,
    Installment = 2,
    IndependentPurchases = 3,
    Rejected = 4
}

/// <summary>Armazena a decisão corrigível separada do candidato e dos fatos financeiros.</summary>
public sealed class RecurrenceDecision
{
    // O EF reidrata a chave composta e a escolha vigente sem criar uma decisão nova.
    private RecurrenceDecision() { }

    private RecurrenceDecision(
        Guid candidateId,
        Guid userId,
        RecurrenceDecisionKind kind,
        DateTimeOffset updatedAtUtc)
    {
        CandidateId = candidateId;
        UserId = userId;
        Kind = kind;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid CandidateId { get; private set; }

    public Guid UserId { get; private set; }

    public RecurrenceDecisionKind Kind { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Cria a escolha explícita corrente para um candidato do mesmo titular.</summary>
    public static RecurrenceDecision Create(
        Guid candidateId,
        Guid userId,
        RecurrenceDecisionKind kind,
        DateTimeOffset updatedAtUtc)
    {
        var decision = new RecurrenceDecision(candidateId, userId, kind, updatedAtUtc);
        decision.ValidateForPersistence();
        return decision;
    }

    /// <summary>Substitui a decisão vigente somente após uma nova escolha explícita do titular.</summary>
    public void ChangeKind(RecurrenceDecisionKind kind, DateTimeOffset updatedAtUtc)
    {
        EnsureValidKind(kind);
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        Kind = kind;
        UpdatedAtUtc = updatedAtUtc;
        ValidateForPersistence();
    }

    /// <summary>Revalida titular, candidato, enum e instante UTC antes de cada gravação pelo EF.</summary>
    public void ValidateForPersistence()
    {
        EnsureNonEmpty(CandidateId, nameof(CandidateId));
        EnsureNonEmpty(UserId, nameof(UserId));
        EnsureValidKind(Kind);
        EnsureUtc(UpdatedAtUtc, nameof(UpdatedAtUtc));
    }

    // O valor default do enum e valores futuros não registrados são recusados antes do provider.
    private static void EnsureValidKind(RecurrenceDecisionKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    // UUID vazio não identifica um titular nem um candidato persistido.
    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("O UUID não pode ser vazio.", parameterName);
        }
    }

    // Instantes com sufixo UTC aceitam somente offset zero, sem conversão silenciosa.
    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante precisa estar normalizado em UTC.", parameterName);
        }
    }
}
