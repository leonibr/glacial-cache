using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GlacialCache.PostgreSQL.Services;

/// <summary>
/// Manages PostgreSQL schema creation and validation for GlacialCache.
/// Provides idempotent schema operations with comprehensive error handling and advisory lock coordination.
/// </summary>
public class SchemaManager : ISchemaManager
{
    private readonly IPostgreSQLDataSource _dataSource;
    private readonly IDbNomenclature _nomeclature;
    private readonly ILogger<SchemaManager> _logger;
    private readonly GlacialCachePostgreSQLOptions _options;

    internal SchemaManager(
        IPostgreSQLDataSource dataSource,
        GlacialCachePostgreSQLOptions options,
        ILogger<SchemaManager> logger,
        IDbNomenclature nomeclature)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nomeclature = nomeclature ?? throw new ArgumentNullException(nameof(nomeclature));
    }

    /// <summary>
    /// Ensures the GlacialCache schema and tables exist in the database.
    /// Uses PostgreSQL advisory locks to coordinate multi-instance deployments.
    /// Respects the CreateInfrastructure configuration flag.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken token = default)
    {
        // Respect CreateInfrastructure configuration flag
        if (!_options.Infrastructure.CreateInfrastructure)
        {
            _logger.LogInformation("Skipping schema creation - CreateInfrastructure is disabled");
            return;
        }

        _logger.LogInformation("Ensuring GlacialCache schema exists");

        // Hold the lock during entire schema creation process
        await using var lockConnection = await _dataSource.GetConnectionAsync(token);

        // Try to acquire PostgreSQL advisory lock
        var lockAcquired = await TryAcquireInfrastructureLockAsync(lockConnection, token);

        if (!lockAcquired)
        {
            _logger.LogInformation("Another instance is creating infrastructure, skipping schema creation");
            return;
        }

        try
        {
            _logger.LogInformation("Acquired infrastructure lock, proceeding with schema creation");

            // Step 1: Check schema permissions and create schema
            if (!await CanCreateSchemaAsync(lockConnection, token))
            {
                LogManualSchemaScript("schema");
                _logger.LogWarning(
                    "Application does not have permission to create schema. " +
                    "Solution: Grant CREATE privilege on the database to the application user. " +
                    "Example: GRANT CREATE ON DATABASE your_database TO your_app_user; " +
                    "Or run the script manually with a user who has CREATE privileges (see logs above).");
            }

            // Create schema first (idempotent)
            await CreateSchemaOnlyAsync(lockConnection, token);

            // Step 2: Now that schema exists, check table permissions and create tables
            if (!await CanCreateTableAsync(lockConnection, token))
            {
                LogManualSchemaScript("table");
                _logger.LogWarning(
                    "Application does not have permission to create tables. " +
                    "Solution: Grant CREATE privilege on the schema to the application user. " +
                    "Example: GRANT CREATE ON SCHEMA glacial_cache TO your_app_user; " +
                    "Or run the script manually with a user who has CREATE privileges (see logs above).");
            }

            // Create tables and indexes (idempotent)
            await CreateTablesAsync(lockConnection, token);

            _logger.LogInformation("✅ GlacialCache schema ensured successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create infrastructure");
            throw;
        }
        // Lock automatically released when lockConnection is disposed
    }

    private async Task<bool> TryAcquireInfrastructureLockAsync(NpgsqlConnection connection, CancellationToken token)
    {
        try
        {
            // Generate lock key based on schema name (using existing pattern)
            var lockKey = GenerateSchemaLockKey(_nomeclature.SchemaName, _nomeclature.TableName);

            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lockKey)", connection);
            command.Parameters.AddWithValue("@lockKey", lockKey);
            command.CommandTimeout = 5; // 5 second timeout

            var result = await command.ExecuteScalarAsync(token);
            return Convert.ToBoolean(result);
        }
        catch (PostgresException ex) when (ex.SqlState == "42501") // Insufficient privilege
        {
            _logger.LogWarning(ex,
                "Advisory lock permission denied. " +
                "Automatic coordination disabled. For multi-instance deployments:\n" +
                "1. Grant permissions: GRANT EXECUTE ON FUNCTION pg_try_advisory_lock(bigint), " +
                "   pg_advisory_unlock(bigint), pg_advisory_lock(bigint), " +
                "   pg_try_advisory_lock_shared(bigint), pg_advisory_unlock_shared(bigint) TO user\n" +
                "2. Or disable coordination: Set CreateInfrastructure=false on all but one instance\n" +
                "3. Manually coordinate schema creation: Choose which instance handles schema creation");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire infrastructure lock");
            return false;
        }
    }

    private int GenerateSchemaLockKey(string schemaName, string tableName)
    {
        var deterministicString = $"schema_creation_{schemaName}_{tableName}";
        return Math.Abs(deterministicString.GetHashCode());
    }


    private async Task<bool> CanCreateSchemaAsync(NpgsqlConnection connection, CancellationToken token)
    {
        try
        {
            // Test schema creation with a temporary schema name
            var testSchemaName = $"glacial_cache_test_{Guid.NewGuid():N}";

            await using var command = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {testSchemaName}", connection);
            await command.ExecuteNonQueryAsync(token);

            // Clean up test schema
            await using var cleanupCommand = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {testSchemaName}", connection);
            await cleanupCommand.ExecuteNonQueryAsync(token);

            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "42501") // Insufficient privilege
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking schema creation permissions");
            return false;
        }
    }

    private async Task<bool> CanCreateTableAsync(NpgsqlConnection connection, CancellationToken token)
    {
        try
        {
            // Test table creation in the target schema
            var testTableName = $"glacial_cache_test_{Guid.NewGuid():N}";

            await using var command = new NpgsqlCommand(
                $"CREATE TABLE IF NOT EXISTS {_nomeclature.SchemaName}.{testTableName} (id SERIAL PRIMARY KEY)",
                connection);
            await command.ExecuteNonQueryAsync(token);

            // Clean up test table
            await using var cleanupCommand = new NpgsqlCommand(
                $"DROP TABLE IF EXISTS {_nomeclature.SchemaName}.{testTableName}",
                connection);
            await cleanupCommand.ExecuteNonQueryAsync(token);

            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "42501") // Insufficient privilege
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking table creation permissions");
            return false;
        }
    }

    private void LogManualSchemaScript(string permissionType)
    {
        _logger.LogError(@"

❌ PERMISSION ERROR - Manual Schema Creation Required
💡 The application cannot create the {PermissionType} due to insufficient permissions.
You can run the following script manually with a user who has CREATE privileges:

## 📋 COPY THE SCRIPT BELOW:

{Script}

✅ After running this script, restart your application.
The script is idempotent and safe to run multiple times.

🔧 Alternative: Grant permissions to your application user:
GRANT CREATE ON DATABASE your_database TO your_app_user;
GRANT CREATE ON SCHEMA glacial_cache TO your_app_user;
", permissionType, GetCreateSchemaSql());
    }

    private string GetCreateSchemaSql()
    {
        return $@"-- GlacialCache PostgreSQL Schema Creation Script

-- This script is idempotent and safe to run multiple times
-- Run this script with a user who has CREATE privileges

CREATE SCHEMA IF NOT EXISTS {_nomeclature.SchemaName};

CREATE TABLE IF NOT EXISTS {_nomeclature.FullTableName} (
key text PRIMARY KEY,
value BYTEA NOT NULL,
absolute_expiration TIMESTAMPTZ,
sliding_interval INTERVAL,
next_expiration TIMESTAMPTZ NOT NULL DEFAULT NOW(),
value_type VARCHAR(255),
value_size INTEGER GENERATED ALWAYS AS (OCTET_LENGTH(value)) STORED
);

-- Performance indexes
CREATE INDEX IF NOT EXISTS idx_{_nomeclature.TableName}_val_type
ON {_nomeclature.FullTableName} (value_type)
WHERE value_type IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_{_nomeclature.TableName}_val_size
ON {_nomeclature.FullTableName} (value_size);

CREATE INDEX IF NOT EXISTS idx_{_nomeclature.TableName}_next_exp
ON {_nomeclature.FullTableName} (next_expiration);

-- Schema creation completed successfully";
    }

    private async Task CreateSchemaOnlyAsync(NpgsqlConnection connection, CancellationToken token)
    {
        var sql = $"CREATE SCHEMA IF NOT EXISTS {_nomeclature.SchemaName};";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);

        _logger.LogInformation("Schema created successfully");
    }

    private async Task CreateTablesAsync(NpgsqlConnection connection, CancellationToken token)
    {
        var sql = $@"
CREATE TABLE IF NOT EXISTS {_nomeclature.FullTableName} (
key text PRIMARY KEY,
value BYTEA NOT NULL,
absolute_expiration TIMESTAMPTZ,
sliding_interval INTERVAL,
next_expiration TIMESTAMPTZ NOT NULL DEFAULT NOW(),
value_type VARCHAR(255),
value_size INTEGER GENERATED ALWAYS AS (OCTET_LENGTH(value)) STORED
);

-- Performance indexes
CREATE INDEX IF NOT EXISTS idx_{_nomeclature.TableName}_val_type
ON {_nomeclature.FullTableName} (value_type)
WHERE value_type IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_{_nomeclature.TableName}_val_size
ON {_nomeclature.FullTableName} (value_size);

CREATE INDEX IF NOT EXISTS idx_{_nomeclature.TableName}_next_exp
ON {_nomeclature.FullTableName} (next_expiration);";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);

        _logger.LogInformation("Schema and tables created successfully");
    }
}
