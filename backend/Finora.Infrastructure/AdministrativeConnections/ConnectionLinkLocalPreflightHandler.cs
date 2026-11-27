using Finora.Application.AdministrativeConnections;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Finora.Infrastructure.Pluggy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.AdministrativeConnections;

/// <summary>Confere um titular único e valida o item/cartão sem abrir transação de escrita.</summary>
internal sealed class ConnectionLinkLocalPreflightHandler :
    IRequestHandler<ConnectionLinkLocalPreflightQuery, ConnectionLinkLocalPreflightResult>
{
    private static readonly TimeSpan DefaultDatabaseTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPluggyTimeout = TimeSpan.FromSeconds(120);

    private readonly FinoraDbContext _context;
    private readonly PluggyConnectionInspector _inspector;
    private readonly PluggyOptions _options;
    private readonly TimeSpan _databaseTimeout;
    private readonly TimeSpan _pluggyTimeout;

    // A composição de produção usa limites finitos para a consulta local e a verificação remota.
    public ConnectionLinkLocalPreflightHandler(
        FinoraDbContext context,
        PluggyConnectionInspector inspector,
        PluggyOptions options)
        : this(context, inspector, options, DefaultDatabaseTimeout, DefaultPluggyTimeout)
    {
    }

    // Os limites injetáveis permitem provar timeout/cancelamento sem aguardar chamadas reais.
    internal ConnectionLinkLocalPreflightHandler(
        FinoraDbContext context,
        PluggyConnectionInspector inspector,
        PluggyOptions options,
        TimeSpan databaseTimeout,
        TimeSpan pluggyTimeout)
    {
        _context = context;
        _inspector = inspector;
        _options = options;
        if (databaseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(databaseTimeout));
        }

        if (pluggyTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pluggyTimeout));
        }

        _databaseTimeout = databaseTimeout;
        _pluggyTimeout = pluggyTimeout;
    }

    // Consulta no máximo dois UUIDs sem tracking e só chama a origem externa quando existe exatamente um titular.
    public async Task<ConnectionLinkLocalPreflightResult> Handle(
        ConnectionLinkLocalPreflightQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _context.Database.SetCommandTimeout(Math.Max(1, (int)Math.Ceiling(_databaseTimeout.TotalSeconds)));

        var ownerIds = await _context.Users
            .AsNoTracking()
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);

        if (ownerIds.Length == 0)
        {
            return new ConnectionLinkLocalPreflightResult(ConnectionLinkLocalPreflightState.OwnerMissing);
        }

        if (ownerIds.Length > 1)
        {
            return new ConnectionLinkLocalPreflightResult(ConnectionLinkLocalPreflightState.OwnerAmbiguous);
        }

        // O prazo total cobre autenticação, metadados do item e todas as páginas de contas selecionáveis.
        using var timeout = new CancellationTokenSource(_pluggyTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        PluggyConnectionInspection inspection;
        try
        {
            inspection = await _inspector.InspectAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Failure(
                ConnectionLinkLocalPreflightState.ExternalFailure,
                PluggyLocalReadFailureCategory.Timeout);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!inspection.IsReady
            || string.IsNullOrWhiteSpace(inspection.AccountId)
            || string.IsNullOrWhiteSpace(_options.ItemId))
        {
            return MapInspectionFailure(inspection);
        }

        // Os IDs opacos só seguem em memória para o comando; a representação textual do resultado os redige.
        return new ConnectionLinkLocalPreflightResult(
            ConnectionLinkLocalPreflightState.Ready,
            ownerIds[0],
            _options.ItemId,
            inspection.AccountId);
    }

    // Converte somente estados e categorias estáveis do inspector, sem propagar payload ou texto do provedor.
    private static ConnectionLinkLocalPreflightResult MapInspectionFailure(
        PluggyConnectionInspection inspection)
    {
        if (inspection.Stage == PluggyLocalReadStage.Configuration)
        {
            return Failure(ConnectionLinkLocalPreflightState.InvalidConfiguration, inspection.FailureCategory);
        }

        if (inspection.Stage == PluggyLocalReadStage.Authentication
            && inspection.FailureCategory is PluggyLocalReadFailureCategory.Unauthorized
                or PluggyLocalReadFailureCategory.Forbidden)
        {
            return Failure(
                ConnectionLinkLocalPreflightState.AuthenticationRejected,
                inspection.FailureCategory);
        }

        if (inspection.FailureOutcome == PluggyLocalReadOutcome.ConnectionBlocked
            || (inspection.Stage == PluggyLocalReadStage.Item
                && inspection.FailureCategory == PluggyLocalReadFailureCategory.Forbidden))
        {
            return Failure(ConnectionLinkLocalPreflightState.ItemBlocked, inspection.FailureCategory);
        }

        if (inspection.Stage == PluggyLocalReadStage.Accounts
            && inspection.FailureCategory == PluggyLocalReadFailureCategory.InvalidAccountSelection)
        {
            return Failure(
                ConnectionLinkLocalPreflightState.CardSelectionInvalid,
                inspection.FailureCategory);
        }

        return Failure(ConnectionLinkLocalPreflightState.ExternalFailure, inspection.FailureCategory);
    }

    // Falhas transportam apenas estado enumerado e categoria estável, nunca IDs ou resposta externa.
    private static ConnectionLinkLocalPreflightResult Failure(
        ConnectionLinkLocalPreflightState state,
        PluggyLocalReadFailureCategory? category) => new(state, FailureCategory: category);
}
