using Finora.Application.FinancialData;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Infrastructure.Persistence;

/// <summary>Registra adapters EF financeiros e de SyncRun, sem abrir conexão ou aplicar migrations no startup.</summary>
public static class SyncRunAcceptanceServiceCollectionExtensions
{
    // O DbContext já é configurado pela composição HTTP; os adapters não abrem conexão nem aplicam migration.
    public static IServiceCollection AddFinoraSyncRunAcceptancePersistence(this IServiceCollection services)
    {
        services.AddScoped<ISyncRunAcceptanceStore, SyncRunAcceptanceStore>();
        services.AddScoped<ICurrentSyncConnectionReader, SyncRunCurrentConnectionReader>();
        services.AddScoped<ICurrentConnectionSyncStateReader, CurrentConnectionSyncStateReader>();
        services.AddScoped<ICurrentCardsReader, CurrentCardsReader>();
        services.AddScoped<ITransactionCardOwnershipReader, TransactionCardOwnershipReader>();
        services.AddScoped<ITransactionMovementReader, TransactionMovementReader>();
        // A recorrência persiste candidatos/decisões à parte; nenhum endpoint é exposto por este registro.
        services.AddScoped<ITransactionRecurrenceStore, TransactionRecurrenceStore>();
        // A leitura agregada é somente leitura e compartilha o contexto scoped da conexão autenticada.
        services.AddScoped<ITransactionDashboardReader, TransactionDashboardReader>();
        // A edição usa o mesmo contexto scoped e mantém autorização, lock e escrita numa transação curta.
        services.AddScoped<ITransactionAnnotationStore, TransactionAnnotationStore>();
        // O catálogo pessoal compartilha apenas o contexto scoped, sem exigir conexão financeira ou provedor.
        services.AddScoped<ICategoryCatalogStore, CategoryCatalogStore>();
        services.AddScoped<ISyncRunStatusReader, SyncRunStatusReader>();
        services.AddScoped<ISyncRunLeaseStore, SyncRunLeaseStore>();
        services.AddScoped<ISyncRunLeaseExecutionGuard, SyncRunLeaseExecutionGuard>();
        return services;
    }
}
