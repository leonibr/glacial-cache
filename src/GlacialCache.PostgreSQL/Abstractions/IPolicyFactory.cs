using Microsoft.Extensions.Logging;
using Polly;

namespace GlacialCache.PostgreSQL.Abstractions;
using Configuration;

/// <summary>
/// Factory interface for creating Polly resilience policies for database operations.
/// </summary>
public interface IPolicyFactory
{
    /// <summary>
    /// Creates a retry policy for transient database failures.
    /// </summary>
    IAsyncPolicy CreateRetryPolicy(GlacialCachePostgreSQLOptions options);

    /// <summary>
    /// Creates a circuit breaker policy to prevent cascading failures.
    /// </summary>
    IAsyncPolicy CreateCircuitBreakerPolicy(GlacialCachePostgreSQLOptions options);

    /// <summary>
    /// Creates a timeout policy for database operations.
    /// </summary>
    IAsyncPolicy CreateTimeoutPolicy(GlacialCachePostgreSQLOptions options);

    /// <summary>
    /// Creates a combined resilience policy that wraps timeout, circuit breaker, and retry policies.
    /// </summary>
    IAsyncPolicy CreateResiliencePolicy(GlacialCachePostgreSQLOptions options);
}