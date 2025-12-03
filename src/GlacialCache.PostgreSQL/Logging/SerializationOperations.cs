using Microsoft.Extensions.Logging;

namespace GlacialCache.Logging;

internal static partial class SerializationOperations
{
    [LoggerMessage(
        LoggingIds.DeserializationError,
        LogLevel.Warning,
        "Failed to deserialize cache entry for key: {Key} to type {Type}")]
    public static partial void LogDeserializationError(this ILogger logger, string key, string type, Exception? exception = null);

    [LoggerMessage(
        LoggingIds.SerializationError,
        LogLevel.Warning,
        "Failed to serialize cache entry for key: {Key}")]
    public static partial void LogSerializationError(this ILogger logger, string key, Exception? exception = null);
}
