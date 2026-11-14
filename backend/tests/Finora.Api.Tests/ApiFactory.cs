using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Finora.Api.Tests;

// Cada teste escolhe explicitamente o ambiente, sem depender do perfil local da máquina.
internal sealed class ApiFactory(string environment) : WebApplicationFactory<Program>
{
    // O host real usa TestServer, sem servidor externo, credenciais ou substitutos de banco.
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(environment);
}
