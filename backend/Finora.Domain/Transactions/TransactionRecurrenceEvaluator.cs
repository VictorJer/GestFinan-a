using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Finora.Domain.FinancialData;

namespace Finora.Domain.Transactions;

/// <summary>Resultado puro de um trio que satisfaz a política de recorrência assistida.</summary>
public sealed record TransactionRecurrenceCandidate(
    Guid UserId,
    Guid CardAccountId,
    string CurrencyCode,
    string PolicyVersion,
    IReadOnlyList<Guid> OccurrenceTransactionIds);

/// <summary>Avalia trios mensais sem persistir, alterar transações ou inferir natureza financeira.</summary>
public static class TransactionRecurrenceEvaluator
{
    /// <summary>Versão imutável dos limites aprovados para esta avaliação.</summary>
    public const string PolicyVersion = "recurrence-v1";

    private const decimal MaximumRelativeAmountDeviation = 0.01m;
    private const int MaximumDayDeviation = 3;

    /// <summary>Localiza todos os trios distintos que atendem aos predicados da política aprovada.</summary>
    public static IReadOnlyList<TransactionRecurrenceCandidate> Evaluate(
        IEnumerable<FinancialTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var occurrences = transactions
            .Select(TryCreateOccurrence)
            .Where(occurrence => occurrence is not null)
            .Select(occurrence => occurrence!)
            .GroupBy(occurrence => new OccurrenceIdentity(
                occurrence.Transaction.UserId,
                occurrence.Transaction.CardAccountId,
                occurrence.Transaction.ExternalId))
            .SelectMany(DeduplicateIdentity)
            .ToArray();

        var candidates = new List<TransactionRecurrenceCandidate>();
        foreach (var pattern in occurrences.GroupBy(occurrence => new PatternIdentity(
                     occurrence.Transaction.UserId,
                     occurrence.Transaction.CardAccountId,
                     occurrence.Transaction.CurrencyCode,
                     occurrence.NormalizedDescription,
                     Math.Sign(occurrence.Transaction.Amount))))
        {
            // As janelas usam meses-calendário consecutivos e não reutilizam uma ocorrência no mesmo trio.
            var occurrencesByMonth = pattern
                .GroupBy(occurrence => GetMonthIndex(occurrence.Date))
                .ToDictionary(
                    month => month.Key,
                    month => month.OrderBy(occurrence => occurrence.Transaction.Id).ToArray());

            foreach (var firstMonth in occurrencesByMonth.Keys.OrderBy(month => month))
            {
                if (!occurrencesByMonth.TryGetValue(firstMonth + 1, out var secondMonth)
                    || !occurrencesByMonth.TryGetValue(firstMonth + 2, out var thirdMonth))
                {
                    continue;
                }

                foreach (var first in occurrencesByMonth[firstMonth])
                {
                    foreach (var second in secondMonth)
                    {
                        foreach (var third in thirdMonth)
                        {
                            if (!AmountMatches(first, second, third)
                                || !DayMatches(first, second, third))
                            {
                                continue;
                            }

                            candidates.Add(new TransactionRecurrenceCandidate(
                                first.Transaction.UserId,
                                first.Transaction.CardAccountId,
                                first.Transaction.CurrencyCode,
                                PolicyVersion,
                                Array.AsReadOnly(new[]
                                {
                                    first.Transaction.Id,
                                    second.Transaction.Id,
                                    third.Transaction.Id
                                })));
                        }
                    }
                }
            }
        }

        // Ordenar a saída torna o resultado estável mesmo quando a origem entrega movimentos em outra ordem.
        return new ReadOnlyCollection<TransactionRecurrenceCandidate>(candidates
            .OrderBy(candidate => candidate.UserId)
            .ThenBy(candidate => candidate.CardAccountId)
            .ThenBy(candidate => candidate.CurrencyCode, StringComparer.Ordinal)
            .ThenBy(candidate => string.Join(",", candidate.OccurrenceTransactionIds), StringComparer.Ordinal)
            .ToArray());
    }

    // Somente fatos completos, não zero e efetivos podem formar uma ocorrência observada da política v1.
    private static Occurrence? TryCreateOccurrence(FinancialTransaction transaction)
    {
        if (transaction.Situation != TransactionSituation.Effective
            || transaction.TransactionDate is not { } date
            || transaction.Amount == 0
            || string.IsNullOrWhiteSpace(transaction.ExternalId)
            || string.IsNullOrWhiteSpace(transaction.CurrencyCode)
            || !TryNormalizeDescription(transaction.Description, out var description))
        {
            return null;
        }

        return new Occurrence(transaction, date, description);
    }

