using System.Data;
using Finora.Api.AdministrativeUsers;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida a composição real em memória, sem abrir conexão, executar migrations ou acessar ambiente externo.
[TestClass]
public sealed class AdministrativeUserIdentityCompositionTests
{
    private const string SyntheticConnectionString =
        "Host=127.0.0.1;Database=finora_integration_test;Username=finora_test";

    // O núcleo compartilhado resolve stores/modelo sem incorporar o acesso CLI nem tokens de reset ao HTTP.
    [TestMethod]
    public void SharedCompositionDoesNotRegisterAdministrativeAccessOrResetProviders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinoraIdentity(SyntheticConnectionString);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.AreEqual(IdentitySchemaVersions.Version2, options.Stores.SchemaVersion);
        Assert.AreEqual(128, options.Stores.MaxLengthForKeys);
        Assert.AreEqual(6, options.Password.RequiredLength);
        Assert.IsTrue(options.Lockout.AllowedForNewUsers);
        Assert.AreEqual(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.AreEqual(TimeSpan.FromMinutes(5), options.Lockout.DefaultLockoutTimeSpan);
        Assert.AreEqual(0, options.Tokens.ProviderMap.Count);
        Assert.IsNull(scope.ServiceProvider.GetService<Finora.Application.AdministrativeUsers.IAdministrativeUserAccess>());
        Assert.IsNull(scope.ServiceProvider.GetService<IDataProtectionProvider>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        Assert.AreEqual(ConnectionState.Closed,
            scope.ServiceProvider.GetRequiredService<FinoraDbContext>().Database.GetDbConnection().State);
    }

    // Os serviços Identity são scoped e reutilizam as opções do modelo sem habilitar roles ou políticas extras.
    [TestMethod]
    public void CompositionUsesScopedStoresPasswordPolicyAndEphemeralTokens()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdministrativeUserIdentity(SyntheticConnectionString);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        var identityOptions = firstScope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.AreNotSame(firstContext, secondContext);
        Assert.AreEqual(ConnectionState.Closed, firstContext.Database.GetDbConnection().State);
        Assert.AreEqual(IdentitySchemaVersions.Version2, identityOptions.Stores.SchemaVersion);
        Assert.AreEqual(FinoraIdentityModelOptions.MaxLengthForKeys, identityOptions.Stores.MaxLengthForKeys);
        Assert.IsTrue(identityOptions.User.RequireUniqueEmail);
        Assert.AreEqual(6, identityOptions.Password.RequiredLength);
        Assert.AreEqual(1, identityOptions.Password.RequiredUniqueChars);
        Assert.IsTrue(identityOptions.Password.RequireUppercase);
        Assert.IsTrue(identityOptions.Password.RequireLowercase);
        Assert.IsTrue(identityOptions.Password.RequireDigit);
        Assert.IsTrue(identityOptions.Password.RequireNonAlphanumeric);
        Assert.AreEqual(TokenOptions.DefaultProvider, identityOptions.Tokens.PasswordResetTokenProvider);
        Assert.IsTrue(identityOptions.Tokens.ProviderMap.ContainsKey(TokenOptions.DefaultProvider));

        var firstProtectionProvider = firstScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var secondProtectionProvider = secondScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        Assert.IsInstanceOfType(firstProtectionProvider, typeof(EphemeralDataProtectionProvider));
        Assert.AreSame(firstProtectionProvider, secondProtectionProvider);
        Assert.IsNotNull(firstScope.ServiceProvider.GetRequiredService<DataProtectorTokenProvider<ApplicationUser>>());
        Assert.IsNotNull(firstScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    // O modelo construído pela composição runtime mantém o schema relacional do snapshot sem conectar ao banco.
    [TestMethod]
    public void RuntimeIdentityModelMatchesMigrationSnapshot()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdministrativeUserIdentity(SyntheticConnectionString);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;

        Assert.IsNotNull(snapshot);

        var modelDiffer = context.GetService<IMigrationsModelDiffer>();
        var configuredModel = context.GetService<IDesignTimeModel>().Model;
        var runtimeCompositionModel = configuredModel.GetRelationalModel();
        // O snapshot é finalizado pelo provider runtime antes da comparação relacional exigida pelo EF Core.
        var initializedSnapshot = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model);
        var snapshotModel = initializedSnapshot.GetRelationalModel();

        Assert.IsNotNull(context.Model.FindEntityType(typeof(ApplicationUser)));
        Assert.IsNull(context.Model.FindEntityType(typeof(IdentityRole<Guid>)));
        Assert.IsNull(context.Model.FindEntityType(typeof(IdentityUserPasskey<Guid>)));
        Assert.IsFalse(modelDiffer.HasDifferences(snapshotModel, runtimeCompositionModel));
    }
}
