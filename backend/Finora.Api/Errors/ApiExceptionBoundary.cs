using Finora.Application.Authentication;

namespace Finora.Api.Errors;

// Converte falhas não tratadas em erros sanitizados e não registra exceção ou dado de entrada.
internal sealed class ApiExceptionBoundary(RequestDelegate next, ILogger<ApiExceptionBoundary> logger)
{
    // Uma dependência só vira 503 quando a aplicação a classifica com o tipo operacional conhecido.
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
        }
        catch (Exception exception)
        {
            var unavailable = exception is AuthenticationUnavailableException;
            var status = unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
            var code = unavailable ? "DEPENDENCY_UNAVAILABLE" : "INTERNAL_ERROR";

            // Registra apenas categoria fixa e traceId; não passa exception ao logger nem inclui mensagem externa.
            logger.LogError("API request failed with code {ErrorCode}; traceId {TraceId}", code, context.TraceIdentifier);
            if (context.Response.HasStarted)
            {
                context.Abort();
                return;
            }

            await ApiProblemDetails.WriteAsync(context, status, code);
        }
    }
}
