using Finora.Domain.FinancialData;
using MediatR;

namespace Finora.Application.FinancialData;

// A aplicação só conhece categorias internas; a projeção HTTP mantém sua própria allowlist.
public sealed record CategoryOption(Guid? Id, string Name, string Kind);

// A ausência de catálogo é falha operacional, não uma lista vazia que pareça completa.
public sealed class CategoryCatalogUnavailableException : Exception;

// O adapter arbitra duplicidade na mesma transação de criação e nunca recebe titular escolhido pelo HTTP.
public interface ICategoryCatalogStore
{
    Task<Guid?> CreatePersonalAsync(Guid userId, CategoryName name, CancellationToken cancellationToken);
    Task<IReadOnlyList<Category>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
}

public enum CreateCategoryStatus { Created, Invalid, Conflict }

// A sessão é a única origem confiável do titular; o comando recebe somente seu ID e o nome fornecido.
public sealed record CreatePersonalCategoryCommand(Guid UserId, string? Name) : IRequest<CreateCategoryResult>;
public sealed record CreateCategoryResult(CreateCategoryStatus Status, CategoryOption? Category = null);

// Normaliza antes da consulta ao banco, recusando também todas as oito chaves reservadas.
public sealed class CreatePersonalCategoryHandler(ICategoryCatalogStore store)
    : IRequestHandler<CreatePersonalCategoryCommand, CreateCategoryResult>
{
    public async Task<CreateCategoryResult> Handle(CreatePersonalCategoryCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || !CategoryNamePolicy.TryNormalize(request.Name, out var name))
        {
            return new(CreateCategoryStatus.Invalid);
        }

        if (SystemCategoryCatalog.IsReservedKey(name!.Key))
        {
            return new(CreateCategoryStatus.Conflict);
        }

        var id = await store.CreatePersonalAsync(request.UserId, name, cancellationToken);
        return id.HasValue
            ? new(CreateCategoryStatus.Created, new CategoryOption(id.Value, name.DisplayName, "personal"))
            : new(CreateCategoryStatus.Conflict);
    }
}

public sealed record GetCategoriesQuery(Guid UserId) : IRequest<IReadOnlyList<CategoryOption>>;

// Consulta somente o escopo aprovado e adiciona ausência virtual depois da ordenação estável.
public sealed class GetCategoriesHandler(ICategoryCatalogStore store)
    : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryOption>>
{
    public async Task<IReadOnlyList<CategoryOption>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty) { throw new CategoryCatalogUnavailableException(); }
        var categories = await store.ListForUserAsync(request.UserId, cancellationToken);
        var systems = categories.Where(category => category.IsSystem).ToArray();
        if (systems.Length != SystemCategoryCatalog.Categories.Count ||
            SystemCategoryCatalog.Categories.Any(expected =>
                !systems.Any(actual => actual.Id == expected.Id && actual.UserId is null &&
                    actual.ScopeId == Guid.Empty && actual.Name == expected.Name && actual.NormalizedName == expected.Key)))
        {
            throw new CategoryCatalogUnavailableException();
        }

        var personal = categories.Where(category => !category.IsSystem).ToArray();
        if (personal.Any(category => category.UserId != request.UserId || category.ScopeId != request.UserId ||
            SystemCategoryCatalog.IsReservedKey(category.NormalizedName)))
        {
            throw new CategoryCatalogUnavailableException();
        }

        var options = SystemCategoryCatalog.Categories
            .Select(expected => new CategoryOption(expected.Id, expected.Name, "system"))
            .Concat(personal.OrderBy(category => category.NormalizedName, StringComparer.Ordinal)
                .ThenBy(category => category.Id)
                .Select(category => new CategoryOption(category.Id, category.Name, "personal")))
            .Append(new CategoryOption(null, SystemCategoryCatalog.UnassignedName, "unassigned"));
        return options.ToArray();
    }
}
