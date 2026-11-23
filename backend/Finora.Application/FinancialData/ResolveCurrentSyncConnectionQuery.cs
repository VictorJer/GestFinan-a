using Finora.Domain.FinancialData;
using MediatR;

namespace Finora.Application.FinancialData;

// A rota current só pode continuar quando a identidade autenticada resolve para uma conexão inequívoca.
public enum CurrentSyncConnectionStatus
{
    Found,
    NotFound,
    Ambiguous,
    InvalidRequest
}

// O resultado não carrega dados externos da conexão, somente o identificador interno necessário à aceitação.
public sealed record CurrentSyncConnectionResult(CurrentSyncConnectionStatus Status, Guid? ConnectionId = null);

// A consulta recebe o titular confiável da sessão, nunca um seletor de usuário fornecido pelo cliente.
public sealed record ResolveCurrentSyncConnectionQuery(Guid UserId) : IRequest<CurrentSyncConnectionResult>;

// A consulta limitada ao dono e a decisão de cardinalidade permanecem atrás da fronteira de persistência.
public interface ICurrentSyncConnectionReader
{
    Task<CurrentSyncConnectionResult> ResolveAsync(Guid userId, CancellationToken cancellationToken);
}

// O handler mantém a camada HTTP sem acesso direto ao EF e recusa identidades vazias antes da consulta.
public sealed class ResolveCurrentSyncConnectionHandler(ICurrentSyncConnectionReader reader)
    : IRequestHandler<ResolveCurrentSyncConnectionQuery, CurrentSyncConnectionResult>
{
    public Task<CurrentSyncConnectionResult> Handle(
        ResolveCurrentSyncConnectionQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return Task.FromResult(new CurrentSyncConnectionResult(CurrentSyncConnectionStatus.InvalidRequest));
        }

        return reader.ResolveAsync(request.UserId, cancellationToken);
    }
}
