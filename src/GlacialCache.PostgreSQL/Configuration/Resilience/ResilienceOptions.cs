namespace GlacialCache.PostgreSQL.Configuration.Resilience;


/// <summary>
/// Resilience and fault tolerance configuration options.
/// </summary>
public class ResilienceOptions
{
    /// <summary>
    /// Whether to enable resilience patterns using Polly library. Default is true.
    /// </summary>
    public bool EnableResiliencePatterns { get; set; } = true;

    /// <summary>
    /// Retry configuration options.
    /// </summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Circuit breaker configuration options.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>
    /// Timeout configuration for resilience patterns.
    /// </summary>
    public TimeoutOptions Timeouts { get; set; } = new();

    /// <summary>
    /// Logging configuration for resilience patterns.
    /// </summary>
    public LoggingOptions Logging { get; set; } = new();
}
