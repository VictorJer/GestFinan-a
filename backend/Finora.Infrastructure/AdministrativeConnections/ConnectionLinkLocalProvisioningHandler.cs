using System.Data;
using Finora.Application.AdministrativeConnections;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finora.Infrastructure.AdministrativeConnections;

/// <summary>Persiste conexão e identidade ativa juntas após reler titular e vínculos sob bloqueio PostgreSQL.</summary>
internal sealed class ConnectionLinkLocalProvisioningHandler(FinoraDbContext context) :
    IRequestHandler<ConnectionLinkLocalLinkCommand, ConnectionLinkLocalLinkResult>
{
    private const string Provider = "pluggy";
    private const int CommandTimeoutSeconds = 30;
    private readonly Func<CancellationToken, Task>? _beforeCommitAsync;
    private readonly Action? _afterCommit;

    // Produção não injeta falhas; os hooks internos existem somente para provar rollback e confirmação incerta.
    internal ConnectionLinkLocalProvisioningHandler(
        FinoraDbContext context,
        Func<CancellationToken, Task>? beforeCommitAsync,
        Action? afterCommit)
        : this(context)
    {
        _beforeCommitAsync = beforeCommitAsync;
        _afterCommit = afterCommit;
    }

    // A transação curta só aceita a identidade que veio da pré-consulta e mantém nulos os horários de importação.
    public async Task<ConnectionLinkLocalLinkResult> Handle(
        ConnectionLinkLocalLinkCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExternalItemId))
        {
            return new ConnectionLinkLocalLinkResult(ConnectionLinkLocalLinkState.InvalidRequest);
        }

        context.Database.SetCommandTimeout(CommandTimeoutSeconds);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            // O lock na linha do titular serializa vínculos concorrentes sem manter transação durante a Pluggy.
            var owner = await context.Users
                .FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {request.UserId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (owner is null)
            {
                return Conflict();
            }

            // Revalida a cardinalidade depois do bloqueio e exige exatamente o titular aprovado na pré-consulta.
            var owners = await context.Users
                .AsNoTracking()
                .OrderBy(user => user.Id)
                .Select(user => user.Id)
                .Take(2)
                .ToArrayAsync(cancellationToken);
            if (owners.Length != 1 || owners[0] != request.UserId)
            {
                return Conflict();
            }

            // Uma conexão preexistente só converge quando item e identidade ativa continuam íntegros e correspondentes.
            var existingConnections = await context.Set<FinancialConnection>()
                .AsNoTracking()
                .Where(connection => connection.UserId == request.UserId)
                .OrderBy(connection => connection.Id)
                .Take(2)
                .ToArrayAsync(cancellationToken);
            if (existingConnections.Length == 1)
            {
                var existingConnection = existingConnections[0];
                var identities = await context.Set<FinancialConnectionItemIdentity>()
                    .AsNoTracking()
                    .Where(identity => identity.ConnectionId == existingConnection.Id
                        && identity.UserId == request.UserId)
                    .ToArrayAsync(cancellationToken);
                var activeIdentities = identities
                    .Where(identity => identity.RetiredAtUtc is null)
                    .Take(2)
                    .ToArray();
                var configuredItemIdentity = await context.Set<FinancialConnectionItemIdentity>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(identity => identity.Provider == Provider
                        && identity.ExternalItemId == request.ExternalItemId,
                        cancellationToken);

                if (existingConnection.Provider == Provider
                    && string.Equals(existingConnection.ExternalItemId, request.ExternalItemId, StringComparison.Ordinal)
                    && activeIdentities.Length == 1
                    && activeIdentities[0].Provider == Provider
                    && string.Equals(activeIdentities[0].ExternalItemId, request.ExternalItemId, StringComparison.Ordinal)
                    && configuredItemIdentity is not null
                    && configuredItemIdentity.ConnectionId == existingConnection.Id
                    && configuredItemIdentity.UserId == request.UserId
                    && configuredItemIdentity.RetiredAtUtc is null)
                {
                    return new ConnectionLinkLocalLinkResult(ConnectionLinkLocalLinkState.AlreadyLinked);
                }

                return Conflict();
            }

            if (existingConnections.Length > 1)
            {
                return Conflict();
            }

            // Sem conexão, qualquer histórico do titular ou reserva global do item impede uma adoção implícita.
            var ownerHasIdentityHistory = await context.Set<FinancialConnectionItemIdentity>()
                .AsNoTracking()
                .AnyAsync(identity => identity.UserId == request.UserId, cancellationToken);
            var itemWasPreviouslyLinked = await context.Set<FinancialConnectionItemIdentity>()
                .AsNoTracking()
                .AnyAsync(identity => identity.Provider == Provider
                    && identity.ExternalItemId == request.ExternalItemId,
                    cancellationToken);
            if (existingConnections.Length != 0 || ownerHasIdentityHistory || itemWasPreviouslyLinked)
            {
                return Conflict();
            }

            // As fábricas aplicam invariantes de domínio; um SaveChanges grava o par dentro da mesma transação.
            var connectionId = Guid.NewGuid();
            context.Set<FinancialConnection>().Add(FinancialConnection.Create(
                connectionId,
                request.UserId,
                Provider,
                request.ExternalItemId));
            context.Set<FinancialConnectionItemIdentity>().Add(FinancialConnectionItemIdentity.CreateActive(
                connectionId,
                request.UserId,
                Provider,
                request.ExternalItemId));

            await context.SaveChangesAsync(cancellationToken);
            if (_beforeCommitAsync is not null)
            {
                await _beforeCommitAsync(cancellationToken);
            }

            // Cancelamentos antes do commit desfazem a escrita; depois de iniciá-lo, não se cancela uma confirmação ambígua.
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None);
            _afterCommit?.Invoke();
            return new ConnectionLinkLocalLinkResult(ConnectionLinkLocalLinkState.Linked);
        }
        catch (DbUpdateException exception) when (ContainsUniqueViolation(exception))
        {
            // Constraints globais permanecem a última barreira contra identidades já ocupadas fora deste protocolo.
            return Conflict();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A camada de apresentação recebe somente falha enumerada; a exceção original nunca vira saída do comando.
            return new ConnectionLinkLocalLinkResult(ConnectionLinkLocalLinkState.OperationalFailure);
        }
    }

    // Converte violações de unicidade em conflito sem expor SQL, nomes de constraint ou valores externos.
    private static bool ContainsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }

    // Um único resultado cobre titular ausente/múltiplo e qualquer identidade ou conexão já ocupada.
    private static ConnectionLinkLocalLinkResult Conflict() =>
        new(ConnectionLinkLocalLinkState.Conflict);
}
