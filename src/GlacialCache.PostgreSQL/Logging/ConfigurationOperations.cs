using Microsoft.Extensions.Logging;

namespace GlacialCache.Logging;

/// <summary>
/// Source-generated logging operations for configuration change detection.
/// </summary>
internal static partial class ConfigurationOperations
{
    [LoggerMessage(LoggingIds.ConfigurationPropertyChanged, LogLevel.Debug,
        "Configuration property {PropertyName} changed from {OldValue} to {NewValue}")]
    public static partial void LogConfigurationPropertyChanged(
        this ILogger logger, string propertyName, object? oldValue, object? newValue);

    [LoggerMessage(LoggingIds.ConfigurationPropertyRegistered, LogLevel.Debug,
        "Property change handler registered for {PropertyName}")]
    public static partial void LogConfigurationPropertyRegistered(
        this ILogger logger, string propertyName);

    [LoggerMessage(LoggingIds.ConfigurationPropertyUnregistered, LogLevel.Debug,
        "Property change handlers unregistered for {PropertyName}")]
    public static partial void LogConfigurationPropertyUnregistered(
        this ILogger logger, string propertyName);

    [LoggerMessage(LoggingIds.ConfigurationSyncCompleted, LogLevel.Information,
        "Configuration synchronization completed for {PropertyCount} properties")]
    public static partial void LogConfigurationSyncCompleted(
        this ILogger logger, int propertyCount);

    [LoggerMessage(LoggingIds.ConfigurationSyncError, LogLevel.Error,
        "Error during configuration synchronization")]
    public static partial void LogConfigurationSyncError(
        this ILogger logger, Exception exception);

    [LoggerMessage(LoggingIds.ObservablePropertyError, LogLevel.Error,
        "Error in observable property {PropertyName} change notification")]
    public static partial void LogObservablePropertyError(
        this ILogger logger, string propertyName, Exception exception);

    [LoggerMessage(LoggingIds.ObservablePropertyThreadSafetyWarning, LogLevel.Warning,
        "Potential thread safety issue detected in observable property {PropertyName}")]
    public static partial void LogObservablePropertyThreadSafetyWarning(
        this ILogger logger, string propertyName);
}
