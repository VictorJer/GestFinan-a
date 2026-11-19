using System.Data;
using Finora.Application.Authentication;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Identity;

/// <summary>Revoga a sessão autenticada em PostgreSQL sem receber JWT ou aceitar seletores do cliente.</summary>
internal sealed class IdentityAuthenticationSessionRevoker(
    FinoraDbContext context,
    TimeProvider clock) : IAuthenticationSessionRevoker
{
    // O limite é propriedade da operação para permitir teste determinístico sem mudar o timeout de produção.
    internal TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // Serializa a mutação e confirma somente depois de reler a referência completa sob o lock compartilhado.
    public async Task<LogoutResult> RevokeAsync(
        AuthenticationSessionReference reference,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(OperationTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var operationToken = linkedCancellation.Token;
        var commitAttempted = false;
        var commandTimeoutSeconds = Math.Max(1, (int)Math.Ceiling(OperationTimeout.TotalSeconds));
        context.Database.SetCommandTimeout(commandTimeoutSeconds);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                operationToken);
            await IdentityMutationLock.AcquireAsync(context, commandTimeoutSeconds, operationToken);

            // Consulta sem tracking após o lock evita usar dados lidos antes de outra mutação concorrente.
            var persisted = await context.Set<AuthSession>().AsNoTracking()
                .SingleOrDefaultAsync(session => session.Id == reference.SessionId
                    && session.UserId == reference.UserId, operationToken);
            var userExists = persisted is not null && await context.Users.AsNoTracking()
                .AnyAsync(user => user.Id == reference.UserId, operationToken);
            if (!userExists || persisted is null
                || persisted.CreatedAtUtc != reference.CreatedAtUtc
                || persisted.ExpiresAtUtc != reference.ExpiresAtUtc
                || IsExpired(reference, clock.GetUtcNow()))
            {
                await transaction.RollbackAsync(operationToken);
                return new LogoutResult(LogoutState.InvalidSession);
            }

            // Uma chamada admitida antes de outro logout pode confirmar o estado revogado sem regravar a data.
            if (persisted.RevokedAtUtc is null)
            {
                var affected = await AuthenticationSessionRevocation.RevokeOneAsync(
                    context,
                    reference,
                    clock.GetUtcNow(),
                    operationToken);
                if (affected != 1)
                {
                    await transaction.RollbackAsync(operationToken);
                    return new LogoutResult(LogoutState.InvalidSession);
                }
            }

            // Expiração ocorrida durante SQL desfaz a atualização antes do commit.
            if (IsExpired(reference, clock.GetUtcNow()))
            {
                await transaction.RollbackAsync(operationToken);
                return new LogoutResult(LogoutState.InvalidSession);
            }

            operationToken.ThrowIfCancellationRequested();
            commitAttempted = true;
            await transaction.CommitAsync(operationToken);
            return new LogoutResult(LogoutState.Confirmed);
        }
        catch (OperationCanceledException) when (commitAttempted)
        {
            // Perda de confirmação após iniciar o commit é indisponibilidade, sem alegar rollback.
            return new LogoutResult(LogoutState.Unavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new LogoutResult(LogoutState.Unavailable);
        }
        catch (Exception exception) when (AuthenticationPersistenceFailure.IsOperational(exception)
            || exception is AuthenticationUnavailableException)
        {
            return new LogoutResult(LogoutState.Unavailable);
        }
    }

    // A validade temporal é conferida novamente depois da espera e do SQL, antes de confirmar revogação.
    private static bool IsExpired(AuthenticationSessionReference reference, DateTimeOffset nowUtc) =>
        nowUtc >= reference.ExpiresAtUtc;
}
