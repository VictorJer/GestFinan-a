using Finora.Application.FinancialData;
using Finora.Domain.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Prova os limites sintáticos do filtro sem banco, HTTP ou dependência de cultura local.
[TestClass]
public sealed class TransactionQueryFilterValidatorTests
{
    private static readonly Guid UserId = Guid.Parse("d082609b-d6bd-41c5-9088-2252950fae21");

    // Valores ausentes usam defaults aprovados; limites unilaterais e busca literal permanecem válidos.
    [TestMethod]
    public void AcceptsDefaultsUnilateralDatesAndLiteralSearch()
    {
        Assert.IsTrue(TransactionQueryFilterValidator.TryValidate(new GetTransactionsQuery(UserId), out var defaults));
        Assert.IsNotNull(defaults);
        Assert.AreEqual(1, defaults.Page);
        Assert.AreEqual(25, defaults.PageSize);

        Assert.IsTrue(TransactionQueryFilterValidator.TryValidate(
            new GetTransactionsQuery(UserId, From: "2026-01-15", Search: "%_ literal", PageSize: "100"),
            out var lowerBound));
        Assert.IsNotNull(lowerBound);
        Assert.AreEqual(new DateOnly(2026, 1, 15), lowerBound.From);
        Assert.IsNull(lowerBound.ToExclusive);
        Assert.AreEqual("%_ literal", lowerBound.Search);
        Assert.AreEqual(100, lowerBound.PageSize);

        Assert.IsTrue(TransactionQueryFilterValidator.TryValidate(
            new GetTransactionsQuery(UserId, ToExclusive: "2026-02-01", Situation: "Pending"),
            out var upperBound));
        Assert.IsNotNull(upperBound);
        Assert.IsNull(upperBound.From);
        Assert.AreEqual(new DateOnly(2026, 2, 1), upperBound.ToExclusive);
        Assert.AreEqual(TransactionSituation.Pending, upperBound.Situation);
    }

    // Entrada inválida é recusada inteira, evitando corrigir silenciosamente datas, IDs ou limites numéricos.
    [TestMethod]
    public void RejectsMalformedIdsDatesSituationsRangesPagesAndLongSearch()
    {
        var invalidRequests = new[]
        {
            new GetTransactionsQuery(Guid.Empty),
            new GetTransactionsQuery(UserId, CardId: "not-a-guid"),
            new GetTransactionsQuery(UserId, CategoryId: "not-a-guid"),
            new GetTransactionsQuery(UserId, Situation: "99"),
            new GetTransactionsQuery(UserId, Situation: "not-a-situation"),
            new GetTransactionsQuery(UserId, From: "2026-02-30"),
            new GetTransactionsQuery(UserId, ToExclusive: "01/02/2026"),
            new GetTransactionsQuery(UserId, From: "2026-02-01", ToExclusive: "2026-02-01"),
            new GetTransactionsQuery(UserId, From: "2026-02-02", ToExclusive: "2026-02-01"),
            new GetTransactionsQuery(UserId, Page: "invalid"),
            new GetTransactionsQuery(UserId, Page: "0"),
            new GetTransactionsQuery(UserId, PageSize: "invalid"),
            new GetTransactionsQuery(UserId, PageSize: "0"),
            new GetTransactionsQuery(UserId, PageSize: "101"),
            new GetTransactionsQuery(UserId, Search: new string('x', 201))
        };

        foreach (var request in invalidRequests)
        {
            Assert.IsFalse(TransactionQueryFilterValidator.TryValidate(request, out _),
                $"A entrada inválida {request} foi aceita.");
        }
    }
}
