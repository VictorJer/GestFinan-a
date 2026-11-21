using Finora.Application.Pluggy;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Pluggy;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

/// <summary>Persiste snapshot completo em transação única iniciada somente após o término do transporte externo.</summary>
public sealed class FinancialTransactionImportPromoter(FinoraDbContext dbContext)
    : ITransactionImportPromoter
{
    // Grava apenas metadados permitidos da falha; não atualiza conexão nem expõe mensagem externa.
    public async Task<TransactionImportPromotionResult> RecordFailureAsync(
        TransactionImportConnection requestedConnection,
        DateOnly requestedDateFrom,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        TransactionImportFailureCode failureCode,
        int? pagesRead,
        int? receivedCount,
        CancellationToken cancellationToken)
    {
        if (requestedConnection.Id == Guid.Empty || requestedConnection.UserId == Guid.Empty)
        {
            return new TransactionImportPromotionResult(false, TransactionImportFailureCode.ConnectionUnavailable);
        }

        var persistedFailureCode = MapFailureCode(failureCode);
        if (persistedFailureCode is null)
        {
            return new TransactionImportPromotionResult(false, TransactionImportFailureCode.InvalidResponse);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var connectionExists = await dbContext.Set<FinancialConnection>().AnyAsync(
                value => value.Id == requestedConnection.Id && value.UserId == requestedConnection.UserId,
                cancellationToken);
            if (!connectionExists)
            {
                return new TransactionImportPromotionResult(false, TransactionImportFailureCode.ConnectionUnavailable);
            }

            dbContext.Set<SyncRun>().Add(SyncRun.Create(
                Guid.NewGuid(),
                requestedConnection.UserId,
                requestedConnection.Id,
                startedAtUtc,
                requestedDateFrom,
                startedAtUtc: startedAtUtc,
                finishedAtUtc: finishedAtUtc,
                state: SyncRunState.Failed,
                pagesRead: pagesRead,
                receivedCount: receivedCount,
                coverageState: pagesRead is > 0 ? SyncCoverageState.Partial : SyncCoverageState.Unknown,
                failureCode: persistedFailureCode));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TransactionImportPromotionResult(true, null);
        }
        catch (DbUpdateException)
        {
            return new TransactionImportPromotionResult(false, TransactionImportFailureCode.PersistenceFailed);
        }
    }

    public Task<TransactionImportPromotionResult> PromoteAsync(
        TransactionImportConnection requestedConnection,
        TransactionImportSnapshot snapshot,
        DateOnly normalizationReferenceDate,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        CancellationToken cancellationToken) => PromoteCoreAsync(
            requestedConnection, replacementExternalItemId: null, snapshot, normalizationReferenceDate,
            startedAtUtc, finishedAtUtc, cancellationToken);

    public Task<TransactionImportPromotionResult> PromoteReconnectionAsync(
        TransactionImportConnection requestedConnection,
        string replacementExternalItemId,
        TransactionImportSnapshot snapshot,
        DateOnly normalizationReferenceDate,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        CancellationToken cancellationToken) => PromoteCoreAsync(
            requestedConnection, replacementExternalItemId, snapshot, normalizationReferenceDate,
            startedAtUtc, finishedAtUtc, cancellationToken);

    // Compartilha a reconciliação, variando apenas a prova e atualização atômica do item externo.
    private async Task<TransactionImportPromotionResult> PromoteCoreAsync(
        TransactionImportConnection requestedConnection,
        string? replacementExternalItemId,
        TransactionImportSnapshot snapshot,
        DateOnly normalizationReferenceDate,
        DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc,
        CancellationToken cancellationToken)
    {
        // Uma referência sem identidade ou uma coleta incompleta nunca chega à transação de promoção.
        if (!snapshot.PaginationCompleted
            || requestedConnection.Id == Guid.Empty
            || requestedConnection.UserId == Guid.Empty)
        {
            return new TransactionImportPromotionResult(false, TransactionImportFailureCode.InvalidResponse);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // O lock de linha serializa somente a fase curta de promoção, depois de todo o HTTP externo.
            var connection = await dbContext.Set<FinancialConnection>()
                .FromSqlInterpolated($"SELECT * FROM \"FinancialConnection\" WHERE \"Id\" = {requestedConnection.Id} AND \"UserId\" = {requestedConnection.UserId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (connection is null
                || !string.Equals(connection.Provider, requestedConnection.Provider, StringComparison.Ordinal)
                || !string.Equals(connection.ExternalItemId, requestedConnection.ExternalItemId, StringComparison.Ordinal))
            {
                return new TransactionImportPromotionResult(false, TransactionImportFailureCode.ConnectionUnavailable);
            }

            // Um snapshot iniciado antes do sucesso já confirmado não pode sobrescrever fatos mais novos.
            if (connection.LastSuccessfulImportAtUtc > startedAtUtc)
            {
                return new TransactionImportPromotionResult(false, TransactionImportFailureCode.StaleSnapshot);
            }

            if (replacementExternalItemId is not null)
            {
                // A prova compara identidades opacas de cartões; atributos visuais não entram no pareamento.
                var replacementIds = snapshot.Accounts
                    .Select(account => account.ExternalId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                var persistedIds = await dbContext.Set<CardAccount>()
                    .Where(card => card.ConnectionId == connection.Id && card.UserId == connection.UserId)
                    .Select(card => card.ExternalId)
                    .ToListAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(replacementExternalItemId)
                    || string.Equals(replacementExternalItemId, connection.ExternalItemId, StringComparison.Ordinal)
                    || replacementIds.Length == 0
                    || replacementIds.Length != snapshot.Accounts.Count
                    || !replacementIds.SequenceEqual(persistedIds.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
                {
                    return new TransactionImportPromotionResult(false, TransactionImportFailureCode.IdentityConflict);
                }

                var candidateExists = await dbContext.Set<FinancialConnectionItemIdentity>().AnyAsync(
                    identity => identity.Provider == connection.Provider
                        && identity.ExternalItemId == replacementExternalItemId,
                    cancellationToken);
                var currentIdentity = await dbContext.Set<FinancialConnectionItemIdentity>().SingleOrDefaultAsync(
                    identity => identity.Provider == connection.Provider
                        && identity.ExternalItemId == connection.ExternalItemId,
                    cancellationToken);
                if (candidateExists
                    || currentIdentity is null
                    || currentIdentity.ConnectionId != connection.Id
                    || currentIdentity.UserId != connection.UserId
                    || currentIdentity.RetiredAtUtc.HasValue)
                {
                    return new TransactionImportPromotionResult(false, TransactionImportFailureCode.IdentityConflict);
                }

                currentIdentity.Retire(finishedAtUtc);
                dbContext.Set<FinancialConnectionItemIdentity>().Add(FinancialConnectionItemIdentity.CreateActive(
                    connection.Id, connection.UserId, connection.Provider, replacementExternalItemId));
                connection.ReplaceExternalItemId(replacementExternalItemId);
            }
            else
            {
                // Importação comum só usa a identidade ativa registrada; registro ausente/aposentado falha fechado.
                var activeIdentityExists = await dbContext.Set<FinancialConnectionItemIdentity>().AnyAsync(
                    identity => identity.Provider == connection.Provider
                        && identity.ExternalItemId == connection.ExternalItemId
                        && identity.ConnectionId == connection.Id
                        && identity.UserId == connection.UserId
                        && identity.RetiredAtUtc == null,
                    cancellationToken);
                if (!activeIdentityExists)
                {
                    return new TransactionImportPromotionResult(false, TransactionImportFailureCode.IdentityConflict);
                }
            }

            // Resolve todas as chaves externas antes de criar movimentos; UUIDs existentes permanecem estáveis.
            var externalAccountIds = snapshot.Accounts.Select(account => account.ExternalId)
                .Distinct(StringComparer.Ordinal).ToArray();
            var cards = await dbContext.Set<CardAccount>()
                .Where(card => card.ConnectionId == connection.Id && card.UserId == connection.UserId
                    && externalAccountIds.Contains(card.ExternalId))
                .ToListAsync(cancellationToken);
            var cardsByExternalId = cards.ToDictionary(card => card.ExternalId, StringComparer.Ordinal);
            foreach (var sourceAccount in snapshot.Accounts)
            {
                if (cardsByExternalId.TryGetValue(sourceAccount.ExternalId, out var existingCard))
                {
                    existingCard.UpdateSourceCurrency(sourceAccount.CurrencyCode);
                }
                else
                {
                    var newCard = CardAccount.Create(
                        Guid.NewGuid(), connection.UserId, connection.Id, sourceAccount.ExternalId,
                        sourceAccount.CurrencyCode);
                    dbContext.Set<CardAccount>().Add(newCard);
                    cardsByExternalId.Add(sourceAccount.ExternalId, newCard);
                }
            }

            // Reconcile por cartão e ID externo; anotações permanecem em tabela separada e não são tocadas.
            foreach (var group in snapshot.Transactions.GroupBy(value => value.AccountId, StringComparer.Ordinal))
            {
                if (!cardsByExternalId.TryGetValue(group.Key, out var card))
                {
                    return new TransactionImportPromotionResult(false, TransactionImportFailureCode.InvalidResponse);
                }

                var externalIds = group.Select(value => value.ExternalId).Distinct(StringComparer.Ordinal).ToArray();
                var existingTransactions = await dbContext.Set<FinancialTransaction>()
                    .Where(value => value.CardAccountId == card.Id && value.UserId == connection.UserId
                        && externalIds.Contains(value.ExternalId))
                    .ToListAsync(cancellationToken);
                var transactionsByExternalId = existingTransactions.ToDictionary(
                    value => value.ExternalId, StringComparer.Ordinal);

                foreach (var source in group)
                {
                    var facts = PluggyTransactionMapper.ToFacts(source);
                    var normalization = TransactionNormalizer.Normalize(
                        facts, normalizationReferenceDate, positivePurchaseRuleConfirmed: false);
                    if (transactionsByExternalId.TryGetValue(source.ExternalId, out var existing))
                    {
                        existing.UpdateSourceFacts(normalization, "transaction-normalizer-v1", normalizationReferenceDate);
                    }
                    else
                    {
                        var created = FinancialTransaction.Create(
                            Guid.NewGuid(), connection.UserId, card.Id, normalization,
                            "transaction-normalizer-v1", normalizationReferenceDate);
                        dbContext.Set<FinancialTransaction>().Add(created);
                        transactionsByExternalId.Add(source.ExternalId, created);
                    }
                }
            }

            // Último sucesso e resultado terminal entram no mesmo commit dos cartões e movimentos reconciliados.
            connection.MarkImportSuccessful(finishedAtUtc);
            var observedDates = snapshot.Transactions
                .Select(source => TransactionDateNormalizer.Normalize(source.OriginalDate).TransactionDate)
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .ToArray();
            var syncRunId = Guid.NewGuid();
            dbContext.Set<SyncRun>().Add(SyncRun.Create(
                syncRunId,
                connection.UserId,
                connection.Id,
                startedAtUtc,
                snapshot.RequestedDateFrom,
                startedAtUtc: startedAtUtc,
                finishedAtUtc: finishedAtUtc,
                state: SyncRunState.Succeeded,
                pagesRead: snapshot.PagesRead,
                receivedCount: snapshot.ReceivedCount ?? snapshot.Transactions.Count,
                uniqueCount: snapshot.Transactions.Count,
                persistedCount: snapshot.Transactions.Count,
                observedFrom: observedDates.Length == 0 ? null : observedDates.Min(),
                observedTo: observedDates.Length == 0 ? null : observedDates.Max(),
                coverageState: SyncCoverageState.CompleteForRequestedWindow));

            await dbContext.SaveChangesAsync(cancellationToken);
            try
            {
                await transaction.CommitAsync(cancellationToken);
                return new TransactionImportPromotionResult(true, null, snapshot.Transactions.Count);
            }
            catch (Exception)
            {
                // Confirmação perdida exige leitura em contexto/conexão novos; ausência não é anunciada como rollback.
                await transaction.DisposeAsync();
                dbContext.ChangeTracker.Clear();
                var confirmed = await VerifyCommittedPromotionAsync(
                    syncRunId, connection.Id, finishedAtUtc, CancellationToken.None);
                return confirmed
                    ? new TransactionImportPromotionResult(true, null, snapshot.Transactions.Count)
                    : new TransactionImportPromotionResult(false, TransactionImportFailureCode.CommitUncertain);
            }
        }
        catch (DbUpdateException)
        {
            return new TransactionImportPromotionResult(false, TransactionImportFailureCode.PersistenceFailed);
        }
    }

    // Traduz enumerações internas para a allowlist persistente; texto livre nunca chega ao banco.
    private static string? MapFailureCode(TransactionImportFailureCode failureCode) => failureCode switch
    {
        TransactionImportFailureCode.AuthenticationRequired => SyncRunFailureCodes.AuthenticationRequired,
        TransactionImportFailureCode.RateLimited => SyncRunFailureCodes.RateLimited,
        TransactionImportFailureCode.InvalidResponse => SyncRunFailureCodes.InvalidResponse,
        TransactionImportFailureCode.ProviderTimeout => SyncRunFailureCodes.ProviderTimeout,
        TransactionImportFailureCode.Cancelled => SyncRunFailureCodes.Cancelled,
        TransactionImportFailureCode.PersistenceFailed => SyncRunFailureCodes.PersistenceFailed,
        TransactionImportFailureCode.ProviderUnavailable => SyncRunFailureCodes.ProviderUnavailable,
        TransactionImportFailureCode.CollectionLimitExceeded => SyncRunFailureCodes.InvalidResponse,
        _ => null
    };

    // Uma conexão independente verifica o SyncRun e o timestamp que deveriam ter sido confirmados juntos.
    private async Task<bool> VerifyCommittedPromotionAsync(
        Guid syncRunId,
        Guid connectionId,
        DateTimeOffset expectedLastSuccessfulAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = dbContext.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return false;
            }

            var options = new DbContextOptionsBuilder<FinoraDbContext>();
            FinoraDbContextConfiguration.Configure(options, connectionString);
            await using var verification = new FinoraDbContext(options.Options);
            var runExists = await verification.Set<SyncRun>().AsNoTracking()
                .AnyAsync(run => run.Id == syncRunId && run.State == SyncRunState.Succeeded, cancellationToken);
            var lastSuccessful = await verification.Set<FinancialConnection>().AsNoTracking()
                .Where(value => value.Id == connectionId)
                .Select(value => value.LastSuccessfulImportAtUtc)
                .SingleOrDefaultAsync(cancellationToken);
            return runExists && lastSuccessful == expectedLastSuccessfulAtUtc;
        }
        catch (Exception)
        {
            // Falha da verificação mantém o resultado indeterminado; nenhuma mensagem técnica cruza o contrato.
            return false;
        }
    }
}
