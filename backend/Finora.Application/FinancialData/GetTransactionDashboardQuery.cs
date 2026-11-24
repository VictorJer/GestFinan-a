using Finora.Domain.Transactions;
using MediatR;

namespace Finora.Application.FinancialData;

// A consulta pública não expõe parâmetros de página e deriva o titular exclusivamente da sessão validada.
public sealed record GetTransactionDashboardQuery(
    Guid UserId,
    string? CardId = null,
    string? CategoryId = null,
    string? Situation = null,
    string? From = null,
    string? ToExclusive = null,
    string? Search = null) : IRequest<GetTransactionDashboardQueryResult>;

// Os filtros normalizados carregam somente o universo autorizado e não contêm paginação nem valor monetário.
public sealed record ValidatedTransactionDashboardQuery(
    Guid UserId,
    Guid ConnectionId,
    Guid? CardId,
    Guid? CategoryId,
    TransactionSituation? Situation,
    DateOnly? From,
    DateOnly? ToExclusive,
    string? Search);

// O status reutiliza a mesma matriz de validação/propriedade aplicada pela lista de movimentos.
public sealed record GetTransactionDashboardQueryResult(
    TransactionsQueryStatus Status,
    TransactionMovementDashboardSummary? Summary = null);

// A Application valida os mesmos filtros da FNR-015, resolve propriedade e só então pede a agregação.
public sealed class GetTransactionDashboardQueryHandler(
    ICurrentSyncConnectionReader connectionReader,
    ITransactionCardOwnershipReader cardOwnershipReader,
    ITransactionDashboardReader dashboardReader)
    : IRequestHandler<GetTransactionDashboardQuery, GetTransactionDashboardQueryResult>
{
    public async Task<GetTransactionDashboardQueryResult> Handle(
        GetTransactionDashboardQuery request,
        CancellationToken cancellationToken)
    {
        // A lista de filtros é adaptada ao validador existente sem aceitar page/pageSize do dashboard.
        var filterRequest = new GetTransactionsQuery(
            request.UserId,
            request.CardId,
            request.CategoryId,
            request.Situation,
            request.From,
            request.ToExclusive,
            request.Search);
        if (!TransactionQueryFilterValidator.TryValidate(filterRequest, out var filters) || filters is null)
        {
            return new GetTransactionDashboardQueryResult(TransactionsQueryStatus.InvalidRequest);
        }

        // A resolução compartilhada mantém conexão ausente/ambígua e cartão inacessível indistinguíveis da lista.
        var access = await TransactionQueryAccessResolver.ResolveAsync(
            filters,
            connectionReader,
            cardOwnershipReader,
            cancellationToken);
        if (access.Status != TransactionsQueryStatus.Ready || access.ConnectionId is not Guid connectionId)
        {
            return new GetTransactionDashboardQueryResult(access.Status);
        }

        // O reader recebe datas e busca já validadas e jamais recebe parâmetros de paginação externos.
        var validatedQuery = new ValidatedTransactionDashboardQuery(
            filters.UserId,
            connectionId,
            filters.CardId,
            filters.CategoryId,
            filters.Situation,
            filters.From,
            filters.ToExclusive,
            filters.Search);
        var summary = await dashboardReader.ReadAsync(validatedQuery, cancellationToken);
        return new GetTransactionDashboardQueryResult(TransactionsQueryStatus.Ready, summary);
    }
}
