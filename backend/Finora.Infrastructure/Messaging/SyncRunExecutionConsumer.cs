using Finora.Application.FinancialData;
using Finora.Infrastructure.Messaging.Contracts;
using MassTransit;
using MediatR;

namespace Finora.Infrastructure.Messaging;

/// <summary>Consumer fino que limita a duração da entrega e encaminha somente o ID persistido ao caso de uso.</summary>
public sealed class SyncRunExecutionConsumer(
    ISender sender,
    SyncRunExecutionWatchdogOptions? watchdogOptions = null) : IConsumer<ExecuteSyncRunV1>
{
    private readonly SyncRunExecutionWatchdogOptions _watchdogOptions =
        watchdogOptions ?? SyncRunExecutionWatchdogOptions.Default;

    public async Task Consume(ConsumeContext<ExecuteSyncRunV1> context)
    {
        // O prazo começa no recebimento pelo consumer e deixa a responsabilidade explicitamente na aplicação.
        using var watchdogCancellation = new CancellationTokenSource(_watchdogOptions.DeliveryDeadline);
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken, watchdogCancellation.Token);

        if (!ExecuteSyncRunV1Contract.IsValid(context.Message))
        {
            throw new InvalidOperationException("A mensagem de execução não atende ao contrato suportado.");
        }

        // A espera também é limitada: um provider que ignore cancelamento não mantém esta entrega unacked indefinidamente.
        var execution = sender.Send(
            new ExecuteSyncRunCommand(context.Message.SyncRunId),
            executionCancellation.Token);
        ExecuteSyncRunResult result;
        try
        {
            result = await execution.WaitAsync(executionCancellation.Token);
        }
        catch (OperationCanceledException) when (
            watchdogCancellation.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
        {
            // O token é cancelado e o erro segue ao fluxo durável do transporte; o timeout não confirma sucesso.
            _ = ObserveAbandonedExecutionAsync(execution);
            throw new SyncRunDeliveryWatchdogExpiredException();
        }

        if (result.Status is ExecuteSyncRunStatus.Executed or ExecuteSyncRunStatus.AlreadyTerminal)
        {
            // Execução concluída ou terminal pode ser confirmada; tentativa em posse vigente não é descartada como feita.
            return;
        }

        // Busy e demais estados sem handoff durável seguem para o fluxo de falha recuperável, nunca para ack silencioso.
        throw new SyncRunExecutionNotStartedException(result.Status);
    }

    // Uma operação não cooperativa pode terminar após o watchdog; observar sua task evita exceção não observada.
    private static async Task ObserveAbandonedExecutionAsync(Task<ExecuteSyncRunResult> execution)
    {
        try { await execution.ConfigureAwait(false); }
        catch (Exception) { }
    }
}

// A falha fixa deixa a política de redrive/error queue para a etapa de recuperação, sem expor IDs ou dados pessoais.
// O estado fechado ajuda a diagnosticar falhas de handoff sem anexar ID, titular ou dados da execução.
public sealed class SyncRunExecutionNotStartedException(ExecuteSyncRunStatus status)
    : InvalidOperationException($"A execução não pôde iniciar ({status}); a entrega exige recuperação.")
{
    public ExecuteSyncRunStatus Status { get; } = status;
}

// Limite interno que encerra a espera do consumer; não representa configuração nem garantia do broker.
public sealed record SyncRunExecutionWatchdogOptions
{
    public static readonly SyncRunExecutionWatchdogOptions Default = new(TimeSpan.FromMinutes(11));

    public SyncRunExecutionWatchdogOptions(TimeSpan deliveryDeadline)
    {
        if (deliveryDeadline <= TimeSpan.Zero || deliveryDeadline > TimeSpan.FromMinutes(11))
        {
            throw new ArgumentOutOfRangeException(nameof(deliveryDeadline),
                "O limite interno precisa ser positivo e não pode exceder onze minutos.");
        }

        DeliveryDeadline = deliveryDeadline;
    }

    public TimeSpan DeliveryDeadline { get; }
}

// Mensagem fixa evita propagar conteúdo da execução quando o consumer ultrapassa o limite interno de entrega.
public sealed class SyncRunDeliveryWatchdogExpiredException()
    : InvalidOperationException("O limite interno da entrega foi atingido; a obrigação exige recuperação.");
