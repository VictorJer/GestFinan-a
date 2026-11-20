using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Finora.Api.Errors;

// Faz também os filtros MVC de erro de cliente usarem os códigos e títulos definidos para a API.
internal sealed class ApiProblemDetailsFactory : ProblemDetailsFactory
{
    // Ignora detail/type/instance recebidos de frameworks ou exceções para impedir divulgação acidental.
    public override ProblemDetails CreateProblemDetails(HttpContext httpContext, int? statusCode = null,
        string? title = null, string? type = null, string? detail = null, string? instance = null) =>
        ApiProblemDetails.Create(httpContext, statusCode ?? StatusCodes.Status500InternalServerError);

    // A resposta de validação não expõe chaves, mensagens nem valores do ModelState.
    public override ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext,
        ModelStateDictionary modelState, int? statusCode = null, string? title = null, string? type = null,
        string? detail = null, string? instance = null)
    {
        var common = ApiProblemDetails.Create(httpContext, statusCode ?? StatusCodes.Status400BadRequest);
        var validation = new ValidationProblemDetails(new Dictionary<string, string[]>())
        {
            Status = common.Status,
            Title = common.Title
        };
        foreach (var extension in common.Extensions) { validation.Extensions[extension.Key] = extension.Value; }
        return validation;
    }
}
