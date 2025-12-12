using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.Timeout;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration.Resilience;

namespace GlacialCache.PostgreSQL.Services;
using Configuration;

/// <summary>
/// Factory for creating Polly resilience policies for database operations.
/// </summary>
public class PolicyFactory : IPolicyFactory
{
    private readonly ILogger _logger;

    public PolicyFactory(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a retry policy for transient database failures.
    /// </summary>
    public IAsyncPolicy CreateRetryPolicy(GlacialCachePostgreSQLOptions options)
    {
        return Policy
            .Handle<PostgresException>(IsTransientException)
            .Or<TimeoutException>()
            .Or<System.Net.Sockets.SocketException>() // Network failures
            .Or<System.Net.NetworkInformation.NetworkInformationException>() // Network info failures
            .Or<NpgsqlException>(ex => IsTransientNpgsqlException(ex)) // Npgsql-specific transient errors
            .Or<InvalidOperationException>(ex => IsTransientInvalidOperationException(ex)) // Connection state issues
            .WaitAndRetryAsync(
                options.Resilience.Retry.MaxAttempts,
                retryAttempt => CalculateBackoff(retryAttempt, options.Resilience.Retry.BaseDelay, options.Resilience.Retry.BackoffStrategy),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    if (options.Resilience.Logging.EnableResilienceLogging)
                    {
                        _logger.LogDebug(exception,
                            "Retry attempt {RetryCount} for operation {Operation} after {Delay}ms",
                            retryCount, context.OperationKey ?? "Unknown", timeSpan.TotalMilliseconds);
                    }
                });
    }

