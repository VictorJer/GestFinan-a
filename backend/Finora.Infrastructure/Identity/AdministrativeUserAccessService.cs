using System.Data;
using Finora.Application.AdministrativeUsers;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finora.Infrastructure.Identity;

/// <summary>Implementa a consulta prévia usando Identity e uma leitura limitada ao estado do titular.</summary>
internal sealed class AdministrativeUserAccessService : IAdministrativeUserAccess
{
    private static readonly TimeSpan DefaultDatabaseOperationTimeout = TimeSpan.FromSeconds(30);

    private readonly FinoraDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _databaseOperationTimeout;

    // O mesmo contexto scoped mantém as consultas do validador Identity sem tracking.
    public AdministrativeUserAccessService(
        FinoraDbContext context,
        UserManager<ApplicationUser> userManager,
        TimeProvider clock)
        : this(context, userManager, clock, DefaultDatabaseOperationTimeout)
    {
    }

    // O timeout é interno e injetável em testes, sem se tornar configuração de runtime que altere o contrato D4.
    internal AdministrativeUserAccessService(
        FinoraDbContext context,
        UserManager<ApplicationUser> userManager,
        TimeProvider clock,
        TimeSpan databaseOperationTimeout)
    {
        _context = context;
        _userManager = userManager;
        _clock = clock;
        if (databaseOperationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(databaseOperationTimeout));
        }

