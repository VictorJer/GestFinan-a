using Finora.Domain.FinancialData;
using MediatR;

namespace Finora.Application.FinancialData;

// A projeção operacional inclui somente a última execução própria, sem conexão, titular ou conteúdo financeiro.
public sealed record CurrentConnectionSyncExecutionSnapshot(
    Guid SyncRunId,
    SyncRunState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? FinishedAtUtc,
    SyncCoverageState CoverageState,
    string? FailureCode);

// O adapter devolve fatos persistidos; a política de antiguidade/cooldown é calculada pela Application.
public sealed record CurrentConnectionSyncStateSnapshot(
    DateTimeOffset? LastSuccessfulImportAtUtc,
    DateTimeOffset? SourceUpdatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    CurrentConnectionSyncExecutionSnapshot? LatestExecution);

// Status diferencia conexão própria única de ausência/ambiguidade sem expor identificadores de outros titulares.
public sealed record CurrentConnectionSyncStateReadResult(
    CurrentSyncConnectionStatus Status,
    CurrentConnectionSyncStateSnapshot? Snapshot = null);

/// <summary>Leitor somente de dados persistidos da conexão inequívoca do titular autenticado.</summary>
public interface ICurrentConnectionSyncStateReader
{
    Task<CurrentConnectionSyncStateReadResult> ReadAsync(Guid userId, CancellationToken cancellationToken);
}

// A resposta Application combina estado persistido e decisões temporais do servidor, não chamadas ao provedor.
public sealed record CurrentConnectionSyncState(
    bool UpdateNeeded,
    bool ReauthorizationRequired,
    DateTimeOffset? LastSuccessfulImportAtUtc,
    DateTimeOffset? SourceUpdatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? NextAllowedAtUtc,
    CurrentConnectionSyncExecutionSnapshot? LatestExecution);

public sealed record GetCurrentConnectionSyncStateQuery(Guid UserId)
    : IRequest<CurrentConnectionSyncStateQueryResult>;

public sealed record CurrentConnectionSyncStateQueryResult(
    CurrentSyncConnectionStatus Status,
    CurrentConnectionSyncState? State = null);

/// <summary>Deriva antiguidade, reautorização e próxima oportunidade usando somente persistência e relógio do servidor.</summary>
public sealed class GetCurrentConnectionSyncStateHandler(
    ICurrentConnectionSyncStateReader reader,
    ConnectionUpdatePolicyOptions policyOptions,
    TimeProvider timeProvider)
    : IRequestHandler<GetCurrentConnectionSyncStateQuery, CurrentConnectionSyncStateQueryResult>
{
    public async Task<CurrentConnectionSyncStateQueryResult> Handle(
        GetCurrentConnectionSyncStateQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return new CurrentConnectionSyncStateQueryResult(CurrentSyncConnectionStatus.InvalidRequest);
        }

        var read = await reader.ReadAsync(request.UserId, cancellationToken);
        if (read.Status != CurrentSyncConnectionStatus.Found || read.Snapshot is null)
        {
            return new CurrentConnectionSyncStateQueryResult(read.Status);
        }

        // O instante do servidor governa limiar, reautorização e intervalo; o endpoint não inicia execução.
        var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var latestExecution = read.Snapshot.LatestExecution;
        DateTimeOffset? nextAllowedAtUtc = latestExecution is null
            ? null
            : latestExecution.CreatedAtUtc.Add(policyOptions.MinimumRequestInterval);
        if (nextAllowedAtUtc is { } calculatedNextAllowedAtUtc
            && calculatedNextAllowedAtUtc <= nowUtc)
        {
            nextAllowedAtUtc = null;
        }

        var state = new CurrentConnectionSyncState(
            ConnectionUpdatePolicy.IsUpdateRequired(
                read.Snapshot.LastSuccessfulImportAtUtc,
                nowUtc,
                policyOptions.StaleAfter),
            ConnectionUpdatePolicy.IsAutomaticEntrySuppressed(
                latestExecution?.State,
                latestExecution?.FailureCode),
            read.Snapshot.LastSuccessfulImportAtUtc,
            read.Snapshot.SourceUpdatedAtUtc,
            read.Snapshot.LastAttemptAtUtc,
            nextAllowedAtUtc,
            latestExecution);
        return new CurrentConnectionSyncStateQueryResult(CurrentSyncConnectionStatus.Found, state);
    }
}
