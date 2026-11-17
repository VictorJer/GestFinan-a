using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finora.Infrastructure.Persistence;

// A factory no próprio projeto permite ao dotnet-ef evitar o host HTTP da Finora.Api.
public sealed class FinoraDbContextFactory : IDesignTimeDbContextFactory<FinoraDbContext>
{
    // Lê a conexão somente do ambiente do processo e nunca inclui seu valor em erros ou logs.
    public FinoraDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Finora");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Defina ConnectionStrings__Finora em configuração externa para executar ferramentas do EF Core.");
        }

        // A mesma configuração fornece ao modelo Identity as opções fixas usadas pela factory e pelos testes.
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, connectionString);
        return new FinoraDbContext(optionsBuilder.Options);
    }
}
