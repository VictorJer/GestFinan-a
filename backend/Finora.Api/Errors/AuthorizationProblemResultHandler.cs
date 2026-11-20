using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Finora.Api.Errors;

// Padroniza forbid sem substituir o challenge Bearer, que mantém o contrato específico de autenticação.
internal sealed class AuthorizationProblemResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    // Somente forbid vira 403 Problem Details aqui; success/challenge seguem o fluxo ASP.NET/JwtBearer existente.
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult) => authorizeResult.Forbidden
            ? ApiProblemDetails.WriteAsync(context, StatusCodes.Status403Forbidden)
            : _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
}
