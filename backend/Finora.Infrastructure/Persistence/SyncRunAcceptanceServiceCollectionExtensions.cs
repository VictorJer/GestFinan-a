using Finora.Application.FinancialData;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Infrastructure.Persistence;

/// <summary>Registra os adapters EF de consulta financeira e aceitação de SyncRun, sem abrir conexão no startup.</summary>
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
        // O catálogo pessoal compartilha apenas o contexto scoped, sem exigir conexão financeira ou provedor.
        services.AddScoped<ICategoryCatalogStore, CategoryCatalogStore>();
        services.AddScoped<ISyncRunStatusReader, SyncRunStatusReader>();
        services.AddScoped<ISyncRunLeaseStore, SyncRunLeaseStore>();
        services.AddScoped<ISyncRunLeaseExecutionGuard, SyncRunLeaseExecutionGuard>();
        return services;
    }
}
