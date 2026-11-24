using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Finora.Api.Tests;

// Evita que hosts de teste concorrentes leiam snapshots diferentes das mesmas variáveis temporárias.
internal static class TestHostEnvironment
{
    internal static object ConfigurationLock { get; } = new();
}

// Cada teste escolhe explicitamente o ambiente, sem depender do perfil local da máquina.
internal sealed class ApiFactory(string environment) : WebApplicationFactory<Program>
{
    private static readonly string[] IsolatedConfigurationKeys =
    [
        "ConnectionStrings:Finora",
        "Authentication:Jwt:Issuer",
        "Authentication:Jwt:Audience",
        "Authentication:Jwt:SigningKeyBase64",
        "Messaging:RabbitMq:HostAddress",
        "Messaging:RabbitMq:Username",
        "Messaging:RabbitMq:Password",
        "Messaging:RabbitMq:UseTls"
    ];

    // Valores vazios têm precedência sobre appsettings/User Secrets e mantêm o host isolado de serviços locais.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            IsolatedConfigurationKeys.ToDictionary(key => key, _ => (string?)string.Empty)));
    }

    // O hosting mínimo lê algumas fontes antes das callbacks do host de teste; o isolamento é temporário e restaurado.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (TestHostEnvironment.ConfigurationLock)
        {
            var previous = new Dictionary<string, string?>();
            foreach (var key in IsolatedConfigurationKeys)
            {
                var variable = key.Replace(":", "__", StringComparison.Ordinal);
                previous[variable] = Environment.GetEnvironmentVariable(variable);
                Environment.SetEnvironmentVariable(variable, string.Empty);
            }

            try { return base.CreateHost(builder); }
            finally
            {
                foreach (var entry in previous) { Environment.SetEnvironmentVariable(entry.Key, entry.Value); }
            }
        }
    }
}
