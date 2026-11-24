using System.ComponentModel;
using System.Text.Json;
using Finora.Api.Authentication;
using Finora.Api.Errors;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// Consulta somente movimentos persistidos do titular autenticado, sem acionar sincronização ou provedor.
[ApiController]
[Authorize]
[Route("api/transactions")]
public sealed class TransactionsController(ISender sender, TimeProvider clock) : ControllerBase
{
    // Todos os parâmetros permanecem textuais para que o caso de uso aplique validação uniforme e sanitizada.
    [HttpGet]
    [EndpointSummary("Consulta movimentos persistidos do titular autenticado")]
    [EndpointDescription("Combina filtros próprios; ordena TransactionDate decrescente, datas nulas por último e ID interno decrescente. O intervalo usa somente a data técnica, com início inclusivo e fim exclusivo. Retorna itens, filteredCount não monetário e coverage separado, sem agregar valores ou iniciar sincronização. Filtros inválidos: 400; conexão/cartão ausente ou inacessível: 404; conexão ambígua: 409; limite vigente: 429; persistência tipadamente indisponível: 503; falha inesperada: 500.")]
    [ProducesResponseType<TransactionPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(
        [Description("ID interno de cartão próprio. Cartão inexistente ou de outro titular resulta no mesmo 404.")]
        [FromQuery(Name = "cardId")] string? cardId,
        [Description("ID interno de categoria vinculada a anotação própria; categoria ausente ou alheia não revela sua existência e não encontra movimentos.")]
        [FromQuery(Name = "categoryId")] string? categoryId,
        [Description("Situação interna: Effective, Pending, FutureInstallment ou Unknown; não representa natureza financeira.")]
        [FromQuery(Name = "situation")] string? situation,
        [Description("Data yyyy-MM-dd inclusiva de TransactionDate; pode ser usada sem toExclusive. Movimentos sem data técnica não correspondem ao intervalo.")]
        [FromQuery(Name = "from")] string? from,
        [Description("Data yyyy-MM-dd exclusiva de TransactionDate; pode ser usada sem from. Não representa competência ou ciclo de fatura.")]
        [FromQuery(Name = "toExclusive")] string? toExclusive,
        [Description("Substring case-insensitive literal da descrição; %, _ e barra invertida não são curingas; máximo de 200 caracteres.")]
        [FromQuery(Name = "search")] string? search,
        [Description("Número positivo da página; padrão 1. Valor inválido resulta em 400.")]
        [FromQuery(Name = "page")] string? page,
        [Description("Tamanho de página de 1 a 100; padrão 25. Valor inválido resulta em 400.")]
        [FromQuery(Name = "pageSize")] string? pageSize,
        CancellationToken cancellationToken)
    {
        // A identidade confiável vem da sessão validada, nunca de um parâmetro fornecido pelo cliente.
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return ProblemResponse(StatusCodes.Status401Unauthorized);
        }

        // O handler valida os filtros, resolve a conexão própria e devolve somente a página já isolada.
        var result = await sender.Send(
            new GetTransactionsQuery(
                session.UserId,
                cardId,
                categoryId,
                situation,
                from,
                toExclusive,
                search,
                page,
                pageSize),
            cancellationToken);
        if (result.Status != TransactionsQueryStatus.Ready || result.Page is null)
        {
            return result.Status switch
            {
                TransactionsQueryStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
                TransactionsQueryStatus.ConnectionNotFound or TransactionsQueryStatus.CardNotFound =>
                    ProblemResponse(StatusCodes.Status404NotFound),
                TransactionsQueryStatus.ConnectionAmbiguous => ProblemResponse(StatusCodes.Status409Conflict),
                _ => ProblemResponse(StatusCodes.Status500InternalServerError)
            };
        }

        // O mapeamento explícito converte enums em códigos string estáveis e impede vazamento de tipos de domínio.
        var response = new TransactionPageResponse(
            result.Page.Items.Select(MapMovement).ToArray(),
            result.Page.Page,
            result.Page.PageSize,
            result.Page.FilteredCount,
            MapCoverage(result.Page.Coverage));
        return Ok(response);
    }

