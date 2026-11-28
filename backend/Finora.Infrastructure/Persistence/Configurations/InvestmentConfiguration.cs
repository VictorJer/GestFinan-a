using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finora.Infrastructure.Persistence.Configurations;

// A carteira referencia somente o titular Identity e não participa do grafo de cartão/importação.
public sealed class InvestmentConfiguration : IEntityTypeConfiguration<Investment>
{
    public void Configure(EntityTypeBuilder<Investment> builder)
    {
        builder.HasKey(investment => investment.Id);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(investment => investment.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(investment => investment.Name).HasMaxLength(Investment.MaximumNameLength).IsRequired();
        builder.Property(investment => investment.Class)
            .HasMaxLength(32)
            .UseCollation("C")
            .IsRequired();
        builder.Property(investment => investment.AppliedAmount).HasColumnType("numeric(19,2)");
        builder.Property(investment => investment.CurrentAmount).HasColumnType("numeric(19,2)");

        // A consulta da carteira filtra pelo titular e conserva ordenação estável pelo UUID.
        builder.HasIndex(investment => new { investment.UserId, investment.Id })
            .HasDatabaseName("IX_Investment_UserId_Id");

        // As constraints protegem gravações diretas além das fábricas e da barreira comum do contexto.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_Investment_IdsNotEmpty",
                $"\"Id\" <> {FinancialDataCheckConstraintSql.EmptyGuid} AND " +
                $"\"UserId\" <> {FinancialDataCheckConstraintSql.EmptyGuid}");
            table.HasCheckConstraint(
                "CK_Investment_Name",
                "length(btrim(\"Name\")) > 0 AND length(\"Name\") <= 120 AND \"Name\" = btrim(\"Name\")");
            table.HasCheckConstraint(
                "CK_Investment_Class",
                "\"Class\" IN ('fixed_income', 'variable_income', 'real_estate_funds')");
            table.HasCheckConstraint(
                "CK_Investment_AmountsNonNegative",
                "\"AppliedAmount\" >= 0 AND \"CurrentAmount\" >= 0");
        });
    }
}
