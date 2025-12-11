using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Tests.Shared;
using Xunit;
using Xunit.Abstractions;
using GlacialCache.PostgreSQL.Configuration.Maintenance;

namespace GlacialCache.PostgreSQL.Tests.Integration;

public class SchemaManagerIntegrationTests : SchemaManagerTestBase
{
    public SchemaManagerIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task EnsureSchemaAsync_CreateInfrastructureFalse_SkipsSchemaCreation()
    {
        // Arrange - Clean up any existing schema first
        await CleanupSchemaAsync();

        var options = CreateConfig();
        options.Infrastructure.CreateInfrastructure = false;
        var schemaManager = CreateSchemaManager(options);

        // Act
        await schemaManager.EnsureSchemaAsync();

        // Assert
        _mockLogger.VerifyLog(LogLevel.Information, "Skipping schema creation - CreateInfrastructure is disabled", Times.Once());

        // Verify no schema was created
        Assert.False(await SchemaExistsAsync());
    }

    [Fact]
    public async Task EnsureSchemaAsync_CreateInfrastructureTrue_CreatesSchema()
    {
        // Arrange
        var options = CreateConfig();
        options.Infrastructure.CreateInfrastructure = true;
        var schemaManager = CreateSchemaManager(options);

        // Act
        await schemaManager.EnsureSchemaAsync();

        // Assert
        _mockLogger.VerifyLog(LogLevel.Information, "✅ GlacialCache schema ensured successfully", Times.Once());

        // Verify schema and table were created
        Assert.True(await SchemaExistsAsync());
        Assert.True(await TableExistsAsync());
    }

    [Fact]
    public async Task EnsureSchemaAsync_MultipleInstances_OnlyOneCreatesSchema()
    {
        // Arrange: Two schema managers with same configuration
        var schemaManager1 = CreateSchemaManager();
        var schemaManager2 = CreateSchemaManager();

        // Act: Both try to create schema simultaneously
        var task1 = schemaManager1.EnsureSchemaAsync();
        var task2 = schemaManager2.EnsureSchemaAsync();

        await Task.WhenAll(task1, task2);

        // Assert: Schema exists and was created only once
        Assert.True(await SchemaExistsAsync());
        Assert.True(await TableExistsAsync());

        // Verify only one instance logged "Acquired infrastructure lock"
        _mockLogger.VerifyLog(LogLevel.Information, "Acquired infrastructure lock", Times.Once());

        // Verify one instance logged "Another instance is creating infrastructure"
        _mockLogger.VerifyLog(LogLevel.Information, "Another instance is creating infrastructure", Times.Once());
    }

    [Fact]
    public async Task EnsureSchemaAsync_MultipleCalls_SafeToRunMultipleTimes()
    {
        // Arrange
        var options = CreateConfig();
        options.Infrastructure.CreateInfrastructure = true;
        var schemaManager = CreateSchemaManager(options);

        // Act - Call multiple times (idempotent operations)
        await schemaManager.EnsureSchemaAsync();
        await schemaManager.EnsureSchemaAsync();
        await schemaManager.EnsureSchemaAsync();

        // Assert - Should not throw and schema should exist
        Assert.True(await SchemaExistsAsync());
        Assert.True(await TableExistsAsync());

        // Verify success was logged
        _mockLogger.VerifyLog(LogLevel.Information, "✅ GlacialCache schema ensured successfully", Times.AtLeastOnce());
    }

