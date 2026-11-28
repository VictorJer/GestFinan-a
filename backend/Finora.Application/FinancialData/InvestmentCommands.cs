using Finora.Domain.FinancialData;
using MediatR;

namespace Finora.Application.FinancialData;

// A projeção transporta somente os campos próprios da posição manual, sem relação com movimentos de cartão.
public sealed record InvestmentItem(
    Guid Id,
    string Name,
    string Class,
    decimal AppliedAmount,
    decimal CurrentAmount)
{
    // A conversão centralizada evita que handlers exponham a entidade rastreada do Domain na borda HTTP.
    public static InvestmentItem From(Investment investment) => new(
        investment.Id,
        investment.Name,
        investment.Class,
        investment.AppliedAmount,
        investment.CurrentAmount);
}

// Estados explícitos permitem que a API converta ausência própria/alheia no mesmo 404 sanitizado.
public enum InvestmentOperationStatus
{
    Created,
    Updated,
    Deleted,
    InvalidRequest,
    NotFound
}

// Um item só é retornado após a persistência confirmar a escrita correspondente.
public sealed record InvestmentOperationResult(
    InvestmentOperationStatus Status,
    InvestmentItem? Item = null);

// O adapter recebe entidade já validada e repete UserId no predicado de cada atualização ou remoção.
public interface IInvestmentStore
{
    Task<IReadOnlyList<InvestmentItem>> ReadOwnedAsync(Guid userId, CancellationToken cancellationToken);
    Task<InvestmentItem> CreateAsync(Investment investment, CancellationToken cancellationToken);
    Task<InvestmentItem?> UpdateOwnedAsync(Investment investment, CancellationToken cancellationToken);
    Task<bool> DeleteOwnedAsync(Guid userId, Guid investmentId, CancellationToken cancellationToken);
}

// O titular integra o comando interno e será fornecido pela sessão, nunca por um campo de payload HTTP.
public sealed record CreateInvestmentCommand(
    Guid UserId,
    string? Name,
    string? Class,
    decimal AppliedAmount,
    decimal CurrentAmount) : IRequest<InvestmentOperationResult>;

public sealed record UpdateInvestmentCommand(
    Guid UserId,
    Guid InvestmentId,
    string? Name,
    string? Class,
    decimal AppliedAmount,
    decimal CurrentAmount) : IRequest<InvestmentOperationResult>;

public sealed record DeleteInvestmentCommand(Guid UserId, Guid InvestmentId)
    : IRequest<InvestmentOperationResult>;

// A validação do Domain ocorre antes do adapter; o status inválido não realiza consulta nem escrita.
public sealed class CreateInvestmentHandler(IInvestmentStore store)
    : IRequestHandler<CreateInvestmentCommand, InvestmentOperationResult>
{
    public async Task<InvestmentOperationResult> Handle(
        CreateInvestmentCommand request,
        CancellationToken cancellationToken)
    {
        var investment = InvestmentCommandValidation.TryCreate(
            Guid.NewGuid(),
            request.UserId,
            request.Name,
            request.Class,
            request.AppliedAmount,
            request.CurrentAmount);
        if (investment is null)
        {
            return new(InvestmentOperationStatus.InvalidRequest);
        }

        var created = await store.CreateAsync(investment, cancellationToken);
        return new(InvestmentOperationStatus.Created, created);
    }
}

// A entidade candidata é validada antes da consulta e o store procura somente o par ID/titular solicitado.
public sealed class UpdateInvestmentHandler(IInvestmentStore store)
    : IRequestHandler<UpdateInvestmentCommand, InvestmentOperationResult>
{
    public async Task<InvestmentOperationResult> Handle(
        UpdateInvestmentCommand request,
        CancellationToken cancellationToken)
    {
        var investment = InvestmentCommandValidation.TryCreate(
            request.InvestmentId,
            request.UserId,
            request.Name,
            request.Class,
            request.AppliedAmount,
            request.CurrentAmount);
        if (investment is null)
        {
            return new(InvestmentOperationStatus.InvalidRequest);
        }

        var updated = await store.UpdateOwnedAsync(investment, cancellationToken);
        return updated is null
            ? new(InvestmentOperationStatus.NotFound)
            : new(InvestmentOperationStatus.Updated, updated);
    }
}

// A remoção valida os dois IDs e deixa a filtragem de propriedade dentro do comando persistente.
public sealed class DeleteInvestmentHandler(IInvestmentStore store)
    : IRequestHandler<DeleteInvestmentCommand, InvestmentOperationResult>
{
    public async Task<InvestmentOperationResult> Handle(
        DeleteInvestmentCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || request.InvestmentId == Guid.Empty)
        {
            return new(InvestmentOperationStatus.InvalidRequest);
        }

        var deleted = await store.DeleteOwnedAsync(request.UserId, request.InvestmentId, cancellationToken);
        return new(deleted ? InvestmentOperationStatus.Deleted : InvestmentOperationStatus.NotFound);
    }
}

// Centraliza a fronteira de validação para que create/update rejeitem os mesmos campos antes de qualquer I/O.
internal static class InvestmentCommandValidation
{
    // ArgumentException cobre nome, classe, identidade e precisão inválidos sem capturar falhas de persistência.
    internal static Investment? TryCreate(
        Guid id,
        Guid userId,
        string? name,
        string? @class,
        decimal appliedAmount,
        decimal currentAmount)
    {
        if (name is null || @class is null)
        {
            return null;
        }

        try
        {
            return Investment.Create(id, userId, name, @class, appliedAmount, currentAmount);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
