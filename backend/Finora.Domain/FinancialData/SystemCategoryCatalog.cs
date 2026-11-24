namespace Finora.Domain.FinancialData;

/// <summary>Identidade estável das sete categorias persistidas e da opção de ausência exclusivamente virtual.</summary>
public static class SystemCategoryCatalog
{
    public const string UnassignedName = "Sem categoria";

    // UUIDs publicados são imutáveis; a migration reproduz exatamente estes valores e esta ordem.
    public static IReadOnlyList<(Guid Id, string Name, string Key)> Categories { get; } =
    [
        (Guid.Parse("4e2fc103-67f4-4f5f-82dc-62c3ddf55c01"), "Alimentação", "ALIMENTACAO"),
        (Guid.Parse("4e2fc103-67f4-4f5f-82dc-62c3ddf55c02"), "Transporte", "TRANSPORTE"),
        (Guid.Parse("4e2fc103-67f4-4f5f-82dc-62c3ddf55c03"), "Compras", "COMPRAS"),
        (Guid.Parse("4e2fc103-67f4-4f5f-82dc-62c3ddf55c04"), "Lazer", "LAZER"),
        (Guid.Parse("4e2fc103-67f4-4f5f-82dc-62c3ddf55c05"), "Saúde", "SAUDE"),
        (Guid.Parse("4e2fc103-67f4-4f5f-82dc-62c3ddf55c06"), "Educação", "EDUCACAO"),
        (Guid.Parse("4e2fc103-67f4-4f5f-82dc-62c3ddf55c07"), "Outros", "OUTROS")
    ];

    // A reserva é independente da presença do catálogo no banco e protege também a ausência virtual.
    public static bool IsReservedKey(string key) =>
        string.Equals(key, "SEM CATEGORIA", StringComparison.Ordinal) ||
        Categories.Any(category => string.Equals(category.Key, key, StringComparison.Ordinal));
}
