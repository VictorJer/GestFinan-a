using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Finora.Api.Authentication;
using Finora.Api.Errors;
using Finora.Application.Authentication;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// Expõe operações próprias de recorrência, sem misturar decisões com a consulta ou anotação dos movimentos.
[ApiController]
[Authorize]
[Route("api/transactions/recurrence-candidates")]
public sealed class TransactionRecurrenceCandidatesController(ISender sender, TimeProvider clock) : ControllerBase
{
    // A leitura lista candidatos atuais e históricos, sempre limitados ao titular da sessão validada.
    [HttpGet]
    [EndpointSummary("Consulta candidatos de recorrência do titular autenticado")]
    [EndpointDescription("Retorna somente IDs internos das ocorrências, versão da política, vigência e decisão explícita; não reavalia, não retorna fatos financeiros e não produz agregados.")]
    [ProducesResponseType<IReadOnlyList<TransactionRecurrenceCandidateResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var session = ReadSession();
        if (session is null)
        {
            return UnauthorizedResponse();
        }

        var result = await sender.Send(new GetTransactionRecurrenceCandidatesQuery(session.UserId), cancellationToken);
        return MapCandidates(result);
    }

    // A reavaliação é explícita e retorna a projeção persistida atualizada, sem efeitos colaterais em GET.
    [HttpPost("evaluate")]
    [EndpointSummary("Reavalia candidatos de recorrência do titular autenticado")]
    [EndpointDescription("Usa somente movimentos persistidos e versões de política suportadas; sincroniza candidatos idempotentemente e mantém as decisões do titular separadas.")]
    [ProducesResponseType<IReadOnlyList<TransactionRecurrenceCandidateResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Evaluate(CancellationToken cancellationToken)
    {
        var session = ReadSession();
        if (session is null)
        {
            return UnauthorizedResponse();
        }

        var result = await sender.Send(
            new EvaluateTransactionRecurrenceCandidatesCommand(session.UserId),
            cancellationToken);
        return MapCandidates(result);
    }

    // PUT substitui a escolha vigente somente por comando explícito e oculta candidatos de outros titulares.
    [HttpPut("{candidateId}/decision")]
    [EndpointSummary("Registra ou corrige uma decisão de recorrência")]
    [EndpointDescription("Aceita somente Subscription, Installment, IndependentPurchases ou Rejected. Titular deriva da sessão; candidato inexistente ou alheio retorna o mesmo 404.")]
    [ProducesResponseType<TransactionRecurrenceDecisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PutDecision(
        [Description("ID interno do candidato acessível ao titular; ausência e propriedade cruzada compartilham 404.")]
        [FromRoute] string candidateId,
        CancellationToken cancellationToken)
    {
        var session = ReadSession();
        if (session is null)
        {
            return UnauthorizedResponse();
        }

        // IDs inválidos não alcançam a Application nem são repetidos na resposta sanitizada.
        if (!Guid.TryParse(candidateId, out var parsedCandidateId) || parsedCandidateId == Guid.Empty)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        // O parser estrito aceita uma única propriedade textual e rejeita campos extras ou valores numéricos.
        var decision = await ReadDecisionAsync(Request, cancellationToken);
        if (decision is null)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        var result = await sender.Send(
            new UpdateTransactionRecurrenceDecisionCommand(session.UserId, parsedCandidateId, decision.Value),
            cancellationToken);
        return result.Status switch
        {
            TransactionRecurrenceOperationStatus.Ready
                when result.Decision is { } savedDecision && result.UpdatedAtUtc is { } updatedAtUtc =>
                    Ok(new TransactionRecurrenceDecisionResponse(
                        parsedCandidateId, MapDecision(savedDecision), updatedAtUtc)),
            TransactionRecurrenceOperationStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
            TransactionRecurrenceOperationStatus.CandidateNotFound => ProblemResponse(StatusCodes.Status404NotFound),
            _ => ProblemResponse(StatusCodes.Status500InternalServerError)
        };
    }

    // Claims inválidas recebem o mesmo challenge Bearer usado pelas operações financeiras existentes.
    private AuthenticationSessionReference? ReadSession() => JwtTokenValidation.ReadSessionClaims(User, clock);

    // A allowlist evita expor enums numéricos ou qualquer dado que não pertence à projeção aprovada.
    private IActionResult MapCandidates(TransactionRecurrenceCandidatesResult result) => result.Status switch
    {
        TransactionRecurrenceOperationStatus.Ready => Ok(result.Candidates.Select(MapCandidate).ToArray()),
        TransactionRecurrenceOperationStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
        _ => ProblemResponse(StatusCodes.Status500InternalServerError)
    };

    // O mapeamento não inclui descrição, valor, conta externa, titular nem metadados de origem.
    private static TransactionRecurrenceCandidateResponse MapCandidate(PersistedRecurrenceCandidate candidate) => new(
        candidate.Id,
        candidate.PolicyVersion,
        candidate.OccurrenceTransactionIds,
        candidate.IsCurrent,
        candidate.Decision is { } decision ? MapDecision(decision) : null,
        candidate.DecisionUpdatedAtUtc);

    // Decisões internas são convertidas por allowlist textual estável para o JSON público.
    private static string MapDecision(RecurrenceDecisionKind decision) => decision switch
    {
        RecurrenceDecisionKind.Subscription => "Subscription",
        RecurrenceDecisionKind.Installment => "Installment",
        RecurrenceDecisionKind.IndependentPurchases => "IndependentPurchases",
        RecurrenceDecisionKind.Rejected => "Rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(decision))
    };

    // Corpos JSON têm exatamente uma propriedade decision e não aceitam aliases, duplicatas ou extensão arbitrária.
    private static async Task<RecurrenceDecisionKind?> ReadDecisionAsync(
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

            var foundDecision = false;
            RecurrenceDecisionKind? decision = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (foundDecision || property.Name != "decision" || property.Value.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                foundDecision = true;
                decision = ParseDecision(property.Value.GetString());
                if (decision is null)
                {
                    return null;
                }
            }

            return foundDecision ? decision : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A API aceita somente os quatro nomes aprovados e não interpreta valores numéricos do enum.
    private static RecurrenceDecisionKind? ParseDecision(string? decision) => decision switch
    {
        "Subscription" => RecurrenceDecisionKind.Subscription,
        "Installment" => RecurrenceDecisionKind.Installment,
        "IndependentPurchases" => RecurrenceDecisionKind.IndependentPurchases,
        "Rejected" => RecurrenceDecisionKind.Rejected,
        _ => null
    };

    // Sessão não reconhecida retorna challenge fixo sem inserir claims ou dados da entrada no envelope.
    private IActionResult UnauthorizedResponse()
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return ProblemResponse(StatusCodes.Status401Unauthorized);
    }

    // Todos os erros seguem o contrato comum sem incluir IDs ou mensagens de domínio/persistência.
    private ObjectResult ProblemResponse(int status) => new(ApiProblemDetails.Create(HttpContext, status))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" }
    };
}

// DTO público contém somente referências internas e estado da decisão, permitindo junção com a lista existente.
public sealed record TransactionRecurrenceCandidateResponse(
    Guid CandidateId,
    string PolicyVersion,
    IReadOnlyList<Guid> OccurrenceTransactionIds,
    bool IsCurrent,
    string? Decision,
    DateTimeOffset? DecisionUpdatedAtUtc);

// A resposta de escrita não repete fatos financeiros nem a identidade do titular.
public sealed record TransactionRecurrenceDecisionResponse(
    Guid CandidateId,
    string Decision,
    DateTimeOffset DecisionUpdatedAtUtc);

// O DTO descreve o corpo permitido no OpenAPI sem substituir o parser HTTP estrito que rejeita duplicatas.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransactionRecurrenceDecisionRequest(
    [property: Description("Uma escolha explícita: Subscription, Installment, IndependentPurchases ou Rejected.")]
    string Decision);
