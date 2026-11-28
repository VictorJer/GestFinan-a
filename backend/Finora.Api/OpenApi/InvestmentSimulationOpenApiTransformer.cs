using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Finora.Api.OpenApi;

// Documenta o corpo que o Controller lê manualmente, sem substituir sua validação estrita de duplicatas.
internal sealed class InvestmentSimulationOpenApiTransformer : IOpenApiOperationTransformer
{
    // Somente a operação de cálculo recebe o schema público da requisição validada na borda.
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Description.HttpMethod, "POST", StringComparison.Ordinal)
            || !string.Equals(context.Description.RelativePath, "api/simulations/calculate", StringComparison.Ordinal))
        {
            return;
        }

        var schema = await context.GetOrCreateSchemaAsync(
            typeof(InvestmentSimulationRequestSchema),
            parameterDescription: null,
            cancellationToken);
        var properties = schema.Properties
            ?? throw new InvalidOperationException("O schema do simulador precisa declarar suas propriedades.");
        var requiredProperties = new[] { "initialAmount", "monthlyContribution", "annualRatePercent", "years" };
        if (requiredProperties.Any(name => !properties.ContainsKey(name)))
        {
            throw new InvalidOperationException("O schema do simulador precisa declarar os quatro campos do contrato.");
        }

        // O campo int é obrigatório e não anulável; normalizar o schema evita sugerir null como formato permitido.
        schema.Required = new HashSet<string>(requiredProperties, StringComparer.Ordinal);
        properties["years"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Minimum = "1",
            Maximum = "30",
            Description = "Inteiro JSON entre 1 e 30 anos; frações, null e notação exponencial não são aceitos."
        };

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Objeto JSON estrito com somente os quatro campos indicados; duplicatas, ausências, propriedades extras e tipos/formas não canônicos são recusados.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = schema }
            }
        };
    }
}

// DTO interno de metadados espelha as propriedades e formatos exigidos pelo parser HTTP estrito.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class InvestmentSimulationRequestSchema
{
    [Required]
    [RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [Description("String decimal canônica com duas casas, de 0.00 a 10000000.00.")]
    public string InitialAmount { get; init; } = string.Empty;

    [Required]
    [RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [Description("String decimal canônica com duas casas, de 0.00 a 1000000.00.")]
    public string MonthlyContribution { get; init; } = string.Empty;

    [Required]
    [RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [Description("Percentual anual efetivo em string decimal canônica com duas casas, de 0.00 a 50.00.")]
    public string AnnualRatePercent { get; init; } = string.Empty;

    [Required]
    [Range(1, 30)]
    [Description("Inteiro JSON entre 1 e 30 anos; frações e notação exponencial não são aceitas.")]
    public int Years { get; init; }
}
