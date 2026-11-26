namespace Finora.Domain.FinancialData;

/// <summary>Registra um trio detectado sem copiar ou alterar fatos dos movimentos de origem.</summary>
public sealed class RecurrenceCandidate
{
    // O EF restaura os IDs de ocorrência e o estado da última avaliação sem abrir mutação pública.
    private RecurrenceCandidate() { }

    private RecurrenceCandidate(
        Guid id,
        Guid userId,
        string policyVersion,
        Guid firstOccurrenceTransactionId,
        Guid secondOccurrenceTransactionId,
        Guid thirdOccurrenceTransactionId,
        DateTimeOffset evaluatedAtUtc)
    {
        Id = id;
        UserId = userId;
        PolicyVersion = policyVersion;
        FirstOccurrenceTransactionId = firstOccurrenceTransactionId;
        SecondOccurrenceTransactionId = secondOccurrenceTransactionId;
        ThirdOccurrenceTransactionId = thirdOccurrenceTransactionId;
        CreatedAtUtc = evaluatedAtUtc;
        LastEvaluatedAtUtc = evaluatedAtUtc;
        IsCurrent = true;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string PolicyVersion { get; private set; } = string.Empty;

    public Guid FirstOccurrenceTransactionId { get; private set; }

    public Guid SecondOccurrenceTransactionId { get; private set; }

    public Guid ThirdOccurrenceTransactionId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastEvaluatedAtUtc { get; private set; }

    public bool IsCurrent { get; private set; }

    /// <summary>Cria um candidato com três movimentos distintos na ordem temporal avaliada.</summary>
    public static RecurrenceCandidate Create(
        Guid id,
        Guid userId,
        string policyVersion,
        Guid firstOccurrenceTransactionId,
        Guid secondOccurrenceTransactionId,
        Guid thirdOccurrenceTransactionId,
        DateTimeOffset evaluatedAtUtc)
    {
        var candidate = new RecurrenceCandidate(
            id,
            userId,
            policyVersion,
            firstOccurrenceTransactionId,
            secondOccurrenceTransactionId,
            thirdOccurrenceTransactionId,
            evaluatedAtUtc);

        candidate.ValidateForPersistence();
        return candidate;
    }

    /// <summary>Atualiza somente a vigência da detecção, sem apagar candidato ou decisão do titular.</summary>
    public void RecordEvaluation(bool isCurrent, DateTimeOffset evaluatedAtUtc)
    {
        EnsureUtc(evaluatedAtUtc, nameof(evaluatedAtUtc));
        if (evaluatedAtUtc < LastEvaluatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evaluatedAtUtc),
                "A reavaliação não pode retroceder o instante registrado.");
        }

        IsCurrent = isCurrent;
        LastEvaluatedAtUtc = evaluatedAtUtc;
        ValidateForPersistence();
    }

    /// <summary>Revalida identidade, trio e instantes antes de cada gravação pelo EF.</summary>
    public void ValidateForPersistence()
    {
        EnsureNonEmpty(Id, nameof(Id));
        EnsureNonEmpty(UserId, nameof(UserId));
        EnsureNonEmpty(FirstOccurrenceTransactionId, nameof(FirstOccurrenceTransactionId));
        EnsureNonEmpty(SecondOccurrenceTransactionId, nameof(SecondOccurrenceTransactionId));
        EnsureNonEmpty(ThirdOccurrenceTransactionId, nameof(ThirdOccurrenceTransactionId));

        if (FirstOccurrenceTransactionId == SecondOccurrenceTransactionId
            || FirstOccurrenceTransactionId == ThirdOccurrenceTransactionId
            || SecondOccurrenceTransactionId == ThirdOccurrenceTransactionId)
        {
            throw new ArgumentException("As três ocorrências precisam ter identidades distintas.");
        }

        if (string.IsNullOrWhiteSpace(PolicyVersion) || PolicyVersion.Length > 64)
        {
            throw new ArgumentException("A versão da política é obrigatória e limitada a 64 caracteres.", nameof(PolicyVersion));
        }

        EnsureUtc(CreatedAtUtc, nameof(CreatedAtUtc));
        EnsureUtc(LastEvaluatedAtUtc, nameof(LastEvaluatedAtUtc));
        if (LastEvaluatedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException("A última avaliação não pode anteceder a criação do candidato.");
        }
    }

    // UUID vazio não pode representar candidato, titular ou ocorrência estável.
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
