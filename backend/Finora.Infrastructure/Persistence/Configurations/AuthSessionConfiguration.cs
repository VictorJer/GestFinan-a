using Finora.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finora.Infrastructure.Persistence.Configurations;

// Constraints no banco reforçam invariantes mesmo quando um escritor não passa pela fábrica da entidade.
public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("AuthSessions", table =>
        {
            table.HasCheckConstraint("CK_AuthSessions_Expiration", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
            table.HasCheckConstraint("CK_AuthSessions_Revocation", "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"CreatedAtUtc\"");
        });
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasMaxLength(64).IsRequired();
        builder.Property(session => session.UserId).IsRequired();
        builder.Property(session => session.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.ExpiresAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.RevokedAtUtc).HasColumnType("timestamp with time zone");
        // Restrict impede excluir titular e deixar sessões órfãs; revogação futura usa o índice por usuário.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(session => session.UserId);
    }
}
