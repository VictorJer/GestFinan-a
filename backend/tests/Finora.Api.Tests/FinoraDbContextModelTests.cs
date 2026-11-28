using System.Data;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Evita concorrência com outros testes que temporariamente configuram variáveis de processo.
[DoNotParallelize]
[TestClass]
public sealed class FinoraDbContextModelTests
{
    // A factory de migrations fornece as mesmas opções do modelo sem abrir conexão nem aplicar DDL.
    [TestMethod]
    public void DesignTimeFactoryUsesVersion2IdentityModelWithoutPasskeysOrSeed()
    {
        const string connectionVariable = "ConnectionStrings__Finora";
        const string syntheticConnection =
            "Host=127.0.0.1;Database=finora_integration_test;Username=finora_test";
        var previousConnection = Environment.GetEnvironmentVariable(connectionVariable);

        try
        {
            Environment.SetEnvironmentVariable(connectionVariable, syntheticConnection);

            using var context = new FinoraDbContextFactory().CreateDbContext(Array.Empty<string>());
            Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);

            var model = context.Model;
            var user = model.FindEntityType(typeof(ApplicationUser));
            Assert.IsNotNull(user);
            Assert.AreEqual("AspNetUsers", user.GetTableName());
            Assert.IsNotNull(model.FindEntityType(typeof(IdentityUserClaim<Guid>)));
            Assert.IsNotNull(model.FindEntityType(typeof(IdentityUserLogin<Guid>)));
            Assert.IsNotNull(model.FindEntityType(typeof(IdentityUserToken<Guid>)));
            Assert.IsNull(model.FindEntityType(typeof(IdentityRole<Guid>)));
            Assert.IsNull(model.FindEntityType(typeof(IdentityRoleClaim<Guid>)));
            Assert.IsNull(model.FindEntityType(typeof(IdentityUserRole<Guid>)));
            Assert.IsNull(model.FindEntityType(typeof(IdentityUserPasskey<Guid>)));

            var login = model.FindEntityType(typeof(IdentityUserLogin<Guid>))!;
            Assert.AreEqual(128, login.FindProperty(nameof(IdentityUserLogin<Guid>.LoginProvider))!.GetMaxLength());
            Assert.AreEqual(128, login.FindProperty(nameof(IdentityUserLogin<Guid>.ProviderKey))!.GetMaxLength());

            var token = model.FindEntityType(typeof(IdentityUserToken<Guid>))!;
            Assert.AreEqual(128, token.FindProperty(nameof(IdentityUserToken<Guid>.LoginProvider))!.GetMaxLength());
            Assert.AreEqual(128, token.FindProperty(nameof(IdentityUserToken<Guid>.Name))!.GetMaxLength());
            Assert.AreEqual(256, user.FindProperty(nameof(IdentityUser<Guid>.PhoneNumber))!.GetMaxLength());

            // A sessão guarda somente os cinco campos técnicos aprovados, com vínculo restritivo ao titular.
            var session = model.FindEntityType(typeof(AuthSession));
            Assert.IsNotNull(session);
            Assert.AreEqual("AuthSessions", session.GetTableName());
            Assert.AreEqual(64, session.FindProperty(nameof(AuthSession.Id))!.GetMaxLength());
            CollectionAssert.AreEquivalent(new[] { "Id", "UserId", "CreatedAtUtc", "ExpiresAtUtc", "RevokedAtUtc" },
                session.GetProperties().Select(property => property.Name).ToArray());
            Assert.AreEqual(DeleteBehavior.Restrict, session.GetForeignKeys().Single().DeleteBehavior);

            // Dados de seed pertencem ao modelo de design, não ao modelo runtime otimizado do contexto.
            var designModel = context.GetService<IDesignTimeModel>().Model;
            Assert.IsFalse(designModel.GetEntityTypes().Any(entityType => entityType.GetSeedData().Any()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionVariable, previousConnection);
        }
    }

    // O modo explícito de testes usa somente FINORA_TEST_CONNECTION_STRING e permanece desconectado do runtime.
    [TestMethod]
    public void DesignTimeFactoryTestModeNeverFallsBackToRuntimeConnection()
    {
        const string testConnectionVariable = "FINORA_TEST_CONNECTION_STRING";
        const string runtimeConnectionVariable = "ConnectionStrings__Finora";
        const string testConnection =
            "Host=127.0.0.1;Port=55463;Database=finora_integration_test;Username=finora_test;Password=synthetic";
        const string runtimeConnection =
            "Host=127.0.0.1;Database=finora_local;Username=finora;Password=unused";
        var previousTestConnection = Environment.GetEnvironmentVariable(testConnectionVariable);
        var previousRuntimeConnection = Environment.GetEnvironmentVariable(runtimeConnectionVariable);

        try
        {
            Environment.SetEnvironmentVariable(testConnectionVariable, testConnection);
            Environment.SetEnvironmentVariable(runtimeConnectionVariable, runtimeConnection);

            using (var context = new FinoraDbContextFactory().CreateDbContext(["--test-connection"]))
            {
                Assert.AreEqual("finora_integration_test", context.Database.GetDbConnection().Database);
                Assert.AreEqual(ConnectionState.Closed, context.Database.GetDbConnection().State);
            }

            Environment.SetEnvironmentVariable(testConnectionVariable, null);
            var exception = Assert.Throws<InvalidOperationException>(
                () => new FinoraDbContextFactory().CreateDbContext(["--test-connection"]));
            StringAssert.Contains(exception.Message, testConnectionVariable);
            Assert.AreEqual(runtimeConnection, Environment.GetEnvironmentVariable(runtimeConnectionVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(testConnectionVariable, previousTestConnection);
            Environment.SetEnvironmentVariable(runtimeConnectionVariable, previousRuntimeConnection);
        }
    }

    // Tanto a criação padrão pelo Identity quanto a atribuição posterior mantêm o UUID diferente do marcador vazio.
    [TestMethod]
    public void ApplicationUserRequiresNonEmptyGuid()
    {
        var generated = new ApplicationUser();
        var suppliedId = Guid.Parse("3c472e18-2c28-4bf1-90dc-f60c0e97ac12");
        var supplied = new ApplicationUser(suppliedId);

        Assert.AreNotEqual(Guid.Empty, generated.Id);
        Assert.AreEqual(suppliedId, supplied.Id);
        Assert.Throws<ArgumentException>(() => new ApplicationUser(Guid.Empty));
        Assert.Throws<ArgumentException>(() => supplied.Id = Guid.Empty);
    }
}
