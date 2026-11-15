using Microsoft.Extensions.Configuration;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Guarda apenas opções lidas de fontes externas; nenhum segredo faz parte de argumentos.</summary>
public sealed record PluggyOptions(string? ClientId, string? ClientSecret, string? ItemId, string? AccountId)
{
    // Lê a seção Pluggy sem validar no host HTTP; validação ocorre somente na execução explícita.
    public static PluggyOptions FromConfiguration(IConfiguration configuration) => new(
        configuration["Pluggy:ClientId"],
        configuration["Pluggy:ClientSecret"],
        configuration["Pluggy:ItemId"],
        configuration["Pluggy:AccountId"]);

    // Retorna nomes de chaves ausentes, nunca os valores configurados ou parcialmente mascarados.
    public IReadOnlyList<string> GetMissingRequiredKeys()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add("Pluggy:ClientId");
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add("Pluggy:ClientSecret");
        if (string.IsNullOrWhiteSpace(ItemId)) missing.Add("Pluggy:ItemId");
        return missing;
    }
}
