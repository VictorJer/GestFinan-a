using Finora.Domain.Transactions;
using Finora.Domain.FinancialData;

namespace Finora.Application.FinancialData;

// Contrato de leitura allowlist: preserva origem e interpretações sem carregar IDs ou payloads externos.
public sealed record TransactionMovementSummary(
    Guid Id,
    Guid CardId,
    string Description,
    decimal Amount,
    string CurrencyCode,
    string OriginalDate,
    DateOnly? TransactionDate,
    TransactionExclusionReason? DateReason,
    TransactionNature Nature,
    TransactionExclusionReason? NatureReason,
    TransactionSituation Situation,
    TransactionExclusionReason? SituationReason,
    int? InstallmentNumber,
    int? TotalInstallments,
    Guid? CategoryId,
    string? Note);

// Uma resposta de página mantém a contagem do filtro separada dos itens materializados.
public sealed record PagedTransactionMovementSummary(
    int Page,
    int PageSize,
    int FilteredCount,
    IReadOnlyList<TransactionMovementSummary> Items,
    TransactionImportCoverageSummary Coverage);

// A janela só existe quando uma SyncRun de sucesso corresponde exatamente ao último sucesso da conexão.
public sealed record TransactionLastSuccessfulWindowSummary(
    DateOnly RequestedFrom,
    DateOnly? RequestedToExclusive,
    DateOnly? ObservedFrom,
    DateOnly? ObservedTo,
    SyncCoverageState CoverageState);

// A execução mais recente é projetada separadamente, sem IDs técnicos ou contadores operacionais adicionais.
public sealed record TransactionLatestExecutionSummary(
    SyncRunState State,
    SyncCoverageState CoverageState,
    string? FailureCode);

// Cobertura informa apenas fatos confirmados da conexão, sem inferir integralidade do histórico financeiro.
public sealed record TransactionImportCoverageSummary(
    DateTimeOffset? LastSuccessfulImportAtUtc,
    TransactionLastSuccessfulWindowSummary? LastSuccessfulWindow,
    TransactionLatestExecutionSummary? LatestExecution);

// O leitor recebe critérios validados e mantém titular/conexão como fronteira obrigatória da consulta.
public interface ITransactionMovementReader
{
    Task<PagedTransactionMovementSummary> ReadAsync(
        ValidatedTransactionsQuery query,
        CancellationToken cancellationToken);
}
