using System.Text.Json.Nodes;
using Finora.Api.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Finora.Api.OpenApi;

// Documenta o corpo da decisão manual sem relaxar o parser estrito da rota PUT.
internal sealed class TransactionRecurrenceDecisionOpenApiTransformer : IOpenApiOperationTransformer
{
    // O gerador não infere o body lido diretamente do HttpRequest; só a rota de decisão recebe este schema.
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Description.HttpMethod, "PUT", StringComparison.Ordinal)
            || !string.Equals(
                context.Description.RelativePath,
                "api/transactions/recurrence-candidates/{candidateId}/decision",
                StringComparison.Ordinal))
        {
            return;
        }

        var schema = await context.GetOrCreateSchemaAsync(
            typeof(TransactionRecurrenceDecisionRequest),
            parameterDescription: null,
            cancellationToken);
        var properties = schema.Properties
            ?? throw new InvalidOperationException("O schema da decisão precisa declarar suas propriedades.");
        if (!properties.ContainsKey("decision"))
        {
            throw new InvalidOperationException("O schema da decisão precisa declarar o campo decision.");
        }

        schema.Required = new HashSet<string>(StringComparer.Ordinal) { "decision" };
        properties["decision"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Escolha explícita allowlisted; repetir o PUT corrige a decisão vigente do titular.",
            Enum =
            [
                JsonValue.Create("Subscription")!,
                JsonValue.Create("Installment")!,
                JsonValue.Create("IndependentPurchases")!,
                JsonValue.Create("Rejected")!
            ]
        };

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Objeto estrito com somente a propriedade decision.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = schema }
            }
        };
    }
}
