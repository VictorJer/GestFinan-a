using Finora.Domain.FinancialData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confere que a chave independente de cultura é a mesma para nomes equivalentes, preservando a apresentação.
[TestClass]
public sealed class CategoryNamePolicyTests
{
    // Acentos compostos/decompostos, caixa e espaços Unicode convergem para a mesma chave.
    [TestMethod]
    public void NormalizesEquivalentNamesWithoutChangingTheDisplayAccents()
    {
        var variants = new[] { " Saúde  da Casa ", "SAUDE\u00a0DA\u2003CASA", "Sau\u0301de da  casa" };
        foreach (var variant in variants)
        {
            Assert.IsTrue(CategoryNamePolicy.TryNormalize(variant, out var name));
            Assert.IsNotNull(name);
            Assert.AreEqual("SAUDE DA CASA", name.Key);
        }

        CategoryNamePolicy.TryNormalize(variants[0], out var presented);
        Assert.AreEqual("Saúde da Casa", presented!.DisplayName);
    }

    // Nenhum texto inválido é truncado; o limite é aplicado também depois de formar a chave.
    [TestMethod]
    public void RejectsEmptyControlsAndExcessLength()
    {
        foreach (var invalid in new string?[] { null, "", " \u00a0 ", "Nome\n", "X\u0000Y", new string('a', 61) })
        {
            Assert.IsFalse(CategoryNamePolicy.TryNormalize(invalid, out _));
        }

        Assert.IsTrue(CategoryNamePolicy.TryNormalize(new string('a', 60), out var boundary));
        Assert.AreEqual(60, boundary!.Key.Length);
        // Simula uma expansão futura do casing: a chave maior que o nome nunca cabe no campo persistido.
        Assert.IsFalse(CategoryNamePolicy.FitsLimits(new string('a', 60), new string('A', 61)));
        Assert.IsFalse(CategoryNamePolicy.FitsLimits(new string('a', 61), new string('A', 60)));
    }
}
