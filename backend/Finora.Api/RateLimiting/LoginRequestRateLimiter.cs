using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Finora.Api.Errors;
using Microsoft.AspNetCore.Authorization;

namespace Finora.Api.RateLimiting;

// Mantém a partição e a janela do login fora do caminho de autenticação que lê o corpo da requisição.
internal sealed class LoginRequestRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    // A partição usa somente o IP observado pela conexão; cabeçalhos de encaminhamento não são consultados.
    internal LoginRequestRateLimiter(int permitLimit, TimeSpan window)
    {
        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                }));
    }

    // O middleware adquire uma permissão antes de a borda de autenticação validar conteúdo ou resolver Identity.
    internal ValueTask<RateLimitLease> AcquireAsync(HttpContext context) =>
        _limiter.AcquireAsync(context, permitCount: 1, context.RequestAborted);

    public void Dispose() => _limiter.Dispose();
}

// Rejeita login excedente antes da leitura do corpo e preserva no-store no envelope comum de 429.
internal sealed class LoginRequestRateLimitBoundary(
    RequestDelegate next,
    LoginRequestRateLimiter limiter)
{
    // As demais rotas seguem adiante; CORS já tratou preflight e mantém cabeçalhos nas respostas subsequentes.
    public async Task InvokeAsync(HttpContext context)
    {
        if (!Authentication.AuthenticationHttp.IsLogin(context))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        using var lease = await limiter.AcquireAsync(context);
        if (!lease.IsAcquired)
        {
            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status429TooManyRequests);
            return;
        }

        await next(context);
    }
}

// Particiona a janela por sub já validado após autenticação/autorização, não por entrada da requisição.
internal sealed class AuthenticatedRequestRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    // Cada titular obtém uma janela independente, mantida somente nesta instância da API.
    internal AuthenticatedRequestRateLimiter(int permitLimit, TimeSpan window)
    {
        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue("sub") ?? "missing-subject",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                }));
    }

    // A action de teste/produção só é alcançada se adquirir uma permissão do titular autenticado.
    internal ValueTask<RateLimitLease> AcquireAsync(HttpContext context) =>
        _limiter.AcquireAsync(context, permitCount: 1, context.RequestAborted);

    public void Dispose() => _limiter.Dispose();
}

// Executa depois da autorização e ignora preflight, endpoints anônimos, documentação e health.
internal sealed class AuthenticatedRequestRateLimitBoundary(
    RequestDelegate next,
    AuthenticatedRequestRateLimiter limiter)
{
    // O fallback de autorização já validou o principal e a sessão antes de selecionar a partição do titular.
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (HttpMethods.IsOptions(context.Request.Method) || endpoint is null ||
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        using var lease = await limiter.AcquireAsync(context);
        if (!lease.IsAcquired)
        {
            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status429TooManyRequests);
            return;
        }

        await next(context);
    }
}

// Lê defaults aprovados e recusa valores ausentes malformados, zero ou negativos antes de atender HTTP.
internal static class FinoraRateLimitRegistration
{
    // Registra limites locais fixos sem fila e sem confiar em origens informadas pelo cliente.
    internal static IServiceCollection AddFinoraRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var permitLimit = ReadPositiveInteger(configuration, "RateLimiting:Login:PermitLimit", 10);
        var windowSeconds = ReadPositiveInteger(configuration, "RateLimiting:Login:WindowSeconds", 60);
        services.AddSingleton(new LoginRequestRateLimiter(permitLimit, TimeSpan.FromSeconds(windowSeconds)));
        var authenticatedPermitLimit = ReadPositiveInteger(configuration, "RateLimiting:Protected:PermitLimit", 120);
        var authenticatedWindowSeconds = ReadPositiveInteger(configuration, "RateLimiting:Protected:WindowSeconds", 60);
        services.AddSingleton(new AuthenticatedRequestRateLimiter(
            authenticatedPermitLimit, TimeSpan.FromSeconds(authenticatedWindowSeconds)));
        return services;
    }

    // Configuração inválida falha explicitamente; não converte erro em desativação ou limite irrestrito.
    private static int ReadPositiveInteger(IConfiguration configuration, string key, int fallback)
    {
        var configured = configuration[key];
        if (configured is null) { return fallback; }
        if (int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        throw new InvalidOperationException($"{key} deve ser um inteiro positivo.");
    }
}
