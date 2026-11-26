using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Finora.Api.Cors;

// Mantém uma única allowlist explícita; a ausência de configuração cria política fechada, sem bloquear HTTP sem Origin.
internal static class FinoraCors
{
    internal const string PolicyName = "FinoraBrowserClients";
    internal const string SyncIntentPolicyName = "FinoraSyncIntentBrowserClients";

    // A allowlist é validada antes da composição para que configuração inválida nunca amplie acesso silenciosamente.
    internal static IServiceCollection AddFinoraCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = ReadOrigins(configuration);
        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy => ConfigurePolicy(policy, origins, includeIdempotencyKey: false));
            options.AddPolicy(SyncIntentPolicyName, policy => ConfigurePolicy(policy, origins, includeIdempotencyKey: true));
        });

        // O provider escolhe a política restrita pelo caminho/método sem criar um segundo ponto UseCors.
        services.AddSingleton<ICorsPolicyProvider, FinoraCorsPolicyProvider>();
        return services;
    }

    // Ambas as políticas compartilham origens e métodos; somente os dois POSTs de intenção recebem a chave extra.
    private static void ConfigurePolicy(CorsPolicyBuilder policy, string[] origins, bool includeIdempotencyKey)
    {
        if (origins.Length == 0) { return; }

        var headers = includeIdempotencyKey
            ? new[] { "Authorization", "Content-Type", "Idempotency-Key" }
            : new[] { "Authorization", "Content-Type" };

        policy.WithOrigins(origins)
            .WithMethods("GET", "POST", "PATCH")
            .WithHeaders(headers);
    }

    // A política adicional vale para POST efetivo ou seu preflight, sem aceitar prefixos ou métodos duplicados.
    internal static bool IsSyncIntentRequest(HttpRequest request)
    {
        if (!IsSyncIntentPath(request.Path)) { return false; }
        if (HttpMethods.IsPost(request.Method)) { return true; }
        if (!HttpMethods.IsOptions(request.Method)) { return false; }

        var requestedMethods = request.Headers["Access-Control-Request-Method"];
        return requestedMethods.Count == 1 &&
            string.Equals(requestedMethods[0], HttpMethods.Post, StringComparison.OrdinalIgnoreCase);
    }

    // A comparação de caminho completo impede que endpoints semelhantes herdem a exceção de cabeçalho.
    private static bool IsSyncIntentPath(PathString path) =>
        string.Equals(path.Value, "/api/connections/current/sync", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path.Value, "/api/connections/current/sync/on-entry", StringComparison.OrdinalIgnoreCase);

    // Aceita somente origens HTTP(S) completas, sem wildcard, path, credenciais, query ou fragmento.
    private static string[] ReadOrigins(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var origins = new List<string>(configured.Length);
        foreach (var origin in configured)
        {
            if (string.IsNullOrWhiteSpace(origin) || !string.Equals(origin, origin.Trim(), StringComparison.Ordinal) ||
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                !string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cors:AllowedOrigins contém origem inválida.");
            }

            if (origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cors:AllowedOrigins contém origens duplicadas.");
            }

            origins.Add(origin);
        }

        return origins.ToArray();
    }
}
