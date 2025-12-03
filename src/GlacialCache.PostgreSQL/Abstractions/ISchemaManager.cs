namespace GlacialCache.PostgreSQL.Abstractions;

/// <summary>
/// Manages PostgreSQL schema creation and validation for GlacialCache.
/// Provides idempotent schema operations with comprehensive error handling.
/// </summary>
public interface ISchemaManager
{
    /// <summary>
    /// Ensures the GlacialCache schema and tables exist in the database.
    /// Uses PostgreSQL advisory locks to coordinate multi-instance deployments.
    /// Respects the CreateInfrastructure configuration flag.
    /// </summary>
    /// <param name="token">Cancellation token for the operation</param>
    /// <returns>Task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the application lacks necessary permissions to create schema/tables.
    /// The exception message includes actionable solutions and references to logged manual scripts.
    /// </exception>
    Task EnsureSchemaAsync(CancellationToken token = default);
}
