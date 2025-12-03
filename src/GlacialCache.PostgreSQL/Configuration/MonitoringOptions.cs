namespace GlacialCache.PostgreSQL.Configuration;

/// <summary>
/// Monitoring and health check configuration options.
/// </summary>
public class MonitoringOptions
{
    // ClockSync removed in Phase 5 - replaced by TimeConverterService

    /// <summary>
    /// Metrics collection configuration.
    /// </summary>
    public MetricsOptions Metrics { get; set; } = new();

    /// <summary>
    /// Health check configuration.
    /// </summary>
    public HealthCheckOptions HealthChecks { get; set; } = new();
}


/// <summary>
/// Metrics collection configuration options.
/// </summary>
public class MetricsOptions
{
    /// <summary>
    /// Whether to enable metrics collection. Default is true.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// The interval for collecting metrics. Default is 1 minute.
    /// </summary>
    public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Array of enabled metrics to collect.
    /// </summary>
    public string[] EnabledMetrics { get; set; } = { "CacheHits", "CacheMisses", "OperationLatency" };
}

/// <summary>
/// Health check configuration options.
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// Whether to enable health checks. Default is true.
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// The interval for running health checks. Default is 30 seconds.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The timeout for health check operations. Default is 10 seconds.
    /// </summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(10);
}