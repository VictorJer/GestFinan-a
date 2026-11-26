using Finora.Domain.FinancialData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finora.Infrastructure.Persistence.Configurations;

// Relaciona cada candidato ao trio/titular exatos e mantém a decisão vigente em uma tabela própria.
public sealed class RecurrenceCandidateConfiguration : IEntityTypeConfiguration<RecurrenceCandidate>
{
    public void Configure(EntityTypeBuilder<RecurrenceCandidate> builder)
    {
        builder.HasKey(candidate => candidate.Id);
        builder.HasAlternateKey(candidate => new { candidate.Id, candidate.UserId });

        // Cada ocorrência aponta para uma transação e titular já verificados pela chave composta existente.
        builder.HasOne<FinancialTransaction>()
            .WithMany()
            .HasForeignKey(candidate => new { candidate.FirstOccurrenceTransactionId, candidate.UserId })
            .HasPrincipalKey(transaction => new { transaction.Id, transaction.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinancialTransaction>()
            .WithMany()
            .HasForeignKey(candidate => new { candidate.SecondOccurrenceTransactionId, candidate.UserId })
            .HasPrincipalKey(transaction => new { transaction.Id, transaction.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinancialTransaction>()
            .WithMany()
            .HasForeignKey(candidate => new { candidate.ThirdOccurrenceTransactionId, candidate.UserId })
            .HasPrincipalKey(transaction => new { transaction.Id, transaction.UserId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(candidate => candidate.PolicyVersion)
            .HasMaxLength(64)
            .UseCollation("C")
            .IsRequired();
        builder.Property(candidate => candidate.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(candidate => candidate.LastEvaluatedAtUtc).HasColumnType("timestamp with time zone");

        // Uma identidade por titular/versão/trio torna reavaliações e reimportações idempotentes.
        builder.HasIndex(candidate => new
            {
                candidate.UserId,
                candidate.PolicyVersion,
                candidate.FirstOccurrenceTransactionId,
                candidate.SecondOccurrenceTransactionId,
                candidate.ThirdOccurrenceTransactionId
            })
            .IsUnique()
            .HasDatabaseName("UX_RecurrenceCandidate_OwnerPolicyOccurrences");

        // A consulta da lista cobre somente candidatos atuais; os índices seguintes servem à revalidação por movimento.
        builder.HasIndex(candidate => new
            {
                candidate.UserId,
                candidate.PolicyVersion,
                candidate.CreatedAtUtc
            })
            .HasDatabaseName("IX_RecurrenceCandidate_CurrentOwnerPolicyCreated")
            .HasFilter("\"IsCurrent\" = TRUE");
        builder.HasIndex(candidate => new { candidate.FirstOccurrenceTransactionId, candidate.UserId })
            .HasDatabaseName("IX_RecurrenceCandidate_FirstOccurrenceOwner");
        builder.HasIndex(candidate => new { candidate.SecondOccurrenceTransactionId, candidate.UserId })
            .HasDatabaseName("IX_RecurrenceCandidate_SecondOccurrenceOwner");
        builder.HasIndex(candidate => new { candidate.ThirdOccurrenceTransactionId, candidate.UserId })
            .HasDatabaseName("IX_RecurrenceCandidate_ThirdOccurrenceOwner");

        // Constraints SQL também recusam IDs vazios, trio parcial/repetido e versão sem conteúdo.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RecurrenceCandidate_IdsNotEmpty",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"FirstOccurrenceTransactionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"SecondOccurrenceTransactionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ThirdOccurrenceTransactionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "CK_RecurrenceCandidate_OccurrencesDistinct",
                "\"FirstOccurrenceTransactionId\" <> \"SecondOccurrenceTransactionId\" AND " +
                "\"FirstOccurrenceTransactionId\" <> \"ThirdOccurrenceTransactionId\" AND " +
                "\"SecondOccurrenceTransactionId\" <> \"ThirdOccurrenceTransactionId\"");
            table.HasCheckConstraint(
                "CK_RecurrenceCandidate_PolicyVersionRequired",
                "length(btrim(\"PolicyVersion\")) > 0");
            table.HasCheckConstraint(
                "CK_RecurrenceCandidate_EvaluationRange",
                "\"LastEvaluatedAtUtc\" >= \"CreatedAtUtc\"");
        });
    }
}

// A decisão tem chave de titular/candidato própria e nunca é armazenada nos fatos financeiros.
public sealed class RecurrenceDecisionConfiguration : IEntityTypeConfiguration<RecurrenceDecision>
{
    public void Configure(EntityTypeBuilder<RecurrenceDecision> builder)
    {
        builder.HasKey(decision => new { decision.CandidateId, decision.UserId });
        builder.HasOne<RecurrenceCandidate>()
            .WithMany()
            .HasForeignKey(decision => new { decision.CandidateId, decision.UserId })
            .HasPrincipalKey(candidate => new { candidate.Id, candidate.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(decision => decision.Kind).HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(decision => decision.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(decision => new { decision.UserId, decision.CandidateId })
            .HasDatabaseName("IX_RecurrenceDecision_OwnerCandidate");

        // A constraint mantém os valores armazenados compatíveis com as quatro decisões explicitamente aprovadas.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RecurrenceDecision_IdsNotEmpty",
                "\"CandidateId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint(
                "CK_RecurrenceDecision_Kind",
                "\"Kind\" IN ('Subscription', 'Installment', 'IndependentPurchases', 'Rejected')");
        });
    }
}