    // A rota aceita somente os campos pessoais da anotação e deriva o titular da sessão validada.
    [HttpPatch("{id}/annotation")]
    [EndpointSummary("Edita categoria e observação de um movimento próprio")]
    [EndpointDescription("Requer Bearer e sessão ativa. PATCH estrito aceita somente categoryId e/ou note: campos omitidos são preservados, null limpa o campo e ao menos um deve estar presente. A resposta contém somente categoryId e note. Entrada inválida: 400; movimento/categoria ausente ou alheio: 404 indistinguível; sessão inválida: 401; limite protegido: 429; persistência indisponível: 503; falha inesperada: 500.")]
    [ProducesResponseType<TransactionAnnotationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PatchAnnotation([FromRoute] string id, CancellationToken cancellationToken)
    {
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return ProblemResponse(StatusCodes.Status401Unauthorized);
        }

        // IDs malformados/vazios e corpos fora do contrato são recusados antes de despachar o comando.
        if (!Guid.TryParse(id, out var transactionId) || transactionId == Guid.Empty)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        var patch = await ReadAnnotationPatchAsync(Request, cancellationToken);
        if (patch is null)
        {
            return ProblemResponse(StatusCodes.Status400BadRequest);
        }

        // Presença e valor seguem separados até a Application; titular nunca vem do corpo da requisição.
        var result = await sender.Send(
            new UpdateTransactionAnnotationCommand(
                session.UserId,
                transactionId,
                patch.HasCategoryId,
                patch.CategoryId,
                patch.HasNote,
                patch.Note),
            cancellationToken);
        return result.Status switch
        {
            UpdateTransactionAnnotationStatus.Updated
                when result.CategoryId != Guid.Empty =>
                    Ok(new TransactionAnnotationResponse(result.CategoryId, result.Note)),
            UpdateTransactionAnnotationStatus.InvalidRequest => ProblemResponse(StatusCodes.Status400BadRequest),
            UpdateTransactionAnnotationStatus.TransactionNotFound or UpdateTransactionAnnotationStatus.CategoryNotFound =>
                ProblemResponse(StatusCodes.Status404NotFound),
            _ => ProblemResponse(StatusCodes.Status500InternalServerError)
        };
    }

