using Finora.Application.Pluggy;
using MediatR;

namespace Finora.Application.AdministrativeConnections;

/// <summary>Solicita a validação isolada do titular e do item antes de pedir confirmação de gravação.</summary>
public sealed class ConnectionLinkLocalPreflightQuery
    : IRequest<ConnectionLinkLocalPreflightResult>
{
    // O comando não aceita seletores: titular, item e cartão vêm apenas das fontes protegidas e verificadas.
    public override string ToString() => $"{nameof(ConnectionLinkLocalPreflightQuery)} {{ }}";
}

/// <summary>Classifica a pré-consulta sem transportar mensagens, payloads ou identificadores de falha externos.</summary>
public enum ConnectionLinkLocalPreflightState
{
    Ready,
    OwnerMissing,
    OwnerAmbiguous,
    InvalidConfiguration,
    AuthenticationRejected,
    ItemBlocked,
    CardSelectionInvalid,
    ExternalFailure
}

/// <summary>Resultado interno da checagem; identificadores opacos só existem para as etapas seguintes do comando.</summary>
public sealed record ConnectionLinkLocalPreflightResult(
    ConnectionLinkLocalPreflightState State,
    Guid? UserId = null,
    string? ExternalItemId = null,
    string? ExternalAccountId = null,
    PluggyLocalReadFailureCategory? FailureCategory = null)
{
    // A representação não serializa titular, item, conta ou qualquer conteúdo externo.
    public override string ToString() =>
        $"{nameof(ConnectionLinkLocalPreflightResult)} {{ State = {State}, FailureCategory = {FailureCategory} }}";
}
