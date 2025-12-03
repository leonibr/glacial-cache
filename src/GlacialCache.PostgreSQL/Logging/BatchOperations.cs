using Microsoft.Extensions.Logging;

namespace GlacialCache.Logging;

internal static partial class BatchOperations
{
    [LoggerMessage(
        LoggingIds.BatchSetSuccess,
        LogLevel.Debug,
        "Successfully set {EntryCount} cache entries in batch")]
    public static partial void LogBatchSetSuccess(this ILogger logger, int entryCount);

    [LoggerMessage(
        LoggingIds.BatchSetError,
        LogLevel.Error,
        "Error setting {EntryCount} cache entries in batch")]
    public static partial void LogBatchSetError(this ILogger logger, int entryCount, Exception exception);

    [LoggerMessage(
        LoggingIds.BatchGetSuccess,
        LogLevel.Debug,
        "Successfully retrieved {EntryCount} cache entries in batch")]
    public static partial void LogBatchGetSuccess(this ILogger logger, int entryCount);

    [LoggerMessage(
        LoggingIds.BatchGetError,
        LogLevel.Error,
        "Error retrieving {EntryCount} cache entries in batch")]
    public static partial void LogBatchGetError(this ILogger logger, int entryCount, Exception exception);

    [LoggerMessage(
        LoggingIds.BatchRemoveSuccess,
        LogLevel.Debug,
        "Successfully removed {EntryCount} cache entries in batch")]
    public static partial void LogBatchRemoveSuccess(this ILogger logger, int entryCount);

    [LoggerMessage(
        LoggingIds.BatchRemoveError,
        LogLevel.Error,
        "Error removing {EntryCount} cache entries in batch")]
    public static partial void LogBatchRemoveError(this ILogger logger, int entryCount, Exception exception);

    [LoggerMessage(
        LoggingIds.BatchRefreshSuccess,
        LogLevel.Debug,
        "Successfully refreshed {EntryCount} cache entries in batch")]
    public static partial void LogBatchRefreshSuccess(this ILogger logger, int entryCount);

    [LoggerMessage(
        LoggingIds.BatchRefreshError,
        LogLevel.Error,
        "Error refreshing {EntryCount} cache entries in batch")]
    public static partial void LogBatchRefreshError(this ILogger logger, int entryCount, Exception exception);

    [LoggerMessage(
        LoggingIds.LargeBatchProcessing,
        LogLevel.Debug,
        "Processing large batch of {EntryCount} entries in chunks")]
    public static partial void LogLargeBatchProcessing(this ILogger logger, int entryCount);
}
