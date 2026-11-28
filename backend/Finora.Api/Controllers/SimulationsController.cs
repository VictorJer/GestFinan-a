using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Finora.Api.Errors;
using Finora.Domain.FinancialData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// O simulador é uma operação protegida, autocontida e sem acesso à carteira ou a persistência.
[ApiController]
[Authorize]
[Route("api/simulations")]
public sealed class SimulationsController : ControllerBase
{
    private const int MaximumRequestBodyBytes = 4096;
    private static readonly JsonDocumentOptions RequestJsonOptions = new() { MaxDepth = 4 };

    // A validação completa da forma HTTP precede a chamada ao cálculo puro e à projeção allowlisted da resposta.
    [HttpPost("calculate")]
    [EndpointSummary("Calcula projeção nominal de aportes do titular autenticado")]
    [EndpointDescription("Aceita somente { initialAmount, monthlyContribution, annualRatePercent, years }. Os três primeiros campos são strings decimais canônicas com ponto e duas casas; years é inteiro JSON. Limites: 0.00–10000000.00, 0.00–1000000.00, 0.00–50.00 e 1–30 anos. A série começa no mês zero, aplica a taxa anual efetiva convertida para mês e adiciona aportes ao fim de cada mês; não arredonda saldos intermediários. Resposta 200 contém somente pontos mensais e totais como strings monetárias. A projeção é nominal, sem impostos, taxas ou inflação, não é recomendação e não lê nem altera a carteira. Campos extras/duplicados, tipos/formas inválidos ou projeção não representável: 400; sem sessão: 401; limite: 429; falha interna: 500; autenticação indisponível: 503.")]
    [ProducesResponseType<InvestmentSimulationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Calculate(CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(Request, cancellationToken);
        if (request is null)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        try
        {
            // A lógica financeira permanece no domínio; a borda só traduz entradas e formata os valores publicados.
            var result = InvestmentSimulationCalculator.Calculate(
                request.InitialAmount,
                request.MonthlyContribution,
                request.AnnualRatePercent,
                request.Years);
            return Ok(MapResponse(result));
        }
        catch (ArgumentOutOfRangeException)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException)
        {
            // Projeções não representáveis são recusadas sem expor exceção, entrada ou detalhe numérico interno.
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }
    }

    // O parser consome no máximo um byte além do limite e exige exatamente os quatro campos do contrato.
    private static async Task<SimulationRequest?> ReadRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType() || request.ContentLength > MaximumRequestBodyBytes)
        {
            return null;
        }

        var buffer = new byte[MaximumRequestBodyBytes + 1];
        try
        {
            // O limite também vale para streams sem Content-Length, evitando materialização irrestrita do corpo.
            var count = 0;
            while (count < buffer.Length)
            {
                var bytesRead = await request.Body.ReadAsync(buffer.AsMemory(count), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                count += bytesRead;
            }

            if (count > MaximumRequestBodyBytes)
            {
                return null;
            }

            using var document = JsonDocument.Parse(buffer.AsMemory(0, count), RequestJsonOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Presença independente distingue campo ausente de valor zero e torna duplicatas inválidas.
            var hasInitialAmount = false;
            var hasMonthlyContribution = false;
            var hasAnnualRatePercent = false;
            var hasYears = false;
            decimal initialAmount = 0m;
            decimal monthlyContribution = 0m;
            decimal annualRatePercent = 0m;
            var years = 0;

            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "initialAmount" when !hasInitialAmount
                        && property.Value.ValueKind == JsonValueKind.String
                        && TryParseDecimal(property.Value.GetString(), InvestmentSimulationCalculator.MaximumInitialAmount,
                            out initialAmount):
                        hasInitialAmount = true;
                        break;
                    case "monthlyContribution" when !hasMonthlyContribution
                        && property.Value.ValueKind == JsonValueKind.String
                        && TryParseDecimal(property.Value.GetString(),
                            InvestmentSimulationCalculator.MaximumMonthlyContribution, out monthlyContribution):
                        hasMonthlyContribution = true;
                        break;
                    case "annualRatePercent" when !hasAnnualRatePercent
                        && property.Value.ValueKind == JsonValueKind.String
                        && TryParseDecimal(property.Value.GetString(),
                            InvestmentSimulationCalculator.MaximumAnnualRatePercent, out annualRatePercent):
                        hasAnnualRatePercent = true;
                        break;
                    case "years" when !hasYears
                        && property.Value.ValueKind == JsonValueKind.Number
                        && TryParseYears(property.Value, out years):
                        hasYears = true;
                        break;
                    default:
                        return null;
                }
            }

            return hasInitialAmount && hasMonthlyContribution && hasAnnualRatePercent && hasYears
                ? new SimulationRequest(initialAmount, monthlyContribution, annualRatePercent, years)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            // O corpo transitório pode conter valores financeiros do titular; seus bytes não permanecem no buffer.
            Array.Clear(buffer);
        }
    }

    // Comparar com a própria forma fixa rejeita sinal, zeros à esquerda, escala variável e cultura local.
    private static bool TryParseDecimal(string? text, decimal maximum, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrEmpty(text)
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0m
            || parsed > maximum
            || !string.Equals(text, FormatAmount(parsed), StringComparison.Ordinal))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    // A forma numérica bruta precisa conter dígitos inteiros, rejeitando também 1.0 e notação exponencial.
    private static bool TryParseYears(JsonElement value, out int years)
    {
        years = 0;
        return int.TryParse(value.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out years)
            && years is >= 1 and <= InvestmentSimulationCalculator.MaximumYears;
    }

    // O arredondamento acontece somente na borda de apresentação, preservando os saldos internos do domínio.
    private static string FormatAmount(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    // A resposta pública contém somente série e totais monetários textuais definidos pelo contrato.
    private static InvestmentSimulationResponse MapResponse(InvestmentSimulationResult result) => new(
        result.Points.Select(point => new InvestmentSimulationPointResponse(
            point.Month,
            FormatAmount(point.TotalContributed),
            FormatAmount(point.ProjectedValue))).ToArray(),
        FormatAmount(result.TotalContributed),
        FormatAmount(result.ProjectedValue),
        FormatAmount(result.ProjectedReturn));

    // Erros de formato e de representação compartilham envelope fixo, sem ecoar conteúdo recebido.
    private ObjectResult ProblemResponse(int status) => new(ApiProblemDetails.Create(HttpContext, status))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" }
    };

    // O DTO interno só é criado depois da validação de todos os campos e limites HTTP.
    private sealed record SimulationRequest(
        decimal InitialAmount,
        decimal MonthlyContribution,
        decimal AnnualRatePercent,
        int Years);
}

// DTOs públicos mantêm os valores monetários como texto e não expõem tipos ou estado interno do domínio.
public sealed record InvestmentSimulationPointResponse(
    int Month,
    [property: RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [property: Description("Total aportado acumulado no mês, em string decimal invariável com duas casas.")]
    string TotalContributed,
    [property: RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [property: Description("Patrimônio projetado para o mês, em string decimal invariável com duas casas.")]
    string ProjectedValue);

public sealed record InvestmentSimulationResponse(
    IReadOnlyList<InvestmentSimulationPointResponse> Points,
    [property: RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [property: Description("Soma do valor inicial e dos aportes mensais, em string decimal invariável com duas casas.")]
    string TotalContributed,
    [property: RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [property: Description("Patrimônio nominal final projetado, em string decimal invariável com duas casas.")]
    string ProjectedValue,
    [property: RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [property: Description("Patrimônio final menos total aportado, em string decimal invariável com duas casas.")]
    string ProjectedReturn);
