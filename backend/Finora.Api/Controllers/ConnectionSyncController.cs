using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Finora.Api.Authentication;
using Finora.Api.Errors;
using Finora.Application.FinancialData;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// As rotas convergem para o mesmo aceite persistente e não iniciam importação fora de uma intenção autenticada explícita.
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
    [EndpointDescription("Exige um único cabeçalho Idempotency-Key. Replay idêntico é resolvido antes de execução ativa e da política; nova intenção ativa recebe 409, intervalo mínimo recebe bloqueio temporário e 202 só confirma SyncRun/outbox após commit. Repetições internas da execução seguem a política FNR-013.")]
    [ProducesResponseType<SyncRunAcceptanceResponse>(202)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(429)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public Task<IActionResult> Accept(
        [FromHeader(Name = "Idempotency-Key"), Required] string? idempotencyKey,
        CancellationToken cancellationToken)
        => AcceptAsync(idempotencyKey, SyncRunRequestMode.Manual, cancellationToken);

    // A entrada autenticada reutiliza idempotência e armazenamento, mas pode responder que a importação não é necessária.
    [HttpPost("sync/on-entry")]
    [EndpointSummary("Avalia atualização automática na entrada autenticada")]
    [EndpointDescription("Exige um único Idempotency-Key e só aceita nova SyncRun quando a entrada indicar dados antigos, não houver execução ativa e o intervalo compartilhado permitir. Replay idêntico precede as políticas; 200 não_required não consulta a origem; 202 confirma somente o commit de SyncRun/outbox. AUTH_REQUIRED orienta reautorização sem renová-la.")]
    [ProducesResponseType<SyncRunEntryDecisionResponse>(200)]
    [ProducesResponseType<SyncRunAcceptanceResponse>(202)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(429)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public Task<IActionResult> AcceptOnEntry(
        [FromHeader(Name = "Idempotency-Key"), Required] string? idempotencyKey,
        CancellationToken cancellationToken)
        => AcceptAsync(idempotencyKey, SyncRunRequestMode.OnEntry, cancellationToken);

    // A leitura deriva estado persistido e não depende do broker nem produz nova intenção de sincronização.
    [HttpGet("sync-state")]
    [EndpointSummary("Consulta o estado sanitizado de sincronização da conexão atual")]
    [EndpointDescription("Lê sucesso Finora, execução conhecida, cobertura e prazo da política para a conexão única do titular. Não agenda sincronização, não consulta a origem e mantém SourceUpdatedAtUtc nulo quando não há informação confiável.")]
    [ProducesResponseType<ConnectionSyncStateResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> GetSyncState(CancellationToken cancellationToken)
    {
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return ProblemResponse(StatusCodes.Status401Unauthorized);
        }

        var result = await sender.Send(
            new GetCurrentConnectionSyncStateQuery(session.UserId),
            cancellationToken);
        if (result.Status != CurrentSyncConnectionStatus.Found || result.State is null)
        {
            return result.Status switch
            {
                CurrentSyncConnectionStatus.NotFound => ProblemResponse(StatusCodes.Status404NotFound),
                CurrentSyncConnectionStatus.Ambiguous => ProblemResponse(StatusCodes.Status409Conflict),
                CurrentSyncConnectionStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
                _ => ProblemResponse(StatusCodes.Status500InternalServerError)
            };
        }

        var state = result.State;
        var latestExecution = state.LatestExecution;
        return Ok(new ConnectionSyncStateResponse(
            state.UpdateNeeded,
            state.ReauthorizationRequired,
            state.LastSuccessfulImportAtUtc,
            state.SourceUpdatedAtUtc,
            state.LastAttemptAtUtc,
            state.NextAllowedAtUtc,
            latestExecution is null
                ? null
                : new ConnectionSyncExecutionResponse(
                    latestExecution.SyncRunId,
                    latestExecution.State.ToString(),
                    latestExecution.CreatedAtUtc,
                    latestExecution.LastAttemptAtUtc,
                    latestExecution.FinishedAtUtc,
                    latestExecution.CoverageState.ToString(),
                    latestExecution.FailureCode)));
    }

    // A validação de sessão/chave e a resolução de titularidade são idênticas nos dois modos de intenção.
    private async Task<IActionResult> AcceptAsync(
        string? idempotencyKey,
        SyncRunRequestMode mode,
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
            new AcceptSyncRunCommand(session.UserId, currentConnection.ConnectionId.Value, idempotencyKey, mode),
            cancellationToken);
        if (result.IsAccepted && result.SyncRunId is Guid syncRunId)
        {
            return Accepted(new SyncRunAcceptanceResponse(syncRunId));
        }

        return result.Status switch
        {
            SyncRunAcceptanceStatus.NotRequired => Ok(new SyncRunEntryDecisionResponse("not_required")),
            SyncRunAcceptanceStatus.ReauthorizationRequired => Ok(new SyncRunEntryDecisionResponse(
                "reauthorization_required",
                "Renove o acesso no provedor antes de solicitar outra sincronização manual.")),
            SyncRunAcceptanceStatus.ConnectionUnavailable => ProblemResponse(StatusCodes.Status404NotFound),
            SyncRunAcceptanceStatus.IdempotencyConflict => ProblemResponse(StatusCodes.Status409Conflict),
            SyncRunAcceptanceStatus.ActiveExecutionConflict when result.SyncRunId is Guid activeRunId =>
                ProblemResponse(StatusCodes.Status409Conflict, "SYNC_RUN_ALREADY_ACTIVE",
                    "Já existe uma sincronização ativa para esta conexão.", activeRunId),
            SyncRunAcceptanceStatus.MinimumIntervalActive when result.NextAllowedAtUtc is { } nextAllowedAtUtc =>
                MinimumIntervalProblem(nextAllowedAtUtc),
            SyncRunAcceptanceStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
            _ => ProblemResponse(StatusCodes.Status500InternalServerError)
        };
    }

    // O delta inteiro arredonda para cima para que Retry-After nunca antecipe o instante permitido pelo banco.
    private ObjectResult MinimumIntervalProblem(DateTimeOffset nextAllowedAtUtc)
    {
        var secondsRemaining = Math.Max(
            0L,
            (long)Math.Ceiling((nextAllowedAtUtc - clock.GetUtcNow().ToUniversalTime()).TotalSeconds));
        Response.Headers["Retry-After"] = secondsRemaining.ToString(CultureInfo.InvariantCulture);
        return ProblemResponse(
            StatusCodes.Status429TooManyRequests,
            "SYNC_RUN_MINIMUM_INTERVAL_ACTIVE",
            "A conexão já recebeu uma solicitação de atualização recentemente.",
            nextAllowedAtUtc: nextAllowedAtUtc);
    }

    // Erros do endpoint usam somente códigos/títulos fixos e não refletem chave, titular ou estado externo.
    private ObjectResult ProblemResponse(
        int status,
        string? code = null,
        string? title = null,
        Guid? syncRunId = null,
        DateTimeOffset? nextAllowedAtUtc = null)
    {
        var problem = ApiProblemDetails.Create(HttpContext, status, code, title);
        if (syncRunId is Guid activeRunId)
        {
            problem.Extensions["syncRunId"] = activeRunId;
        }

        if (nextAllowedAtUtc is { } nextAllowed)
        {
            problem.Extensions["nextAllowedAtUtc"] = nextAllowed;
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

// A decisão não carrega execução; a orientação de reautorização é fixa e não afirma alteração concluída no provedor.
public sealed record SyncRunEntryDecisionResponse(string Decision, string? Guidance = null);

// O estado público expõe somente timestamps próprios, decisões e resumo allowlist da última execução.
public sealed record ConnectionSyncStateResponse(
    bool UpdateNeeded,
    bool ReauthorizationRequired,
    DateTimeOffset? LastSuccessfulImportAtUtc,
    DateTimeOffset? SourceUpdatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? NextAllowedAtUtc,
    ConnectionSyncExecutionResponse? LatestExecution);

public sealed record ConnectionSyncExecutionResponse(
    Guid SyncRunId,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string CoverageState,
    string? FailureCode);
