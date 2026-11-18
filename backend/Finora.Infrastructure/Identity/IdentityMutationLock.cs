using System.Data;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finora.Infrastructure.Identity;

// O protocolo cooperativo serializa login/criação/reset; não coordena SQL externo que ignore a chave.
internal static class IdentityMutationLock
{
    internal const long Key = 0x46494E4F5241L;

    // Exige transação existente e usa sua conexão: o PostgreSQL libera o lock ao confirmar ou desfazer.
    internal static async Task AcquireAsync(FinoraDbContext context, int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = "SELECT pg_advisory_xact_lock(@lock_key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.DbType = DbType.Int64;
        parameter.Value = Key;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
