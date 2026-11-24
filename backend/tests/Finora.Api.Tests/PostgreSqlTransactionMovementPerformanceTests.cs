using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Finora.Api.Tests;

// Mede uma consulta representativa sobre 5.000 movimentos sintéticos sem impor limite de tempo instável à suíte.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlTransactionMovementPerformanceTests
{
    private static readonly Guid OwnerId = Guid.Parse("b93c63ef-d383-46a2-9e20-6b3814d96143");
    private static readonly Guid ConnectionId = Guid.Parse("a9f34cb8-b780-42ba-b2ac-8cd00ba9c786");
    private static readonly Guid CardId = Guid.Parse("27d3308d-e0f7-42ef-9724-d95da83fa2e5");
    private static readonly DateOnly ReferenceDate = new(2026, 11, 23);

    public TestContext TestContext { get; set; } = null!;

    // O conjunto distribui 5.000 movimentos em 90 dias para medir intervalo seletivo, count total e página limitada.
    [TestMethod]
    public async Task CountAndPagePlanAreMeasuredForFiveThousandSyntheticMovements()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedMovementsAsync(fixture.Context);
        await fixture.Context.Database.ExecuteSqlRawAsync(
            "ANALYZE \"FinancialConnection\"; ANALYZE \"CardAccount\"; ANALYZE \"FinancialTransaction\";");

        var capture = new FinancialQueryCommandCapture();
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, settings.ConnectionString);
        options.AddInterceptors(capture);
        await using var queryContext = new FinoraDbContext(options.Options);
        var reader = new TransactionMovementReader(queryContext);
        var from = ReferenceDate.AddDays(-14);
        var toExclusive = ReferenceDate.AddDays(-7);
        var selectiveCriteria = new ValidatedTransactionsQuery(
            OwnerId, ConnectionId, CardId, null, null, from, toExclusive, null, 1, 25);

        // A leitura seletiva mede o índice de cartão/data; a leitura sem filtros mede a página padrão sobre toda a base.
        var selective = await MeasureQueryAsync(reader, capture, settings.ConnectionString, selectiveCriteria);
        Assert.AreEqual(392, selective.FilteredCount);
        Assert.AreEqual(25, selective.PageCount);
        var unfilteredCriteria = new ValidatedTransactionsQuery(
            OwnerId, ConnectionId, null, null, null, null, null, null, 1, 25);
        var unfiltered = await MeasureQueryAsync(reader, capture, settings.ConnectionString, unfilteredCriteria);
        Assert.AreEqual(5_000, unfiltered.FilteredCount);
        Assert.AreEqual(25, unfiltered.PageCount);

        // As medições registram tempos e operadores locais, sem converter uma máquina de teste em SLA de produção.
        TestContext.WriteLine(FormatMeasurement("card_date", selective));
        TestContext.WriteLine(FormatMeasurement("unfiltered", unfiltered));
    }

    // O fixture usa somente identidade e valores financeiros sintéticos, todos ligados à única conexão/cartão próprios.
    private static async Task SeedMovementsAsync(FinoraDbContext context)
    {
        context.Users.Add(new ApplicationUser(OwnerId));
        await context.SaveChangesAsync();
        context.Set<FinancialConnection>().Add(
            FinancialConnection.Create(ConnectionId, OwnerId, "pluggy", "synthetic-performance-item"));
        await context.SaveChangesAsync();
        context.Set<CardAccount>().Add(CardAccount.Create(
            CardId, OwnerId, ConnectionId, "synthetic-performance-account", "BRL"));
        await context.SaveChangesAsync();

        var movements = new List<FinancialTransaction>(capacity: 5_000);
        for (var index = 0; index < 5_000; index++)
        {
            var date = ReferenceDate.AddDays(-(index % 90));
            var facts = new TransactionFacts(
                $"synthetic-performance-{index:D5}",
                "synthetic-performance-account",
                $"movimento sintético {index:D5}",
                1.25m,
                "BRL",
                $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}T12:00:00Z",
                "DEBIT",
                "POSTED",
                "TARIFA");
            var normalized = TransactionNormalizer.Normalize(facts, ReferenceDate);
            movements.Add(FinancialTransaction.Create(
                Guid.NewGuid(), OwnerId, CardId, normalized, "fnr015-performance-synthetic-v1", ReferenceDate));
        }

        await context.Set<FinancialTransaction>().AddRangeAsync(movements);
        await context.SaveChangesAsync();
    }

    // Mede a chamada completa do reader e reexecuta seus SQLs count/página com EXPLAIN ANALYZE.
    private static async Task<ReaderMeasurement> MeasureQueryAsync(
        TransactionMovementReader reader,
        FinancialQueryCommandCapture capture,
        string connectionString,
        ValidatedTransactionsQuery criteria)
    {
        capture.Clear();
        var elapsed = Stopwatch.StartNew();
        var result = await reader.ReadAsync(criteria, CancellationToken.None);
        elapsed.Stop();

        var financialCommands = capture.Commands
            .Where(command => command.CommandText.Contains("\"FinancialTransaction\"", StringComparison.Ordinal))
            .ToArray();
        var countCommand = financialCommands.Single(command =>
            command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase));
        var pageCommand = financialCommands.Single(command =>
            !command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase));
        return new ReaderMeasurement(
            result.FilteredCount,
            result.Items.Count,
            elapsed.Elapsed.TotalMilliseconds,
            await ExplainAsync(connectionString, countCommand),
            await ExplainAsync(connectionString, pageCommand));
    }

    // A saída diferencia índices da contagem e da página e mantém os operadores úteis para a decisão de schema.
    private static string FormatMeasurement(string scenario, ReaderMeasurement measurement) => string.Format(
        CultureInfo.InvariantCulture,
        "FNR015_PERF scenario={0} base_rows=5000 filtered={1} page={2} reader_ms={3:F3} " +
        "count_plan_ms={4:F3} page_plan_ms={5:F3} count_nodes=[{6}] page_nodes=[{7}] " +
        "count_indexes=[{8}] page_indexes=[{9}]",
        scenario,
        measurement.FilteredCount,
        measurement.PageCount,
        measurement.ReaderMilliseconds,
        measurement.CountPlan.ExecutionMilliseconds,
        measurement.PagePlan.ExecutionMilliseconds,
        string.Join(',', measurement.CountPlan.NodeTypes),
        string.Join(',', measurement.PagePlan.NodeTypes),
        string.Join(',', measurement.CountPlan.IndexNames.Distinct()),
        string.Join(',', measurement.PagePlan.IndexNames.Distinct()));

    // EXPLAIN ANALYZE reexecuta apenas os dois SELECT capturados, no mesmo banco efêmero e com os mesmos parâmetros.
    private static async Task<PlanMetrics> ExplainAsync(string connectionString, CapturedCommand captured)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {captured.CommandText}";
        foreach (var capturedParameter in captured.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = capturedParameter.Name;
            parameter.DbType = capturedParameter.DbType;
            parameter.Value = capturedParameter.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var json = (string?)await command.ExecuteScalarAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(json));
        using var document = JsonDocument.Parse(json!);
        var summary = document.RootElement[0];
        var nodeTypes = new List<string>();
        var indexNames = new List<string>();
        CollectPlanNodes(summary.GetProperty("Plan"), nodeTypes, indexNames);
        return new PlanMetrics(
            summary.GetProperty("Execution Time").GetDouble(),
            nodeTypes,
            indexNames);
    }

    // A árvore JSON mantém os operadores e nomes de índice encontrados no plano real do PostgreSQL.
    private static void CollectPlanNodes(JsonElement node, ICollection<string> nodeTypes, ICollection<string> indexNames)
    {
        if (node.TryGetProperty("Node Type", out var nodeType))
        {
            nodeTypes.Add(nodeType.GetString()!);
        }

        if (node.TryGetProperty("Index Name", out var indexName))
        {
            indexNames.Add(indexName.GetString()!);
        }

        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                CollectPlanNodes(child, nodeTypes, indexNames);
            }
        }
    }

    // O interceptador registra somente SQL que toca a tabela medida e preserva os parâmetros sem interpolá-los.
    private sealed class FinancialQueryCommandCapture : DbCommandInterceptor
    {
        private readonly List<CapturedCommand> _commands = [];

        public IReadOnlyList<CapturedCommand> Commands
        {
            get
            {
                lock (_commands)
                {
                    return _commands.ToArray();
                }
            }
        }

        // Cada cenário substitui a lista para parear seus próprios comandos count e página.
        public void Clear()
        {
            lock (_commands)
            {
                _commands.Clear();
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        // A cópia mantém os valores tipados separados do SQL parametrizado observado.
        private void Capture(DbCommand command)
        {
            if (!command.CommandText.Contains("\"FinancialTransaction\"", StringComparison.Ordinal))
            {
                return;
            }

            var captured = new CapturedCommand(
                command.CommandText,
                command.Parameters.Cast<DbParameter>()
                    .Select(parameter => new CapturedParameter(
                        parameter.ParameterName,
                        parameter.DbType,
                        parameter.Value is DBNull ? null : parameter.Value))
                    .ToArray());
            lock (_commands)
            {
                _commands.Add(captured);
            }
        }
    }

    // Capturas independentes mantêm comando e parâmetros imutáveis para repetir EXPLAIN após a leitura medida.
    private sealed record CapturedCommand(string CommandText, IReadOnlyList<CapturedParameter> Parameters);

    private sealed record CapturedParameter(string Name, DbType DbType, object? Value);

    private sealed record PlanMetrics(
        double ExecutionMilliseconds,
        IReadOnlyList<string> NodeTypes,
        IReadOnlyList<string> IndexNames);

    private sealed record ReaderMeasurement(
        int FilteredCount,
        int PageCount,
        double ReaderMilliseconds,
        PlanMetrics CountPlan,
        PlanMetrics PagePlan);
}
