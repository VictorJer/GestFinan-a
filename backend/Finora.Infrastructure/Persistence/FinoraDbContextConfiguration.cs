using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

// Centraliza provider e assembly de migrations para manter criação em design-time e runtime coerentes.
internal static class FinoraDbContextConfiguration
{
    // Configura Npgsql sem registrar serviços ou iniciar a API.
    public static void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        optionsBuilder.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsAssembly(typeof(FinoraDbContext).Assembly.GetName().Name));
    }
}
