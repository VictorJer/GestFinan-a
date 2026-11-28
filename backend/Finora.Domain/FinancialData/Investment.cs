namespace Finora.Domain.FinancialData;

/// <summary>Representa uma posição manual do titular, separada dos movimentos de cartão e de cenários simulados.</summary>
public sealed class Investment
{
    public const int MaximumNameLength = 120;

    // O EF materializa registros existentes; as invariantes são novamente verificadas antes de cada gravação.
    private Investment() { }

    private Investment(Guid id, Guid userId, string name, string @class, decimal appliedAmount, decimal currentAmount)
    {
        Id = id;
        UserId = userId;
        Name = name;
        Class = @class;
        AppliedAmount = appliedAmount;
        CurrentAmount = currentAmount;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Class { get; private set; } = string.Empty;

    public decimal AppliedAmount { get; private set; }

    public decimal CurrentAmount { get; private set; }

    /// <summary>Cria uma posição após validar identidade, classe, nome e valores sem coerção monetária.</summary>
    public static Investment Create(
        Guid id,
        Guid userId,
        string name,
        string @class,
        decimal appliedAmount,
        decimal currentAmount)
    {
        EnsureIdentity(id, nameof(id));
        EnsureIdentity(userId, nameof(userId));

        var normalizedName = NormalizeName(name, nameof(name));
        EnsureClass(@class, nameof(@class));
        FinancialValueRules.EnsureNonNegativeNumeric19Scale2(appliedAmount, nameof(appliedAmount));
        FinancialValueRules.EnsureNonNegativeNumeric19Scale2(currentAmount, nameof(currentAmount));

        return new Investment(id, userId, normalizedName, @class, appliedAmount, currentAmount);
    }

    /// <summary>Valida todos os novos dados antes de alterar a posição, evitando atualização parcial em caso de erro.</summary>
    public void Update(string name, string @class, decimal appliedAmount, decimal currentAmount)
    {
        var normalizedName = NormalizeName(name, nameof(name));
        EnsureClass(@class, nameof(@class));
        FinancialValueRules.EnsureNonNegativeNumeric19Scale2(appliedAmount, nameof(appliedAmount));
        FinancialValueRules.EnsureNonNegativeNumeric19Scale2(currentAmount, nameof(currentAmount));

        Name = normalizedName;
        Class = @class;
        AppliedAmount = appliedAmount;
        CurrentAmount = currentAmount;
    }

    /// <summary>Revalida os invariantes do titular e dos campos imediatamente antes da persistência.</summary>
    public void ValidateForPersistence()
    {
        EnsureIdentity(Id, nameof(Id));
        EnsureIdentity(UserId, nameof(UserId));
        EnsureStoredName(Name, nameof(Name));
        EnsureClass(Class, nameof(Class));
        FinancialValueRules.EnsureNonNegativeNumeric19Scale2(AppliedAmount, nameof(AppliedAmount));
        FinancialValueRules.EnsureNonNegativeNumeric19Scale2(CurrentAmount, nameof(CurrentAmount));
    }

    // UUID vazio não pode representar nem a posição persistida nem o titular que a possui.
    private static void EnsureIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("O UUID do investimento e do titular não pode ser vazio.", parameterName);
        }
    }

    // Espaços externos são removidos antes de validar; nomes vazios ou acima do limite são recusados sem truncar.
    private static string NormalizeName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O nome do investimento é obrigatório.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumNameLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"O nome do investimento excede {MaximumNameLength} caracteres.");
        }

        return normalized;
    }

    // Materialização persistida exige que o nome já esteja na forma canônica produzida na criação/edição.
    private static void EnsureStoredName(string value, string parameterName)
    {
        var normalized = NormalizeName(value, parameterName);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new ArgumentException("O nome persistido do investimento precisa estar sem espaços externos.", parameterName);
        }
    }

    // Somente os três códigos estáveis aprovados podem ser gravados como classe da posição.
    private static void EnsureClass(string value, string parameterName)
    {
        if (!InvestmentClassCatalog.IsSupported(value))
        {
            throw new ArgumentException("A classe do investimento não é permitida.", parameterName);
        }
    }
}
