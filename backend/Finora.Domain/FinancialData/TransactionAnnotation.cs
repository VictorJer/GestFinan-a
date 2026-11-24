namespace Finora.Domain.FinancialData;

/// <summary>Armazena organização pessoal separada dos fatos externos do movimento.</summary>
public sealed class TransactionAnnotation
{
    private TransactionAnnotation(
        Guid transactionId,
        Guid userId,
        Guid? categoryId,
        Guid? categoryScopeId,
        string? note,
        DateTimeOffset updatedAtUtc)
    {
        TransactionId = transactionId;
        UserId = userId;
        CategoryId = categoryId;
        CategoryScopeId = categoryScopeId;
        Note = note;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Setters privados permitem materialização sem tornar a anotação mutável pela camada de aplicação.
    public Guid TransactionId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid? CategoryScopeId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Cria anotação com categoria ausente, do sistema ou pertencente ao mesmo titular.</summary>
    public static TransactionAnnotation Create(
        Guid transactionId,
        Guid userId,
        Guid? categoryId,
        Guid? categoryScopeId,
        string? note,
        DateTimeOffset updatedAtUtc)
    {
        var annotation = new TransactionAnnotation(
            transactionId,
            userId,
            categoryId,
            categoryScopeId,
            note,
            updatedAtUtc);

        annotation.ValidateForPersistence();
        return annotation;
    }

    /// <summary>Revalida titular, categoria, observação e instante UTC antes de cada gravação EF.</summary>
    public void ValidateForPersistence()
    {
        EnsureNonEmpty(TransactionId, nameof(TransactionId));
        EnsureNonEmpty(UserId, nameof(UserId));
        EnsureUtc(UpdatedAtUtc, nameof(UpdatedAtUtc));
        EnsureCategoryScope(UserId, CategoryId, CategoryScopeId);
        EnsureNote(Note);
    }

    /// <summary>Substitui somente a observação e conserva titular, movimento e categoria escolhida.</summary>
    public TransactionAnnotation WithNote(string? note, DateTimeOffset updatedAtUtc) =>
        Create(TransactionId, UserId, CategoryId, CategoryScopeId, note, updatedAtUtc);

    /// <summary>Limpa a observação sem remover a categoria pessoal ou de sistema.</summary>
    public TransactionAnnotation ClearNote(DateTimeOffset updatedAtUtc) =>
        WithNote(null, updatedAtUtc);

    /// <summary>Atualiza categoria e observação como uma única anotação pessoal.</summary>
    public TransactionAnnotation WithCategoryAndNote(
        Guid? categoryId,
        Guid? categoryScopeId,
        string? note,
        DateTimeOffset updatedAtUtc) =>
        Create(TransactionId, UserId, categoryId, categoryScopeId, note, updatedAtUtc);

    // Categoria e escopo são ambos nulos ou ambos presentes; escopo só pode ser sistema ou o próprio titular.
    private static void EnsureCategoryScope(Guid userId, Guid? categoryId, Guid? categoryScopeId)
    {
        if (categoryId.HasValue != categoryScopeId.HasValue)
        {
            throw new ArgumentException("Categoria e escopo devem estar ambos preenchidos ou ambos ausentes.");
        }

        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("O UUID da categoria não pode ser vazio.", nameof(categoryId));
        }

        if (categoryScopeId.HasValue
            && categoryScopeId.Value != Guid.Empty
            && categoryScopeId.Value != userId)
        {
            throw new ArgumentException("A categoria pessoal precisa pertencer ao mesmo titular.", nameof(categoryScopeId));
        }
    }

    // A anotação não interpreta nem trunca texto; o limite é aplicado antes da persistência.
    private static void EnsureNote(string? note)
    {
        if (note?.Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(note), "A observação excede 500 caracteres.");
        }
    }

    // UUIDs vazios não representam entidades ou titulares válidos.
    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("O UUID não pode ser vazio.", parameterName);
        }
    }

    // Campos nomeados UTC aceitam apenas instantes em offset zero.
    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante precisa estar normalizado em UTC.", parameterName);
        }
    }
}
