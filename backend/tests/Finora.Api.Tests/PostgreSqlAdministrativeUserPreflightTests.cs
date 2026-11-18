using Finora.Application.AdministrativeUsers;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Usa somente FINORA_TEST_CONNECTION_STRING e serializa o schema descartável compartilhado pelos testes PostgreSQL.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlAdministrativeUserPreflightTests
{
    private const string SyntheticPassword = "Synthetic!123";
    private DisposablePostgresMigrationFixture? _fixture;
    private ServiceProvider? _serviceProvider;

    // Prepara explicitamente o schema FNR-007 no banco descartável validado pela fixture existente.
    [TestInitialize]
    public async Task InitializeAsync()
    {
        _fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await _fixture.ResetToEmptySchemaAsync();
        await _fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);

        var connectionString = DisposablePostgresTestConfiguration.ReadRequired().ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinoraAdministrativeIdentity(connectionString);
        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    // Libera o provider e o contexto da fixture sem reset automático ou alteração fora do banco de teste.
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    // O validador Identity rejeita formato inválido, preserva trim externo e normaliza diferenças de caixa.
    [TestMethod]
    public async Task PreflightValidatesEmailAndRecognizesNormalizedExistingUser()
    {
        var invalidEmail = await PreflightAsync(AdministrativeUserAction.Create, "not-an-email");
        Assert.AreEqual(AdministrativeUserPreflightState.InvalidEmail, invalidEmail.State);

        var createReady = await PreflightAsync(
            AdministrativeUserAction.Create,
            "  Holder@Example.com  ");
        Assert.AreEqual(AdministrativeUserPreflightState.Ready, createReady.State);

        var resetAbsent = await PreflightAsync(
            AdministrativeUserAction.ResetPassword,
            "holder@example.com");
        Assert.AreEqual(AdministrativeUserPreflightState.NotFound, resetAbsent.State);

        var userId = await CreateIdentityUserAsync("holder@example.com");
        var repeatedCreate = await PreflightAsync(
            AdministrativeUserAction.Create,
            "  HOLDER@EXAMPLE.COM  ");
        var resetExisting = await PreflightAsync(
            AdministrativeUserAction.ResetPassword,
            "  Holder@Example.com ");

        Assert.AreEqual(AdministrativeUserPreflightState.AlreadyExists, repeatedCreate.State);
        Assert.AreEqual(AdministrativeUserPreflightState.Ready, resetExisting.State);
        Assert.AreEqual(userId, resetExisting.CandidateUserId);
    }

    // Outro e-mail não pode substituir o titular; reset ausente permanece distinto de conflito de criação.
    [TestMethod]
    public async Task PreflightRejectsAnotherOwnerAndReportsMissingReset()
    {
        await CreateIdentityUserAsync("owner@example.com");

        var otherCreate = await PreflightAsync(
            AdministrativeUserAction.Create,
            "another@example.com");
        var otherReset = await PreflightAsync(
            AdministrativeUserAction.ResetPassword,
            "another@example.com");

        Assert.AreEqual(AdministrativeUserPreflightState.Conflict, otherCreate.State);
        Assert.AreEqual(AdministrativeUserPreflightState.NotFound, otherReset.State);
    }

    // Mais de um usuário torna a pré-consulta ambígua e ambas as operações falham fechadas.
    [TestMethod]
    public async Task PreflightRejectsDatabaseWithMultipleUsers()
    {
        await CreateIdentityUserAsync("first@example.com");
        await CreateIdentityUserAsync("second@example.com");

        var create = await PreflightAsync(
            AdministrativeUserAction.Create,
            "third@example.com");
        var reset = await PreflightAsync(
            AdministrativeUserAction.ResetPassword,
            "first@example.com");

        Assert.AreEqual(AdministrativeUserPreflightState.Conflict, create.State);
        Assert.AreEqual(AdministrativeUserPreflightState.Conflict, reset.State);
    }

    // Cada chamada usa um escopo novo e confirma que a consulta e os validadores Identity não deixam tracking.
    private async Task<AdministrativeUserPreflightResult> PreflightAsync(
        AdministrativeUserAction action,
        string email)
    {
        using var scope = _serviceProvider!.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IAdministrativeUserAccess>();
        var context = scope.ServiceProvider.GetRequiredService<FinoraDbContext>();
        var result = await access.PreflightAsync(action, email, CancellationToken.None);

        Assert.AreEqual(0, context.ChangeTracker.Entries<ApplicationUser>().Count());
        return result;
    }

    // Cria titulares somente com Identity e dados sintéticos para exercitar o estado persistido real.
    private async Task<Guid> CreateIdentityUserAsync(string email)
    {
        using var scope = _serviceProvider!.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };
        var result = await userManager.CreateAsync(user, SyntheticPassword);

        Assert.IsTrue(result.Succeeded);
        return user.Id;
    }
}
