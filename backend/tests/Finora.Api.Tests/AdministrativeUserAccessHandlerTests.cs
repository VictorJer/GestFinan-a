using Finora.Application.AdministrativeUsers;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Os testes verificam o contrato MediatR com uma implementação falsa, sem Identity, EF ou banco.
[TestClass]
public sealed class AdministrativeUserAccessHandlerTests
{
    // A pré-consulta encaminha operação, e-mail e cancelamento sem refletir o e-mail na representação textual.
    [TestMethod]
    public async Task PreflightQueryDelegatesTypedResultAndCancellation()
    {
        const string email = "preflight-sentinel@example.invalid";
        var expected = new AdministrativeUserPreflightResult(
            AdministrativeUserPreflightState.Ready,
            Guid.NewGuid());
        var access = new FakeAdministrativeUserAccess { PreflightResult = expected };
        await using var provider = CreateProvider(access);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        using var cancellation = new CancellationTokenSource();
        var query = new CheckAdministrativeUserPreflightQuery(AdministrativeUserAction.ResetPassword, email);

        var result = await sender.Send(query, cancellation.Token);

        Assert.AreSame(expected, result);
        Assert.AreEqual(AdministrativeUserAction.ResetPassword, access.ReceivedAction);
        Assert.AreEqual(email, access.ReceivedEmail);
        Assert.AreEqual(cancellation.Token, access.ReceivedCancellationToken);
        Assert.IsFalse(query.ToString().Contains(email, StringComparison.Ordinal));
        Assert.IsFalse(result.ToString().Contains(expected.CandidateUserId!.Value.ToString(), StringComparison.Ordinal));
    }

    // A criação encaminha a senha sem transformação e sua representação textual não contém credenciais ou PII.
    [TestMethod]
    public async Task CreateCommandDelegatesResultAndRedactsSensitiveValues()
    {
        const string email = "create-email-sentinel@example.invalid";
        const string password = "Create-password-sentinel-9!";
        var expected = new AdministrativeUserOperationResult(AdministrativeUserOperationState.Created);
        var access = new FakeAdministrativeUserAccess { OperationResult = expected };
        await using var provider = CreateProvider(access);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        using var cancellation = new CancellationTokenSource();
        var command = new CreateAdministrativeUserCommand(email, password);

        var result = await sender.Send(command, cancellation.Token);

        Assert.AreSame(expected, result);
        Assert.AreEqual(email, access.ReceivedEmail);
        Assert.AreEqual(password, access.ReceivedPassword);
        Assert.AreEqual(cancellation.Token, access.ReceivedCancellationToken);
        Assert.IsFalse(command.ToString().Contains(email, StringComparison.Ordinal));
        Assert.IsFalse(command.ToString().Contains(password, StringComparison.Ordinal));
    }

    // O reset mantém o vínculo com a pré-consulta e redige também o identificador interno.
    [TestMethod]
    public async Task ResetCommandDelegatesExpectedUserAndRedactsSensitiveValues()
    {
        const string email = "reset-email-sentinel@example.invalid";
        const string password = "Reset-password-sentinel-9!";
        var expectedUserId = Guid.NewGuid();
        var expected = new AdministrativeUserOperationResult(AdministrativeUserOperationState.PasswordReset);
        var access = new FakeAdministrativeUserAccess { OperationResult = expected };
        await using var provider = CreateProvider(access);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        using var cancellation = new CancellationTokenSource();
        var command = new ResetAdministrativeUserPasswordCommand(email, expectedUserId, password);

        var result = await sender.Send(command, cancellation.Token);

        Assert.AreSame(expected, result);
        Assert.AreEqual(email, access.ReceivedEmail);
        Assert.AreEqual(expectedUserId, access.ReceivedExpectedUserId);
        Assert.AreEqual(password, access.ReceivedPassword);
        Assert.AreEqual(cancellation.Token, access.ReceivedCancellationToken);
        Assert.IsFalse(command.ToString().Contains(email, StringComparison.Ordinal));
        Assert.IsFalse(command.ToString().Contains(expectedUserId.ToString(), StringComparison.Ordinal));
        Assert.IsFalse(command.ToString().Contains(password, StringComparison.Ordinal));
    }

    // A composição valida escopos e descobre os handlers no assembly real da Application.
    private static ServiceProvider CreateProvider(FakeAdministrativeUserAccess access)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAdministrativeUserAccess>(_ => access);
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(CheckAdministrativeUserPreflightQuery).Assembly));

        return services.BuildServiceProvider(validateScopes: true);
    }

    // O fake registra somente os valores recebidos para provar delegação; não simula Identity ou persistência.
    private sealed class FakeAdministrativeUserAccess : IAdministrativeUserAccess
    {
        public AdministrativeUserPreflightResult PreflightResult { get; init; } =
            new(AdministrativeUserPreflightState.Ready);

        public AdministrativeUserOperationResult OperationResult { get; init; } =
            new(AdministrativeUserOperationState.OperationalFailure);

        public AdministrativeUserAction? ReceivedAction { get; private set; }

        public Guid? ReceivedExpectedUserId { get; private set; }

        public string? ReceivedEmail { get; private set; }

        public string? ReceivedPassword { get; private set; }

        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<AdministrativeUserPreflightResult> PreflightAsync(
            AdministrativeUserAction action,
            string email,
            CancellationToken cancellationToken)
        {
            ReceivedAction = action;
            ReceivedEmail = email;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(PreflightResult);
        }

        public Task<AdministrativeUserOperationResult> CreateAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            ReceivedEmail = email;
            ReceivedPassword = password;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(OperationResult);
        }

        public Task<AdministrativeUserOperationResult> ResetPasswordAsync(
            string email,
            Guid expectedUserId,
            string newPassword,
            CancellationToken cancellationToken)
        {
            ReceivedEmail = email;
            ReceivedExpectedUserId = expectedUserId;
            ReceivedPassword = newPassword;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(OperationResult);
        }
    }
}
