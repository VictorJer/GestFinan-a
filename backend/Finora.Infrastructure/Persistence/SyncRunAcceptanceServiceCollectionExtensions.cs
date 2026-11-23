using Finora.Application.FinancialData;
using Microsoft.Extensions.DependencyInjection;

namespace Finora.Infrastructure.Persistence;

/// <summary>Registra os adapters EF necessários à resolução da conexão atual e à aceitação de SyncRun.</summary>
public static class SyncRunAcceptanceServiceCollectionExtensions
{
    // O DbContext já é configurado pela composição HTTP; os adapters não abrem conexão nem aplicam migration.
    public static IServiceCollection AddFinoraSyncRunAcceptancePersistence(this IServiceCollection services)
    {
        services.AddScoped<ISyncRunAcceptanceStore, SyncRunAcceptanceStore>();
        services.AddScoped<ICurrentSyncConnectionReader, SyncRunCurrentConnectionReader>();
        services.AddScoped<ISyncRunStatusReader, SyncRunStatusReader>();
        services.AddScoped<ISyncRunLeaseStore, SyncRunLeaseStore>();
        services.AddScoped<ISyncRunLeaseExecutionGuard, SyncRunLeaseExecutionGuard>();
        return services;
    }
}