    // JSON estrito distingue omissão de null, rejeita duplicatas e não materializa campos desconhecidos.
    private static async Task<TransactionAnnotationPatch?> ReadAnnotationPatchAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType()) { return null; }

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return null; }

            var hasCategoryId = false;
            Guid? categoryId = null;
            var hasNote = false;
            string? note = null;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "categoryId" when !hasCategoryId:
                        hasCategoryId = true;
                        if (property.Value.ValueKind == JsonValueKind.Null) { break; }
                        if (property.Value.ValueKind != JsonValueKind.String
                            || !Guid.TryParse(property.Value.GetString(), out var parsedCategoryId)
                            || parsedCategoryId == Guid.Empty)
                        {
                            return null;
                        }
                        categoryId = parsedCategoryId;
                        break;
                    case "note" when !hasNote:
                        hasNote = true;
                        if (property.Value.ValueKind == JsonValueKind.Null) { break; }
                        if (property.Value.ValueKind != JsonValueKind.String) { return null; }
                        note = property.Value.GetString();
                        if (note?.Length > 500) { return null; }
                        if (string.IsNullOrEmpty(note)) { note = null; }
                        break;
                    default:
                        return null;
                }
            }

            return hasCategoryId || hasNote
                ? new TransactionAnnotationPatch(hasCategoryId, categoryId, hasNote, note)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // O envelope público mantém códigos fixos e não reflete filtros, titular ou dados financeiros.
    private ObjectResult ProblemResponse(int status) => new(ApiProblemDetails.Create(HttpContext, status))
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" }
    };

    // Cada movimento mantém seus próprios dados permitidos; nenhum total ou campo da origem externa é projetado.
    private static TransactionMovementResponse MapMovement(TransactionMovementSummary movement) => new(
        movement.Id,
        movement.CardId,
        movement.Description,
        movement.Amount,
        movement.CurrencyCode,
        movement.OriginalDate,
        movement.TransactionDate,
        MapReason(movement.DateReason),
        MapNature(movement.Nature),
        MapReason(movement.NatureReason),
        MapSituation(movement.Situation),
        MapReason(movement.SituationReason),
        movement.InstallmentNumber,
        movement.TotalInstallments,
        movement.CategoryId,
        movement.Note);

    // A cobertura preserva sucesso, janela comprovada e execução recente em campos independentes.
    private static TransactionCoverageResponse MapCoverage(TransactionImportCoverageSummary coverage) => new(
        coverage.LastSuccessfulImportAtUtc,
        coverage.LastSuccessfulWindow is { } window
            ? new TransactionLastSuccessfulWindowResponse(
                window.RequestedFrom,
                window.RequestedToExclusive,
                window.ObservedFrom,
                window.ObservedTo,
                MapCoverageState(window.CoverageState))
            : null,
        coverage.LatestExecution is { } latest
            ? new TransactionLatestExecutionResponse(
                MapRunState(latest.State),
                MapCoverageState(latest.CoverageState),
                MapFailureCode(latest.FailureCode))
            : null);

    // As naturezas publicadas são uma allowlist textual independente do valor numérico do enum interno.
    private static string MapNature(TransactionNature nature) => nature switch
    {
        TransactionNature.Purchase => "Purchase",
        TransactionNature.Refund => "Refund",
        TransactionNature.BillPayment => "BillPayment",
        TransactionNature.Fee => "Fee",
        TransactionNature.Adjustment => "Adjustment",
        TransactionNature.Unknown => "Unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(nature))
    };

    // A situação permanece separada da natureza e também é publicada por uma allowlist de strings.
    private static string MapSituation(TransactionSituation situation) => situation switch
    {
        TransactionSituation.Effective => "Effective",
        TransactionSituation.Pending => "Pending",
        TransactionSituation.FutureInstallment => "FutureInstallment",
        TransactionSituation.Unknown => "Unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(situation))
    };

    // Estados operacionais são strings explícitas do contrato, nunca a representação numérica do enum de domínio.
    private static string MapRunState(SyncRunState state) => state switch
    {
        SyncRunState.Pending => "Pending",
        SyncRunState.Running => "Running",
        SyncRunState.Succeeded => "Succeeded",
        SyncRunState.Failed => "Failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    // Cobertura enumera somente estados permitidos e não implica integralidade bancária além da janela solicitada.
    private static string MapCoverageState(SyncCoverageState state) => state switch
    {
        SyncCoverageState.Unknown => "Unknown",
        SyncCoverageState.Partial => "Partial",
        SyncCoverageState.CompleteForRequestedWindow => "CompleteForRequestedWindow",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    // Defesa adicional na borda HTTP impede que códigos históricos inesperados sejam serializados.
    private static string? MapFailureCode(string? failureCode) =>
        failureCode is not null && SyncRunFailureCodes.IsAllowed(failureCode) ? failureCode : null;

    // Motivos internos têm códigos HTTP estáveis e nunca reutilizam descrição ou mensagem da origem.
    private static string? MapReason(TransactionExclusionReason? reason) => reason switch
    {
        null => null,
        TransactionExclusionReason.MissingOperationType => "MISSING_OPERATION_TYPE",
        TransactionExclusionReason.UnknownOperationType => "UNKNOWN_OPERATION_TYPE",
        TransactionExclusionReason.ContradictoryTypeOrSign => "CONTRADICTORY_TYPE_OR_SIGN",
        TransactionExclusionReason.MissingRequiredField => "MISSING_REQUIRED_FIELD",
        TransactionExclusionReason.UnsupportedCurrency => "UNSUPPORTED_CURRENCY",
        TransactionExclusionReason.NotPurchase => "NOT_PURCHASE",
        TransactionExclusionReason.NotEffective => "NOT_EFFECTIVE",
        TransactionExclusionReason.UnknownStatus => "UNKNOWN_STATUS",
        TransactionExclusionReason.InvalidInstallment => "INVALID_INSTALLMENT",
        TransactionExclusionReason.FutureInstallment => "FUTURE_INSTALLMENT",
        TransactionExclusionReason.InvalidDate => "INVALID_DATE",
        TransactionExclusionReason.MissingDateOffset => "MISSING_DATE_OFFSET",
        TransactionExclusionReason.TimeZoneUnavailable => "TIME_ZONE_UNAVAILABLE",
        TransactionExclusionReason.UnlinkedRefund => "UNLINKED_REFUND",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };
}

// A estrutura interna preserva a presença das propriedades sem expor tipos de parsing no contrato público.
internal sealed record TransactionAnnotationPatch(bool HasCategoryId, Guid? CategoryId, bool HasNote, string? Note);

// A resposta HTTP é uma allowlist deliberada sem dados financeiros, IDs externos ou titularidade.
public sealed record TransactionAnnotationResponse(Guid? CategoryId, string? Note);

// O DTO é uma allowlist independente do domínio e não inclui IDs externos, enums numéricos ou agregados monetários.
public sealed record TransactionMovementResponse(
    Guid Id,
    Guid CardId,
    string Description,
    decimal Amount,
    string CurrencyCode,
    string OriginalDate,
    DateOnly? TransactionDate,
    string? DateReason,
    string Nature,
    string? NatureReason,
    string Situation,
    string? SituationReason,
    int? InstallmentNumber,
    int? TotalInstallments,
    Guid? CategoryId,
    string? Note);

// A contagem descreve o conjunto filtrado, sem somar ou converter os valores individuais dos itens.
public sealed record TransactionPageResponse(
    IReadOnlyList<TransactionMovementResponse> Items,
    int Page,
    int PageSize,
    int FilteredCount,
    TransactionCoverageResponse Coverage);

// A cobertura separa o instante de sucesso confirmado das janelas e execuções que podem não ser correlatas.
public sealed record TransactionCoverageResponse(
    DateTimeOffset? LastSuccessfulImportAtUtc,
    TransactionLastSuccessfulWindowResponse? LastSuccessfulWindow,
    TransactionLatestExecutionResponse? LatestExecution);

// A janela só é preenchida quando a execução confirmada e o sucesso persistido da conexão correspondem.
public sealed record TransactionLastSuccessfulWindowResponse(
    DateOnly RequestedFrom,
    DateOnly? RequestedToExclusive,
    DateOnly? ObservedFrom,
    DateOnly? ObservedTo,
    string CoverageState);

// A execução mais recente contém apenas estado, cobertura e código de falha interno allowlisted.
public sealed record TransactionLatestExecutionResponse(
    string State,
    string CoverageState,
    string? FailureCode);
