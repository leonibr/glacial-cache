using Microsoft.Extensions.Logging;

namespace GlacialCache.Logging;

internal static partial class ResilienceOperations
{
    [LoggerMessage(
        LoggingIds.ResilienceConnectionFailure,
        LogLevel.Warning,
        "Database connection failed for {Operation} operation. Key: {Key}")]
    public static partial void LogResilienceConnectionFailure(this ILogger logger, string operation, string? key, Exception? exception = null);

    // Method to log resilience connection failure with custom log level
    public static void LogResilienceConnectionFailureWithLevel(ILogger logger, LogLevel logLevel, string operationName, string? key, Exception? ex)
    {
        switch (logLevel)
        {
            case LogLevel.Trace:
                logger.LogTrace(ex, "Database connection failed for {Operation} operation. Key: {Key}", operationName, key);
                break;
            case LogLevel.Debug:
                logger.LogDebug(ex, "Database connection failed for {Operation} operation. Key: {Key}", operationName, key);
                break;
            case LogLevel.Information:
                logger.LogInformation(ex, "Database connection failed for {Operation} operation. Key: {Key}", operationName, key);
                break;
            case LogLevel.Warning:
                logger.LogWarning(ex, "Database connection failed for {Operation} operation. Key: {Key}", operationName, key);
                break;
            case LogLevel.Error:
                logger.LogError(ex, "Database connection failed for {Operation} operation. Key: {Key}", operationName, key);
                break;
            case LogLevel.Critical:
                logger.LogCritical(ex, "Database connection failed for {Operation} operation. Key: {Key}", operationName, key);
                break;
            default:
                logger.LogWarning(ex, "Database connection failed for {Operation} operation. Key: {Key}", operationName, key);
                break;
        }
    }
}
