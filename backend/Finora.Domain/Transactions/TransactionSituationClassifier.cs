using System.Globalization;

namespace Finora.Domain.Transactions;

// Situação do lançamento é uma dimensão independente da natureza e do sinal financeiro.
public enum TransactionSituation
{
    Effective,
    Pending,
    FutureInstallment,
    Unknown
}

/// <summary>Resultado de situação com motivo fixo quando status ou parcela é ambíguo.</summary>
public sealed record TransactionSituationResult(
    TransactionSituation Situation,
    TransactionExclusionReason? ExclusionReason = null);

/// <summary>Interpreta status e metadados opcionais sem fabricar parcelas ou efetivação.</summary>
public static class TransactionSituationClassifier
{
    // Só a combinação explícita de parcela, numeração válida e previsão futura identifica parcela futura.
    public static TransactionSituationResult Classify(TransactionFacts transaction, DateOnly referenceDate)
    {
        if (!HasValidInstallmentMetadata(transaction))
        {
            return Unknown(TransactionExclusionReason.InvalidInstallment);
        }

        var futureInstallment = IsFutureInstallment(transaction, referenceDate);
        if (futureInstallment && !string.Equals(transaction.Status, "PENDING", StringComparison.Ordinal))
        {
            return Unknown(TransactionExclusionReason.UnknownStatus);
        }

        return transaction.Status switch
        {
            "POSTED" => new TransactionSituationResult(TransactionSituation.Effective),
            "PENDING" when futureInstallment =>
                new TransactionSituationResult(TransactionSituation.FutureInstallment,
                    TransactionExclusionReason.FutureInstallment),
            "PENDING" => new TransactionSituationResult(TransactionSituation.Pending,
                TransactionExclusionReason.NotEffective),
            _ => Unknown(TransactionExclusionReason.UnknownStatus)
        };
    }

    // Metadados ausentes são permitidos, mas números presentes precisam ser positivos e coerentes.
    private static bool HasValidInstallmentMetadata(TransactionFacts transaction)
    {
        if (transaction.InstallmentNumber is <= 0 || transaction.TotalInstallments is <= 0)
        {
            return false;
        }

        return transaction.InstallmentNumber is null
            || transaction.TotalInstallments is null
            || transaction.InstallmentNumber <= transaction.TotalInstallments;
    }

    // Previsão de fatura futura sozinha não basta: exige modalidade parcelada e numeração válida.
    private static bool IsFutureInstallment(TransactionFacts transaction, DateOnly referenceDate)
    {
        if (!string.Equals(transaction.PaymentType, "INSTALLMENT", StringComparison.Ordinal)
            || transaction.InstallmentNumber is null
            || transaction.TotalInstallments is null
            || string.IsNullOrWhiteSpace(transaction.BillForecastDate))
        {
            return false;
        }

        var format = transaction.BillForecastDate.Length == 7 ? "yyyy-MM" : "yyyy-MM-dd";
        if (!DateTime.TryParseExact(
                transaction.BillForecastDate,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var forecast))
        {
            return false;
        }

        return forecast.Year > referenceDate.Year
            || forecast.Year == referenceDate.Year && forecast.Month > referenceDate.Month;
    }

    // Razões enumeradas permitem explicar exclusões sem expor os valores recebidos.
    private static TransactionSituationResult Unknown(TransactionExclusionReason reason) =>
        new(TransactionSituation.Unknown, reason);
}
