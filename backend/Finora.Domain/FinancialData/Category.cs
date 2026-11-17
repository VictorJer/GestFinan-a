namespace Finora.Domain.FinancialData;

/// <summary>Define uma categoria pessoal ou de sistema sem impor política de normalização de produção.</summary>
public sealed class Category
{
    private Category(
        Guid id,
        Guid? userId,
        bool isSystem,
        string name,
        string normalizedName,
        Guid scopeId)
    {
        Id = id;
        UserId = userId;
        IsSystem = isSystem;
        Name = name;
        NormalizedName = normalizedName;
        ScopeId = scopeId;
    }

    // Setters privados permitem leitura relacional preservando a criação e a normalização no Domain.
    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public bool IsSystem { get; private set; }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    public Guid ScopeId { get; private set; }

    /// <summary>Cria categoria com chave de escopo técnica e exige coerência entre sistema e titular.</summary>
    public static Category Create(
        Guid id,
        Guid? userId,
        bool isSystem,
        string name,
        string normalizedName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O UUID da categoria não pode ser vazio.", nameof(id));
        }

        if (isSystem != !userId.HasValue)
        {
            throw new ArgumentException("Categoria de sistema deve ter titular nulo; categoria pessoal deve ter titular.");
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("O UUID do titular não pode ser vazio.", nameof(userId));
        }

        EnsureName(name, nameof(name));
        EnsureName(normalizedName, nameof(normalizedName));

        return new Category(id, userId, isSystem, name, normalizedName, userId ?? Guid.Empty);
    }

    // Nome e chave são recebidos prontos; aqui só se impede vazio ou excesso, sem normalizar texto.
    private static void EnsureName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O nome da categoria é obrigatório.", parameterName);
        }

        if (value.Length > 60)
        {
            throw new ArgumentOutOfRangeException(parameterName, "O nome da categoria excede 60 caracteres.");
        }
    }
}
