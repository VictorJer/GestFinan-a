using System.Net;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

/// <summary>Valida o banco de integração usando exclusivamente o PostgreSQL descartável local.</summary>
// Os testes compartilham temporariamente variáveis de processo; serializá-los evita interferência.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlPersistenceTests
{
    // O teste exige uma variável própria, sem recorrer à connection string usada pelo runtime.
    private const string TestConnectionStringVariable =
        DisposablePostgresTestConfiguration.ConnectionStringVariable;
    internal const string ExpectedDatabase = "finora_integration_test";
    internal const string SmokeMigrationId = "20261116175137_Fnr006PersistenceSmoke";
    internal const string CoreMigrationId = "20261117193137_Fnr007CorePersistence";

    // Uma configuração ausente é rejeitada antes de qualquer tentativa de abrir conexão.
    [TestMethod]
    public void MissingTestConnectionIsRejectedBeforeConnection()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DisposablePostgresTestConfiguration.Validate(null));

        StringAssert.Contains(exception.Message, TestConnectionStringVariable);
    }

    // Uma configuração de runtime presente não pode substituir a variável dedicada dos testes.
    [TestMethod]
    public void MissingTestVariableDoesNotFallbackToRuntimeConnection()
    {
        const string runtimeConnectionVariable = "ConnectionStrings__Finora";
        var previousTestConnection = Environment.GetEnvironmentVariable(TestConnectionStringVariable);
        var previousRuntimeConnection = Environment.GetEnvironmentVariable(runtimeConnectionVariable);

        try
        {
            Environment.SetEnvironmentVariable(TestConnectionStringVariable, null);
            Environment.SetEnvironmentVariable(
                runtimeConnectionVariable,
                "Host=127.0.0.1;Port=5432;Database=finora_local;Username=finora;Password=unused");

            var exception = Assert.Throws<InvalidOperationException>(
                DisposablePostgresTestConfiguration.ReadRequired);

            StringAssert.Contains(exception.Message, TestConnectionStringVariable);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestConnectionStringVariable, previousTestConnection);
            Environment.SetEnvironmentVariable(runtimeConnectionVariable, previousRuntimeConnection);
        }
    }

    // Um host remoto é recusado pela validação local sem resolução DNS ou acesso ao servidor.
    [TestMethod]
    public void NonLoopbackHostIsRejectedBeforeConnection()
    {
        const string connectionString =
            "Host=database.example.invalid;Port=5432;Database=finora_integration_test;Username=finora_test;Password=unused";

        var exception = Assert.Throws<InvalidOperationException>(
            () => DisposablePostgresTestConfiguration.Validate(connectionString));

        StringAssert.Contains(exception.Message, "loopback");
    }

    // Um banco com outro nome é recusado mesmo que esteja no loopback, protegendo o volume local.
    [TestMethod]
    public void UnexpectedDatabaseIsRejectedBeforeConnection()
    {
        const string connectionString =
            "Host=127.0.0.1;Port=5432;Database=finora_local;Username=finora_test;Password=unused";

        var exception = Assert.Throws<InvalidOperationException>(
            () => DisposablePostgresTestConfiguration.Validate(connectionString));

        StringAssert.Contains(exception.Message, ExpectedDatabase);
    }

    // O teste histórico parte do schema vazio e aplica somente o alvo FNR-006, sem avançar para migrations futuras.
    [TestMethod]
    public async Task DisposableDatabaseAppliesOnlyTheExplicitSmokeTargetFromEmptySchema()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        var context = fixture.Context;

        await AssertDatabaseIsEmptyAsync(context);
        await context.Database.MigrateAsync(SmokeMigrationId);

        Assert.AreEqual(ExpectedDatabase, context.Database.GetDbConnection().Database);
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        CollectionAssert.AreEquivalent(new[] { SmokeMigrationId }, appliedMigrations);

        var publicTables = await ReadPublicTablesAsync(context);
        CollectionAssert.AreEquivalent(new[] { "__EFMigrationsHistory" }, publicTables);
    }

    // Confirma ausência de tabelas/histórico antes da migration smoke no banco descartável já validado.
    internal static async Task AssertDatabaseIsEmptyAsync(FinoraDbContext context)
    {
        // Ausência de tabelas comprova base vazia sem consultar __EFMigrationsHistory antes de sua criação.
        var publicTables = await ReadPublicTablesAsync(context);
        Assert.AreEqual(0, publicTables.Length);
    }

    // Consulta apenas metadados do schema public para detectar tabelas financeiras indevidas.
    internal static async Task<string[]> ReadPublicTablesAsync(FinoraDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables.ToArray();
    }
}

// Mantém o acesso do teste preso ao único banco descartável autorizado.
internal static class DisposablePostgresTestConfiguration
{
    // O nome do database é parte da barreira contra o banco local persistente e a Neon.
    private const string ExpectedDatabase = PostgreSqlPersistenceTests.ExpectedDatabase;
    internal const string ConnectionStringVariable = "FINORA_TEST_CONNECTION_STRING";

    // Lê apenas a variável dedicada aos testes, sem fallback para a configuração da API/EF.
    public static NpgsqlConnectionStringBuilder ReadRequired()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        return Validate(connectionString);
    }

    // Valida destino e database em memória, antes que qualquer código de conexão seja criado.
    public static NpgsqlConnectionStringBuilder Validate(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Defina FINORA_TEST_CONNECTION_STRING para o serviço PostgreSQL descartável local.");
        }

        NpgsqlConnectionStringBuilder settings;
        try
        {
            settings = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "FINORA_TEST_CONNECTION_STRING não contém uma connection string válida.");
        }

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            throw new InvalidOperationException(
                "O PostgreSQL de integração precisa usar um único host de loopback.");
        }

        var hosts = settings.Host.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length != 1 || !IsLoopbackHost(hosts[0]))
        {
            throw new InvalidOperationException(
                "O PostgreSQL de integração precisa usar um único host de loopback.");
        }

        if (!string.Equals(settings.Database, ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"O PostgreSQL de integração precisa usar o database {ExpectedDatabase}.");
        }

        return settings;
    }

    // Aceita endereços IP de loopback e localhost, sem permitir nomes de hosts remotos.
    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out var address)
            && IPAddress.IsLoopback(address);
    }
}
