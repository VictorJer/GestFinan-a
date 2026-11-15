namespace Finora.Application.Pluggy;

/// <summary>Executa a análise local da cobertura Pluggy e devolve somente um resumo sanitizado.</summary>
public interface IPluggyAnalysisReader
{
    Task<PluggyAnalysisResult> AnalyzeAsync(CancellationToken cancellationToken);
}
