using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finora.Infrastructure.Persistence.Configurations;

// Centraliza a representação PostgreSQL do UUID vazio usado exclusivamente como escopo de categoria de sistema.
internal static class FinancialDataCheckConstraintSql
{
    internal const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'::uuid";

    // O limite de índice conta bytes UTF-8 e não aplica trim, transliteração ou truncamento ao conteúdo.
    internal static string ExternalIdMaximumUtf8Bytes(string propertyName) =>
        $"octet_length(convert_to(\"{propertyName}\", 'UTF8')) <= 1024";

    // Uma só configuração mantém IDs externos como text, com comparação determinística e sensível a bytes.
    internal static void ConfigureExternalIdentity(PropertyBuilder<string> property)
    {
        property.HasColumnType("text").UseCollation("C").IsRequired();
    }
}

// Impede que a identidade reservada de escopo seja usada como titular Identity real.
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("AspNetUsers", table => table.HasCheckConstraint(
            "CK_AspNetUsers_Id_NotEmpty",
            $"\"Id\" <> {FinancialDataCheckConstraintSql.EmptyGuid}"));
    }
}

// A conexão é a raiz financeira e conserva vínculo obrigatório com um usuário existente.
public sealed class FinancialConnectionConfiguration : IEntityTypeConfiguration<FinancialConnection>
{
    public void Configure(EntityTypeBuilder<FinancialConnection> builder)
    {
        builder.HasKey(connection => connection.Id);
        builder.HasAlternateKey(connection => new { connection.Id, connection.UserId });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(connection => connection.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        FinancialDataCheckConstraintSql.ConfigureExternalIdentity(builder.Property(connection => connection.ExternalItemId));
        builder.Property(connection => connection.Provider)
            .HasMaxLength(32)
            .UseCollation("C")
            .IsRequired();
        builder.Property(connection => connection.SourceStatus).HasColumnType("text");
        builder.Property(connection => connection.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(connection => connection.LastAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(connection => connection.LastSuccessfulImportAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(connection => connection.SourceUpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(connection => new { connection.UserId, connection.Provider, connection.ExternalItemId })
            .IsUnique();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_FinancialConnection_Ids_NotEmpty",
                $"\"Id\" <> {FinancialDataCheckConstraintSql.EmptyGuid} AND " +
                $"\"UserId\" <> {FinancialDataCheckConstraintSql.EmptyGuid}");
            table.HasCheckConstraint(
                "CK_FinancialConnection_ExternalItemId_MaxUtf8Bytes",
                FinancialDataCheckConstraintSql.ExternalIdMaximumUtf8Bytes(nameof(FinancialConnection.ExternalItemId)));
        });
    }
}

// Cartão só pode apontar para conexão cujo titular coincide com o UserId persistido no próprio cartão.
public sealed class CardAccountConfiguration : IEntityTypeConfiguration<CardAccount>
{
    public void Configure(EntityTypeBuilder<CardAccount> builder)
    {
        builder.HasKey(account => account.Id);
        builder.HasAlternateKey(account => new { account.Id, account.UserId, account.ExternalId });
        builder.HasOne<FinancialConnection>()
            .WithMany()
            .HasForeignKey(account => new { account.ConnectionId, account.UserId })
            .HasPrincipalKey(connection => new { connection.Id, connection.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        FinancialDataCheckConstraintSql.ConfigureExternalIdentity(builder.Property(account => account.ExternalId));
        builder.Property(account => account.Name).HasColumnType("text");
        builder.Property(account => account.LastFour).HasColumnType("text");
        builder.Property(account => account.CurrencyCode).HasColumnType("text").IsRequired();
        builder.Property(account => account.AvailableCreditLimit).HasColumnType("numeric(19,4)");
        builder.Property(account => account.TotalCreditLimit).HasColumnType("numeric(19,4)");
        builder.HasIndex(account => new { account.ConnectionId, account.ExternalId }).IsUnique();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CardAccount_Ids_NotEmpty",
                $"\"Id\" <> {FinancialDataCheckConstraintSql.EmptyGuid} AND " +
                $"\"UserId\" <> {FinancialDataCheckConstraintSql.EmptyGuid}");
            table.HasCheckConstraint(
                "CK_CardAccount_ExternalId_MaxUtf8Bytes",
                FinancialDataCheckConstraintSql.ExternalIdMaximumUtf8Bytes(nameof(CardAccount.ExternalId)));
        });
    }
}

// Movimento referencia o cartão, titular e conta externa como um único vínculo verificável pelo banco.
public sealed class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.HasKey(transaction => transaction.Id);
        builder.HasAlternateKey(transaction => new { transaction.Id, transaction.UserId });
        builder.HasOne<CardAccount>()
            .WithMany()
            .HasForeignKey(transaction => new
            {
                transaction.CardAccountId,
                transaction.UserId,
                transaction.ExternalAccountId
            })
            .HasPrincipalKey(account => new { account.Id, account.UserId, account.ExternalId })
            .OnDelete(DeleteBehavior.Restrict);
        FinancialDataCheckConstraintSql.ConfigureExternalIdentity(builder.Property(transaction => transaction.ExternalId));
        FinancialDataCheckConstraintSql.ConfigureExternalIdentity(builder.Property(transaction => transaction.ExternalAccountId));
        builder.Property(transaction => transaction.Description).HasColumnType("text").IsRequired();
        builder.Property(transaction => transaction.Amount).HasColumnType("numeric(19,4)");
        builder.Property(transaction => transaction.CurrencyCode).HasColumnType("text").IsRequired();
        builder.Property(transaction => transaction.OriginalDate).HasColumnType("text").IsRequired();
        builder.Property(transaction => transaction.Type).HasColumnType("text");
        builder.Property(transaction => transaction.Status).HasColumnType("text");
        builder.Property(transaction => transaction.OperationType).HasColumnType("text");
        builder.Property(transaction => transaction.PurchaseDate).HasColumnType("text");
        builder.Property(transaction => transaction.BillId).HasColumnType("text");
        builder.Property(transaction => transaction.BillPostDate).HasColumnType("text");
        builder.Property(transaction => transaction.BillForecastDate).HasColumnType("text");
        builder.Property(transaction => transaction.PaymentType).HasColumnType("text");
        builder.Property(transaction => transaction.TransactionDate).HasColumnType("date");
        builder.Property(transaction => transaction.Nature).HasConversion<string>().HasColumnType("text");
        builder.Property(transaction => transaction.NatureReason).HasConversion<string>().HasColumnType("text");
        builder.Property(transaction => transaction.Situation).HasConversion<string>().HasColumnType("text");
        builder.Property(transaction => transaction.SituationReason).HasConversion<string>().HasColumnType("text");
        builder.Property(transaction => transaction.DateReason).HasConversion<string>().HasColumnType("text");
        builder.Property(transaction => transaction.NormalizationRuleVersion).HasColumnType("text").IsRequired();
        builder.Property(transaction => transaction.NormalizationReferenceDate).HasColumnType("date");
        builder.HasIndex(transaction => new { transaction.CardAccountId, transaction.ExternalId }).IsUnique();
        builder.HasIndex(transaction => new
        {
            transaction.CardAccountId,
            transaction.TransactionDate,
            transaction.Id
        });

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_FinancialTransaction_Ids_NotEmpty",
                $"\"Id\" <> {FinancialDataCheckConstraintSql.EmptyGuid} AND " +
                $"\"UserId\" <> {FinancialDataCheckConstraintSql.EmptyGuid}");
            table.HasCheckConstraint(
                "CK_FinancialTransaction_ExternalId_MaxUtf8Bytes",
                FinancialDataCheckConstraintSql.ExternalIdMaximumUtf8Bytes(nameof(FinancialTransaction.ExternalId)));
            table.HasCheckConstraint(
                "CK_FinancialTransaction_ExternalAccountId_MaxUtf8Bytes",
                FinancialDataCheckConstraintSql.ExternalIdMaximumUtf8Bytes(nameof(FinancialTransaction.ExternalAccountId)));
        });
    }
}

