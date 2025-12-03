namespace GlacialCache.PostgreSQL.Configuration.Infrastructure;

/// <summary>
/// Infrastructure configuration options for GlacialCache.
/// </summary>
public class InfrastructureOptions
{
    /// <summary>
    /// Whether this instance should attempt to create infrastructure (tables, indexes).
    /// Only one instance should have this set to true to prevent race conditions.
    /// Default is false for safe multi-instance deployments.
    /// </summary>
    public bool CreateInfrastructure { get; set; } = false;

    /// <summary>
    /// Whether this instance should attempt to become the manager for database operations.
    /// Only one instance should have this set to true to prevent race conditions.
    /// </summary>
    public bool EnableManagerElection { get; set; } = true;

    /// <summary>
    /// Lock and coordination configuration for advisory locks.
    /// </summary>
    public LockOptions Lock { get; set; } = new();
}