    /// <summary>
    /// Creates a circuit breaker policy to prevent cascading failures.
    /// </summary>
    public IAsyncPolicy CreateCircuitBreakerPolicy(GlacialCachePostgreSQLOptions options)
    {
        return Policy
            .Handle<PostgresException>(IsTransientException)
            .Or<TimeoutException>()
            .Or<System.Net.Sockets.SocketException>() // Network failures
            .Or<System.Net.NetworkInformation.NetworkInformationException>() // Network info failures
            .Or<NpgsqlException>(IsTransientNpgsqlException) // Npgsql-specific transient errors
            .Or<InvalidOperationException>(IsTransientInvalidOperationException) // Connection state issues
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: options.Resilience.CircuitBreaker.FailureThreshold,
                durationOfBreak: options.Resilience.CircuitBreaker.DurationOfBreak,
                onBreak: (exception, duration) =>
                {
                    if (options.Resilience.Logging.EnableResilienceLogging)
                    {
                        _logger.LogWarning(exception,
                            "Circuit breaker opened after {FailureCount} failures. Will reset after {Duration}",
                            options.Resilience.CircuitBreaker.FailureThreshold, duration);
                    }
                },
                onReset: () =>
                {
                    if (options.Resilience.Logging.EnableResilienceLogging)
                    {
                        _logger.LogInformation("Circuit breaker reset - attempting normal operation");
                    }
                },
                onHalfOpen: () =>
                {
                    if (options.Resilience.Logging.EnableResilienceLogging)
                    {
                        _logger.LogDebug("Circuit breaker half-open - testing connection");
                    }
                });
    }

    /// <summary>
    /// Creates a timeout policy for database operations.
    /// </summary>
    public IAsyncPolicy CreateTimeoutPolicy(GlacialCachePostgreSQLOptions options)
    {
        return Policy.TimeoutAsync(
            options.Resilience.Timeouts.OperationTimeout,
            TimeoutStrategy.Pessimistic,
            onTimeoutAsync: (context, timeSpan, _) =>
            {
                if (options.Resilience.Logging.EnableResilienceLogging)
                {
                    _logger.LogWarning("Database operation {Operation} timed out after {Timeout}ms",
                        context.OperationKey, timeSpan.TotalMilliseconds);
                }
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// Creates a combined resilience policy that wraps timeout, circuit breaker, and retry policies.
    /// </summary>
    public IAsyncPolicy CreateResiliencePolicy(GlacialCachePostgreSQLOptions options)
    {
        var timeoutPolicy = CreateTimeoutPolicy(options);
        var retryPolicy = CreateRetryPolicy(options);

        if (options.Resilience.CircuitBreaker.Enable || options.Resilience.CircuitBreaker.FailureThreshold > 0)
        {
            var circuitBreakerPolicy = CreateCircuitBreakerPolicy(options);
            // Order: Timeout -> Circuit Breaker -> Retry
            return Policy.WrapAsync(timeoutPolicy, circuitBreakerPolicy, retryPolicy);
        }
        else
        {
            // Order: Timeout -> Retry (no circuit breaker)
            return Policy.WrapAsync(timeoutPolicy, retryPolicy);
        }
    }

    /// <summary>
    /// Calculates backoff delay for retry attempts based on the configured strategy.
    /// </summary>
    private static TimeSpan CalculateBackoff(int retryAttempt, TimeSpan baseDelay, BackoffStrategy strategy)
    {
        double delayMultiplier;

        switch (strategy)
        {
            case BackoffStrategy.Linear:
                // Linear: baseDelay, baseDelay * 2, baseDelay * 3, etc.
                delayMultiplier = retryAttempt;
                break;

            case BackoffStrategy.Exponential:
                // Exponential: baseDelay, baseDelay * 2, baseDelay * 4, etc.
                delayMultiplier = Math.Pow(2, retryAttempt - 1);
                break;

            case BackoffStrategy.ExponentialWithJitter:
            default:
                // Exponential with jitter: exponential backoff with randomization
                delayMultiplier = Math.Pow(2, retryAttempt - 1);
                var jitter = Random.Shared.NextDouble() * 0.1; // 10% jitter
                delayMultiplier *= (1 + jitter);
                break;
        }

        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * delayMultiplier);
    }

    /// <summary>
    /// Determines if a PostgreSQL exception is transient and should be retried.
    /// </summary>
    private static bool IsTransientException(PostgresException ex)
    {
        return ex.SqlState switch
        {
            // Connection failures (likely temporary)
            "08001" => true, // Connection failed - server unavailable or network issue
            "08006" => true, // Connection failure - connection lost during operation
            "08000" => true, // Connection exception - general connection problem
            "08003" => true, // Connection does not exist - connection was closed
            "08004" => true, // SQL server rejected establishment of SQL connection - server overload
            "08007" => true, // Connection failure during transaction - network interruption

            // Resource exhaustion (likely temporary)
            "53300" => true, // Too many connections - connection pool exhausted
            "57014" => true, // Query canceled - server canceled due to resource constraints
            "57000" => true, // Statement timeout - query took too long, server busy

            // Server issues (likely temporary)
            "57P01" => true, // Admin shutdown - server shutting down for maintenance
            "57P02" => true, // Crash shutdown - server crashed, will restart
            "57P03" => true, // Cannot connect now - server temporarily unavailable
            "57P04" => true, // Database shutdown - database shutting down
            "57P05" => true, // Database restart - database restarting

            // Network issues (likely temporary)
            "XX000" => true, // Internal error - some internal errors are transient

            _ => false
        };
    }

    /// <summary>
    /// Determines if a permanent PostgreSQL exception should not be retried.
    /// </summary>
    public static bool IsPermanentException(PostgresException ex)
    {
        return ex.SqlState switch
        {
            // Authentication/authorization failures (permanent)
            "28P01" => true, // Invalid password
            "28P02" => true, // Invalid authorization
            "28P03" => true, // No such role
            "28P04" => true, // Database does not exist

            // Permission errors (permanent)
            "42501" => true, // Insufficient privilege
            "42502" => true, // Syntax error or access rule violation
            "42503" => true, // Insufficient resources - permanent resource limit
            "42504" => true, // Invalid cursor name
            "42505" => true, // Invalid prepare statement name
            "42506" => true, // Invalid schema name

            // Schema/object errors (permanent)
            "3D000" => true, // Invalid catalog name
            "3F000" => true, // Invalid schema name
            "42P01" => true, // Undefined table
            "42P02" => true, // Undefined parameter
            "42P03" => true, // Duplicate cursor
            "42P04" => true, // Duplicate database

            // Data type/format errors (permanent)
            "22P02" => true, // Invalid text representation
            "22P03" => true, // Invalid binary representation
            "22P04" => true, // Bad copy file format
            "22P05" => true, // Untranslatable character

            _ => false
        };
    }

    /// <summary>
    /// Determines if an NpgsqlException is transient and should be retried.
    /// </summary>
    private static bool IsTransientNpgsqlException(NpgsqlException ex)
    {
        // Check if the inner exception is a PostgresException
        if (ex.InnerException is PostgresException pgEx)
        {
            return IsTransientException(pgEx);
        }

        // Check for common transient error messages
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("timeout") ||
               message.Contains("connection") ||
               message.Contains("network") ||
               message.Contains("host") ||
               message.Contains("socket");
    }

    /// <summary>
    /// Determines if an InvalidOperationException is transient and should be retried.
    /// </summary>
    private static bool IsTransientInvalidOperationException(InvalidOperationException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("connection") ||
               message.Contains("timeout") ||
               message.Contains("pool") ||
               message.Contains("network");
    }
}