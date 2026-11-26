using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Finora.Api.Cors;

// Seleciona a política estreita para os POSTs de sincronização e mantém a política nomeada original como fallback.
internal sealed class FinoraCorsPolicyProvider(IOptions<CorsOptions> corsOptions) : ICorsPolicyProvider
{
    // A escolha contextual ocorre no mesmo UseCors, antes da fronteira de autenticação.
    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var selectedPolicyName = string.Equals(policyName, FinoraCors.PolicyName, StringComparison.Ordinal) &&
            FinoraCors.IsSyncIntentRequest(context.Request)
                ? FinoraCors.SyncIntentPolicyName
                : policyName;

        var selectedPolicy = selectedPolicyName is null
            ? null
            : corsOptions.Value.GetPolicy(selectedPolicyName);
        return Task.FromResult(selectedPolicy);
    }
}
