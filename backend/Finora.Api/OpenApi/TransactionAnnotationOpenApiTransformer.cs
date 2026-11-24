using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Finora.Api.OpenApi;

// Publica o contrato estrito do PATCH sem trocar o parser manual que distingue propriedade omitida de null.
internal sealed class TransactionAnnotationOpenApiTransformer : IOpenApiOperationTransformer
{
    // O gerador não infere body de HttpRequest; este transformer adiciona somente o esquema público da rota PATCH.
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Description.HttpMethod, "PATCH", StringComparison.Ordinal)
            || !string.Equals(context.Description.RelativePath, "api/transactions/{id}/annotation", StringComparison.Ordinal))
        {
            return;
        }

        var schema = await context.GetOrCreateSchemaAsync(
            typeof(TransactionAnnotationPatchRequest),
            parameterDescription: null,
            cancellationToken);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Objeto estrito com pelo menos uma propriedade: categoryId e/ou note. Omitir preserva; null limpa o campo correspondente.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = schema }
            }
        };
    }
}

// DTO exclusivo de metadados mantém a allowlist e o limite coerentes com o parser HTTP manual.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransactionAnnotationPatchRequest
{
    [Description("UUID persistido de categoria de sistema ou pessoal própria; null remove a associação.")]
    public Guid? CategoryId { get; init; }

    [Description("Texto simples de até 500 caracteres; null ou string vazia limpa a observação.")]
    [MaxLength(500)]
    public string? Note { get; init; }
}
