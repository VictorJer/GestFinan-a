using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Finora.Api;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Finora.Api.OpenApi;

// Documenta corpos lidos pelo parser estrito a partir de HttpRequest, sem trocar a rejeição manual de duplicatas.
internal sealed class InvestmentOpenApiTransformer : IOpenApiOperationTransformer
{
    // POST e PUT compartilham o mesmo contrato JSON; titular e ID permanecem fora do corpo.
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath;
        var isCreate = string.Equals(context.Description.HttpMethod, "POST", StringComparison.Ordinal)
            && string.Equals(path, "api/investments", StringComparison.Ordinal);
        var isUpdate = string.Equals(context.Description.HttpMethod, "PUT", StringComparison.Ordinal)
            && string.Equals(path, "api/investments/{id}", StringComparison.Ordinal);
        if (!isCreate && !isUpdate)
        {
            return;
        }

        var schema = await context.GetOrCreateSchemaAsync(
            typeof(InvestmentRequestSchema),
            parameterDescription: null,
            cancellationToken);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Objeto estrito com name, class, appliedAmount e currentAmount; valores monetários são strings canônicas invariáveis com ponto e exatamente duas casas. Campos extras, duplicados, ausentes ou com tipo/formato inválido são rejeitados.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = schema }
            }
        };
    }
}

// DTO exclusivo de metadados mantém documentados os mesmos campos que o parser HTTP aceita.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class InvestmentRequestSchema
{
    [Required]
    [MaxLength(120)]
    [Description("Nome manual obrigatório, sem espaços externos e com até 120 caracteres.")]
    public string Name { get; init; } = string.Empty;

    [Required]
    [MaxLength(32)]
    [Description("Código estável: fixed_income, variable_income ou real_estate_funds.")]
    public string Class { get; init; } = string.Empty;

    [RegularExpression(InvestmentHttpMoney.ItemAmountPattern)]
    [Description("String decimal BRL invariável com ponto e exatamente duas casas; faixa 0.00–99999999999999999.99, sem arredondamento.")]
    public string AppliedAmount { get; init; } = string.Empty;

    [RegularExpression(InvestmentHttpMoney.ItemAmountPattern)]
    [Description("String decimal BRL invariável com ponto e exatamente duas casas; faixa 0.00–99999999999999999.99, sem arredondamento.")]
    public string CurrentAmount { get; init; } = string.Empty;
}
