using Finora.Domain.Transactions;

namespace Finora.Domain.FinancialData;

/// <summary>Snapshot persistível de origem e interpretação, sem declarar compra ou competência validada.</summary>
public sealed class FinancialTransaction
{
    // EF usa este construtor para reidratar o snapshot plano; os valores temporários são sobrescritos pelas colunas.
    private FinancialTransaction()
    {
        ExternalId = string.Empty;
        ExternalAccountId = string.Empty;
        Description = string.Empty;
        CurrencyCode = string.Empty;
        OriginalDate = string.Empty;
        Nature = TransactionNature.Unknown;
        Situation = TransactionSituation.Unknown;
        NormalizationRuleVersion = string.Empty;
    }

    private FinancialTransaction(
        Guid id,
        Guid userId,
        Guid cardAccountId,
        TransactionFacts original,
        TransactionNormalizationResult normalization,
        string normalizationRuleVersion,
        DateOnly normalizationReferenceDate,
        decimal amount)
    {
        Id = id;
        UserId = userId;
        CardAccountId = cardAccountId;
        ExternalId = original.ExternalId;
        ExternalAccountId = original.AccountId;
        Description = original.Description;
        Amount = amount;
        CurrencyCode = original.CurrencyCode;
        OriginalDate = original.OriginalDate;
        Type = original.Type;
        Status = original.Status;
        OperationType = original.OperationType;
        InstallmentNumber = original.InstallmentNumber;
        TotalInstallments = original.TotalInstallments;
        PurchaseDate = original.PurchaseDate;
        BillId = original.BillId;
        BillPostDate = original.BillPostDate;
        BillForecastDate = original.BillForecastDate;
        PaymentType = original.PaymentType;
        TransactionDate = normalization.Date.TransactionDate;
        Nature = normalization.Nature;
        NatureReason = normalization.NatureReason;
        Situation = normalization.Situation;
        SituationReason = normalization.SituationReason;
        DateReason = normalization.DateReason;
        NormalizationRuleVersion = normalizationRuleVersion;
        NormalizationReferenceDate = normalizationReferenceDate;
    }

    // Setters privados permitem restaurar as colunas sem expor alteração do snapshot fora das fábricas do Domain.
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid CardAccountId { get; private set; }

    public string ExternalId { get; private set; }

    public string ExternalAccountId { get; private set; }

    public string Description { get; private set; }

    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public string OriginalDate { get; private set; }

    public string? Type { get; private set; }

    public string? Status { get; private set; }

    public string? OperationType { get; private set; }

    public int? InstallmentNumber { get; private set; }

    public int? TotalInstallments { get; private set; }

    public string? PurchaseDate { get; private set; }

    public string? BillId { get; private set; }

    public string? BillPostDate { get; private set; }

    public string? BillForecastDate { get; private set; }

    public string? PaymentType { get; private set; }

    public DateOnly? TransactionDate { get; private set; }

    public TransactionNature Nature { get; private set; }

    public TransactionExclusionReason? NatureReason { get; private set; }

    public TransactionSituation Situation { get; private set; }

    public TransactionExclusionReason? SituationReason { get; private set; }

    public TransactionExclusionReason? DateReason { get; private set; }

    public string NormalizationRuleVersion { get; private set; }

    public DateOnly NormalizationReferenceDate { get; private set; }

    /// <summary>Cria o snapshot preservando origem e motivos independentes para cada interpretação.</summary>
    public static FinancialTransaction Create(
        Guid id,
        Guid userId,
        Guid cardAccountId,
        TransactionNormalizationResult normalization,
        string normalizationRuleVersion,
        DateOnly normalizationReferenceDate)
    {
        ArgumentNullException.ThrowIfNull(normalization);
        ArgumentNullException.ThrowIfNull(normalization.Original);
        ArgumentNullException.ThrowIfNull(normalization.Date);

        EnsureReasonContract(normalization);

        var transaction = new FinancialTransaction(
            id,
            userId,
            cardAccountId,
            normalization.Original,
            normalization,
            normalizationRuleVersion,
            normalizationReferenceDate,
            normalization.Original.Amount);

        transaction.ValidateForPersistence();
        return transaction;
    }

