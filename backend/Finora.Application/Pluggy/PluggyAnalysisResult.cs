namespace Finora.Application.Pluggy;

// O resultado contém apenas métricas agregadas e códigos internos, nunca transações ou identificadores.
public sealed record PluggyAnalysisResult(
    PluggyAnalysisOutcome Outcome,
    int PagesRead = 0,
    int TransactionsReceived = 0,
    int UniqueTransactions = 0,
    int DuplicateTransactions = 0,
    TimeSpan Duration = default,
    bool PaginationCompleted = false,
    int FutureTransactions = 0,
    IReadOnlyDictionary<string, int>? AvailableFieldCounts = null,
    PluggyAnalysisFailure? Failure = null,
    DateOnly? RequestedDateFrom = null,
    int DistinctObservedMonths = 0,
    IReadOnlyDictionary<string, int>? ObservedContractCounts = null,
    IReadOnlyDictionary<string, int>? UniqueObservedContractCounts = null);

// Resultados distinguem amostra vazia, coleta completa e bloqueios operacionais.
public enum PluggyAnalysisOutcome
{
    CompletedWithTransactions,
    EmptySample,
    InvalidConfiguration,
    ConnectionBlocked,
    Incomplete,
    Failed
}

// Categorias fixas evitam que mensagens/corpos externos contaminem logs e saída.
public enum PluggyAnalysisFailure
{
    InvalidResponse,
    UnsafeContinuation,
    RepeatedContinuation,
    ContradictoryDuplicate,
    PageLimit,
    RequestTimeout,
    TotalTimeout,
    UnsupportedTimeZone,
    RequestFailure,
    Cancelled
}
