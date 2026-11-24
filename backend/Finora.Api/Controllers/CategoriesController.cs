using System.Text.Json.Serialization;
using Finora.Api.Authentication;
using Finora.Api.Errors;
using Finora.Application.FinancialData;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// O catálogo usa somente a sessão ativa; nem ID, nem tipo, nem titular são aceitos como campos do cliente.
[ApiController]
[Authorize]
[Route("api/categories")]
public sealed class CategoriesController(ISender sender, TimeProvider clock) : ControllerBase
{
    // A leitura não dispara importação e falha fechada se as sete categorias de sistema não estiverem provisionadas.
    [HttpGet]
    [EndpointSummary("Lista categorias próprias e iniciais")]
    [EndpointDescription("Sete categorias de sistema em ordem fixa, pessoais por chave/ID e Sem categoria virtual por último. Catálogo ausente/incompatível: 503; sessão inválida: 401; limite protegido: 429. Nenhuma conexão financeira é consultada.")]
    [ProducesResponseType<CategoryResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null) { return UnauthorizedSession(); }
        var options = await sender.Send(new GetCategoriesQuery(session.UserId), cancellationToken);
        return Ok(options.Select(option => new CategoryResponse(option.Id, option.Name, option.Kind)).ToArray());
    }

    // A desserialização estrita recusa propriedades extras; a política de nome e unicidade vive na Application.
    [HttpPost]
    [EndpointSummary("Cria categoria pessoal do titular")]
    [EndpointDescription("Aceita apenas { name }; o titular vem da sessão. 201 retorna { id, name, kind: personal }; entrada inválida: 400; nome reservado ou duplicado: 409; limite: 429; banco indisponível: 503.")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Post([FromBody] CreateCategoryRequest? request, CancellationToken cancellationToken)
    {
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null) { return UnauthorizedSession(); }
        if (request is null) { return ProblemResponse(StatusCodes.Status400BadRequest); }

        var result = await sender.Send(new CreatePersonalCategoryCommand(session.UserId, request.Name), cancellationToken);
        return result.Status switch
        {
            CreateCategoryStatus.Created when result.Category is { Id: not null } category =>
                StatusCode(StatusCodes.Status201Created, new CategoryResponse(category.Id, category.Name, category.Kind)),
            CreateCategoryStatus.Conflict => ProblemResponse(StatusCodes.Status409Conflict),
            _ => ProblemResponse(StatusCodes.Status400BadRequest)
        };
    }

    // O challenge e o envelope fixo não expõem claims, entradas ou detalhes do banco.
    private IActionResult UnauthorizedSession()
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return ProblemResponse(StatusCodes.Status401Unauthorized);
    }

    private ObjectResult ProblemResponse(int status) => new(ApiProblemDetails.Create(HttpContext, status))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" }
    };
}

// JsonUnmappedMemberHandling impede que clientes escolham ID, titular, escopo ou tipo.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCategoryRequest(string? Name);

// A opção virtual tem ID nulo; nenhum DTO publica o UUID vazio do escopo de sistema.
public sealed record CategoryResponse(Guid? Id, string Name, string Kind);
