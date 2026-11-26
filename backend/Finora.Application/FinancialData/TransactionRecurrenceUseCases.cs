using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using MediatR;

namespace Finora.Application.FinancialData;

// Estados fixos permitem que a API mapeie validação e ausência sem propagar exceções do domínio/persistência.
public enum TransactionRecurrenceOperationStatus
{
    Ready,
    InvalidRequest,
    CandidateNotFound
}

// A projeção contém somente IDs internos, versão e decisão; não replica fatos financeiros no contrato de recorrência.
public sealed record TransactionRecurrenceCandidatesResult(
    TransactionRecurrenceOperationStatus Status,
    IReadOnlyList<PersistedRecurrenceCandidate> Candidates);

// O resultado da escrita confirma somente a escolha explícita e seu instante UTC.
public sealed record UpdateTransactionRecurrenceDecisionResult(
    TransactionRecurrenceOperationStatus Status,
    RecurrenceDecisionKind? Decision = null,
    DateTimeOffset? UpdatedAtUtc = null);

// Consultar candidatos é somente leitura e sempre recebe o titular resolvido pela sessão na borda HTTP.
public sealed record GetTransactionRecurrenceCandidatesQuery(Guid UserId)
    : IRequest<TransactionRecurrenceCandidatesResult>;

// Reavaliação é comando separado da consulta para manter GET sem efeitos de escrita.
public sealed record EvaluateTransactionRecurrenceCandidatesCommand(Guid UserId)
    : IRequest<TransactionRecurrenceCandidatesResult>;

// Uma chamada explícita do titular cria ou corrige a única decisão vigente do candidato.
public sealed record UpdateTransactionRecurrenceDecisionCommand(
    Guid UserId,
    Guid CandidateId,
    RecurrenceDecisionKind Decision)
    : IRequest<UpdateTransactionRecurrenceDecisionResult>;

// A query limita a leitura ao titular e não executa avaliação nem sincronização implícita.
public sealed class GetTransactionRecurrenceCandidatesHandler(ITransactionRecurrenceStore store)
    : IRequestHandler<GetTransactionRecurrenceCandidatesQuery, TransactionRecurrenceCandidatesResult>
{
    public async Task<TransactionRecurrenceCandidatesResult> Handle(
        GetTransactionRecurrenceCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return new TransactionRecurrenceCandidatesResult(
                TransactionRecurrenceOperationStatus.InvalidRequest,
                Array.Empty<PersistedRecurrenceCandidate>());
        }

        var candidates = await store.ReadCandidatesAsync(request.UserId, cancellationToken);
        return new TransactionRecurrenceCandidatesResult(TransactionRecurrenceOperationStatus.Ready, candidates);
    }
}

// O comando avalia apenas o histórico persistido do titular e grava candidatos pela versão ativa do domínio.
public sealed class EvaluateTransactionRecurrenceCandidatesHandler(
    ITransactionRecurrenceStore store,
    TimeProvider clock)
    : IRequestHandler<EvaluateTransactionRecurrenceCandidatesCommand, TransactionRecurrenceCandidatesResult>
{
    public async Task<TransactionRecurrenceCandidatesResult> Handle(
        EvaluateTransactionRecurrenceCandidatesCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return new TransactionRecurrenceCandidatesResult(
                TransactionRecurrenceOperationStatus.InvalidRequest,
                Array.Empty<PersistedRecurrenceCandidate>());
        }

        var movements = await store.ReadTransactionsAsync(request.UserId, cancellationToken);
        var evaluated = TransactionRecurrenceEvaluator.Evaluate(movements);
        await store.SynchronizeCandidatesAsync(
            request.UserId,
            TransactionRecurrenceEvaluator.PolicyVersion,
            evaluated,
            clock.GetUtcNow().ToUniversalTime(),
            cancellationToken);

        // A resposta vem de novo da persistência para incluir estado e decisões preservadas após o upsert.
        var candidates = await store.ReadCandidatesAsync(request.UserId, cancellationToken);
        return new TransactionRecurrenceCandidatesResult(TransactionRecurrenceOperationStatus.Ready, candidates);
    }
}

// A decisão só é persistida após validar IDs e enum; propriedade é sempre conferida pelo store com o titular da sessão.
public sealed class UpdateTransactionRecurrenceDecisionHandler(
    ITransactionRecurrenceStore store,
    TimeProvider clock)
    : IRequestHandler<UpdateTransactionRecurrenceDecisionCommand, UpdateTransactionRecurrenceDecisionResult>
{
    public async Task<UpdateTransactionRecurrenceDecisionResult> Handle(
        UpdateTransactionRecurrenceDecisionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty
            || request.CandidateId == Guid.Empty
            || !Enum.IsDefined(request.Decision))
        {
            return new UpdateTransactionRecurrenceDecisionResult(
                TransactionRecurrenceOperationStatus.InvalidRequest);
        }

        var updatedAtUtc = clock.GetUtcNow().ToUniversalTime();
        var persistenceStatus = await store.SaveDecisionAsync(
            request.UserId,
            request.CandidateId,
            request.Decision,
            updatedAtUtc,
            cancellationToken);
        return persistenceStatus switch
        {
            RecurrenceDecisionPersistenceStatus.Updated => new UpdateTransactionRecurrenceDecisionResult(
                TransactionRecurrenceOperationStatus.Ready, request.Decision, updatedAtUtc),
            RecurrenceDecisionPersistenceStatus.CandidateNotFound => new UpdateTransactionRecurrenceDecisionResult(
                TransactionRecurrenceOperationStatus.CandidateNotFound),
            _ => new UpdateTransactionRecurrenceDecisionResult(TransactionRecurrenceOperationStatus.InvalidRequest)
        };
    }
}
