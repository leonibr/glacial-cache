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
    /// All instances using the same schema/table use the same lock key.
    /// </summary>
    internal void GenerateLockKey(string schemaName, string tableName)
    {
        var deterministicString = $"{schemaName}_{tableName}";
        AdvisoryLockKey = Math.Abs(deterministicString.GetHashCode());
    }
}
