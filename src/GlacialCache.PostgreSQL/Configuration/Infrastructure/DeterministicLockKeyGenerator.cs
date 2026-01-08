using System.Security.Cryptography;
using System.Text;

namespace GlacialCache.PostgreSQL.Configuration.Infrastructure;

/// <summary>
/// Provides deterministic hash-based lock key generation for PostgreSQL advisory locks.
/// Uses SHA256 to ensure the same input always produces the same output across all .NET versions,
/// processes, and deployments.
/// </summary>
internal static class DeterministicLockKeyGenerator
{
    /// <summary>
    /// Generates a deterministic lock key from the given identifiers.
    /// The same input will always produce the same output, ensuring all instances
    /// with the same configuration compete for the same lock.
    /// </summary>
    /// <param name="prefix">A prefix to differentiate lock purposes (e.g., "schema_creation", "manager_election").</param>
    /// <param name="identifiers">Additional identifiers to include in the hash (e.g., schema name, table name).</param>
    /// <returns>A deterministic 32-bit integer lock key suitable for PostgreSQL advisory locks.</returns>
    public static int GenerateLockKey(string prefix, params string[] identifiers)
    {
        var combined = string.Join("_", new[] { prefix }.Concat(identifiers));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));

        // Use first 4 bytes of hash as a 32-bit integer
        // BitConverter.ToInt32 is deterministic across platforms
        return BitConverter.ToInt32(hash, 0);
    }

    /// <summary>
    /// Generates a deterministic lock key for schema creation coordination.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>A deterministic lock key for schema creation.</returns>
    public static int GenerateSchemaLockKey(string schemaName, string tableName)
    {
        return GenerateLockKey("schema_creation", schemaName, tableName);
    }

    /// <summary>
    /// Generates a deterministic lock key for manager election coordination.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>A deterministic lock key for manager election.</returns>
    public static int GenerateManagerElectionLockKey(string schemaName, string tableName)
    {
        return GenerateLockKey("manager_election", schemaName, tableName);
    }
}
