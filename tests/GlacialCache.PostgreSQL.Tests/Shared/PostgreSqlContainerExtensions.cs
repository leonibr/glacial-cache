using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// Extension methods for PostgreSqlContainer to handle common startup issues in CI environments.
/// </summary>
public static class PostgreSqlContainerExtensions
{
    /// <summary>
    /// Starts a PostgreSQL container with retry logic to handle Docker conflicts.
    /// This is useful in CI environments where containers from previous tests may not be fully cleaned up.
    /// </summary>
    /// <param name="container">The container to start.</param>
    /// <param name="output">Test output helper for logging.</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3).</param>
    /// <param name="retryDelayMs">Delay between retries in milliseconds (default: 1000).</param>
    /// <returns>The started container.</returns>
    /// <exception cref="Exception">Thrown if all retry attempts fail.</exception>
    public static async Task<PostgreSqlContainer> StartWithRetryAsync(
        this PostgreSqlContainer container,
        ITestOutputHelper output,
        int maxRetries = 3,
        int retryDelayMs = 1000)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await container.StartAsync();
                return container;
            }
            catch (Exception ex) when (attempt < maxRetries && ex.Message.Contains("is not running", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine($"⚠️ Container conflict on attempt {attempt}/{maxRetries}: {ex.Message}");
                output.WriteLine($"Waiting {retryDelayMs}ms before retry...");

                // Dispose the failed container
                try
                {
                    await container.DisposeAsync();
                }
                catch
                {
                    // Ignore disposal errors during retry
                }

                // Wait before retrying
                await Task.Delay(retryDelayMs);
            }
        }

        // If we get here, all retries failed
        throw new Exception($"Failed to start PostgreSQL container after {maxRetries} attempts");
    }
}