        _databaseOperationTimeout = databaseOperationTimeout;
    }

    // Valida e normaliza com Identity, conta no máximo dois usuários e decide sem reter entidades rastreadas.
    public async Task<AdministrativeUserPreflightResult> PreflightAsync(
        AdministrativeUserAction action,
        string email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trimmedEmail = email?.Trim() ?? string.Empty;
        var candidate = CreateValidationUser(trimmedEmail);
        _context.Database.SetCommandTimeout(CommandTimeoutSeconds);
        var users = await ReadUsersAsync(cancellationToken);

        if (users.Length > 1)
        {
            return new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Conflict);
        }

        var matchingUser = FindMatchingUser(users, candidate);
        if (matchingUser is not null)
        {
            // A validação de unicidade do Identity reconhece o mesmo titular sem tratar a repetição como duplicata.
            candidate.Id = matchingUser.Id;
        }

        var validation = await ValidateWithIdentityAsync(candidate, cancellationToken);
        if (validation == ValidationState.Invalid)
        {
            return new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.InvalidEmail);
        }

        if (validation == ValidationState.Conflict)
        {
            return new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Conflict);
        }

        return action switch
        {
            AdministrativeUserAction.Create => ResolveCreatePreflight(users.Length, matchingUser),
            AdministrativeUserAction.ResetPassword => ResolveResetPreflight(matchingUser),
            _ => new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Conflict)
        };
    }

    // Serializa a criação entre processos e confirma sucesso somente após o commit PostgreSQL.
    public async Task<AdministrativeUserOperationResult> CreateAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_databaseOperationTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var operationToken = linkedCancellation.Token;
        _context.Database.SetCommandTimeout(CommandTimeoutSeconds);

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                operationToken);
            await AcquireAdministrativeLockAsync(operationToken);

            // A leitura ocorre depois da aquisição do lock, em novo statement para observar commits anteriores.
            var users = await ReadUsersAsync(operationToken);
            if (users.Length > 1)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.Conflict);
            }

            var candidate = CreateValidationUser(email?.Trim() ?? string.Empty);
            var matchingUser = FindMatchingUser(users, candidate);
            if (matchingUser is not null)
            {
                candidate.Id = matchingUser.Id;
            }

            var validation = await ValidateWithIdentityAsync(candidate, operationToken);
            if (validation == ValidationState.Invalid)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.InvalidEmail);
            }

            if (validation == ValidationState.Conflict)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.Conflict);
            }

            if (matchingUser is not null)
            {
                await transaction.CommitAsync(operationToken);
                return Operation(AdministrativeUserOperationState.AlreadyExists);
            }

            if (users.Length > 0)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.Conflict);
            }

            var identityResult = await _userManager.CreateAsync(candidate, password);
            if (!identityResult.Succeeded)
            {
                await transaction.RollbackAsync(operationToken);
                return MapIdentityFailure(identityResult);
            }

            await transaction.CommitAsync(operationToken);
            return Operation(AdministrativeUserOperationState.Created);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Operation(AdministrativeUserOperationState.OperationalFailure);
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            return Operation(AdministrativeUserOperationState.Conflict);
        }
        catch
        {
            return Operation(AdministrativeUserOperationState.OperationalFailure);
        }
    }

    // O reset revalida o titular sob o mesmo lock da criação e só retorna sucesso após o commit confirmado.
    public async Task<AdministrativeUserOperationResult> ResetPasswordAsync(
        string email,
        Guid expectedUserId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_databaseOperationTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var operationToken = linkedCancellation.Token;
        var commitAttempted = false;
        _context.Database.SetCommandTimeout(CommandTimeoutSeconds);

        try
        {
            // A transação envolve lock, revalidação, operações Identity e commit no mesmo contexto scoped.
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                operationToken);
            await AcquireAdministrativeLockAsync(operationToken);
            var revocationTimestampUtc = _clock.GetUtcNow();

            // A segunda leitura ocorre após o lock para observar criação/reset concorrente já confirmado.
            var users = await ReadUsersAsync(operationToken);
            if (users.Length > 1)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.Conflict);
            }

            var candidate = CreateValidationUser(email?.Trim() ?? string.Empty);
            var matchingUser = FindMatchingUser(users, candidate);
            if (matchingUser is not null)
            {
                candidate.Id = matchingUser.Id;
            }

            // A normalização e os validadores permanecem os mesmos da criação, sem revelar descrições Identity.
            var validation = await ValidateWithIdentityAsync(candidate, operationToken);
            if (validation == ValidationState.Invalid)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.InvalidEmail);
            }

            if (validation == ValidationState.Conflict)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.Conflict);
            }

            if (matchingUser is null)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.NotFound);
            }

            if (matchingUser.Id != expectedUserId)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.Conflict);
            }

            // Recupera o usuário pelo ID observado após o lock; o store Identity continuará usando este contexto.
            var user = await _userManager.FindByIdAsync(matchingUser.Id.ToString());
            if (user is null)
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.NotFound);
            }

            if (!string.Equals(user.NormalizedEmail, candidate.NormalizedEmail, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(operationToken);
                return Operation(AdministrativeUserOperationState.Conflict);
            }

            // O token existe somente nesta variável e é consumido pelo Identity no mesmo processo/contexto.
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            operationToken.ThrowIfCancellationRequested();
            var identityResult = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);
            operationToken.ThrowIfCancellationRequested();

            if (!identityResult.Succeeded)
            {
                await transaction.RollbackAsync(operationToken);
                return MapIdentityFailure(identityResult);
            }

            // Revoga na mesma transação credencial/stamp; o helper preserva registros já revogados e linhas futuras.
            await AuthenticationSessionRevocation.RevokeAllAsync(
                _context,
                user.Id,
                revocationTimestampUtc,
                operationToken);

            // Depois que o commit começa, resultado incerto é falha operacional, nunca confirmação de cancelamento.
            operationToken.ThrowIfCancellationRequested();
            commitAttempted = true;
            await transaction.CommitAsync(operationToken);
            return Operation(AdministrativeUserOperationState.PasswordReset);
        }
        catch (OperationCanceledException) when (commitAttempted)
        {
            return Operation(AdministrativeUserOperationState.OperationalFailure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Operation(AdministrativeUserOperationState.OperationalFailure);
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            return Operation(AdministrativeUserOperationState.Conflict);
        }
        catch
        {
            return Operation(AdministrativeUserOperationState.OperationalFailure);
        }
    }

    // Constrói os campos da validação a partir da normalização padrão do Identity, sem regras próprias de caixa.
    private ApplicationUser CreateValidationUser(string email)
    {
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            NormalizedEmail = _userManager.NormalizeEmail(email),
            NormalizedUserName = _userManager.NormalizeName(email)
        };

        return user;
    }

    // Conta somente até dois usuários em projeção sem tracking; o segundo registro basta para falhar fechado.
    private async Task<UserSnapshot[]> ReadUsersAsync(CancellationToken cancellationToken)
    {
        // As consultas adicionais feitas pelos validadores UserManager herdam este modo sem tracking.
        _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return await _context.Users
            .AsNoTracking()
            .OrderBy(user => user.Id)
            .Select(user => new UserSnapshot(user.Id, user.NormalizedEmail))
            .Take(2)
            .ToArrayAsync(cancellationToken);
    }

    // Compara a chave produzida pelo normalizador Identity com a chave já persistida pelo mesmo Identity.
    private static UserSnapshot? FindMatchingUser(UserSnapshot[] users, ApplicationUser candidate) =>
        users.SingleOrDefault(user =>
            string.Equals(user.NormalizedEmail, candidate.NormalizedEmail, StringComparison.Ordinal));

    // Reaproveita os validadores registrados no Identity e interpreta somente códigos estáveis, nunca descrições.
    private async Task<ValidationState> ValidateWithIdentityAsync(
        ApplicationUser candidate,
        CancellationToken cancellationToken)
    {
        var errorCodes = new List<string>();
        foreach (var validator in _userManager.UserValidators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await validator.ValidateAsync(_userManager, candidate);
            cancellationToken.ThrowIfCancellationRequested();
            errorCodes.AddRange(result.Errors.Select(error => error.Code));
        }

        if (errorCodes.Count == 0)
        {
            return ValidationState.Valid;
        }

        if (errorCodes.Any(code => code is "InvalidEmail" or "InvalidUserName"))
        {
            return ValidationState.Invalid;
        }

        if (errorCodes.Any(code => code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return ValidationState.Conflict;
        }

        // Qualquer rejeição restante do validador fecha o fluxo como e-mail/identidade inválidos.
        return ValidationState.Invalid;
    }

    // Delega ao protocolo único que ordena login em relação à criação e ao reset.
    private Task AcquireAdministrativeLockAsync(CancellationToken cancellationToken) =>
        IdentityMutationLock.AcquireAsync(_context, CommandTimeoutSeconds, cancellationToken);

    // Converte apenas códigos Identity conhecidos em estados enumerados, sem transportar descrições ao chamador.
    private static AdministrativeUserOperationResult MapIdentityFailure(IdentityResult result)
    {
        var errorCodes = result.Errors.Select(error => error.Code).ToArray();
        if (errorCodes.Any(code => code is "DuplicateEmail" or "DuplicateUserName" or "ConcurrencyFailure"))
        {
            return Operation(AdministrativeUserOperationState.Conflict);
        }

        if (errorCodes.Any(code => code is "InvalidEmail" or "InvalidUserName"))
        {
            return Operation(AdministrativeUserOperationState.InvalidEmail);
        }

        if (errorCodes.Any(code => code is "PasswordTooShort"
            or "PasswordRequiresNonAlphanumeric"
            or "PasswordRequiresDigit"
            or "PasswordRequiresLower"
            or "PasswordRequiresUpper"
            or "PasswordRequiresUniqueChars"))
        {
            return Operation(AdministrativeUserOperationState.PasswordPolicyViolation);
        }

        return Operation(AdministrativeUserOperationState.OperationalFailure);
    }

    // Detecta a violação única mesmo quando EF a encapsula, sem depender do texto do servidor.
    private static bool IsUniqueViolation(Exception exception)
    {
        if (exception is PostgresException postgresException)
        {
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
        }

        return exception.InnerException is { } innerException && IsUniqueViolation(innerException);
    }

    // Centraliza a construção dos únicos estados que podem sair da camada Identity.
    private static AdministrativeUserOperationResult Operation(AdministrativeUserOperationState state) =>
        new(state);

    // Mantém o timeout ADO em segundos sem reduzir a precisão do cancellation token de teste.
    private int CommandTimeoutSeconds => Math.Max(1, (int)Math.Ceiling(_databaseOperationTimeout.TotalSeconds));

    // A criação aceita somente estado vazio ou a repetição normalizada do mesmo titular.
    private static AdministrativeUserPreflightResult ResolveCreatePreflight(
        int userCount,
        UserSnapshot? matchingUser) =>
        matchingUser is not null
            ? new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.AlreadyExists)
            : userCount == 0
                ? new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Ready)
                : new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.Conflict);

    // O reset só retorna o identificador interno quando a pré-consulta encontrou exatamente o titular solicitado.
    private static AdministrativeUserPreflightResult ResolveResetPreflight(UserSnapshot? matchingUser) =>
        matchingUser is null
            ? new AdministrativeUserPreflightResult(AdministrativeUserPreflightState.NotFound)
            : new AdministrativeUserPreflightResult(
                AdministrativeUserPreflightState.Ready,
                matchingUser.Id);

    // O snapshot limita a consulta ao identificador e ao valor que o normalizador Identity já persistiu.
    private sealed record UserSnapshot(Guid Id, string? NormalizedEmail);

    // Mantém os estados de validação internos; mensagens Identity não atravessam esta camada.
    private enum ValidationState
    {
        Valid,
        Invalid,
        Conflict
    }
}
