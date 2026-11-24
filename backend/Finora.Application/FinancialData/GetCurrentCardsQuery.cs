using MediatR;

namespace Finora.Application.FinancialData;

// O resultado público da consulta contém somente campos necessários à identificação visual do cartão.
public sealed record CurrentCardSummary(
    Guid Id,
    string? Name,
    string? LastFour,
    string CurrencyCode);

// A cardinalidade da conexão própria é explícita para impedir escolha arbitrária quando há ambiguidade.
public enum CurrentCardsQueryStatus
{
    Found,
    ConnectionNotFound,
    ConnectionAmbiguous,
    InvalidRequest
}

// A lista é preenchida somente quando a conexão própria foi resolvida; ausência de cartões é lista vazia.
public sealed record CurrentCardsQueryResult(
    CurrentCardsQueryStatus Status,
    IReadOnlyList<CurrentCardSummary>? Cards = null);

// A identidade confiável vem da sessão; a consulta não aceita um UserId selecionável pelo cliente.
public sealed record GetCurrentCardsQuery(Guid UserId) : IRequest<CurrentCardsQueryResult>;

// O reader mantém EF Core e os predicados relacionais fora da camada de casos de uso.
public interface ICurrentCardsReader
{
    Task<CurrentCardsQueryResult> ReadAsync(Guid userId, CancellationToken cancellationToken);
}

// A Application rejeita identidade vazia antes de delegar a leitura à persistência.
public sealed class GetCurrentCardsHandler(ICurrentCardsReader reader)
    : IRequestHandler<GetCurrentCardsQuery, CurrentCardsQueryResult>
{
    public Task<CurrentCardsQueryResult> Handle(
        GetCurrentCardsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return Task.FromResult(new CurrentCardsQueryResult(CurrentCardsQueryStatus.InvalidRequest));
        }

        return reader.ReadAsync(request.UserId, cancellationToken);
    }
}
