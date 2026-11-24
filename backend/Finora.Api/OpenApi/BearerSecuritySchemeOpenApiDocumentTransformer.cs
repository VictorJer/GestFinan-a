using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Finora.Api.OpenApi;

// Registra o esquema Bearer no documento sem apagar componentes ou esquemas já existentes.
internal sealed class BearerSecuritySchemeOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    // O valor descreve somente o mecanismo; nenhum token ou credencial integra o documento.
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header
        };

        return Task.CompletedTask;
    }
}
