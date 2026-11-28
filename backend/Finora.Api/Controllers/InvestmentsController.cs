using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Finora.Api.Authentication;
using Finora.Api.Errors;
using Finora.Api;
using Finora.Application.Authentication;
using Finora.Application.FinancialData;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// Todas as rotas são protegidas e derivam o titular exclusivamente das claims validadas da sessão.
[ApiController]
[Authorize]
[Route("api/investments")]
public sealed class InvestmentsController(ISender sender, TimeProvider clock) : ControllerBase
{
    // Uma única query retorna lista e resumo do mesmo conjunto manual, sem consultar movimentos de cartão.
    [HttpGet]
    [EndpointSummary("Consulta carteira manual e resumo do titular autenticado")]
    [EndpointDescription("Retorna { items, summary } ordenados por nome/ID. Todos os valores monetários são strings decimais canônicas com ponto e exatamente duas casas; diferença simples (atual menos aplicado) pode ser negativa. Apresenta valores por classe sem percentuais nem alegação de rentabilidade ajustada. A carteira vazia retorna valores \"0.00\". Requer sessão Bearer ativa; sessão inválida: 401; limite: 429; persistência indisponível: 503.")]
    [ProducesResponseType<InvestmentPortfolioResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var session = ReadSession();
        if (session is null)
        {
            return UnauthorizedSession();
        }

        var result = await sender.Send(new GetInvestmentPortfolioQuery(session.UserId), cancellationToken);
        return Ok(MapPortfolio(result));
    }

    // O cadastro aceita somente os quatro campos de posição e responde após confirmação da persistência.
    [HttpPost]
    [EndpointSummary("Cadastra investimento manual do titular")]
    [EndpointDescription("Aceita somente { name, class, appliedAmount, currentAmount }; os valores são strings BRL invariáveis com exatamente duas casas (0.00 a 99999999999999999.99). O titular vem da sessão. Sucesso: 201 com o item e valores monetários em strings; entrada inválida: 400; sem sessão: 401; limite: 429; persistência indisponível: 503.")]
    [ProducesResponseType<InvestmentItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var session = ReadSession();
        if (session is null)
        {
            return UnauthorizedSession();
        }

        var request = await ReadInvestmentRequestAsync(Request, cancellationToken);
        if (request is null)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        var result = await sender.Send(
            new CreateInvestmentCommand(
                session.UserId,
                request.Name,
                request.Class,
                request.AppliedAmount,
                request.CurrentAmount),
            cancellationToken);
        return result.Status switch
        {
            InvestmentOperationStatus.Created when result.Item is not null =>
                StatusCode(StatusCodes.Status201Created, MapItem(result.Item)),
            InvestmentOperationStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
            _ => ProblemResponse(StatusCodes.Status500InternalServerError)
        };
    }

    // A alteração valida ID e corpo antes de enviar comando com o UserId da sessão, nunca do cliente.
    [HttpPut("{id}")]
    [EndpointSummary("Edita investimento próprio")]
    [EndpointDescription("Aceita somente { name, class, appliedAmount, currentAmount }, com valores monetários como strings decimais canônicas de duas casas. Sucesso: 200 com item e valores em strings; ID/campos inválidos: 400; item ausente ou alheio: 404 indistinguível; sessão inválida: 401; limite: 429; persistência indisponível: 503.")]
    [ProducesResponseType<InvestmentItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Put([FromRoute] string id, CancellationToken cancellationToken)
    {
        var session = ReadSession();
        if (session is null)
        {
            return UnauthorizedSession();
        }

        if (!Guid.TryParse(id, out var investmentId) || investmentId == Guid.Empty)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        var request = await ReadInvestmentRequestAsync(Request, cancellationToken);
        if (request is null)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        var result = await sender.Send(
            new UpdateInvestmentCommand(
                session.UserId,
                investmentId,
                request.Name,
                request.Class,
                request.AppliedAmount,
                request.CurrentAmount),
            cancellationToken);
        return result.Status switch
        {
            InvestmentOperationStatus.Updated when result.Item is not null => Ok(MapItem(result.Item)),
            InvestmentOperationStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
            InvestmentOperationStatus.NotFound => ProblemResponse(StatusCodes.Status404NotFound),
            _ => ProblemResponse(StatusCodes.Status500InternalServerError)
        };
    }

    // A exclusão só confirma 204 para registro próprio e não revela se um ID alheio existe.
    [HttpDelete("{id}")]
    [EndpointSummary("Exclui investimento próprio")]
    [EndpointDescription("Titular vem da sessão. Sucesso: 204 sem corpo; ID inválido: 400; item ausente ou alheio: 404 indistinguível; sessão inválida: 401; limite: 429; persistência indisponível: 503.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken cancellationToken)
    {
        var session = ReadSession();
        if (session is null)
        {
            return UnauthorizedSession();
        }

        if (!Guid.TryParse(id, out var investmentId) || investmentId == Guid.Empty)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        var result = await sender.Send(
            new DeleteInvestmentCommand(session.UserId, investmentId),
            cancellationToken);
        return result.Status switch
        {
            InvestmentOperationStatus.Deleted => NoContent(),
            InvestmentOperationStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
            InvestmentOperationStatus.NotFound => ProblemResponse(StatusCodes.Status404NotFound),
            _ => ProblemResponse(StatusCodes.Status500InternalServerError)
        };
    }

    // Claims expiradas ou inconsistentes recebem o challenge Bearer sanitizado usado pelas outras rotas.
    private AuthenticationSessionReference? ReadSession() => JwtTokenValidation.ReadSessionClaims(User, clock);

    // O parser exige os quatro nomes exatos e strings monetárias canônicas; não converte números JSON nem duplica campos.
    private static async Task<SaveInvestmentRequest?> ReadInvestmentRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var hasName = false;
            var hasClass = false;
            var hasAppliedAmount = false;
            var hasCurrentAmount = false;
            string? name = null;
            string? @class = null;
            decimal appliedAmount = 0m;
            decimal currentAmount = 0m;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "name" when !hasName && property.Value.ValueKind == JsonValueKind.String:
                        hasName = true;
                        name = property.Value.GetString();
                        break;
                    case "class" when !hasClass && property.Value.ValueKind == JsonValueKind.String:
                        hasClass = true;
                        @class = property.Value.GetString();
                        break;
                    case "appliedAmount" when !hasAppliedAmount
                        && property.Value.ValueKind == JsonValueKind.String
                        && InvestmentHttpMoney.TryParseItemAmount(property.Value.GetString(), out appliedAmount):
                        hasAppliedAmount = true;
                        break;
                    case "currentAmount" when !hasCurrentAmount
                        && property.Value.ValueKind == JsonValueKind.String
                        && InvestmentHttpMoney.TryParseItemAmount(property.Value.GetString(), out currentAmount):
                        hasCurrentAmount = true;
                        break;
                    default:
                        return null;
                }
            }

            return hasName && hasClass && hasAppliedAmount && hasCurrentAmount && name is not null && @class is not null
                ? new SaveInvestmentRequest(name, @class, appliedAmount, currentAmount)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // O challenge e o envelope comum não repetem IDs, claims, payload ou detalhes de persistência.
    private IActionResult UnauthorizedSession()
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return ProblemResponse(StatusCodes.Status401Unauthorized);
    }

