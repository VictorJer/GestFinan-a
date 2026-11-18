using System.Text.Json;
using Finora.Application.Authentication;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita somente a fronteira Application: nenhum resultado falso é prova de login/persistência reais.
[TestClass]
public sealed class AuthenticationHandlerTests
{
    // A descoberta real MediatR encaminha valores exatos e token de cancelamento ao adaptador.
    [TestMethod]
    public async Task HandlerDelegatesExactCredentialsResultAndCancellation()
    {
        var expected = LoginResult.Success(new PreparedAccessToken("jwt-sentinel", DateTimeOffset.UtcNow));
        var login = new FakeLogin(expected);
        await using var provider = CreateProvider(login);
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new LoginCommand(" email-sentinel@example.invalid ", " password-sentinel "), cancellation.Token);

        Assert.AreSame(expected, result);
        Assert.AreEqual(" email-sentinel@example.invalid ", login.Email);
        Assert.AreEqual(" password-sentinel ", login.Password);
        Assert.AreEqual(cancellation.Token, login.Cancellation);
    }

    // Cancelamento não é convertido em senha incorreta ou resultado de sucesso.
    [TestMethod]
    public async Task HandlerPropagatesCancellation()
    {
        var login = new FakeLogin(LoginResult.InvalidCredentials());
        var handler = new LoginHandler(login);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            handler.Handle(new LoginCommand("email", "password"), cancellation.Token));
    }

    // Contratos redigem texto incidental e o request não oferece propriedades públicas serializáveis.
    [TestMethod]
    public void SensitiveRepresentationsAreRedactedAndFailuresContainNoToken()
    {
        var command = new LoginCommand("email-sentinel", "password-sentinel");
        var token = new PreparedAccessToken("jwt-sentinel", DateTimeOffset.UtcNow);
        var session = new AuthenticationSessionReference(Guid.NewGuid(), "session-sentinel",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30));
        foreach (var text in new[] { command.ToString(), token.ToString(), LoginResult.Success(token).ToString(), session.ToString(), JsonSerializer.Serialize(command) })
        {
            Assert.IsFalse(text.Contains("sentinel", StringComparison.Ordinal));
        }

        Assert.IsNull(LoginResult.InvalidCredentials().Token);
        Assert.IsNull(LoginResult.Unavailable().Token);
        Assert.AreEqual(1800, token.ExpiresIn);
        Assert.AreEqual("Bearer", token.TokenType);
    }

    // A composição usa os handlers do assembly real sem registrar dependências de infraestrutura.
    private static ServiceProvider CreateProvider(IAuthenticationLogin login)
    {
        var services = new ServiceCollection();
        services.AddSingleton(login);
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly));
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Registra somente entradas sintéticas para provar delegação e cancelamento.
    private sealed class FakeLogin(LoginResult result) : IAuthenticationLogin
    {
        public string? Email { get; private set; }
        public string? Password { get; private set; }
        public CancellationToken Cancellation { get; private set; }

        public Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Email = email;
            Password = password;
            Cancellation = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
