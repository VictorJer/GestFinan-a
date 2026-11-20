using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

/// <summary>Contexto único do Identity básico e do grafo financeiro persistido pela Infrastructure.</summary>
public sealed class FinoraDbContext(DbContextOptions<FinoraDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    // Configura o modelo base Identity antes dos mapeamentos financeiros e das sessões de autenticação.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Aplica constraints financeiras/de sessão depois das entidades e relações básicas do Identity.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinoraDbContext).Assembly);
    }

    // Toda gravação síncrona valida entidades financeiras e sessões antes de entregar valores ao provider.
    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    // A sobrecarga completa é a barreira comum também para atualizações, não apenas para objetos recém-criados.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTrackedEntities();
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
        ValidateTrackedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Revalida entidades financeiras e sessões Added/Modified, inclusive valores alterados após as fábricas.
    private void ValidateTrackedEntities()
    {
        ChangeTracker.DetectChanges();

        // Sessões compartilham a barreira pré-provider, sem misturar regras Identity ao Domain financeiro.
        foreach (var entry in ChangeTracker.Entries<AuthSession>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ValidateForPersistence();
        }

        foreach (var entry in ChangeTracker.Entries<FinancialConnection>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ValidateForPersistence();
        }

        // O registro de identidade tem sua própria validação para bloquear dados opacos inválidos antes do provider.
        foreach (var entry in ChangeTracker.Entries<FinancialConnectionItemIdentity>()
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
