using Microsoft.Extensions.Logging;

namespace GlacialCache.Logging;

/// <summary>
/// Source-generated logging operations for election state management.
/// </summary>
internal static partial class ElectionOperations
{
    // Election State Operations

    [LoggerMessage(LoggingIds.ElectionStateInitialized, LogLevel.Information,
        "ElectionState initialized for instance {InstanceId}")]
    public static partial void LogElectionStateInitialized(
        this ILogger logger, string instanceId);

    [LoggerMessage(LoggingIds.ElectionStateUpdated, LogLevel.Information,
        "Election state updated to {NewState} for instance {InstanceId}")]
    public static partial void LogElectionStateUpdated(
        this ILogger logger, string newState, string instanceId);

    [LoggerMessage(LoggingIds.ElectionStateConcurrentAccess, LogLevel.Warning,
        "Concurrent access to election state detected for instance {InstanceId}")]
    public static partial void LogElectionStateConcurrentAccess(
        this ILogger logger, string instanceId);

    // Election Background Service Operations

    [LoggerMessage(LoggingIds.ElectionServiceStarted, LogLevel.Information,
        "ElectionBackgroundService started for instance {InstanceId}")]
    public static partial void LogElectionServiceStarted(
        this ILogger logger, string instanceId);

    [LoggerMessage(LoggingIds.ElectionServiceStopped, LogLevel.Information,
        "ElectionBackgroundService stopped for instance {InstanceId}")]
    public static partial void LogElectionServiceStopped(
        this ILogger logger, string instanceId);

    [LoggerMessage(LoggingIds.ElectionLeadershipAcquired, LogLevel.Information,
        "Leadership acquired for instance {InstanceId}")]
    public static partial void LogElectionLeadershipAcquired(
        this ILogger logger, string instanceId);

    [LoggerMessage(LoggingIds.ElectionLeadershipLost, LogLevel.Warning,
        "Leadership lost for instance {InstanceId}. Reason: {Reason}")]
    public static partial void LogElectionLeadershipLost(
        this ILogger logger, string instanceId, string reason);

    [LoggerMessage(LoggingIds.ElectionVoluntaryYield, LogLevel.Information,
        "Instance {InstanceId} voluntarily yielding leadership after {Duration}")]
    public static partial void LogElectionVoluntaryYield(
        this ILogger logger, string instanceId, TimeSpan duration);

    [LoggerMessage(LoggingIds.ElectionBackoffAttempt, LogLevel.Debug,
        "Instance {InstanceId} backing off for attempt {AttemptCount} with delay {Delay}")]
    public static partial void LogElectionBackoffAttempt(
        this ILogger logger, string instanceId, int attemptCount, TimeSpan delay);

    [LoggerMessage(LoggingIds.ElectionAdvisoryLockAcquired, LogLevel.Debug,
        "Advisory lock acquired for instance {InstanceId}")]
    public static partial void LogElectionAdvisoryLockAcquired(
        this ILogger logger, string instanceId);

    [LoggerMessage(LoggingIds.ElectionAdvisoryLockFailed, LogLevel.Warning,
        "Failed to acquire advisory lock for instance {InstanceId}")]
    public static partial void LogElectionAdvisoryLockFailed(
        this ILogger logger, string instanceId);

    [LoggerMessage(LoggingIds.ElectionLockVerification, LogLevel.Debug,
        "Advisory lock ({LockKey}) verification completed for instance {InstanceId}. Still holds lock: {StillHoldsLock}")]
    public static partial void LogElectionLockVerification(
        this ILogger logger, string instanceId, int lockKey, bool stillHoldsLock);

    [LoggerMessage(LoggingIds.ElectionServiceError, LogLevel.Error,
        "Unexpected error in election background service for instance {InstanceId}")]
    public static partial void LogElectionServiceError(
        this ILogger logger, string instanceId, Exception exception);

    [LoggerMessage(LoggingIds.ElectionServiceDisposed, LogLevel.Debug,
        "ElectionBackgroundService disposed for instance {InstanceId}")]
    public static partial void LogElectionServiceDisposed(
        this ILogger logger, string instanceId);
}
