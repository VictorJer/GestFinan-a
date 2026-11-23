using MediatR;
using Finora.Application.FinancialData;

namespace Finora.Application.Pluggy;

/// <summary>Inicia uma coleta interna para a conexão do titular confiável, sem aceitar IDs externos do cliente.</summary>
public sealed record ImportTransactionsCommand(Guid UserId, Guid ConnectionId)
    : IRequest<TransactionImportResult>;

/// <summary>Resolve propriedade antes de qualquer chamada externa e converte o snapshot em resultado sanitizado.</summary>
public sealed class ImportTransactionsHandler(
    ITransactionImportConnectionReader connections,
    ITransactionImportReader reader,
    ITransactionImportPromoter promoter,
    TimeProvider timeProvider)
    : IRequestHandler<ImportTransactionsCommand, TransactionImportResult>, ISyncRunAttemptExecutor
{
    public async Task<TransactionImportResult> Handle(
        ImportTransactionsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || request.ConnectionId == Guid.Empty)
        {
            return new TransactionImportResult(false, TransactionImportFailureCode.ConnectionUnavailable);
        }

        // A janela é fixada uma única vez para que paginação e resultado usem a mesma referência UTC.
        var startedAtUtc = timeProvider.GetUtcNow();
        var referenceDate = DateOnly.FromDateTime(startedAtUtc.UtcDateTime);
        var dateFrom = referenceDate.AddMonths(-12);
        var connection = await connections.FindOwnedAsync(
            request.ConnectionId, request.UserId, cancellationToken);
        if (connection is null || connection.Id != request.ConnectionId || connection.UserId != request.UserId)
        {
            return new TransactionImportResult(false, TransactionImportFailureCode.ConnectionUnavailable);
        }

        if (!string.Equals(connection.Provider, "pluggy", StringComparison.Ordinal))
        {
            return new TransactionImportResult(false, TransactionImportFailureCode.ProviderUnavailable);
        }

        var read = await reader.ReadAsync(connection.ExternalItemId, dateFrom, cancellationToken);
        if (!read.Succeeded || read.Snapshot is null)
        {
            var failureCode = read.FailureCode ?? TransactionImportFailureCode.InvalidResponse;
            // A falha terminal é registrada separadamente e nunca recebe um token já cancelado.
            await promoter.RecordFailureAsync(
                connection,
                dateFrom,
                startedAtUtc,
                timeProvider.GetUtcNow(),
                failureCode,
                read.PagesRead,
                read.ReceivedCount,
                CancellationToken.None);
            return new TransactionImportResult(false, failureCode);
        }

        var finishedAtUtc = timeProvider.GetUtcNow();
        var promotion = await promoter.PromoteAsync(
            connection,
            read.Snapshot,
            referenceDate,
            startedAtUtc,
            finishedAtUtc,
            cancellationToken);
        if (!promotion.Succeeded)
        {
            return new TransactionImportResult(false, promotion.FailureCode ?? TransactionImportFailureCode.PersistenceFailed);
        }

        // Só declara sucesso depois da confirmação conjunta de movimentos, conexão e SyncRun.
        return new TransactionImportResult(
            true,
            null,
            read.Snapshot.Accounts.Count,
            read.Snapshot.Transactions.Count,
            promotion.PersistedCount,
            read.Snapshot.RequestedDateFrom,
            read.Snapshot.PaginationCompleted);
    }

    public async Task<TransactionImportResult> ExecuteAttemptAsync(
        SyncRunLeaseGrant execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (execution.SyncRunId == Guid.Empty || execution.UserId == Guid.Empty
            || execution.ConnectionId == Guid.Empty || execution.AttemptNumber < 1
            || execution.FencingToken < 1
            || execution.RequestedToExclusive is not { } requestedToExclusive
            || requestedToExclusive <= execution.RequestedFrom
            || execution.AttemptDeadlineAtUtc <= execution.AttemptStartedAtUtc)
        {
            return new TransactionImportResult(false, TransactionImportFailureCode.InvalidResponse);
        }

        // O importador recebe a janela persistida e o início da posse, sem recalcular datas pelo relógio atual.
        var connection = await connections.FindOwnedAsync(
            execution.ConnectionId, execution.UserId, cancellationToken);
        if (connection is null || connection.Id != execution.ConnectionId || connection.UserId != execution.UserId)
        {
            return new TransactionImportResult(false, TransactionImportFailureCode.ConnectionUnavailable);
        }

        if (!string.Equals(connection.Provider, "pluggy", StringComparison.Ordinal))
        {
            return new TransactionImportResult(false, TransactionImportFailureCode.ProviderUnavailable);
        }

        var read = await reader.ReadAsync(connection.ExternalItemId, execution.RequestedFrom, cancellationToken);
        // Shutdown prevalece sobre o código Cancelled devolvido pelo adapter e nunca vira falha de negócio da execução.
        cancellationToken.ThrowIfCancellationRequested();
        if (!read.Succeeded || read.Snapshot is null)
        {
            var failureCode = read.FailureCode ?? TransactionImportFailureCode.InvalidResponse;
            return new TransactionImportResult(
                false,
                failureCode,
                PagesRead: read.PagesRead,
                ReceivedCount: read.ReceivedCount,
                RetryAfterAtUtc: read.RetryAfterAtUtc);
        }

        var finishedAtUtc = timeProvider.GetUtcNow();
        var normalizationReferenceDate = requestedToExclusive.AddDays(-1);
        var promotion = await promoter.PromoteAttemptAsync(
            execution,
            connection,
            read.Snapshot,
            normalizationReferenceDate,
            finishedAtUtc,
            cancellationToken);
        if (!promotion.Succeeded)
        {
            return new TransactionImportResult(
                false,
                promotion.FailureCode ?? TransactionImportFailureCode.PersistenceFailed,
                PagesRead: read.Snapshot.PagesRead,
                ReceivedCount: read.Snapshot.ReceivedCount ?? read.Snapshot.Transactions.Count);
        }

        // O resultado volta ao orquestrador sem expor snapshot, valores ou conteúdo externo.
        return new TransactionImportResult(
            true,
            null,
            read.Snapshot.Accounts.Count,
            read.Snapshot.Transactions.Count,
            promotion.PersistedCount,
            execution.RequestedFrom,
            read.Snapshot.PaginationCompleted,
            read.Snapshot.PagesRead,
            read.Snapshot.ReceivedCount ?? read.Snapshot.Transactions.Count);
    }
}