    // Todos os erros públicos usam somente status/códigos fixos e application/problem+json.
    private ObjectResult ProblemResponse(int status) => new(ApiProblemDetails.Create(HttpContext, status))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" }
    };

    // A projeção de resposta é explícita e não publica titular, entity tracking ou metadados de banco.
    private static InvestmentItemResponse MapItem(InvestmentItem item) => new(
        item.Id,
        item.Name,
        item.Class,
        InvestmentHttpMoney.Format(item.AppliedAmount),
        InvestmentHttpMoney.Format(item.CurrentAmount));

    // A resposta mantém juntos os mesmos itens e seus totais e valores atuais por classe.
    private static InvestmentPortfolioResponse MapPortfolio(InvestmentPortfolioResult result) => new(
        result.Items.Select(MapItem).ToArray(),
        new InvestmentPortfolioSummaryResponse(
            InvestmentHttpMoney.Format(result.Summary.TotalAppliedAmount),
            InvestmentHttpMoney.Format(result.Summary.TotalCurrentAmount),
            InvestmentHttpMoney.Format(result.Summary.Difference),
            result.Summary.CurrentAmountsByClass
                .Select(value => new InvestmentClassValueResponse(
                    value.Class,
                    value.Label,
                    InvestmentHttpMoney.Format(value.CurrentAmount)))
                .ToArray()));

    // O record interno existe apenas após parser estrito confirmar presença e tipo de cada propriedade.
    private sealed record SaveInvestmentRequest(
        string Name,
        string Class,
        decimal AppliedAmount,
        decimal CurrentAmount);
}

// Tipos HTTP públicos evitam serializar contratos internos do MediatR diretamente.
public sealed record InvestmentItemResponse(
    Guid Id,
    string Name,
    string Class,
    [property: RegularExpression(InvestmentHttpMoney.ItemAmountPattern)]
    [property: Description("String decimal invariável com ponto e exatamente duas casas, limitado a numeric(19,2).")]
    string AppliedAmount,
    [property: RegularExpression(InvestmentHttpMoney.ItemAmountPattern)]
    [property: Description("String decimal invariável com ponto e exatamente duas casas, limitado a numeric(19,2).")]
    string CurrentAmount);

public sealed record InvestmentClassValueResponse(
    string Class,
    string Label,
    [property: RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [property: Description("String decimal invariável com ponto e exatamente duas casas, não negativa.")]
    string CurrentAmount);

public sealed record InvestmentPortfolioSummaryResponse(
    [property: RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [property: Description("String decimal invariável com ponto e exatamente duas casas, não negativa.")]
    string TotalAppliedAmount,
    [property: RegularExpression(InvestmentHttpMoney.SummaryAmountPattern)]
    [property: Description("String decimal invariável com ponto e exatamente duas casas, não negativa.")]
    string TotalCurrentAmount,
    [property: RegularExpression(InvestmentHttpMoney.SignedSummaryAmountPattern)]
    [property: Description("String decimal invariável com ponto e exatamente duas casas; pode ser negativa.")]
    string Difference,
    IReadOnlyList<InvestmentClassValueResponse> CurrentAmountsByClass);

public sealed record InvestmentPortfolioResponse(
    IReadOnlyList<InvestmentItemResponse> Items,
    InvestmentPortfolioSummaryResponse Summary);