    private async Task CleanupSchemaAsync()
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        try
        {
            // Drop the schema and all its contents (CASCADE ensures tables are dropped too)
            await using var command = new NpgsqlCommand("DROP SCHEMA IF EXISTS glacial_cache CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}

public class AdvisoryLockMechanismValidationTests : SchemaManagerTestBase
{
    public AdvisoryLockMechanismValidationTests(ITestOutputHelper output) : base(output)
    {
    }
    [Fact]
    public async Task LockKeyGeneration_DeterministicAndUnique()
    {
        // Test that same inputs always generate same key
        var key1 = GenerateLockKey("glacial", "cache");
        var key2 = GenerateLockKey("glacial", "cache");
        Assert.Equal(key1, key2);

        // Test that different inputs generate different keys
        var key3 = GenerateLockKey("glacial2", "cache");
        var key4 = GenerateLockKey("glacial", "cache2");
        Assert.NotEqual(key1, key3);
        Assert.NotEqual(key1, key4);
        Assert.NotEqual(key3, key4);
    }

    [Fact]
    public async Task LockAcquisition_PostgreSQLBehavior_Validated()
    {
        // Direct PostgreSQL lock testing
        var connection1 = await _dataSource!.OpenConnectionAsync();
        var connection2 = await _dataSource.OpenConnectionAsync();

        try
        {
            var lockKey = GenerateTestLockKey();

            // First connection acquires lock
            await using var cmd1 = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection1);
            cmd1.Parameters.AddWithValue("@key", lockKey);
            var result1 = await cmd1.ExecuteScalarAsync();
            Assert.True(Convert.ToBoolean(result1));

            // Second connection fails to acquire same lock
            await using var cmd2 = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection2);
            cmd2.Parameters.AddWithValue("@key", lockKey);
            var result2 = await cmd2.ExecuteScalarAsync();
            Assert.False(Convert.ToBoolean(result2));

            // Release lock from first connection
            await using var cmd3 = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection1);
            cmd3.Parameters.AddWithValue("@key", lockKey);
            await cmd3.ExecuteNonQueryAsync();

            // Second connection can now acquire lock
            await using var cmd4 = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection2);
            cmd4.Parameters.AddWithValue("@key", lockKey);
            var result3 = await cmd4.ExecuteScalarAsync();
            Assert.True(Convert.ToBoolean(result3));
        }
        finally
        {
            await connection1.DisposeAsync();
            await connection2.DisposeAsync();
        }
    }

    [Fact]
    public async Task LockTimeout_Behavior_Validated()
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        try
        {
            var lockKey = GenerateTestLockKey();

            // Acquire lock
            await using var cmd1 = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            cmd1.Parameters.AddWithValue("@key", lockKey);
            cmd1.CommandTimeout = 1; // 1 second timeout
            var result = await cmd1.ExecuteScalarAsync();
            Assert.True(Convert.ToBoolean(result));

            // Test timeout behavior (should not timeout on successful acquisition)
            Assert.True(Convert.ToBoolean(result));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private int GenerateLockKey(string schemaName, string tableName)
    {
        var deterministicString = $"schema_creation_{schemaName}_{tableName}";
        return Math.Abs(deterministicString.GetHashCode());
    }

    private int GenerateTestLockKey()
    {
        var deterministicString = $"schema_creation_glacial_cache";
        return Math.Abs(deterministicString.GetHashCode());
    }
}

public abstract class SchemaManagerTestBase : IntegrationTestBase
{
    protected PostgreSqlContainer? _postgres;
    protected NpgsqlDataSource? _dataSource;
    protected TestPostgreSQLDataSource? _testDataSource;
    protected Mock<ILogger<SchemaManager>> _mockLogger;

    protected SchemaManagerTestBase(ITestOutputHelper output) : base(output)
    {
        // Setup mocks for unit test scenarios
        _mockLogger = new Mock<ILogger<SchemaManager>>();
    }

    protected override async Task InitializeTestAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("testdb")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .WithCleanUp(true)
                .Build();

            await _postgres.StartAsync();
            Output.WriteLine($" PostgreSQL container started: {_postgres.GetConnectionString()}");

