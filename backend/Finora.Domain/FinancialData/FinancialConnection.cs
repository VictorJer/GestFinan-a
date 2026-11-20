namespace Finora.Domain.FinancialData;

/// <summary>Representa a conexão de origem pertencente a um titular sem guardar credenciais.</summary>
public sealed class FinancialConnection
{
    private FinancialConnection(
        Guid id,
        Guid userId,
        string provider,
        string externalItemId,
        string? sourceStatus,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset? lastAttemptAtUtc,
        DateTimeOffset? lastSuccessfulImportAtUtc,
        DateTimeOffset? sourceUpdatedAtUtc)
    {
        Id = id;
        UserId = userId;
        Provider = provider;
        ExternalItemId = externalItemId;
        SourceStatus = sourceStatus;
        ExpiresAtUtc = expiresAtUtc;
        LastAttemptAtUtc = lastAttemptAtUtc;
        LastSuccessfulImportAtUtc = lastSuccessfulImportAtUtc;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
    }

    // Setters privados permitem a materialização relacional sem expor mutação de origem aos consumidores.
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Provider { get; private set; }

    public string ExternalItemId { get; private set; }

    public string? SourceStatus { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptAtUtc { get; private set; }

    public DateTimeOffset? LastSuccessfulImportAtUtc { get; private set; }

    public DateTimeOffset? SourceUpdatedAtUtc { get; private set; }

    /// <summary>Cria a conexão após validar identidade, titular, código do provedor e instantes UTC.</summary>
    public static FinancialConnection Create(
        Guid id,
        Guid userId,
        string provider,
        string externalItemId,
        string? sourceStatus = null,
        DateTimeOffset? expiresAtUtc = null,
        DateTimeOffset? lastAttemptAtUtc = null,
        DateTimeOffset? lastSuccessfulImportAtUtc = null,
        DateTimeOffset? sourceUpdatedAtUtc = null)
    {
        var connection = new FinancialConnection(
            id,
            userId,
            provider,
            externalItemId,
            sourceStatus,
            expiresAtUtc,
            lastAttemptAtUtc,
            lastSuccessfulImportAtUtc,
            sourceUpdatedAtUtc);

        connection.ValidateForPersistence();
        return connection;
    }

    /// <summary>Atualiza tentativa e último sucesso somente após confirmar uma promoção completa.</summary>
    public void MarkImportSuccessful(DateTimeOffset completedAtUtc)
    {
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        LastAttemptAtUtc = completedAtUtc;
        LastSuccessfulImportAtUtc = completedAtUtc;
        ValidateForPersistence();
    }

    /// <summary>Troca o item externo somente após a camada de persistência comprovar continuidade.</summary>
    public void ReplaceExternalItemId(string externalItemId)
    {
        FinancialValueRules.EnsureOpaqueExternalId(externalItemId, nameof(externalItemId));
        ExternalItemId = externalItemId;
        ValidateForPersistence();
    }

    /// <summary>Revalida identidade, código do provedor, ID externo e instantes antes de cada gravação EF.</summary>
    public void ValidateForPersistence()
    {
        EnsureNonEmpty(Id, nameof(Id));
        EnsureNonEmpty(UserId, nameof(UserId));
        FinancialValueRules.EnsureProviderCode(Provider, nameof(Provider));
        FinancialValueRules.EnsureOpaqueExternalId(ExternalItemId, nameof(ExternalItemId));
        EnsureUtc(ExpiresAtUtc, nameof(ExpiresAtUtc));
        EnsureUtc(LastAttemptAtUtc, nameof(LastAttemptAtUtc));
        EnsureUtc(LastSuccessfulImportAtUtc, nameof(LastSuccessfulImportAtUtc));
        EnsureUtc(SourceUpdatedAtUtc, nameof(SourceUpdatedAtUtc));
    }

    // UUIDs vazios não podem representar identidade interna ou titular.
    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("O UUID não pode ser vazio.", parameterName);
        }
    }

    // Campos nomeados UTC aceitam somente instantes já expressos em offset zero.
    private static void EnsureUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue && value.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante precisa estar normalizado em UTC.", parameterName);
        }
    }
}
