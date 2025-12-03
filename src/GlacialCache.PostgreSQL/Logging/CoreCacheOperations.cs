using Microsoft.Extensions.Logging;

namespace GlacialCache.Logging;

public static partial class CoreCacheOperations
{
    [LoggerMessage(
        LoggingIds.CacheInitialized,
        LogLevel.Information,
        "PostgreSQL cache table initialized successfully.")]
    public static partial void LogCacheInitialized(this ILogger logger);

    [LoggerMessage(
        LoggingIds.CacheGetError,
        LogLevel.Error,
        "Error retrieving cache entry for key: {Key}")]
    public static partial void LogCacheGetError(this ILogger logger, string key, Exception? exception = null);

    [LoggerMessage(
        LoggingIds.CacheSetError,
        LogLevel.Error,
        "Error setting cache entry for key: {Key}")]
    public static partial void LogCacheSetError(this ILogger logger, string key, Exception? exception = null);

    [LoggerMessage(
        LoggingIds.CacheRemoveError,
        LogLevel.Error,
        "Error removing cache entry for key: {Key}")]
    public static partial void LogCacheRemoveError(this ILogger logger, string key, Exception? exception = null);

    [LoggerMessage(
        LoggingIds.CacheRefreshError,
        LogLevel.Error,
        "Error refreshing cache entry for key: {Key}")]
    public static partial void LogCacheRefreshError(this ILogger logger, string key, Exception? exception = null);





    [LoggerMessage(
        LoggingIds.CleanupExpiredItems,
        LogLevel.Information,
        "Cleaned up {DeletedCount} expired cache entries.")]
    public static partial void LogCleanupExpiredItems(this ILogger logger, int deletedCount);

    [LoggerMessage(
        LoggingIds.CleanupExpiredItemsError,
        LogLevel.Warning,
        "Error during cleanup of expired cache entries.")]
    public static partial void LogCleanupExpiredItemsError(this ILogger logger, Exception exception);

    [LoggerMessage(
        LoggingIds.InitializationError,
        LogLevel.Error,
        "Failed to initialize PostgreSQL cache table.")]
    public static partial void LogInitializationError(this ILogger logger, Exception exception);
}
