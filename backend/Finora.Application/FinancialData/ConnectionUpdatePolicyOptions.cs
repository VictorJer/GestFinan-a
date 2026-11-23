using Finora.Domain.FinancialData;

namespace Finora.Application.FinancialData;

/// <summary>Defaults and configurable durations used by the connection update policy.</summary>
public sealed class ConnectionUpdatePolicyOptions
{
    public const string ConfigurationSectionName = "ConnectionUpdatePolicy";

    /// <summary>Age after which the last complete import makes an entry eligible to request an update.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Minimum per-connection delay between distinct accepted update intentions.</summary>
    public TimeSpan MinimumRequestInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Rejects missing-or-nonpositive policy durations without exposing configuration values.</summary>
    public static bool IsValid(ConnectionUpdatePolicyOptions options) =>
        options.StaleAfter > TimeSpan.Zero && options.MinimumRequestInterval > TimeSpan.Zero;
}

/// <summary>Evaluates update age from server-side instants using the strict RF16 threshold.</summary>
public static class ConnectionUpdatePolicy
{
    /// <summary>Requests an update when no complete success exists or its age is strictly greater than the threshold.</summary>
    public static bool IsUpdateRequired(
        DateTimeOffset? lastSuccessfulImportAtUtc,
        DateTimeOffset evaluatedAtUtc,
        TimeSpan staleAfter)
    {
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter));
        }

        if (lastSuccessfulImportAtUtc is null)
        {
            return true;
        }

        return evaluatedAtUtc.ToUniversalTime() - lastSuccessfulImportAtUtc.Value.ToUniversalTime() > staleAfter;
    }

    // Somente a última execução terminal com falha de autorização exige ação explícita antes de outra entrada.
    public static bool IsAutomaticEntrySuppressed(SyncRunState? latestRunState, string? latestFailureCode) =>
        latestRunState == SyncRunState.Failed
        && string.Equals(latestFailureCode, SyncRunFailureCodes.AuthenticationRequired, StringComparison.Ordinal);
}
