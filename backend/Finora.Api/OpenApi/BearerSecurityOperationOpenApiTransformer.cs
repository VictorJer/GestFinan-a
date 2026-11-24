using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Finora.Api.OpenApi;

// Traduz a política HTTP efetiva em segurança por operação sem criar requisito global no documento.
internal sealed class BearerSecurityOperationOpenApiTransformer : IOpenApiOperationTransformer
{
    // AllowAnonymous sempre vence; autorização explícita e fallback autenticada tornam a operação protegida.
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any()) { return; }
        if (!metadata.OfType<IAuthorizeData>().Any())
        {
            var policyProvider = context.ApplicationServices.GetRequiredService<IAuthorizationPolicyProvider>();
            var fallback = await policyProvider.GetFallbackPolicyAsync();
            if (fallback?.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Any() != true) { return; }
        }

        // Adiciona Bearer a cada alternativa existente para preservar transformers prévios sem afrouxar segurança.
        operation.Security ??= [];
        var bearer = new OpenApiSecuritySchemeReference("Bearer", context.Document);
        if (operation.Security.Count == 0)
        {
            operation.Security.Add(new OpenApiSecurityRequirement { [bearer] = [] });
            return;
        }

        foreach (var requirement in operation.Security)
        {
            if (!requirement.ContainsKey(bearer)) { requirement.Add(bearer, []); }
        }
    }
}
