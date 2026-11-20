using MediatR;

namespace Finora.Application.Pluggy;

/// <summary>Solicita reconexão por fluxo interno confiável; não há endpoint HTTP que aceite o item candidato.</summary>
public sealed record ReconnectTransactionConnectionCommand(
    Guid UserId,
    Guid ConnectionId,
    string ReplacementExternalItemId) : IRequest<TransactionImportResult>;

/// <summary>Coleta o novo item fora de transação e delega a prova e a troca atômica à Infrastructure.</summary>
public sealed class ReconnectTransactionConnectionHandler(
    ITransactionImportConnectionReader connections,
    ITransactionImportReader reader,
    ITransactionImportPromoter promoter,
    TimeProvider timeProvider)
    : IRequestHandler<ReconnectTransactionConnectionCommand, TransactionImportResult>
{
    public async Task<TransactionImportResult> Handle(
        ReconnectTransactionConnectionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty
            || request.ConnectionId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ReplacementExternalItemId))
        {
            return new TransactionImportResult(false, TransactionImportFailureCode.ConnectionUnavailable);
        }

        // A janela usa uma única referência UTC e sempre começa do item persistido depois da autorização interna.
        var startedAtUtc = timeProvider.GetUtcNow();
        var referenceDate = DateOnly.FromDateTime(startedAtUtc.UtcDateTime);
        var dateFrom = referenceDate.AddMonths(-12);
        var connection = await connections.FindOwnedAsync(
            request.ConnectionId, request.UserId, cancellationToken);
        if (connection is null
            || connection.Id != request.ConnectionId
            || connection.UserId != request.UserId
            || !string.Equals(connection.Provider, "pluggy", StringComparison.Ordinal)
            || string.Equals(connection.ExternalItemId, request.ReplacementExternalItemId, StringComparison.Ordinal))
        {
            return new TransactionImportResult(false, TransactionImportFailureCode.ConnectionUnavailable);
        }

        var read = await reader.ReadAsync(request.ReplacementExternalItemId, dateFrom, cancellationToken);
        if (!read.Succeeded || read.Snapshot is null)
        {
            var failureCode = read.FailureCode ?? TransactionImportFailureCode.InvalidResponse;
            await promoter.RecordFailureAsync(
                connection, dateFrom, startedAtUtc, timeProvider.GetUtcNow(), failureCode,
                read.PagesRead, read.ReceivedCount, CancellationToken.None);
            return new TransactionImportResult(false, failureCode);
        }

        var finishedAtUtc = timeProvider.GetUtcNow();
        var promotion = await promoter.PromoteReconnectionAsync(
            connection,
            request.ReplacementExternalItemId,
            read.Snapshot,
            referenceDate,
            startedAtUtc,
            finishedAtUtc,
            cancellationToken);
        if (!promotion.Succeeded)
        {
            return new TransactionImportResult(
                false, promotion.FailureCode ?? TransactionImportFailureCode.PersistenceFailed);
        }

        // Só informa sucesso após trocar o item, reconciliar o snapshot e confirmar o SyncRun em conjunto.
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
