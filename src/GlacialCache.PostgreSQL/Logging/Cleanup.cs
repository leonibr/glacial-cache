using Microsoft.Extensions.Logging;

namespace GlacialCache.Logging;

public static partial class CleanupOperations
{
    // CleanupBackgroundService logging methods
    [LoggerMessage(
        LoggingIds.CleanupServiceStarted,
        LogLevel.Information,
        "CleanupBackgroundService started. Cleanup interval: {CleanupIntervalMinutes} minutes.")]
    public static partial void LogCleanupServiceStarted(this ILogger logger, double cleanupIntervalMinutes);

    [LoggerMessage(
        LoggingIds.CleanupServiceStopping,
        LogLevel.Information,
        "CleanupBackgroundService stopping due to cancellation.")]
    public static partial void LogCleanupServiceStopping(this ILogger logger);

    [LoggerMessage(
        LoggingIds.CleanupServiceSkipped,
        LogLevel.Information,
        "CleanupBackgroundService skipping cleanup - this instance is not the elected manager.")]
    public static partial void LogCleanupServiceSkipped(this ILogger logger);

    [LoggerMessage(
        LoggingIds.CleanupServiceError,
        LogLevel.Error,
        "Unexpected error in CleanupBackgroundService.")]
    public static partial void LogCleanupServiceError(this ILogger logger, Exception exception);

    [LoggerMessage(
        LoggingIds.CleanupServiceDisposed,
        LogLevel.Information,
        "CleanupBackgroundService disposed successfully.")]
    public static partial void LogCleanupServiceDisposed(this ILogger logger);

    // Legacy cleanup methods (keeping for backward compatibility)
    [LoggerMessage(
        LoggingIds.CleanupExpiredItems,
        LogLevel.Information,
        "Cleaned up {Count} expired cache entries.")]
    public static partial void LogCleanupCompleted(this ILogger logger, int count);

    [LoggerMessage(
        LoggingIds.CleanupExpiredItemsError,
        LogLevel.Warning,
        "Error during periodic cleanup.")]
    public static partial void LogCleanupError(this ILogger logger, Exception exception);
}