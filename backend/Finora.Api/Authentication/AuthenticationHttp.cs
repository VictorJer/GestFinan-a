using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;

namespace Finora.Api.Authentication;

// Mantém erros de autenticação locais, sem transformar esta entrega na padronização geral da FNR-011.
internal static class AuthenticationHttp
{
    internal static readonly object UnavailableKey = new();
    internal const int MaximumBodyBytes = 16 * 1024;

    // A proteção é explícita por endpoint; health e documentação não autenticam headers incidentais.
    internal static bool IsProtected(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0
        && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null;

    // Limita o tratamento de corpo/no-store à operação de login, sem alterar rotas de diagnóstico ou comandos.
    internal static bool IsLogin(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/auth/login", StringComparison.OrdinalIgnoreCase);

    // Somente títulos/códigos fixos saem da borda, sem ModelState, valores recebidos ou detalhes do Identity.
    internal static ProblemDetails Problem(HttpContext context, int status)
    {
        var (code, title) = status switch
        {
            400 => ("AUTH_INVALID_INPUT", "Entrada inválida."),
            413 => ("AUTH_BODY_TOO_LARGE", "Corpo da requisição excede o limite."),
            401 => ("AUTH_INVALID_CREDENTIALS", "Autenticação inválida."),
            503 => ("AUTH_UNAVAILABLE", "Autenticação indisponível."),
            _ => ("AUTH_UNEXPECTED_FAILURE", "Falha ao processar autenticação.")
        };
        return new ProblemDetails
        {
            Status = status, Title = title,
            Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
        };
    }

    // A escrita mantém o mesmo contrato inclusive para erros anteriores à execução do Controller.
    internal static Task WriteProblemAsync(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Problem(context, status),
            options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}

// Contém o tamanho da entrada antes do model binder, inclusive streams sem Content-Length.
internal sealed class AuthenticationBoundary(RequestDelegate next)
{
    // A fronteira cobre apenas login/endpoints protegidos; os diagnósticos conservam o contrato existente.
    public async Task InvokeAsync(HttpContext context)
    {
        var login = AuthenticationHttp.IsLogin(context);
        if (!login && !AuthenticationHttp.IsProtected(context)) { await next(context); return; }
        if (login) { context.Response.Headers.CacheControl = "no-store"; }
        var original = context.Request.Body;
        byte[]? buffer = null;
        try
        {
            if (login)
            {
                // A unidade de configuração é verificada sem resolver contexto, issuer ou conexão inválidos.
                if (!context.RequestServices.GetRequiredService<AuthenticationAvailability>().IsAvailable)
                {
                    await AuthenticationHttp.WriteProblemAsync(context, 503);
                    return;
                }
                if (context.Request.ContentLength > AuthenticationHttp.MaximumBodyBytes)
                {
                    await AuthenticationHttp.WriteProblemAsync(context, 413);
                    return;
                }
                if (!context.Request.HasJsonContentType())
                {
                    await AuthenticationHttp.WriteProblemAsync(context, 400);
                    return;
                }
                // Um byte além do limite distingue corpo permitido de stream excessivo sem alocação irrestrita.
                buffer = new byte[AuthenticationHttp.MaximumBodyBytes + 1];
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = await original.ReadAsync(buffer.AsMemory(count), context.RequestAborted);
                    if (read == 0) { break; }
                    count += read;
                }
                if (count > AuthenticationHttp.MaximumBodyBytes)
                {
                    await AuthenticationHttp.WriteProblemAsync(context, 413);
                    return;
                }
                context.Request.Body = new MemoryStream(buffer, 0, count, writable: false);
            }
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
        catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
        {
            await AuthenticationHttp.WriteProblemAsync(context, exception.StatusCode == 413 ? 413 : 400);
        }
        catch (Exception) when (!context.Response.HasStarted)
        {
            // Falha inesperada não imprime exceção bruta, e-mail, senha ou conexão, mesmo em Development.
            await AuthenticationHttp.WriteProblemAsync(context, 500);
        }
        finally
        {
            // O buffer transitório contém credenciais; restaurar o stream original e apagar os bytes após o binding.
            if (buffer is not null)
            {
                if (!ReferenceEquals(context.Request.Body, original)) { context.Request.Body.Dispose(); }
                context.Request.Body = original;
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }
}
