namespace Finora.Application.Pluggy;

/// <summary>Executa a leitura delimitada da Pluggy e retorna somente um resumo sanitizado.</summary>
public interface IPluggyLocalReader
{
    /// <summary>Consulta uma página do cartão selecionado sem persistir dados financeiros.</summary>
    Task<PluggyLocalReadResult> ReadAsync(CancellationToken cancellationToken);
}
