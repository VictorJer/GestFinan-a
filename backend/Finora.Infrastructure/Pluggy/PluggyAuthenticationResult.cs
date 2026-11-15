using Finora.Application.Pluggy;

namespace Finora.Infrastructure.Pluggy;

/// <summary>Resultado mínimo da autenticação, sem retornar nem serializar a API key.</summary>
public sealed record PluggyAuthenticationResult(
    bool Succeeded,
    PluggyLocalReadFailureCategory? FailureCategory = null,
    IReadOnlyList<string>? MissingConfiguration = null);
