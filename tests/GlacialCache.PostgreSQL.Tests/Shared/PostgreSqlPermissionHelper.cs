using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// Helper methods for granting PostgreSQL permissions in test containers.
/// </summary>
public static class PostgreSqlPermissionHelper
{
    /// <summary>
    /// Grants advisory lock permissions to the specified user.
    /// This is required for manager election functionality.
    /// </summary>
    /// <param name="container">The PostgreSQL container.</param>
    /// <param name="username">The username to grant permissions to (default: "testuser").</param>
    /// <param name="output">Optional test output helper for logging.</param>
    public static async Task GrantAdvisoryLockPermissionsAsync(
        this PostgreSqlContainer container,
        string username = "testuser",
        ITestOutputHelper? output = null)
    {
        var connectionString = container.GetConnectionString();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        try
        {
            // Grant advisory lock function permissions
            var advisoryLockFunctions = new[]
            {
                "pg_try_advisory_lock(bigint)",
                "pg_advisory_unlock(bigint)",
                "pg_advisory_lock(bigint)",
                "pg_try_advisory_lock_shared(bigint)",
                "pg_advisory_unlock_shared(bigint)"
            };

            foreach (var function in advisoryLockFunctions)
            {
                await using var command = new NpgsqlCommand(
                    $"GRANT EXECUTE ON FUNCTION {function} TO {username}",
                    connection);
                await command.ExecuteNonQueryAsync();
            }

            output?.WriteLine($"✅ Granted advisory lock permissions to {username}");
        }
        catch (Exception ex)
        {
            output?.WriteLine($"⚠️ Warning: Failed to grant advisory lock permissions: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Grants all necessary permissions for GlacialCache operations including advisory locks.
    /// This includes CREATE permissions, schema permissions, and advisory lock permissions.
    /// </summary>
    /// <param name="container">The PostgreSQL container.</param>
    /// <param name="databaseName">The database name (default: "testdb").</param>
    /// <param name="username">The username to grant permissions to (default: "testuser").</param>
    /// <param name="output">Optional test output helper for logging.</param>
    public static async Task GrantAllGlacialCachePermissionsAsync(
        this PostgreSqlContainer container,
        string databaseName = "testdb",
        string username = "testuser",
        ITestOutputHelper? output = null)
    {
        var connectionString = container.GetConnectionString();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        try
        {
            // Grant CREATE privilege on the database
            await using var command1 = new NpgsqlCommand(
                $"GRANT CREATE ON DATABASE {databaseName} TO {username}",
                connection);
            await command1.ExecuteNonQueryAsync();

            // Grant CREATE and USAGE on public schema
            await using var command2 = new NpgsqlCommand(
                $"GRANT CREATE ON SCHEMA public TO {username}",
                connection);
            await command2.ExecuteNonQueryAsync();

            await using var command3 = new NpgsqlCommand(
                $"GRANT USAGE ON SCHEMA public TO {username}",
                connection);
            await command3.ExecuteNonQueryAsync();

            // Grant advisory lock permissions
            await container.GrantAdvisoryLockPermissionsAsync(username, output);

            output?.WriteLine($"✅ Granted all GlacialCache permissions to {username}");
        }
        catch (Exception ex)
        {
            output?.WriteLine($"⚠️ Warning: Failed to grant permissions: {ex.Message}");
            throw;
        }
    }
}

