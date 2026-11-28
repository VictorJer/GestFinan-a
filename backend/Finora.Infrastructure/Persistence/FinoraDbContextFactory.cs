using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finora.Infrastructure.Persistence;

// A factory no próprio projeto permite ao dotnet-ef evitar o host HTTP da Finora.Api.
public sealed class FinoraDbContextFactory : IDesignTimeDbContextFactory<FinoraDbContext>
{
    private const string TestConnectionArgument = "--test-connection";

    // O modo de migration em testes só aceita a variável dedicada; nunca herda a conexão runtime como fallback.
    public FinoraDbContext CreateDbContext(string[] args)
    {
        var connectionVariable = args.Contains(TestConnectionArgument, StringComparer.Ordinal)
            ? "FINORA_TEST_CONNECTION_STRING"
            : "ConnectionStrings__Finora";
        var connectionString = Environment.GetEnvironmentVariable(connectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Defina {connectionVariable} em configuração externa para executar ferramentas do EF Core.");
        }

        // A mesma configuração fornece ao modelo Identity as opções fixas usadas pela factory e pelos testes.
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, connectionString);
        return new FinoraDbContext(optionsBuilder.Options);
    }
}
