using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace GlacialCache.PostgreSQL.Configuration;
using Infrastructure;
using Maintenance;
using Resilience;
using Security;

/// <summary>
/// Configuration options for PostgreSQL distributed cache.
/// </summary>
public class GlacialCachePostgreSQLOptions : IOptions<GlacialCachePostgreSQLOptions>, IValidatableObject
{
    /// <summary>
    /// Connection and database configuration.
    /// </summary>
    public ConnectionOptions Connection { get; set; } = new();

    /// <summary>
    /// Cache-specific configuration.
    /// </summary>
    public CacheOptions Cache { get; set; } = new();

    /// <summary>
    /// Maintenance and cleanup configuration.
    /// </summary>
    public MaintenanceOptions Maintenance { get; set; } = new();

    /// <summary>
    /// Resilience and fault tolerance configuration.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Infrastructure and migration configuration.
    /// </summary>
    public InfrastructureOptions Infrastructure { get; set; } = new();

    /// <summary>
    /// Security and audit configuration.
    /// </summary>
    public SecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Monitoring and health check configuration.
    /// </summary>
    public MonitoringOptions Monitoring { get; set; } = new();

    /// <summary>
    /// Gets the current configuration instance.
    /// </summary>
    public GlacialCachePostgreSQLOptions Value => this;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        // Validate individual sections
        results.AddRange(ValidateConnection(validationContext));
        results.AddRange(ValidateCache(validationContext));
        results.AddRange(ValidateMaintenance(validationContext));
        results.AddRange(ValidateResilience(validationContext));
        results.AddRange(ValidateInfrastructure(validationContext));
        results.AddRange(ValidateMonitoring(validationContext));

        // Cross-section validation
        results.AddRange(ValidateCrossSectionDependencies());

        return results;
    }

    private IEnumerable<ValidationResult> ValidateConnection(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        if (Connection.Pool.MinSize > Connection.Pool.MaxSize)
        {
            results.Add(new ValidationResult(
                "Min connection pool size cannot be greater than max connection pool size",
                new[] { "Connection.Pool.MinSize", "Connection.Pool.MaxSize" }));
        }

        if (Connection.Pool.IdleLifetimeSeconds <= 0)
        {
            results.Add(new ValidationResult(
                "Connection idle lifetime must be positive",
                new[] { "Connection.Pool.IdleLifetimeSeconds" }));
        }

        if (Connection.Pool.PruningIntervalSeconds <= 0)
        {
            results.Add(new ValidationResult(
                "Connection pruning interval must be positive",
                new[] { "Connection.Pool.PruningIntervalSeconds" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateCache(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        if (string.IsNullOrWhiteSpace(Cache.TableName))
        {
            results.Add(new ValidationResult(
                "Cache table name is required",
                new[] { "Cache.TableName" }));
        }

        if (string.IsNullOrWhiteSpace(Cache.SchemaName))
        {
            results.Add(new ValidationResult(
                "Cache schema name is required",
                new[] { "Cache.SchemaName" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateMaintenance(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        if (Maintenance.CleanupInterval <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                "Cleanup interval must be positive",
                new[] { "Maintenance.CleanupInterval" }));
        }

        if (Maintenance.MaxCleanupBatchSize <= 0)
        {
            results.Add(new ValidationResult(
                "Max cleanup batch size must be positive",
                new[] { "Maintenance.MaxCleanupBatchSize" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateResilience(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        if (Resilience.Retry.MaxAttempts < 0)
        {
            results.Add(new ValidationResult(
                "Max retry attempts cannot be negative",
                new[] { "Resilience.Retry.MaxAttempts" }));
        }

        if (Resilience.Retry.BaseDelay <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                "Retry base delay must be positive",
                new[] { "Resilience.Retry.BaseDelay" }));
        }

        if (Resilience.CircuitBreaker.FailureThreshold < 1)
        {
            results.Add(new ValidationResult(
                "Circuit breaker failure threshold must be at least 1",
                new[] { "Resilience.CircuitBreaker.FailureThreshold" }));
        }

        if (Resilience.CircuitBreaker.DurationOfBreak <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                "Circuit breaker duration of break must be positive",
                new[] { "Resilience.CircuitBreaker.DurationOfBreak" }));
        }

        if (Resilience.Timeouts.OperationTimeout <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                "Operation timeout must be positive",
                new[] { "Resilience.Timeouts.OperationTimeout" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateInfrastructure(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        // Infrastructure validation - simplified without migration system
        // No additional validation needed for the simplified infrastructure options

        return results;
    }

    private IEnumerable<ValidationResult> ValidateMonitoring(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        // ClockSync validation removed in Phase 5 - replaced by TimeConverterService

        if (Monitoring.Metrics.MetricsCollectionInterval <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                "Metrics collection interval must be positive",
                new[] { "Monitoring.Metrics.MetricsCollectionInterval" }));
        }

        if (Monitoring.HealthChecks.HealthCheckInterval <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                "Health check interval must be positive",
                new[] { "Monitoring.HealthChecks.HealthCheckInterval" }));
        }

        if (Monitoring.HealthChecks.HealthCheckTimeout <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                "Health check timeout must be positive",
                new[] { "Monitoring.HealthChecks.HealthCheckTimeout" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateCrossSectionDependencies()
    {
        var results = new List<ValidationResult>();

        // Connection pool validation
        if (Connection.Pool.MinSize > Connection.Pool.MaxSize)
        {
            results.Add(new ValidationResult(
                "Min connection pool size cannot be greater than max connection pool size",
                new[] { "Connection.Pool.MinSize", "Connection.Pool.MaxSize" }));
        }

        // Maintenance validation - simplified with new options
        // No additional validation needed for the simplified maintenance options

        // Infrastructure validation - simplified without migration system
        // No additional validation needed for the simplified infrastructure options

        return results;
    }
}