// Categoria pessoal usa o titular como escopo; a categoria de sistema usa o UUID vazio sem titular fictício.
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(category => category.Id);
        builder.HasAlternateKey(category => new { category.Id, category.ScopeId });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(category => category.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(category => category.Name).HasMaxLength(60).IsRequired();
        builder.Property(category => category.NormalizedName)
            .HasMaxLength(60)
            .UseCollation("C")
            .IsRequired();
        builder.HasIndex(category => new { category.ScopeId, category.NormalizedName }).IsUnique();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_Category_Id_NotEmpty",
                $"\"Id\" <> {FinancialDataCheckConstraintSql.EmptyGuid}");
            table.HasCheckConstraint(
                "CK_Category_SystemOwnershipAndScope",
                $"((\"IsSystem\" = TRUE AND \"UserId\" IS NULL AND \"ScopeId\" = {FinancialDataCheckConstraintSql.EmptyGuid}) OR " +
                $"(\"IsSystem\" = FALSE AND \"UserId\" IS NOT NULL AND " +
                $"\"UserId\" <> {FinancialDataCheckConstraintSql.EmptyGuid} AND \"ScopeId\" = \"UserId\"))");
        });
    }
}

// A anotação é única por movimento, sempre do mesmo titular, e só aceita escopo de sistema ou do próprio usuário.
public sealed class TransactionAnnotationConfiguration : IEntityTypeConfiguration<TransactionAnnotation>
{
    public void Configure(EntityTypeBuilder<TransactionAnnotation> builder)
    {
        builder.HasKey(annotation => annotation.TransactionId);
        builder.HasOne<FinancialTransaction>()
            .WithMany()
            .HasForeignKey(
                nameof(TransactionAnnotation.TransactionId),
                nameof(TransactionAnnotation.UserId))
            .HasPrincipalKey(
                nameof(FinancialTransaction.Id),
                nameof(FinancialTransaction.UserId))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(annotation => new { annotation.CategoryId, annotation.CategoryScopeId })
            .HasPrincipalKey(category => new { category.Id, category.ScopeId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(annotation => annotation.Note).HasMaxLength(500);
        builder.Property(annotation => annotation.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        // O índice composto convencional da FK começa por CategoryId e atende filtros simples por categoria.

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_TransactionAnnotation_CategoryAndScope",
            $"((\"CategoryId\" IS NULL AND \"CategoryScopeId\" IS NULL) OR " +
            $"(\"CategoryId\" IS NOT NULL AND \"CategoryScopeId\" IS NOT NULL AND " +
            $"(\"CategoryScopeId\" = \"UserId\" OR " +
            $"\"CategoryScopeId\" = {FinancialDataCheckConstraintSql.EmptyGuid})))"));
    }
}

// Execução referencia conexão e titular em conjunto, sem permitir exclusão em cascata do histórico.
public sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRun>
{
    public void Configure(EntityTypeBuilder<SyncRun> builder)
    {
        builder.HasKey(run => run.Id);
        builder.HasOne<FinancialConnection>()
            .WithMany()
            .HasForeignKey(run => new { run.ConnectionId, run.UserId })
            .HasPrincipalKey(connection => new { connection.Id, connection.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(run => run.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(run => run.StartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(run => run.FinishedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(run => run.RequestedFrom).HasColumnType("date");
        builder.Property(run => run.RequestedToExclusive).HasColumnType("date");
        builder.Property(run => run.ObservedFrom).HasColumnType("date");
        builder.Property(run => run.ObservedTo).HasColumnType("date");
        builder.Property(run => run.State).HasConversion<string>().HasColumnType("text");
        builder.Property(run => run.CoverageState).HasConversion<string>().HasColumnType("text");
        builder.Property(run => run.FailureCode).HasColumnType("text");
        builder.HasIndex(run => new { run.ConnectionId, run.State });

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_SyncRun_Ids_NotEmpty",
            $"\"Id\" <> {FinancialDataCheckConstraintSql.EmptyGuid} AND " +
            $"\"UserId\" <> {FinancialDataCheckConstraintSql.EmptyGuid}"));
    }
}