    // Cópias da mesma identidade contam uma vez; fatos conflitantes para a chave contextual falham fechados.
    private static IEnumerable<Occurrence> DeduplicateIdentity(IGrouping<OccurrenceIdentity, Occurrence> group)
    {
        var ordered = group.OrderBy(occurrence => occurrence.Transaction.Id).ToArray();
        var first = ordered[0];
        if (ordered.Skip(1).Any(occurrence => !HasSameEvaluationFacts(first, occurrence)))
        {
            return Array.Empty<Occurrence>();
        }

        return new[] { first };
    }

    // A repetição só é equivalente quando os campos usados pela regra permanecem iguais.
    private static bool HasSameEvaluationFacts(Occurrence left, Occurrence right) =>
        left.Transaction.Amount == right.Transaction.Amount
        && string.Equals(left.Transaction.CurrencyCode, right.Transaction.CurrencyCode, StringComparison.Ordinal)
        && left.Transaction.Situation == right.Transaction.Situation
        && left.Date == right.Date
        && string.Equals(left.NormalizedDescription, right.NormalizedDescription, StringComparison.Ordinal);

    // Cada magnitude deve permanecer até um por cento da mediana e ter direção não nula em comum.
    private static bool AmountMatches(Occurrence first, Occurrence second, Occurrence third)
    {
        var magnitudes = new[]
        {
            Math.Abs(first.Transaction.Amount),
            Math.Abs(second.Transaction.Amount),
            Math.Abs(third.Transaction.Amount)
        };
        Array.Sort(magnitudes);
        var median = magnitudes[1];
        if (median == 0)
        {
            return false;
        }

        var maximumDeviation = median * MaximumRelativeAmountDeviation;
        return magnitudes.All(magnitude => Math.Abs(magnitude - median) <= maximumDeviation);
    }

    // O fim de cada mês é alinhado ao dia 31 antes de comparar a janela civil de três datas.
    private static bool DayMatches(Occurrence first, Occurrence second, Occurrence third)
    {
        var days = new[]
        {
            GetNormalizedDay(first.Date),
            GetNormalizedDay(second.Date),
            GetNormalizedDay(third.Date)
        };
        Array.Sort(days);
        var median = days[1];
        return days.All(day => Math.Abs(day - median) <= MaximumDayDeviation);
    }

    // Descrições são comparadas sem alterar a origem e sem descartar números ou acentos.
    private static bool TryNormalizeDescription(string description, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        string canonical;
        try
        {
            canonical = description.Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        var builder = new StringBuilder(canonical.Length);
        var pendingSeparator = false;
        foreach (var rune in canonical.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || IsPunctuation(Rune.GetUnicodeCategory(rune)))
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                builder.Append(' ');
                pendingSeparator = false;
            }

            builder.Append(rune.ToString());
        }

        normalized = builder.ToString();
        return normalized.Length > 0;
    }

    // Categorias de pontuação Unicode viram separadores; símbolos e marcas permanecem intactos.
    private static bool IsPunctuation(UnicodeCategory category) => category is
        UnicodeCategory.ConnectorPunctuation
        or UnicodeCategory.DashPunctuation
        or UnicodeCategory.OpenPunctuation
        or UnicodeCategory.ClosePunctuation
        or UnicodeCategory.InitialQuotePunctuation
        or UnicodeCategory.FinalQuotePunctuation
        or UnicodeCategory.OtherPunctuation;

    // Dias finais de qualquer mês compartilham o mesmo marcador para acomodar fevereiro e anos bissextos.
    private static int GetNormalizedDay(DateOnly date) =>
        date.Day == DateTime.DaysInMonth(date.Year, date.Month) ? 31 : date.Day;

    // O índice inteiro permite testar continuidade de mês sem depender de duração em dias.
    private static int GetMonthIndex(DateOnly date) => date.Year * 12 + date.Month;

    // A ocorrência carrega somente o snapshot e a chave textual derivada para comparação pura.
    private sealed record Occurrence(
        FinancialTransaction Transaction,
        DateOnly Date,
        string NormalizedDescription);

    // A chave contextual impede que a mesma origem seja contada mais de uma vez por titular/cartão.
    private readonly record struct OccurrenceIdentity(Guid UserId, Guid CardAccountId, string ExternalId);

    // A chave de padrão fixa as dimensões que precisam coincidir antes das tolerâncias.
    private readonly record struct PatternIdentity(
        Guid UserId,
        Guid CardAccountId,
        string CurrencyCode,
        string NormalizedDescription,
        int Sign);
}
