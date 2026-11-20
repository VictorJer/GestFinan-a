using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Finora.Api.Cors;

// Mantém uma única allowlist explícita; a ausência de configuração cria política fechada, sem bloquear HTTP sem Origin.
internal static class FinoraCors
{
    internal const string PolicyName = "FinoraBrowserClients";

    // A allowlist é validada antes da composição para que configuração inválida nunca amplie acesso silenciosamente.
    internal static IServiceCollection AddFinoraCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = ReadOrigins(configuration);
        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length == 0) { return; }

            policy.WithOrigins(origins)
                .WithMethods("GET", "POST", "PATCH")
                .WithHeaders("Authorization", "Content-Type");
        }));
        return services;
    }

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
