using Finora.Infrastructure.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confere geração/validação sem depender do banco ou da biblioteca JWT.
[TestClass]
public sealed class AuthenticationSessionModelTests
{
    // A geração produz identificadores opacos canônicos, distintos e com os 256 bits previstos.
    [TestMethod]
    public void GeneratedIdentifiersAreCanonicalAndDistinct()
    {
        var ids = Enumerable.Range(0, 1024).Select(_ => AuthSession.GenerateId()).ToArray();
        Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(ids.All(id => id.Length == 43 && AuthSession.IsValidId(id)));
        foreach (var invalid in new[] { "", "short", new string('a', 64), new string('!', 43), new string('A', 42) + "B" })
        {
            Assert.IsFalse(AuthSession.IsValidId(invalid));
        }
        Assert.IsFalse(AuthSession.IsValidId(null));
    }

    // Datas devem ser UTC e válidas antes de chegar ao provider, sem incluir valores na mensagem de falha.
    [TestMethod]
    public void InvalidSessionFieldsAreRejected()
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var id = AuthSession.GenerateId();
        Assert.ThrowsExactly<InvalidOperationException>(() => AuthSession.Create("bad", Guid.NewGuid(), now, now.AddMinutes(30)));
        Assert.ThrowsExactly<InvalidOperationException>(() => AuthSession.Create(id, Guid.Empty, now, now.AddMinutes(30)));
        Assert.ThrowsExactly<InvalidOperationException>(() => AuthSession.Create(id, Guid.NewGuid(), now, now));
        Assert.ThrowsExactly<InvalidOperationException>(() => AuthSession.Create(id, Guid.NewGuid(), now.ToOffset(TimeSpan.FromHours(1)), now.AddMinutes(30)));
    }
}
