using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Mede somente o custo de guardar contratos mínimos sintéticos, sem simular rede, banco ou promoção.
[TestClass]
public sealed class PluggyImportBudgetTests
{
    // Dois cenários exercitam o teto agregado e o limite de 100 páginas de um cartão isolado.
    [TestMethod]
    [DataRow(2, 50)]
    [DataRow(1, 100)]
    public void RetainsFiftyThousandSyntheticTransactionsAcrossCardsAndPages(int cards, int pagesPerCard)
    {
        const int transactionsPerPage = 500;
        var snapshot = new Dictionary<string, PluggyTransactionData>(cards * pagesPerCard * transactionsPerPage);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var started = Stopwatch.GetTimestamp();
        var longestPage = TimeSpan.Zero;

        // Cria e descarta cada payload JSON local, retendo apenas os contratos que uma promoção futura precisaria.
        for (var card = 0; card < cards; card++)
        {
            var accountId = $"synthetic-card-{card}";
            for (var page = 0; page < pagesPerCard; page++)
            {
                var pageStarted = Stopwatch.GetTimestamp();
                var json = BuildSyntheticPage(accountId, page, transactionsPerPage);
                var records = JsonSerializer.Deserialize<PluggyAnalysisTransactionResponse[]>(json, options);
                Assert.IsNotNull(records);
                Assert.HasCount(transactionsPerPage, records);
                foreach (var record in records)
                {
                    Assert.IsTrue(record.TryToContract(out var transaction));
                    Assert.IsNotNull(transaction);
                    snapshot.Add($"{accountId}:{transaction.ExternalId}", transaction);
                }

                var pageDuration = Stopwatch.GetElapsedTime(pageStarted);
                if (pageDuration > longestPage)
                {
                    longestPage = pageDuration;
                }
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        Assert.HasCount(50_000, snapshot);
        Console.WriteLine($"FNR-012 orçamento sintético: cartões={cards}; páginas/cartão={pagesPerCard}; movimentos={snapshot.Count}; memória retida={retained} bytes; tempo total={elapsed.TotalMilliseconds:F0} ms; maior página={longestPage.TotalMilliseconds:F0} ms; limites HTTP planejados=30 s/chamada e 120 s/execução (não exercitados sem rede).");
        GC.KeepAlive(snapshot);
    }

    // O gerador usa apenas campos mínimos inventados e nunca lê configuração ou dados bancários.
    private static string BuildSyntheticPage(string accountId, int page, int count)
    {
        var json = new StringBuilder(count * 240);
        json.Append('[');
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append("{\"id\":\"synthetic-").Append(page).Append('-').Append(index)
                .Append("\",\"accountId\":\"").Append(accountId)
                .Append("\",\"description\":\"Fixture sintética\",\"amount\":12.3456,\"currencyCode\":\"BRL\",\"date\":\"2026-11-01\",\"type\":\"DEBIT\",\"status\":\"POSTED\",\"operationType\":\"OUTROS\"}");
        }

        return json.Append(']').ToString();
    }
}
