using System.ComponentModel.DataAnnotations;

namespace GlacialCache.PostgreSQL.Configuration.Resilience;


/// <summary>
/// Circuit breaker configuration options.
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Whether to enable circuit breaker pattern. Default is true.
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    /// The number of failures allowed before the circuit breaker opens. Default is 5.
    /// </summary>
    [Range(1, 100, ErrorMessage = "Circuit breaker failure threshold must be between 1 and 100")]
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// The duration the circuit breaker stays open before attempting to close. Default is 1 minute.
    /// </summary>
    public TimeSpan DurationOfBreak { get; set; } = TimeSpan.FromMinutes(1);
}