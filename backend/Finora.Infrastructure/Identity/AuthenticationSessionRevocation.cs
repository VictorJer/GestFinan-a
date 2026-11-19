using Finora.Application.Authentication;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Identity;

/// <summary>Centraliza atualizações condicionais de revogação para logout e reset no contexto transacional recebido.</summary>
internal static class AuthenticationSessionRevocation
{
    // Atualiza uma sessão somente quando a referência completa ainda aponta para uma sessão ativa.
    internal static Task<int> RevokeOneAsync(
        FinoraDbContext context,
        AuthenticationSessionReference reference,
        DateTimeOffset referenceTimeUtc,
        CancellationToken cancellationToken) =>
        context.Set<AuthSession>()
            .Where(session => session.Id == reference.SessionId
                && session.UserId == reference.UserId
                && session.CreatedAtUtc == reference.CreatedAtUtc
                && session.ExpiresAtUtc == reference.ExpiresAtUtc
                && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                session => session.RevokedAtUtc,
                session => session.CreatedAtUtc > referenceTimeUtc ? session.CreatedAtUtc : referenceTimeUtc),
                cancellationToken);

    // O reset usa o mesmo timestamp monotônico por linha e deixa datas anteriores sem alterações.
    internal static Task<int> RevokeAllAsync(
        FinoraDbContext context,
        Guid userId,
        DateTimeOffset referenceTimeUtc,
        CancellationToken cancellationToken) =>
        context.Set<AuthSession>()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                session => session.RevokedAtUtc,
                session => session.CreatedAtUtc > referenceTimeUtc ? session.CreatedAtUtc : referenceTimeUtc),
                cancellationToken);
}
