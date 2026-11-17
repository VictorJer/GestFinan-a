using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Api.Tests;

// Encapsula conexão e reset do schema, exigindo a allowlist de destino antes de qualquer acesso destrutivo.
internal sealed class DisposablePostgresMigrationFixture : IAsyncDisposable
{
    private readonly string _validatedConnectionString;

    private DisposablePostgresMigrationFixture(FinoraDbContext context, string validatedConnectionString)
    {
        Context = context;
        _validatedConnectionString = validatedConnectionString;
    }

    public FinoraDbContext Context { get; }

    // Valida FINORA_TEST_CONNECTION_STRING antes de configurar o contexto usado pela fixture.
    public static DisposablePostgresMigrationFixture CreateValidated()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var optionsBuilder = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(optionsBuilder, settings.ConnectionString);

        return new DisposablePostgresMigrationFixture(
            new FinoraDbContext(optionsBuilder.Options),
            settings.ConnectionString);
    }

    // Limpa somente public dentro de uma transação, após revalidar host/database e confirmar o destino conectado.
    public async Task ResetToEmptySchemaAsync()
    {
        var settings = DisposablePostgresTestConfiguration.Validate(_validatedConnectionString);
        var databaseBeforeConnect = Context.Database.GetDbConnection().Database;
        if (!string.Equals(settings.Database, PostgreSqlPersistenceTests.ExpectedDatabase, StringComparison.Ordinal)
            || !string.Equals(databaseBeforeConnect, PostgreSqlPersistenceTests.ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"O reset de migrations só é permitido em {PostgreSqlPersistenceTests.ExpectedDatabase}.");
        }

        await using var transaction = await Context.Database.BeginTransactionAsync();
        var connectedDatabase = Context.Database.GetDbConnection().Database;
        if (!string.Equals(connectedDatabase, PostgreSqlPersistenceTests.ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A conexão aberta não corresponde ao banco descartável {PostgreSqlPersistenceTests.ExpectedDatabase}.");
        }

        // Recria apenas o schema público do banco descartável; a transação restaura-o se qualquer passo falhar.
        await Context.Database.ExecuteSqlRawAsync("DROP SCHEMA public CASCADE;");
        await Context.Database.ExecuteSqlRawAsync("CREATE SCHEMA public;");
        await transaction.CommitAsync();
    }

    // Descarta contexto/conexão sem limpar automaticamente o banco ao final do teste.
    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
