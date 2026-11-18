using System.Data;
using Finora.Application.Authentication;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Identity;

// O hash sintético usa o mesmo hasher configurado e fica somente em memória, sem conta ou segredo persistidos.
internal sealed class SyntheticCredential
{
    private readonly object _gate = new();
    private readonly ApplicationUser _user = new();
    private string? _hash;

    // O custo inicial é pago uma vez; não há promessa de tempo constante nem contador para conta inexistente.
    internal void Verify(IPasswordHasher<ApplicationUser> hasher, string password)
    {
        string hash;
        lock (_gate)
        {
            _hash ??= hasher.HashPassword(_user, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            hash = _hash;
        }
        hasher.VerifyHashedPassword(_user, hash, password);
    }
}

/// <summary>Serializa credencial, tentativas e sessão com os mesmos stores/transação do titular administrativo.</summary>
internal sealed class IdentityLoginService(
    FinoraDbContext context,
    UserManager<ApplicationUser> users,
    IPasswordHasher<ApplicationUser> hasher,
    SyntheticCredential synthetic,
    IAccessTokenIssuer issuer,
    TimeProvider clock) : IAuthenticationLogin
{
    // Somente testes internos podem reduzir a espera; o contrato de runtime permanece em 30 segundos.
    internal TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // Timeout inclui espera pelo lock; a senha é preservada e somente o e-mail é aparado/normalizado por Identity.
    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(OperationTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;
        var commitAttempted = false;
        var commandTimeout = Math.Max(1, (int)Math.Ceiling(OperationTimeout.TotalSeconds));
        context.Database.SetCommandTimeout(commandTimeout);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await IdentityMutationLock.AcquireAsync(context, commandTimeout, token);
            // Nenhuma entidade de uma consulta anterior ao lock pode fornecer credencial obsoleta ao login.
            context.ChangeTracker.Clear();
            // Validadores Identity consultam novamente por nome/e-mail; não podem rastrear outra instância do mesmo titular.
            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var candidates = await context.Users.AsNoTracking().OrderBy(user => user.Id).Take(2).ToArrayAsync(token);
            if (candidates.Length > 1) { return LoginResult.Unavailable(); }
            var normalizedEmail = users.NormalizeEmail(email.Trim());
            var user = candidates.SingleOrDefault(candidate => string.Equals(candidate.NormalizedEmail, normalizedEmail, StringComparison.Ordinal));
            var valid = false;
            if (user is null)
            {
                synthetic.Verify(hasher, password);
            }
            else
            {
                // Legados sem lockout habilitado passam a observar a mesma política, dentro da transação.
                if (!user.LockoutEnabled) { RequireSuccess(await users.SetLockoutEnabledAsync(user, true)); }
                token.ThrowIfCancellationRequested();
                if (!await users.IsLockedOutAsync(user))
                {
                    valid = await users.CheckPasswordAsync(user, password);
                    token.ThrowIfCancellationRequested();
                    if (!valid) { RequireSuccess(await users.AccessFailedAsync(user)); }
                }
            }

            // A falha do contador precisa ser confirmada antes do 401; bloqueio vigente não é estendido.
            if (!valid)
            {
                token.ThrowIfCancellationRequested();
                commitAttempted = true;
                await transaction.CommitAsync(token);
                return LoginResult.InvalidCredentials();
            }

            // Preparação/assinatura anterior ao commit não publica o token nem confirma persistência parcial.
            var created = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
            var session = AuthSession.Create(AuthSession.GenerateId(), user!.Id, created, created.AddSeconds(1800));
            var prepared = issuer.Issue(user.Id, session.Id, session.CreatedAtUtc, session.ExpiresAtUtc);
            RequireSuccess(await users.ResetAccessFailedCountAsync(user));
            token.ThrowIfCancellationRequested();
            context.Add(session);
            await context.SaveChangesAsync(token);
            token.ThrowIfCancellationRequested();
            commitAttempted = true;
            await transaction.CommitAsync(token);
            return LoginResult.Success(prepared);
        }
        catch (OperationCanceledException) when (commitAttempted) { return LoginResult.Unavailable(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return LoginResult.Unavailable(); }
        catch (Exception exception) when (AuthenticationPersistenceFailure.IsOperational(exception) || exception is AuthenticationUnavailableException)
        {
            // Falhas conhecidas não carregam exceções do provider até logs, resultado ou resposta HTTP.
            return LoginResult.Unavailable();
        }
    }

    // Qualquer conflito/rejeição Identity na mutação impede anunciar senha incorreta ou sessão válida.
    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) { throw new AuthenticationUnavailableException(); }
    }
}
