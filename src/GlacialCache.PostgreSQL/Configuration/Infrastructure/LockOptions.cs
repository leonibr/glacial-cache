namespace GlacialCache.PostgreSQL.Configuration.Infrastructure;

/// <summary>
/// Lock and coordination configuration options.
/// </summary>
public class LockOptions
{
    /// <summary>
    /// Advisory lock key for infrastructure creation coordination.
    /// Auto-generated at runtime to ensure uniqueness across applications.
    /// </summary>
    public int AdvisoryLockKey { get; private set; }

    /// <summary>
    /// Timeout for infrastructure creation lock acquisition.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Generates a deterministic lock key based on schema and table configuration.
    /// Uses SHA256 to ensure all instances with the same schema/table configuration
    /// generate the same lock key, which is critical for leader election to work correctly.
    /// </summary>
    internal void GenerateLockKey(string schemaName, string tableName)
    {
        // Use deterministic SHA256-based hash to ensure all instances with the same
        // schema/table configuration generate the same lock key for manager election
        AdvisoryLockKey = DeterministicLockKeyGenerator.GenerateManagerElectionLockKey(schemaName, tableName);
    }
}
