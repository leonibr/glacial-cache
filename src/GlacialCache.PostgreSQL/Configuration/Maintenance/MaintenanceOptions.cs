using System.ComponentModel.DataAnnotations;

namespace GlacialCache.PostgreSQL.Configuration.Maintenance;

/// <summary>
/// Maintenance and cleanup configuration options.
/// </summary>
public class MaintenanceOptions
{
    /// <summary>
    /// Whether to enable automatic periodic cleanup. Default: true
    /// </summary>
    public bool EnableAutomaticCleanup { get; set; } = true;

    /// <summary>
    /// How often to run cleanup. Default: 30 minutes
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum entries to clean up per batch. Default: 1000
    /// </summary>
    [Range(1, 10000, ErrorMessage = "Max cleanup batch size must be between 1 and 10000")]
    public int MaxCleanupBatchSize { get; set; } = 1000;
}


