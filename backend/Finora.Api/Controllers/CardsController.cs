using Finora.Api.Authentication;
using Finora.Api.Errors;
using Finora.Application.FinancialData;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// Lista cartões persistidos somente depois de validar a sessão e derivar dela o titular confiável.
[ApiController]
[Authorize]
[Route("api/cards")]
public sealed class CardsController(ISender sender, TimeProvider clock) : ControllerBase
{
    // A resposta é uma allowlist de apresentação e mantém sucesso vazio distinto de conexão não resolvida.
    [HttpGet]
    [EndpointSummary("Lista cartões persistidos do titular autenticado")]
    [EndpointDescription("Retorna cartões da conexão própria única, sem identificadores externos, credenciais ou números completos. Lista vazia não afirma ausência de cartões na origem. A leitura não inicia sincronização.")]
    [ProducesResponseType<CardResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // A camada HTTP não confia em identidade fornecida pelo cliente nem em claims ausentes ou duplicadas.
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return ProblemResponse(StatusCodes.Status401Unauthorized);
        }

        var result = await sender.Send(new GetCurrentCardsQuery(session.UserId), cancellationToken);
        if (result.Status != CurrentCardsQueryStatus.Found || result.Cards is null)
        {
            return result.Status switch
            {
                CurrentCardsQueryStatus.ConnectionNotFound => ProblemResponse(StatusCodes.Status404NotFound),
                CurrentCardsQueryStatus.ConnectionAmbiguous => ProblemResponse(StatusCodes.Status409Conflict),
                CurrentCardsQueryStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
                _ => ProblemResponse(StatusCodes.Status500InternalServerError)
            };
        }

        // O mapeamento explícito impede que alterações futuras no contrato Application exponham campos de origem.
        return Ok(result.Cards.Select(card => new CardResponse(
            card.Id,
            card.Name,
            card.LastFour,
            card.CurrencyCode)).ToArray());
    }

    // Erros seguem o envelope sanitizado comum e não revelam identidade nem cardinalidade de dados alheios.
    private ObjectResult ProblemResponse(int status) => new(ApiProblemDetails.Create(HttpContext, status))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" }
    };
}

// O DTO expõe apenas ID interno e valores de apresentação que podem estar ausentes na origem.
public sealed record CardResponse(Guid Id, string? Name, string? LastFour, string CurrencyCode);
