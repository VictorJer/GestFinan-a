using MediatR;

namespace Finora.Application.Pluggy;

/// <summary>Solicita a leitura local explicitamente iniciada pelo titular, sem carregar segredos.</summary>
public sealed record PluggyLocalReadQuery : IRequest<PluggyLocalReadResult>;

/// <summary>Encaminha a consulta ao contrato Pluggy sem acoplar Application a HTTP ou console.</summary>
public sealed class PluggyLocalReadHandler(IPluggyLocalReader reader)
    : IRequestHandler<PluggyLocalReadQuery, PluggyLocalReadResult>
{
    /// <summary>Preserva o cancelamento do chamador e devolve o resultado sanitizado do leitor.</summary>
    public Task<PluggyLocalReadResult> Handle(
        PluggyLocalReadQuery request,
        CancellationToken cancellationToken) => reader.ReadAsync(cancellationToken);
}
