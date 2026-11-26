using Finora.Domain.FinancialData;
using Finora.Domain.Transactions;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Captura o snapshot financeiro inteiro para provar que decisões não reescrevem fatos de origem.
internal static class FinancialTransactionSnapshotAssertions
{
    // Lê do banco sem tracking para observar o estado persistido após requests e sincronizações.
    public static async Task<FinancialTransactionSnapshot[]> CaptureAsync(FinoraDbContext context, Guid userId)
    {
        var movements = await context.Set<FinancialTransaction>()
            .AsNoTracking()
            .Where(movement => movement.UserId == userId)
            .OrderBy(movement => movement.Id)
            .ToArrayAsync();

        return movements.Select(FinancialTransactionSnapshot.Capture).ToArray();
    }

    // Compara todos os campos mapeados, inclusive datas, natureza, Status e Situation da origem.
    public static void AssertUnchanged(
        IReadOnlyList<FinancialTransactionSnapshot> expected,
        IReadOnlyList<FinancialTransactionSnapshot> actual)
    {
        CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
    }
}

// O snapshot inclui cada coluna do movimento; não há propriedade independente de competência financeira.
internal sealed record FinancialTransactionSnapshot(
    Guid Id,
    Guid UserId,
    Guid CardAccountId,
    string ExternalId,
    string ExternalAccountId,
    string Description,
    decimal Amount,
    string CurrencyCode,
    string OriginalDate,
    string? Type,
    string? Status,
    string? OperationType,
    int? InstallmentNumber,
    int? TotalInstallments,
    string? PurchaseDate,
    string? BillId,
    string? BillPostDate,
    string? BillForecastDate,
    string? PaymentType,
    DateOnly? TransactionDate,
    TransactionNature Nature,
    TransactionExclusionReason? NatureReason,
    TransactionSituation Situation,
    TransactionExclusionReason? SituationReason,
    TransactionExclusionReason? DateReason,
    string NormalizationRuleVersion,
    DateOnly NormalizationReferenceDate)
{
    // Copia os campos por valor para que mudanças posteriores não alterem a referência de comparação.
    public static FinancialTransactionSnapshot Capture(FinancialTransaction movement) => new(
        movement.Id,
        movement.UserId,
        movement.CardAccountId,
        movement.ExternalId,
        movement.ExternalAccountId,
        movement.Description,
        movement.Amount,
        movement.CurrencyCode,
        movement.OriginalDate,
        movement.Type,
        movement.Status,
        movement.OperationType,
        movement.InstallmentNumber,
        movement.TotalInstallments,
        movement.PurchaseDate,
        movement.BillId,
        movement.BillPostDate,
        movement.BillForecastDate,
        movement.PaymentType,
        movement.TransactionDate,
        movement.Nature,
        movement.NatureReason,
        movement.Situation,
        movement.SituationReason,
        movement.DateReason,
        movement.NormalizationRuleVersion,
        movement.NormalizationReferenceDate);
}