    /// <summary>Atualiza somente fatos e interpretação da origem para a mesma identidade externa e conta.</summary>
    public void UpdateSourceFacts(
        TransactionNormalizationResult normalization,
        string normalizationRuleVersion,
        DateOnly normalizationReferenceDate)
    {
        ArgumentNullException.ThrowIfNull(normalization);
        ArgumentNullException.ThrowIfNull(normalization.Original);
        ArgumentNullException.ThrowIfNull(normalization.Date);

        // Valida o snapshot substituto antes de tocar na entidade rastreada, evitando estado parcial em falhas.
        var replacement = Create(
            Id,
            UserId,
            CardAccountId,
            normalization,
            normalizationRuleVersion,
            normalizationReferenceDate);
        if (!string.Equals(replacement.ExternalId, ExternalId, StringComparison.Ordinal)
            || !string.Equals(replacement.ExternalAccountId, ExternalAccountId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A atualização precisa preservar a identidade externa contextual.", nameof(normalization));
        }

        Description = replacement.Description;
        Amount = replacement.Amount;
        CurrencyCode = replacement.CurrencyCode;
        OriginalDate = replacement.OriginalDate;
        Type = replacement.Type;
        Status = replacement.Status;
        OperationType = replacement.OperationType;
        InstallmentNumber = replacement.InstallmentNumber;
        TotalInstallments = replacement.TotalInstallments;
        PurchaseDate = replacement.PurchaseDate;
        BillId = replacement.BillId;
        BillPostDate = replacement.BillPostDate;
        BillForecastDate = replacement.BillForecastDate;
        PaymentType = replacement.PaymentType;
        TransactionDate = replacement.TransactionDate;
        Nature = replacement.Nature;
        NatureReason = replacement.NatureReason;
        Situation = replacement.Situation;
        SituationReason = replacement.SituationReason;
        DateReason = replacement.DateReason;
        NormalizationRuleVersion = replacement.NormalizationRuleVersion;
        NormalizationReferenceDate = replacement.NormalizationReferenceDate;
    }

    /// <summary>Revalida campos essenciais, IDs opacos, valor e motivos antes de cada gravação EF.</summary>
    public void ValidateForPersistence()
    {
        EnsureNonEmpty(Id, nameof(Id));
        EnsureNonEmpty(UserId, nameof(UserId));
        EnsureNonEmpty(CardAccountId, nameof(CardAccountId));
        FinancialValueRules.EnsureOpaqueExternalId(ExternalId, nameof(ExternalId));
        FinancialValueRules.EnsureOpaqueExternalId(ExternalAccountId, nameof(ExternalAccountId));
        EnsureRequired(Description, nameof(Description));
        EnsureRequired(CurrencyCode, nameof(CurrencyCode));
        EnsureRequired(OriginalDate, nameof(OriginalDate));
        EnsureRequired(NormalizationRuleVersion, nameof(NormalizationRuleVersion));
        FinancialValueRules.EnsureNumeric19Scale4(Amount, nameof(Amount));

        if (Nature == TransactionNature.Unknown && NatureReason is null)
        {
            throw new ArgumentException("Natureza desconhecida exige motivo.", nameof(NatureReason));
        }

        if (Situation == TransactionSituation.Unknown && SituationReason is null)
        {
            throw new ArgumentException("Situação desconhecida exige motivo.", nameof(SituationReason));
        }

        if (TransactionDate is null && DateReason is null)
        {
            throw new ArgumentException("Data técnica ausente exige motivo.", nameof(DateReason));
        }

        if (TransactionDate is not null && DateReason is not null)
        {
            throw new ArgumentException("Data técnica presente não pode carregar motivo de ausência.", nameof(DateReason));
        }
    }

    // Exige motivo quando a classificação é desconhecida e preserva a data ausente com sua causa.
    private static void EnsureReasonContract(TransactionNormalizationResult normalization)
    {
        if (normalization.Nature == TransactionNature.Unknown && normalization.NatureReason is null)
        {
            throw new ArgumentException("Natureza desconhecida exige motivo.", nameof(normalization));
        }

        if (normalization.Situation == TransactionSituation.Unknown && normalization.SituationReason is null)
        {
            throw new ArgumentException("Situação desconhecida exige motivo.", nameof(normalization));
        }

        if (normalization.Date.TransactionDate is null && normalization.DateReason is null)
        {
            throw new ArgumentException("Data técnica ausente exige motivo.", nameof(normalization));
        }

        if (normalization.Date.TransactionDate is not null && normalization.DateReason is not null)
        {
            throw new ArgumentException("Data técnica presente não pode carregar motivo de ausência.", nameof(normalization));
        }

        if (!string.Equals(
                normalization.Original.OriginalDate,
                normalization.Date.OriginalDate,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("A data normalizada precisa corresponder à origem.", nameof(normalization));
        }
    }

    // Os campos de conteúdo e a versão técnica precisam ter valor, sem substituir a origem.
    private static void EnsureRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O valor é obrigatório.", parameterName);
        }
    }

    // UUIDs vazios não podem representar movimento, titular ou cartão.
    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("O UUID não pode ser vazio.", parameterName);
        }
    }
}
