using MediatR;

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
    : IRequestHandler<ImportTransactionsCommand, TransactionImportResult>
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
}
