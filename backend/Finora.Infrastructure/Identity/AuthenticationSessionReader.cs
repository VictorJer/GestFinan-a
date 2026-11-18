using Finora.Application.Authentication;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Identity;

/// <summary>Consulta usuário e sessão em toda autenticação, sem tracking ou cache positivo.</summary>
internal sealed class AuthenticationSessionReader(FinoraDbContext context, TimeProvider clock) : IAuthenticationSessionReader
{
    // A injeção interna permite testar timeout sem transformar o limite em configuração operacional.
    internal TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // O mesmo instante controla os prazos; igualdade com expiração já nega acesso.
    public async Task<bool> IsActiveAsync(AuthenticationSessionReference session, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(OperationTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        context.Database.SetCommandTimeout(Math.Max(1, (int)Math.Ceiling(OperationTimeout.TotalSeconds)));
        try
        {
            var now = clock.GetUtcNow();
            var active = await (from persisted in context.Set<AuthSession>().AsNoTracking()
                          join user in context.Users.AsNoTracking() on persisted.UserId equals user.Id
                          where persisted.Id == session.SessionId && persisted.UserId == session.UserId
                              && persisted.RevokedAtUtc == null && persisted.ExpiresAtUtc > now
                              && persisted.CreatedAtUtc == session.CreatedAtUtc && persisted.ExpiresAtUtc == session.ExpiresAtUtc
                          select persisted.Id).AnyAsync(linked.Token);
            // A consulta pode esperar por lock/rede; não liberar acesso se o prazo terminou durante essa espera.
            return active && session.ExpiresAtUtc > clock.GetUtcNow();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (AuthenticationPersistenceFailure.IsOperational(exception) || exception is OperationCanceledException)
        {
            // O handler HTTP distinguirá indisponibilidade de uma sessão inexistente/revogada.
            throw new AuthenticationUnavailableException();
        }
    }
}
