using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Identity;

// Reconhece falhas de provider mesmo quando a execution strategy as encapsula, sem depender de texto sensível.
internal static class AuthenticationPersistenceFailure
{
    internal static bool IsOperational(Exception exception) => exception is DbException or DbUpdateException
        || (exception.InnerException is { } inner && IsOperational(inner));
}
