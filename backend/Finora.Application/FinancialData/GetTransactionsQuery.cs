using System.Globalization;
using Finora.Domain.Transactions;
using MediatR;

namespace Finora.Application.FinancialData;

// Parâmetros continuam como texto até a validação para produzir o mesmo 400 sanitizado em entrada malformada.
public sealed record GetTransactionsQuery(
    Guid UserId,
    string? CardId = null,
    string? CategoryId = null,
    string? Situation = null,
    string? From = null,
    string? ToExclusive = null,
    string? Search = null,
    string? Page = null,
    string? PageSize = null) : IRequest<GetTransactionsQueryResult>;

// A query validada carrega o titular e a conexão resolvidos para reutilizar os mesmos limites no reader de movimentos.
public sealed record ValidatedTransactionsQuery(
    Guid UserId,
    Guid ConnectionId,
    Guid? CardId,
    Guid? CategoryId,
    TransactionSituation? Situation,
    DateOnly? From,
    DateOnly? ToExclusive,
    string? Search,
    int Page,
    int PageSize);

// Os estados mantêm filtros inválidos, conexão ambígua e cartão inacessível separados para o Controller mapear.
public enum TransactionsQueryStatus
{
    Ready,
    InvalidRequest,
    ConnectionNotFound,
    ConnectionAmbiguous,
    CardNotFound
}

// Somente Ready contém critérios validados; CardNotFound não diferencia ausência de cartão alheio.
public sealed record GetTransactionsQueryResult(
    TransactionsQueryStatus Status,
    ValidatedTransactionsQuery? Query = null,
    PagedTransactionMovementSummary? Page = null);

// O adapter confirma que o ID selecionado pertence ao titular e à conexão já resolvida.
public interface ITransactionCardOwnershipReader
{
    Task<bool> IsOwnedAsync(
        Guid userId,
        Guid connectionId,
        Guid cardId,
        CancellationToken cancellationToken);
}

// Valida forma, limites e intervalos sem depender de acesso ao banco ou de qualquer interpretação financeira.
public static class TransactionQueryFilterValidator
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;
    public const int MaximumSearchLength = 200;

    public static bool TryValidate(
        GetTransactionsQuery request,
        out ValidatedTransactionQueryFilters? filters)
    {
        filters = null;
        if (request.UserId == Guid.Empty
            || !TryParseOptionalGuid(request.CardId, out var cardId)
            || !TryParseOptionalGuid(request.CategoryId, out var categoryId)
            || !TryParseSituation(request.Situation, out var situation)
            || !TryParseDate(request.From, out var from)
            || !TryParseDate(request.ToExclusive, out var toExclusive)
            || !TryParsePage(request.Page, DefaultPage, out var page)
            || !TryParsePage(request.PageSize, DefaultPageSize, out var pageSize)
            || pageSize > MaximumPageSize
            || (from.HasValue && toExclusive.HasValue && toExclusive.Value <= from.Value)
            || (request.Search?.Length ?? 0) > MaximumSearchLength)
        {
            return false;
        }

        // Busca vazia não adiciona filtro; texto não vazio, inclusive curingas, permanece literal.
        var search = string.IsNullOrEmpty(request.Search) ? null : request.Search;
        filters = new ValidatedTransactionQueryFilters(
            request.UserId,
            cardId,
            categoryId,
            situation,
            from,
            toExclusive,
            search,
            page,
            pageSize);
        return true;
    }

    // Somente parâmetros omitidos são opcionais; texto inválido não se transforma silenciosamente em ausência.
    private static bool TryParseOptionalGuid(string? value, out Guid? parsed)
    {
        parsed = null;
        if (value is null) { return true; }
        if (!Guid.TryParse(value, out var id)) { return false; }
        parsed = id;
        return true;
    }

    // A consulta usa exclusivamente o formato ISO de calendário aprovado, sem horário ou cultura da máquina.
    private static bool TryParseDate(string? value, out DateOnly? parsed)
    {
        parsed = null;
        if (value is null) { return true; }
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return false;
        }

        parsed = date;
        return true;
    }

    // Enum aceita apenas nomes conhecidos, nunca inteiros numéricos que poderiam mapear para valores não definidos.
    private static bool TryParseSituation(string? value, out TransactionSituation? parsed)
    {
        parsed = null;
        if (value is null) { return true; }
        if (!Enum.GetNames<TransactionSituation>().Contains(value, StringComparer.OrdinalIgnoreCase)
            || !Enum.TryParse(value, ignoreCase: true, out TransactionSituation situation)
            || !Enum.IsDefined(situation))
        {
            return false;
        }

        parsed = situation;
        return true;
    }

    // Números são lidos sem cultura e devem permanecer no intervalo contratado para cada parâmetro.
    private static bool TryParsePage(string? value, int defaultValue, out int parsed)
    {
        if (value is null)
        {
            parsed = defaultValue;
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
            && parsed > 0;
    }
}

