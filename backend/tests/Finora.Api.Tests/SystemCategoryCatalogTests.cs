using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// IDs e chaves não podem divergir entre o catálogo executável e a migration de dados.
[TestClass]
public sealed class SystemCategoryCatalogTests
{
    // As sete entradas persistidas são distintas e a opção virtual só aparece entre as reservas.
    [TestMethod]
    public void HasSevenUniqueStableEntriesAndOneVirtualReservedKey()
    {
        Assert.HasCount(7, SystemCategoryCatalog.Categories);
        Assert.HasCount(7, SystemCategoryCatalog.Categories.Select(category => category.Id).Distinct().ToArray());
        Assert.HasCount(7, SystemCategoryCatalog.Categories.Select(category => category.Key).Distinct().ToArray());

        foreach (var (id, name, key) in SystemCategoryCatalog.Categories)
        {
            Assert.AreNotEqual(Guid.Empty, id);
            Assert.IsTrue(CategoryNamePolicy.TryNormalize(name, out var normalized));
            Assert.AreEqual(key, normalized!.Key);
            Assert.IsTrue(SystemCategoryCatalog.IsReservedKey(key));
        }

        Assert.IsTrue(CategoryNamePolicy.TryNormalize(" SEM\u00a0 categoria ", out var virtualName));
        Assert.AreEqual("SEM CATEGORIA", virtualName!.Key);
        Assert.IsTrue(SystemCategoryCatalog.IsReservedKey(virtualName.Key));
        Assert.IsFalse(SystemCategoryCatalog.Categories.Any(category => category.Name == SystemCategoryCatalog.UnassignedName));
        Assert.IsFalse(SystemCategoryCatalog.IsReservedKey("PROJETOS"));
    }
}
