namespace Finora.Domain.FinancialData;

/// <summary>Registro permanente de uma identidade de item ativa ou aposentada para impedir colisão/replay.</summary>
public sealed class FinancialConnectionItemIdentity
{
    private FinancialConnectionItemIdentity(
        Guid connectionId,
        Guid userId,
        string provider,
        string externalItemId,
        DateTimeOffset? retiredAtUtc)
    {
        ConnectionId = connectionId;
        UserId = userId;
        Provider = provider;
        ExternalItemId = externalItemId;
        RetiredAtUtc = retiredAtUtc;
    }

    public Guid ConnectionId { get; private set; }

    public Guid UserId { get; private set; }

    public string Provider { get; private set; }

    public string ExternalItemId { get; private set; }

    public DateTimeOffset? RetiredAtUtc { get; private set; }

    // A fábrica cria somente identidades ativas; a persistência impõe chave global por provedor/item.
    public static FinancialConnectionItemIdentity CreateActive(
        Guid connectionId,
        Guid userId,
        string provider,
        string externalItemId)
    {
        var identity = new FinancialConnectionItemIdentity(
            connectionId, userId, provider, externalItemId, null);
        identity.ValidateForPersistence();
        return identity;
    }

    // Aposentadoria é monotônica: nenhum fluxo de domínio limpa o timestamp para reativar a identidade.
    public void Retire(DateTimeOffset retiredAtUtc)
    {
        if (RetiredAtUtc.HasValue)
        {
            throw new InvalidOperationException("A identidade externa já está aposentada.");
        }

        if (retiredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante precisa estar normalizado em UTC.", nameof(retiredAtUtc));
        }

        RetiredAtUtc = retiredAtUtc;
        ValidateForPersistence();
    }

    // Revalida os dados opacos e a relação temporal antes de qualquer persistência.
    public void ValidateForPersistence()
    {
        if (ConnectionId == Guid.Empty || UserId == Guid.Empty)
        {
            throw new ArgumentException("A identidade externa precisa de conexão e titular válidos.");
        }

        FinancialValueRules.EnsureProviderCode(Provider, nameof(Provider));
        FinancialValueRules.EnsureOpaqueExternalId(ExternalItemId, nameof(ExternalItemId));
        if (RetiredAtUtc.HasValue && RetiredAtUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante precisa estar normalizado em UTC.", nameof(RetiredAtUtc));
        }
    }
}
