using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Centraliza provider, assembly de migrations e opções Identity para criação coerente em todos os contextos.
public static class FinoraDbContextConfiguration
{
    // Configura Npgsql e as opções de modelo sem registrar autenticação HTTP nem abrir a conexão.
    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        IServiceProvider? applicationServiceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Factory e testes usam opções mínimas próprias; a aplicação poderá fornecer seu provider real.
        optionsBuilder.UseApplicationServiceProvider(
            applicationServiceProvider ?? FinoraIdentityModelOptions.DesignTimeServiceProvider);
        optionsBuilder.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsAssembly(typeof(FinoraDbContext).Assembly.GetName().Name));
    }
}
