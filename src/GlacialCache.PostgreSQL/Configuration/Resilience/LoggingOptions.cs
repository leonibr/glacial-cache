using Microsoft.Extensions.Logging;

namespace GlacialCache.PostgreSQL.Configuration.Resilience;



/// <summary>
/// Logging configuration for resilience patterns.
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// Whether to enable detailed logging for resilience patterns. Default is true.
    /// </summary>
    public bool EnableResilienceLogging { get; set; } = true;

    /// <summary>
    /// The log level for connection failure events. Default is Warning.
    /// </summary>
    public LogLevel ConnectionFailureLogLevel { get; set; } = LogLevel.Warning;
}