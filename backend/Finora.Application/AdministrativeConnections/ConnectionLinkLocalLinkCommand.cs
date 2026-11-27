using MediatR;

namespace Finora.Application.AdministrativeConnections;

/// <summary>Solicita a gravação atômica dos identificadores já validados e confirmados pelo operador.</summary>
public sealed class ConnectionLinkLocalLinkCommand : IRequest<ConnectionLinkLocalLinkResult>
{
    // Os valores vêm somente do resultado interno da pré-consulta e nunca de argumentos ou prompts.
    public ConnectionLinkLocalLinkCommand(Guid userId, string externalItemId)
    {
        UserId = userId;
        ExternalItemId = externalItemId ?? throw new ArgumentNullException(nameof(externalItemId));
    }

    public Guid UserId { get; }

    public string ExternalItemId { get; }

    // O request pode aparecer em logs de diagnóstico sem divulgar titular ou identidade externa.
    public override string ToString() =>
        $"{nameof(ConnectionLinkLocalLinkCommand)} {{ UserId = [redacted], ExternalItemId = [redacted] }}";
}

/// <summary>Resultado enumerado da transação de primeira vinculação, sem conteúdo vindo do banco.</summary>
public sealed record ConnectionLinkLocalLinkResult(ConnectionLinkLocalLinkState State)
{
    public override string ToString() =>
        $"{nameof(ConnectionLinkLocalLinkResult)} {{ State = {State} }}";
}

/// <summary>Estados seguros para a CLI distinguir vínculo criado de recusa ou falha operacional.</summary>
public enum ConnectionLinkLocalLinkState
{
    Linked,
    AlreadyLinked,
    Conflict,
    InvalidRequest,
    OperationalFailure
}
