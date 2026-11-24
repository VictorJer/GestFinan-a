using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Os casos de uso recebem titular validado e nunca constroem conexão financeira ou sincronização.
[TestClass]
public sealed class CategoryCatalogHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("a657ed92-1e86-4d56-9828-dcae5834c0b4");

    // Nomes reservados/invalidos não chegam ao adapter; nome pessoal validado leva só o titular da sessão.
    [TestMethod]
    public async Task CreationRejectsInvalidAndReservedNamesAndNormalizesPersonalNames()
    {
        var store = new FakeStore();
        var handler = new CreatePersonalCategoryHandler(store);
        foreach (var name in new string?[] { null, "  ", "Com\npras", " saúde ", "SeM\u00a0CATEGORIA", "Outros" })
        {
            var status = (await handler.Handle(new CreatePersonalCategoryCommand(OwnerId, name), default)).Status;
            Assert.AreEqual(name is " saúde " or "SeM\u00a0CATEGORIA" or "Outros"
                ? CreateCategoryStatus.Conflict : CreateCategoryStatus.Invalid, status);
        }

        Assert.AreEqual(0, store.Creations);
        var created = await handler.Handle(new CreatePersonalCategoryCommand(OwnerId, "  Proje\u0301tos  pessoais "), default);
        Assert.AreEqual(CreateCategoryStatus.Created, created.Status);
        Assert.AreEqual("personal", created.Category!.Kind);
        Assert.AreEqual(store.NextId, created.Category.Id);
        Assert.AreEqual("Proje\u0301tos pessoais", created.Category.Name);
        Assert.AreEqual("PROJETOS PESSOAIS", store.LastName!.Key);
        Assert.AreEqual(OwnerId, store.LastUser);
        Assert.AreEqual(1, store.Creations);
    }

    // Ausência ou incompatibilidade dos sete registros não deve apresentar lista parcialmente válida.
    [TestMethod]
    public async Task ListingFailsClosedAndAddsVirtualOptionAfterPersonalCategories()
    {
        var store = new FakeStore();
        var handler = new GetCategoriesHandler(store);
        await Assert.ThrowsAsync<CategoryCatalogUnavailableException>(async () =>
            await handler.Handle(new GetCategoriesQuery(OwnerId), default));

        store.Entries.AddRange(SystemCategoryCatalog.Categories.Select(item =>
            Category.Create(item.Id, null, true, item.Name, item.Key)));
        store.Entries.Add(Category.Create(Guid.NewGuid(), OwnerId, false, "Zebra", "ZEBRA"));
        store.Entries.Add(Category.Create(Guid.NewGuid(), OwnerId, false, "Árvore", "ARVORE"));
        var options = await handler.Handle(new GetCategoriesQuery(OwnerId), default);
        Assert.HasCount(10, options);
        CollectionAssert.AreEqual(SystemCategoryCatalog.Categories.Select(item => item.Name).ToArray(),
            options.Take(7).Select(option => option.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Árvore", "Zebra", "Sem categoria" },
            options.Skip(7).Select(option => option.Name).ToArray());
        Assert.IsNull(options[^1].Id);
        Assert.AreEqual("unassigned", options[^1].Kind);
    }

    // A simulação memoriza exclusivamente chamadas ao adapter, sem fingir prova PostgreSQL ou HTTP.
    private sealed class FakeStore : ICategoryCatalogStore
    {
        public Guid NextId { get; } = Guid.Parse("dd8f5c56-7d4f-481e-9057-d30cd465bb70");
        public List<Category> Entries { get; } = [];
        public int Creations { get; private set; }
        public Guid LastUser { get; private set; }
        public CategoryName? LastName { get; private set; }

        public Task<Guid?> CreatePersonalAsync(Guid userId, CategoryName name, CancellationToken cancellationToken)
        {
            Creations++;
            LastUser = userId;
            LastName = name;
            return Task.FromResult<Guid?>(NextId);
        }

        public Task<IReadOnlyList<Category>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Category>>(Entries);
    }
}
