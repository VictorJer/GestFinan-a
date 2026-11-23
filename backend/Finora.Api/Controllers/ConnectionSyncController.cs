using System.ComponentModel.DataAnnotations;
using Finora.Api.Authentication;
using Finora.Api.Errors;
using Finora.Application.FinancialData;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// O endpoint transforma somente identidade autenticada e chave opaca em uma intenção persistente de sincronização.
[ApiController]
[Authorize]
[Route("api/connections/current")]
public sealed class ConnectionSyncController(
    ISender sender,
    TimeProvider clock,
    SyncRunAcceptanceAvailability availability) : ControllerBase
{
    // A resposta 202 só é construída depois que o handler retorna após o commit conhecido de SyncRun e outbox.
    [HttpPost("sync")]
    [EndpointSummary("Solicita sincronização idempotente da conexão atual do titular")]
    [EndpointDescription("Exige um único cabeçalho Idempotency-Key; 202 confirma somente a aceitação persistida. Outra intenção durante execução ativa recebe 409 com a referência existente.")]
    [ProducesResponseType<SyncRunAcceptanceResponse>(202)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> Accept(
        [FromHeader(Name = "Idempotency-Key"), Required] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // A sessão já foi validada pelo middleware; a leitura estrita evita confiar em claims ausentes ou duplicadas.
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return ProblemResponse(StatusCodes.Status401Unauthorized);
        }

        // Um único valor visível ASCII preserva a mesma chave opaca em retries sem aceitar concatenação ambígua.
        var keyValues = Request.Headers["Idempotency-Key"];
        if (keyValues.Count != 1
            || !string.Equals(keyValues[0], idempotencyKey, StringComparison.Ordinal)
            || !SyncRunIdempotency.TryHashKey(idempotencyKey, out _))
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        // Sem transporte configurado, a rota falha antes de consultar/gravar execução ou outbox.
        if (!availability.IsAvailable)
        {
            return ProblemResponse(StatusCodes.Status503ServiceUnavailable);
        }

        var currentConnection = await sender.Send(
            new ResolveCurrentSyncConnectionQuery(session.UserId), cancellationToken);
        if (currentConnection.Status != CurrentSyncConnectionStatus.Found || currentConnection.ConnectionId is null)
        {
            return currentConnection.Status switch
            {
                CurrentSyncConnectionStatus.NotFound => ProblemResponse(StatusCodes.Status404NotFound),
                CurrentSyncConnectionStatus.Ambiguous => ProblemResponse(StatusCodes.Status409Conflict),
                _ => ProblemResponse(StatusCodes.Status400BadRequest)
            };
        }

        // O caso de uso devolve Accepted/Replayed somente depois da confirmação transacional da execução e obrigação.
        var result = await sender.Send(
            new AcceptSyncRunCommand(session.UserId, currentConnection.ConnectionId.Value, idempotencyKey),
            cancellationToken);
        if (result.IsAccepted && result.SyncRunId is Guid syncRunId)
        {
            return Accepted(new SyncRunAcceptanceResponse(syncRunId));
        }

        return result.Status switch
        {
            SyncRunAcceptanceStatus.ConnectionUnavailable => ProblemResponse(StatusCodes.Status404NotFound),
            SyncRunAcceptanceStatus.IdempotencyConflict => ProblemResponse(StatusCodes.Status409Conflict),
            SyncRunAcceptanceStatus.ActiveExecutionConflict when result.SyncRunId is Guid activeRunId =>
                ProblemResponse(StatusCodes.Status409Conflict, "SYNC_RUN_ALREADY_ACTIVE",
                    "Já existe uma sincronização ativa para esta conexão.", activeRunId),
            SyncRunAcceptanceStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
            _ => ProblemResponse(StatusCodes.Status500InternalServerError)
        };
    }

    // Erros do endpoint usam somente códigos/títulos fixos e não refletem chave, titular ou estado externo.
    private ObjectResult ProblemResponse(int status, string? code = null, string? title = null, Guid? syncRunId = null)
    {
        var problem = ApiProblemDetails.Create(HttpContext, status, code, title);
        if (syncRunId is Guid activeRunId)
        {
            problem.Extensions["syncRunId"] = activeRunId;
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}

// O corpo de aceitação publica somente o identificador persistido que poderá ser consultado pela rota de status.
public sealed record SyncRunAcceptanceResponse(Guid SyncRunId);
