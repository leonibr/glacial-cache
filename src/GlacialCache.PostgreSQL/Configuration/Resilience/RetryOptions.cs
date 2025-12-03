using System.ComponentModel.DataAnnotations;

namespace GlacialCache.PostgreSQL.Configuration.Resilience;

/// <summary>
/// Retry configuration options.
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// The maximum number of retry attempts for transient failures. Default is 3.
    /// </summary>
    [Range(0, 10, ErrorMessage = "Max retry attempts must be between 0 and 10")]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// The base delay between retry attempts. Default is 1 second.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The backoff strategy to use for retries. Default is ExponentialWithJitter.
    /// </summary>
    public BackoffStrategy BackoffStrategy { get; set; } = BackoffStrategy.ExponentialWithJitter;
}
