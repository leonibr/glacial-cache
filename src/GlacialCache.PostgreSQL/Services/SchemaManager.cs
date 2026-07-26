using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Configuration.Security;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GlacialCache.PostgreSQL.Services;

/// <summary>
/// Represents the result of attempting to acquire an advisory lock for schema creation.
/// </summary>
internal enum LockAcquisitionResult
{
    /// <summary>
    /// Lock was successfully acquired by this instance.
    /// </summary>
    Acquired,

    /// <summary>
    /// Lock is held by another instance. This instance should wait for schema readiness.
    /// </summary>
    HeldByOther,

    /// <summary>
    /// Permission was denied to use advisory locks. Coordination is not possible.
    /// </summary>
    PermissionDenied
}

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
    private readonly TimeProvider _timeProvider;

    internal SchemaManager(
        IPostgreSQLDataSource dataSource,
        GlacialCachePostgreSQLOptions options,
        ILogger<SchemaManager> logger,
        IDbNomenclature nomeclature,
        TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nomeclature = nomeclature ?? throw new ArgumentNullException(nameof(nomeclature));
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        var lockResult = await TryAcquireInfrastructureLockAsync(lockConnection, token);

        // Handle different lock acquisition results
        switch (lockResult)
        {
            case LockAcquisitionResult.HeldByOther:
                _logger.LogInformation("Another instance is creating infrastructure, waiting for schema to be ready");
                await WaitForSchemaReadyAsync(token);
                return;

            case LockAcquisitionResult.PermissionDenied:
                _logger.LogWarning(
                    "Advisory lock permission denied - proceeding with schema creation without coordination. " +
                    "In multi-instance deployments, this may cause race conditions. " +
                    "Consider granting advisory lock permissions or coordinating schema creation manually.");
                // Fall through to create schema
                break;

            case LockAcquisitionResult.Acquired:
                _logger.LogInformation("Acquired infrastructure lock, proceeding with schema creation");
                // Fall through to create schema
                break;
        }

        try
        {

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

    private async Task<LockAcquisitionResult> TryAcquireInfrastructureLockAsync(NpgsqlConnection connection, CancellationToken token)
    {
        try
        {
            // Generate lock key based on schema name (using existing pattern)
            var lockKey = GenerateSchemaLockKey(_nomeclature.SchemaName, _nomeclature.TableName);

            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lockKey)", connection);
            command.Parameters.AddWithValue("@lockKey", lockKey);
            command.CommandTimeout = 5; // 5 second timeout

            var result = await command.ExecuteScalarAsync(token);
            var lockAcquired = Convert.ToBoolean(result);

            return lockAcquired ? LockAcquisitionResult.Acquired : LockAcquisitionResult.HeldByOther;
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
            return LockAcquisitionResult.PermissionDenied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire infrastructure lock");
            return LockAcquisitionResult.HeldByOther;
        }
    }

    private static int GenerateSchemaLockKey(string schemaName, string tableName)
    {
        // Use deterministic SHA256-based hash to ensure all instances with the same
        // schema/table configuration generate the same lock key
        return DeterministicLockKeyGenerator.GenerateSchemaLockKey(schemaName, tableName);
    }


    private async Task<bool> CanCreateSchemaAsync(NpgsqlConnection connection, CancellationToken token)
    {
        try
        {
            // Test schema creation with a temporary schema name (validated and lowercased)
            var testSchemaName = PostgreSQLIdentifierSanitizer.ValidateAndNormalize($"glacial_cache_test_{Guid.NewGuid():N}");

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
            // Test table creation in the target schema (validated and lowercased)
            var testTableName = PostgreSQLIdentifierSanitizer.ValidateAndNormalize($"glacial_cache_test_{Guid.NewGuid():N}");

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
        // Log summary at Error level to avoid exposing implementation details
        _logger.LogError(
            "Permission error: Cannot create {PermissionType} due to insufficient permissions. " +
            "Enable Debug logging to see the required SQL script, or grant CREATE privileges to the application user.",
            permissionType);

        // Log full SQL script only at Debug level for security
        _logger.LogDebug(@"
Manual Schema Creation Script:
{Script}

To fix, either:
1. Run the script above with a user who has CREATE privileges
2. Grant permissions: GRANT CREATE ON DATABASE your_database TO your_app_user;
   and GRANT CREATE ON SCHEMA glacial_cache TO your_app_user;
", GetCreateSchemaSql());
    }

    private string GetCreateSchemaSql()
    {
        // Identifiers are validated and lowercase, safe to use directly in SQL
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
value_type TEXT,
value_size INTEGER GENERATED ALWAYS AS (OCTET_LENGTH(value)) STORED
);

-- Migrate installations created before provider-neutral typed entries allowed long generic names.
DROP INDEX IF EXISTS {_nomeclature.SchemaName}.idx_{_nomeclature.TableName}_val_type;
ALTER TABLE {_nomeclature.FullTableName} ALTER COLUMN value_type TYPE TEXT;

-- Performance indexes
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
        // Identifiers are validated and lowercase, safe to use directly in SQL
        var sql = $@"
CREATE TABLE IF NOT EXISTS {_nomeclature.FullTableName} (
key text PRIMARY KEY,
value BYTEA NOT NULL,
absolute_expiration TIMESTAMPTZ,
sliding_interval INTERVAL,
next_expiration TIMESTAMPTZ NOT NULL DEFAULT NOW(),
value_type TEXT,
value_size INTEGER GENERATED ALWAYS AS (OCTET_LENGTH(value)) STORED
);

-- Migrate installations created before provider-neutral typed entries allowed long generic names.
DROP INDEX IF EXISTS {_nomeclature.SchemaName}.idx_{_nomeclature.TableName}_val_type;
ALTER TABLE {_nomeclature.FullTableName} ALTER COLUMN value_type TYPE TEXT;

-- Performance indexes
CREATE INDEX IF NOT EXISTS idx_{_nomeclature.TableName}_val_size
ON {_nomeclature.FullTableName} (value_size);

CREATE INDEX IF NOT EXISTS idx_{_nomeclature.TableName}_next_exp
ON {_nomeclature.FullTableName} (next_expiration);";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);

        _logger.LogInformation("Schema and tables created successfully");
    }

    /// <summary>
    /// Waits for the schema to be ready by polling until the table is accessible.
    /// Used when another instance is creating the infrastructure.
    /// </summary>
    private async Task WaitForSchemaReadyAsync(CancellationToken token)
    {
        var timeout = TimeSpan.FromSeconds(30);
        var startTime = _timeProvider.GetUtcNow();
        var delay = TimeSpan.FromMilliseconds(100);
        const int maxDelay = 2000;
        var attemptCount = 0;

        _logger.LogDebug("Waiting for schema to be ready (timeout: {Timeout}s)", timeout.TotalSeconds);

        while (_timeProvider.GetUtcNow() - startTime < timeout)
        {
            attemptCount++;

            if (await IsSchemaReadyAsync(token))
            {
                _logger.LogInformation("Schema is ready after {AttemptCount} attempt(s) and {ElapsedMs}ms",
                    attemptCount, (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds);
                return;
            }

            _logger.LogDebug("Schema not yet ready, attempt {AttemptCount}, waiting {DelayMs}ms before retry",
                attemptCount, delay.TotalMilliseconds);

            await Task.Delay(delay, token);

            // Exponential backoff with max delay
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, maxDelay));
        }

        _logger.LogWarning(
            "Timeout after {Timeout}s waiting for schema to be ready. " +
            "Proceeding anyway - operations may fail until schema is created by another instance",
            timeout.TotalSeconds);
    }

    /// <summary>
    /// Checks if the schema and table are ready by first verifying table existence
    /// and then testing accessibility.
    /// </summary>
    private async Task<bool> IsSchemaReadyAsync(CancellationToken token)
    {
        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromMilliseconds(50);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await using var connection = await _dataSource.GetConnectionAsync(token);

                // Step 1: Check if table exists using information_schema (more reliable)
                // Identifiers are already lowercase
                await using var existsCommand = new NpgsqlCommand(
                    @"SELECT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = @schema AND table_name = @table
                    )",
                    connection);

                existsCommand.Parameters.AddWithValue("@schema", _nomeclature.SchemaName);
                existsCommand.Parameters.AddWithValue("@table", _nomeclature.TableName);

                var tableExists = (bool)(await existsCommand.ExecuteScalarAsync(token))!;
                if (!tableExists)
                {
                    return false; // Table doesn't exist yet
                }

                // Step 2: Verify table is accessible with a simple query
                await using var accessCommand = new NpgsqlCommand(
                    $"SELECT 1 FROM {_nomeclature.FullTableName} LIMIT 0",
                    connection);

                await accessCommand.ExecuteNonQueryAsync(token);
                return true; // Table exists and is accessible
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") // Relation does not exist
            {
                return false; // Table doesn't exist
            }
            catch (Exception ex)
            {
                // For transient errors, retry up to maxRetries times
                if (attempt == maxRetries)
                {
                    // Log at debug level to avoid noise - this is expected during schema creation
                    _logger.LogDebug(ex, "Error checking if schema is ready after {AttemptCount} attempts", maxRetries);
                    return false;
                }

                // Wait before retrying
                await Task.Delay(retryDelay, token);
                retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff
            }
        }

        return false; // Should never reach here, but compiler requires it
    }
}
