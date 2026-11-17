using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

/// <summary>Contexto único do Identity básico e do grafo financeiro persistido pela Infrastructure.</summary>
public sealed class FinoraDbContext(DbContextOptions<FinoraDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    // O modelo base Identity é configurado antes dos mapeamentos financeiros adicionados em seguida na FNR-007.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Aplica as constraints financeiras somente depois das entidades e relações básicas do Identity.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinoraDbContext).Assembly);
    }

    // Toda gravação síncrona valida entidades rastreadas antes de entregar valores monetários ao provider.
    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    // A sobrecarga completa é a barreira comum também para atualizações, não apenas para objetos recém-criados.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTrackedFinancialEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    // A chamada assíncrona sem opções preserva o mesmo fluxo de validação prévia.
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    // A sobrecarga completa impede que caminho async contorne as invariantes pré-provider.
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidateTrackedFinancialEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Entidades Added/Modified são revalidadas após o tracking, inclusive valores alterados após as fábricas do Domain.
    private void ValidateTrackedFinancialEntities()
    {
        ChangeTracker.DetectChanges();

        foreach (var entry in ChangeTracker.Entries<FinancialConnection>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ValidateForPersistence();
        }

        foreach (var entry in ChangeTracker.Entries<CardAccount>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ValidateForPersistence();
        }

        foreach (var entry in ChangeTracker.Entries<FinancialTransaction>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ValidateForPersistence();
        }

        foreach (var entry in ChangeTracker.Entries<TransactionAnnotation>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ValidateForPersistence();
        }

        foreach (var entry in ChangeTracker.Entries<SyncRun>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ValidateForPersistence();
        }
    }
}