// O parser retorna critérios sem conexão, que só é anexada depois da resolução de titularidade no banco.
public sealed record ValidatedTransactionQueryFilters(
    Guid UserId,
    Guid? CardId,
    Guid? CategoryId,
    TransactionSituation? Situation,
    DateOnly? From,
    DateOnly? ToExclusive,
    string? Search,
    int Page,
    int PageSize);

// A query resolve conexão própria e, quando pedido, confirma posse do cartão antes de liberar os filtros ao reader.
public sealed class GetTransactionsQueryHandler(
    ICurrentSyncConnectionReader connectionReader,
    ITransactionCardOwnershipReader cardOwnershipReader,
    ITransactionMovementReader movementReader)
    : IRequestHandler<GetTransactionsQuery, GetTransactionsQueryResult>
{
    public async Task<GetTransactionsQueryResult> Handle(
        GetTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!TransactionQueryFilterValidator.TryValidate(request, out var filters) || filters is null)
        {
            return new GetTransactionsQueryResult(TransactionsQueryStatus.InvalidRequest);
        }

        // Ausência e ambiguidade da conexão são resolvidas antes de qualquer consulta a movimentos.
        var connection = await connectionReader.ResolveAsync(request.UserId, cancellationToken);
        if (connection.Status != CurrentSyncConnectionStatus.Found || connection.ConnectionId is null)
        {
            return connection.Status switch
            {
                CurrentSyncConnectionStatus.NotFound => new GetTransactionsQueryResult(TransactionsQueryStatus.ConnectionNotFound),
                CurrentSyncConnectionStatus.Ambiguous => new GetTransactionsQueryResult(TransactionsQueryStatus.ConnectionAmbiguous),
                _ => new GetTransactionsQueryResult(TransactionsQueryStatus.InvalidRequest)
            };
        }

        // O filtro de cartão converte ID alheio e inexistente no mesmo estado antes de qualquer paginação.
        if (filters.CardId is Guid cardId
            && !await cardOwnershipReader.IsOwnedAsync(
                request.UserId,
                connection.ConnectionId.Value,
                cardId,
                cancellationToken))
        {
            return new GetTransactionsQueryResult(TransactionsQueryStatus.CardNotFound);
        }

        var validatedQuery = new ValidatedTransactionsQuery(
            filters.UserId,
            connection.ConnectionId.Value,
            filters.CardId,
            filters.CategoryId,
            filters.Situation,
            filters.From,
            filters.ToExclusive,
            filters.Search,
            filters.Page,
            filters.PageSize);

        // Só critérios validados e vinculados à conexão própria chegam ao reader da página persistida.
        var page = await movementReader.ReadAsync(validatedQuery, cancellationToken);
        return new GetTransactionsQueryResult(TransactionsQueryStatus.Ready, validatedQuery, page);
    }
}
