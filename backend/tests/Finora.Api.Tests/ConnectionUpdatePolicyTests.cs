using Finora.Api.Authentication;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Finora.Api.Tests;

// Confere defaults, binding/validação da configuração real e a fronteira exata de antiguidade com relógio controlado.
[TestClass]
public sealed class ConnectionUpdatePolicyTests
{
    // Os valores padrão deixam a política segura sem configuração extra e permanecem compartilhados por conexão.
    [TestMethod]
    public void DefaultsUseTwentyFourHoursAndFiveMinutes()
    {
        var options = new ConnectionUpdatePolicyOptions();

        Assert.AreEqual(TimeSpan.FromHours(24), options.StaleAfter);
        Assert.AreEqual(TimeSpan.FromMinutes(5), options.MinimumRequestInterval);
        Assert.IsTrue(ConnectionUpdatePolicyOptions.IsValid(options));
    }

    // O binder recebe overrides pela seção única e a composição de produção os entrega ao consumidor.
    [TestMethod]
    public async Task ValidOverridesAreBoundAndValidatedAtStartup()
    {
        using var host = CreateHost(new Dictionary<string, string?>
        {
            [$"{ConnectionUpdatePolicyOptions.ConfigurationSectionName}:StaleAfter"] = "12:00:00",
            [$"{ConnectionUpdatePolicyOptions.ConfigurationSectionName}:MinimumRequestInterval"] = "00:02:30"
        });

        await host.StartAsync();
        var options = host.Services.GetRequiredService<ConnectionUpdatePolicyOptions>();

        Assert.AreEqual(TimeSpan.FromHours(12), options.StaleAfter);
        Assert.AreEqual(TimeSpan.FromMinutes(2.5), options.MinimumRequestInterval);
        await host.StopAsync();
    }

    // Zero e duração negativa são recusados no startup, antes de uma configuração inválida chegar aos handlers.
    [TestMethod]
    public async Task NonpositiveDurationsFailStartupValidation()
    {
        var invalidConfigurations = new[]
        {
            new Dictionary<string, string?>
            {
                [$"{ConnectionUpdatePolicyOptions.ConfigurationSectionName}:StaleAfter"] = "00:00:00"
            },
            new Dictionary<string, string?>
            {
                [$"{ConnectionUpdatePolicyOptions.ConfigurationSectionName}:MinimumRequestInterval"] = "-00:00:01"
            }
        };

        foreach (var configuration in invalidConfigurations)
        {
            using var host = CreateHost(configuration);
            await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => host.StartAsync());
        }
    }

    // A controlled server clock proves equality is recent and the first tick after 24 hours is stale.
    [TestMethod]
    public void UpdateIsRequiredOnlyAfterTheConfiguredThreshold()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
        var lastSuccess = clock.GetUtcNow().Subtract(TimeSpan.FromHours(24));

        Assert.IsFalse(ConnectionUpdatePolicy.IsUpdateRequired(
            lastSuccess, clock.GetUtcNow(), TimeSpan.FromHours(24)));

        clock.Set(clock.GetUtcNow().AddTicks(1));
        Assert.IsTrue(ConnectionUpdatePolicy.IsUpdateRequired(
            lastSuccess, clock.GetUtcNow(), TimeSpan.FromHours(24)));
        Assert.IsTrue(ConnectionUpdatePolicy.IsUpdateRequired(
            null, clock.GetUtcNow(), TimeSpan.FromHours(24)));
    }

    // A entrada só pede intervenção após falha terminal de autorização, não durante retry ou outra falha.
    [TestMethod]
    public void AutomaticEntryRequiresReauthorizationOnlyForTerminalAuthorizationFailure()
    {
        Assert.IsTrue(ConnectionUpdatePolicy.IsAutomaticEntrySuppressed(
            SyncRunState.Failed,
            SyncRunFailureCodes.AuthenticationRequired));
        Assert.IsFalse(ConnectionUpdatePolicy.IsAutomaticEntrySuppressed(
            SyncRunState.Pending,
            SyncRunFailureCodes.AuthenticationRequired));
        Assert.IsFalse(ConnectionUpdatePolicy.IsAutomaticEntrySuppressed(
            SyncRunState.Failed,
            SyncRunFailureCodes.ProviderUnavailable));
        Assert.IsFalse(ConnectionUpdatePolicy.IsAutomaticEntrySuppressed(SyncRunState.Failed, null));
    }

    // Usa a mesma extensão de composição da API para que o teste cubra binding e validação de produção.
    private static IHost CreateHost(Dictionary<string, string?> values) =>
        new HostBuilder()
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(values))
            .ConfigureServices((_, services) => services.AddFinoraConnectionUpdatePolicyOptions())
            .Build();

    // Relógio mutável mantém o cenário determinístico sem dependência do horário de execução do teste.
    private sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Set(DateTimeOffset value) => _current = value;
    }
}