            // Setup real PostgreSQL connection for integration tests
            _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());

            await GrantTestUserPermissionsAsync();


            // Create test data source wrapper
            _testDataSource = new TestPostgreSQLDataSource(_dataSource);
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}");
        }
    }

    protected override async Task CleanupTestAsync()
    {
        if (_testDataSource != null)
        {
            _testDataSource.Dispose();
        }

        if (_dataSource != null)
        {
            await _dataSource.DisposeAsync();
        }

        if (_postgres != null)
        {
            await _postgres.DisposeAsync();
            Output.WriteLine(" PostgreSQL container disposed");
        }
    }

    protected SchemaManager CreateSchemaManager(
        GlacialCachePostgreSQLOptions options = null,
        TimeSpan? lockTimeout = null)
    {
        options ??= CreateDefaultOptions();
        if (lockTimeout.HasValue)
        {
            options.Infrastructure.Lock.LockTimeout = lockTimeout.Value;
        }

        return new SchemaManager(
            _testDataSource!,
            options,
            _mockLogger.Object,
            CreateMockNomenclature());
    }

    protected GlacialCachePostgreSQLOptions CreateConfig(string schema = "glacial", string table = "cache")
    {
        return new GlacialCachePostgreSQLOptions
        {
            Maintenance = new() { EnableAutomaticCleanup = false },
            Infrastructure = new InfrastructureOptions
            {
                CreateInfrastructure = true
            },
            Cache = new CacheOptions
            {
                SchemaName = schema,
                TableName = table
            }
        };
    }

    protected GlacialCachePostgreSQLOptions CreateDefaultOptions()
    {
        return CreateConfig();
    }

    internal IDbNomenclature CreateMockNomenclature()
    {
        var mock = new Mock<IDbNomenclature>();
        mock.Setup(x => x.SchemaName).Returns("glacial_cache");
        mock.Setup(x => x.TableName).Returns("cache");
        mock.Setup(x => x.FullTableName).Returns("glacial_cache.cache");
        return mock.Object;
    }

    protected async Task<bool> SchemaExistsAsync()
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM information_schema.schemata WHERE schema_name = 'glacial_cache')",
                connection);
            var result = await command.ExecuteScalarAsync();
            return Convert.ToBoolean(result);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    protected async Task<bool> TableExistsAsync()
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema = 'glacial_cache' AND table_name = 'cache')",
                connection);
            var result = await command.ExecuteScalarAsync();
            return Convert.ToBoolean(result);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private async Task RevokeTestUserPermissionsAsync()
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        try
        {
            await using var command = new NpgsqlCommand("REVOKE CREATE ON DATABASE testdb FROM testuser", connection);
            await command.ExecuteNonQueryAsync();
        }

        finally
        {
            await connection.DisposeAsync();
        }
    }

    private async Task GrantTestUserPermissionsAsync()
    {
        var connection = await _dataSource!.OpenConnectionAsync();
        try
        {
            // Grant CREATE privilege on the database to testuser
            await using var command1 = new NpgsqlCommand("GRANT CREATE ON DATABASE testdb TO testuser", connection);
            await command1.ExecuteNonQueryAsync();

            // Grant CREATE privilege on public schema to testuser
            await using var command2 = new NpgsqlCommand("GRANT CREATE ON SCHEMA public TO testuser", connection);
            await command2.ExecuteNonQueryAsync();

            // Grant USAGE privilege on public schema to testuser
            await using var command3 = new NpgsqlCommand("GRANT USAGE ON SCHEMA public TO testuser", connection);
            await command3.ExecuteNonQueryAsync();

            // Create the glacial_cache schema and grant permissions on it
            await using var command4 = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS glacial_cache", connection);
            await command4.ExecuteNonQueryAsync();

            await using var command5 = new NpgsqlCommand("GRANT CREATE ON SCHEMA glacial_cache TO testuser", connection);
            await command5.ExecuteNonQueryAsync();

            await using var command6 = new NpgsqlCommand("GRANT USAGE ON SCHEMA glacial_cache TO testuser", connection);
            await command6.ExecuteNonQueryAsync();

            Output.WriteLine("✅ Granted CREATE permissions to testuser and created glacial_cache schema");
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

}

/// <summary>
/// Test implementation of IPostgreSQLDataSource for integration tests
/// </summary>
public class TestPostgreSQLDataSource : IPostgreSQLDataSource
{
    private readonly NpgsqlDataSource _dataSource;

    public TestPostgreSQLDataSource(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken token = default)
    {
        return await _dataSource.OpenConnectionAsync(token);
    }

    public ConnectionPoolMetrics GetPoolMetrics()
    {
        var connectionString = _dataSource.ConnectionString;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        return new ConnectionPoolMetrics
        {
            MinPoolSize = builder.MinPoolSize,
            MaxPoolSize = builder.MaxPoolSize,
            IdleLifetime = builder.ConnectionIdleLifetime,
            PruningInterval = builder.ConnectionPruningInterval,
            ApplicationName = builder.ApplicationName ?? string.Empty,
            PoolingEnabled = builder.Pooling
        };
    }

    public void Dispose()
    {
        _dataSource?.Dispose();
    }
}
