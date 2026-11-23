using Finora.Api.Authentication;
using Finora.Api.Errors;
using Finora.Application.FinancialData;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// A rota expõe somente o estado sanitizado da execução autenticada e não depende da disponibilidade do broker.
[ApiController]
[Authorize]
[Route("api/sync-runs")]
public sealed class SyncRunsController(ISender sender, TimeProvider clock) : ControllerBase
{
    // A consulta combina o ID da rota com o titular extraído da sessão validada; ausência e titular alheio retornam 404.
    [HttpGet("{syncRunId:guid}")]
    [EndpointSummary("Consulta o estado sanitizado de uma sincronização própria")]
    [EndpointDescription("Retorna somente metadados operacionais da SyncRun pertencente ao titular autenticado.")]
    [ProducesResponseType<SyncRunStatusResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Get(Guid syncRunId, CancellationToken cancellationToken)
    {
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return ProblemResponse(StatusCodes.Status401Unauthorized);
        }

        var result = await sender.Send(
            new GetSyncRunStatusQuery(session.UserId, syncRunId),
            cancellationToken);
        if (result is null)
        {
            return ProblemResponse(StatusCodes.Status404NotFound);
        }

        return Ok(new SyncRunStatusResponse(
            result.SyncRunId,
            result.State.ToString(),
            result.CreatedAtUtc,
            result.StartedAtUtc,
            result.FinishedAtUtc,
            result.RequestedFrom,
            result.RequestedToExclusive,
            result.PagesRead,
            result.ReceivedCount,
            result.UniqueCount,
            result.PersistedCount,
            result.ObservedFrom,
            result.ObservedTo,
            result.CoverageState.ToString(),
            result.FailureCode,
            result.NextAttemptAtUtc));
    }

    // Erros de consulta não distinguem ID inexistente de execução pertencente a outro titular.
    private ObjectResult ProblemResponse(int status) => new(ApiProblemDetails.Create(HttpContext, status))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" }
    };
}

// O contrato público não inclui titular, conexão, fingerprint, chave opaca ou conteúdo financeiro.
public sealed record SyncRunStatusResponse(
    Guid SyncRunId,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    DateOnly RequestedFrom,
    DateOnly? RequestedToExclusive,
    int? PagesRead,
    int? ReceivedCount,
    int? UniqueCount,
    int? PersistedCount,
    DateOnly? ObservedFrom,
    DateOnly? ObservedTo,
    string CoverageState,
    string? FailureCode,
    DateTimeOffset? NextAttemptAtUtc = null);
