using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Finora.Infrastructure.Messaging;

/// <summary>Reenvia obrigações persistidas enquanto a API está ativa, sem atuar como executor financeiro.</summary>
public sealed class SyncRunExecutionRecoveryService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<SyncRunExecutionRecoveryService> logger) : BackgroundService
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RecoveryInterval, timeProvider);
        do
        {
            try
            {
                // Scope novo por ciclo mantém DbContext e adapter outbox curtos e independentes do consumer.
                await using var scope = scopeFactory.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<SyncRunExecutionRecoveryCoordinator>();
                await coordinator.EnqueueRecoverableExecutionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // A falha do reconciliador não derruba login/liveness; o próximo ciclo reavalia o estado persistido.
                logger.LogWarning("A reconciliação de execuções de sincronização falhou; a próxima passagem tentará novamente.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
