using MediatR;

namespace Finora.Application.Pluggy;

/// <summary>Solicita análise local sem carregar credenciais ou argumentos financeiros.</summary>
public sealed record PluggyAnalysisQuery : IRequest<PluggyAnalysisResult>;

/// <summary>Encaminha a análise ao contrato de leitura implementado pela Infrastructure.</summary>
public sealed class PluggyAnalysisHandler(IPluggyAnalysisReader reader)
    : IRequestHandler<PluggyAnalysisQuery, PluggyAnalysisResult>
{
    /// <summary>Preserva cancelamento e devolve apenas métricas agregadas e sanitizadas.</summary>
    public Task<PluggyAnalysisResult> Handle(
        PluggyAnalysisQuery request,
        CancellationToken cancellationToken) => reader.AnalyzeAsync(cancellationToken);
}
