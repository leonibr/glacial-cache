namespace GlacialCache.PostgreSQL.Configuration.Resilience;

/// <summary>
/// Defines the backoff strategy for retry operations.
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// Linear backoff - delay increases linearly with each attempt.
    /// </summary>
    Linear,

    /// <summary>
    /// Exponential backoff - delay doubles with each attempt.
    /// </summary>
    Exponential,

    /// <summary>
    /// Exponential backoff with jitter to prevent thundering herd.
    /// </summary>
    ExponentialWithJitter
}