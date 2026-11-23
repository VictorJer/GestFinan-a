using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Finora.Api.Tests;

// Cada teste escolhe explicitamente o ambiente, sem depender do perfil local da máquina.
internal sealed class ApiFactory(string environment) : WebApplicationFactory<Program>
{
    // O host de superfície OpenAPI usa TestServer e não herda configuração que conecte ao broker local.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:HostAddress"] = null,
                ["Messaging:RabbitMq:Username"] = null,
                ["Messaging:RabbitMq:Password"] = null,
                ["Messaging:RabbitMq:UseTls"] = null
            }));
    }
}
