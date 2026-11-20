using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Errors;

// Centraliza o envelope HTTP sem incorporar entrada, exceções ou detalhes de provedores.
internal static class ApiProblemDetails
{
    // Cada status público tem código e título estáveis; chamadores podem preservar contratos autenticados legados.
    internal static ProblemDetails Create(HttpContext context, int status, string? code = null, string? title = null)
    {
        var defaults = status switch
        {
            400 => ("REQUEST_INVALID_INPUT", "A entrada não é válida."),
            401 => ("AUTH_INVALID_CREDENTIALS", "Autenticação inválida."),
            403 => ("PERMISSION_REQUIRED", "Permissão insuficiente."),
            404 => ("RESOURCE_NOT_FOUND", "Recurso não encontrado."),
            409 => ("RESOURCE_CONFLICT", "A operação conflita com o estado atual."),
            429 => ("RATE_LIMITED", "Limite de solicitações excedido."),
            500 => ("INTERNAL_ERROR", "Falha interna ao processar a solicitação."),
            503 => ("DEPENDENCY_UNAVAILABLE", "Serviço temporariamente indisponível."),
            _ => ("REQUEST_FAILED", "A solicitação não pôde ser concluída.")
        };

        return new ProblemDetails
        {
            Status = status,
            Title = title ?? defaults.Item2,
            Extensions =
            {
                ["code"] = code ?? defaults.Item1,
                ["traceId"] = context.TraceIdentifier
            }
        };
    }

    // Respostas já iniciadas não podem ser substituídas; erros novos recebem somente application/problem+json.
    internal static Task WriteAsync(HttpContext context, int status, string? code = null, string? title = null)
    {
        if (context.Response.HasStarted) { return Task.CompletedTask; }
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Create(context, status, code, title),
            options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
