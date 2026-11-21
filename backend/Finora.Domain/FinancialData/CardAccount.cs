namespace Finora.Domain.FinancialData;

/// <summary>Representa um cartão associado à conexão e ao mesmo titular, sem identidade inferida.</summary>
public sealed class CardAccount
{
    private CardAccount(
        Guid id,
        Guid userId,
        Guid connectionId,
        string externalId,
        string? name,
        string? lastFour,
        string currencyCode,
        decimal? availableCreditLimit,
        decimal? totalCreditLimit)
    {
        Id = id;
        UserId = userId;
        ConnectionId = connectionId;
        ExternalId = externalId;
        Name = name;
        LastFour = lastFour;
        CurrencyCode = currencyCode;
        AvailableCreditLimit = availableCreditLimit;
        TotalCreditLimit = totalCreditLimit;
    }

    // Setters privados deixam o EF restaurar o snapshot sem abrir mutação pública da identidade da conta.
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ConnectionId { get; private set; }

    public string ExternalId { get; private set; }

    public string? Name { get; private set; }

    public string? LastFour { get; private set; }

    public string CurrencyCode { get; private set; }

    public decimal? AvailableCreditLimit { get; private set; }

    public decimal? TotalCreditLimit { get; private set; }

    /// <summary>Cria a conta com valores originais, sem deduzir identidade por nome ou final do cartão.</summary>
    public static CardAccount Create(
        Guid id,
        Guid userId,
        Guid connectionId,
        string externalId,
        string currencyCode,
        string? name = null,
        string? lastFour = null,
        decimal? availableCreditLimit = null,
        decimal? totalCreditLimit = null)
    {
        var account = new CardAccount(
            id,
            userId,
            connectionId,
            externalId,
            name,
            lastFour,
            currencyCode,
            availableCreditLimit,
            totalCreditLimit);

        account.ValidateForPersistence();
        return account;
    }

    /// <summary>Atualiza somente moeda de origem para a mesma identidade contextual do cartão.</summary>
    public void UpdateSourceCurrency(string currencyCode)
    {
        var validated = Create(Id, UserId, ConnectionId, ExternalId, currencyCode, Name, LastFour,
            AvailableCreditLimit, TotalCreditLimit);
        CurrencyCode = validated.CurrencyCode;
    }

    /// <summary>Revalida identidade, ID opaco, moeda e limites monetários antes de cada gravação EF.</summary>
    public void ValidateForPersistence()
    {
        EnsureNonEmpty(Id, nameof(Id));
        EnsureNonEmpty(UserId, nameof(UserId));
        EnsureNonEmpty(ConnectionId, nameof(ConnectionId));
        FinancialValueRules.EnsureOpaqueExternalId(ExternalId, nameof(ExternalId));
        EnsureRequired(CurrencyCode, nameof(CurrencyCode));

        if (AvailableCreditLimit.HasValue)
        {
            FinancialValueRules.EnsureNumeric19Scale4(AvailableCreditLimit.Value, nameof(AvailableCreditLimit));
        }

        if (TotalCreditLimit.HasValue)
        {
            FinancialValueRules.EnsureNumeric19Scale4(TotalCreditLimit.Value, nameof(TotalCreditLimit));
        }
    }

    // A moeda é obrigatória, mas não se força BRL nem uma conversão monetária.
    private static void EnsureRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O valor é obrigatório.", parameterName);
        }
    }

    // UUIDs vazios não podem representar registros ou titulares.
    private static void EnsureNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("O UUID não pode ser vazio.", parameterName);
        }
    }
}
