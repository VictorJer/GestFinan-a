using Finora.Application.Authentication;
using Finora.Application.FinancialData;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Finora.Api.Errors;

// Converte falhas não tratadas em erros sanitizados e não registra exceção ou dado de entrada.
internal sealed class ApiExceptionBoundary(RequestDelegate next, ILogger<ApiExceptionBoundary> logger)
{
    // Falhas de banco/dependência e commit incerto recebem 503; erros não classificados permanecem falha interna.
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
            var acceptanceIndeterminate = exception is SyncRunAcceptanceIndeterminateException;
            var unavailable = exception is AuthenticationUnavailableException or DbException or DbUpdateException;
            var status = unavailable || acceptanceIndeterminate
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status500InternalServerError;
            var code = acceptanceIndeterminate
                ? "ACCEPTANCE_RESULT_INDETERMINATE"
                : unavailable ? "DEPENDENCY_UNAVAILABLE" : "INTERNAL_ERROR";
            var title = acceptanceIndeterminate
                ? "O resultado da aceitação é indeterminado. Repita a solicitação usando a mesma chave Idempotency-Key."
                : null;

            // Registra apenas categoria fixa e traceId; não passa exception ao logger nem inclui mensagem externa.
            logger.LogError("API request failed with code {ErrorCode}; traceId {TraceId}", code, context.TraceIdentifier);
            if (context.Response.HasStarted)
            {
                context.Abort();
                return;
            }

            await ApiProblemDetails.WriteAsync(context, status, code, title);
        }
    }
}
