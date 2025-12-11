using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Services;
using Xunit.Abstractions;
using Npgsql;
using System.Text;
using GlacialCache.PostgreSQL.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Integration test to verify that dynamic table/schema name changes work correctly with real database operations.
/// This test verifies that SQL updates correctly when table/schema names change at runtime, and that cache operations
/// work with the new table/schema names. This is a gap that was identified - no existing test verifies this scenario.
/// </summary>
public sealed class DynamicTableSchemaChangeIntegrationTest : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private IGlacialCache? _glacialCache;
    private IDistributedCache? _distributedCache;
    private IOptionsMonitor<GlacialCachePostgreSQLOptions>? _optionsMonitor;
    private NpgsqlDataSource? _dataSource;

    public DynamicTableSchemaChangeIntegrationTest(ITestOutputHelper output) : base(output)
    {
    }

    protected override async Task InitializeTestAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithUsername("test")
                .WithPassword("test")
                .WithDatabase("glacialcache_test")
                .WithCleanUp(true)
                .Build();

            await _postgres.StartAsync();
            Output.WriteLine($"✅ PostgreSQL container started: {_postgres.GetConnectionString()}");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());

            services.AddGlacialCachePostgreSQL(options =>
            {
                options.Connection.ConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { ApplicationName = GetType().Name }.ConnectionString;
                options.Cache.SchemaName = "test_schema_1";
                options.Cache.TableName = "test_table_1";
                options.Infrastructure.EnableManagerElection = false;
                options.Infrastructure.CreateInfrastructure = true;
            });

            _serviceProvider = services.BuildServiceProvider();
            _glacialCache = _serviceProvider.GetRequiredService<IGlacialCache>();
            _distributedCache = _serviceProvider.GetRequiredService<IDistributedCache>();
            _optionsMonitor = _serviceProvider.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();

            // Create data source for manual table operations
            _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}");
        }
    }

    protected override async Task CleanupTestAsync()
    {
        try
        {
            if (_dataSource != null)
            {
                await _dataSource.DisposeAsync();
            }

            if (_serviceProvider != null)
            {
                await _serviceProvider.DisposeAsync();
            }

            if (_postgres != null)
            {
                await _postgres.DisposeAsync();
                Output.WriteLine("✅ PostgreSQL container disposed");
            }
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Error during cleanup: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a table and schema in the database for testing.
    /// </summary>
    private async Task CreateTableInDatabaseAsync(string schemaName, string tableName)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();

        // Create schema if it doesn't exist
        await using var createSchemaCmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {schemaName}", connection);
        await createSchemaCmd.ExecuteNonQueryAsync();

        // Create table using the same structure as GlacialCache
        var createTableSql = $@"
            CREATE TABLE IF NOT EXISTS {schemaName}.{tableName} (
                key text PRIMARY KEY,
                value BYTEA NOT NULL,
                absolute_expiration TIMESTAMPTZ,
                sliding_interval INTERVAL,
                next_expiration TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                value_type VARCHAR(255),
                value_size INTEGER GENERATED ALWAYS AS (OCTET_LENGTH(value)) STORED
            )";

        await using var createTableCmd = new NpgsqlCommand(createTableSql, connection);
        await createTableCmd.ExecuteNonQueryAsync();

        Output.WriteLine($"✅ Created table {schemaName}.{tableName}");
    }

    [Fact]
    public async Task TableNameChange_AtRuntime_ShouldWorkWithCacheOperations()
    {
        // Arrange - Verify cache works with initial table
        const string initialKey = "initial-key";
        const string initialValue = "initial-value";

        await _distributedCache!.SetStringAsync(initialKey, initialValue);
        var retrieved1 = await _distributedCache.GetStringAsync(initialKey);
        retrieved1.ShouldBe(initialValue, "Cache should work with initial table");

        // Act - Change table name at runtime
        const string newTableName = "test_table_2";
        _optionsMonitor!.CurrentValue.Cache.TableNameObservable.Value = newTableName;

        // Allow time for SQL to rebuild (observable property change events are processed asynchronously)
        await Task.Delay(200);

        // Create the new table in the database
        await CreateTableInDatabaseAsync("test_schema_1", newTableName);

        // Verify cache operations work with new table
        const string newKey = "new-table-key";
        const string newValue = "new-table-value";

        await _distributedCache.SetStringAsync(newKey, newValue);
        var retrieved2 = await _distributedCache.GetStringAsync(newKey);
        retrieved2.ShouldBe(newValue, "Cache operations should work with new table name");

        // Verify old table data is not accessible (cache should not find it in new table)
        var oldRetrieved = await _distributedCache.GetStringAsync(initialKey);
        oldRetrieved.ShouldBeNull("Old table data should not be accessible after table name change");
    }

    [Fact]
    public async Task SchemaNameChange_AtRuntime_ShouldWorkWithCacheOperations()
    {
        // Arrange - Verify cache works with initial schema
        const string initialKey = "initial-schema-key";
        const string initialValue = "initial-schema-value";

        await _distributedCache!.SetStringAsync(initialKey, initialValue);
        var retrieved1 = await _distributedCache.GetStringAsync(initialKey);
        retrieved1.ShouldBe(initialValue, "Cache should work with initial schema");

        // Act - Change schema name at runtime
        const string newSchemaName = "test_schema_2";
        _optionsMonitor!.CurrentValue.Cache.SchemaNameObservable.Value = newSchemaName;

        // Allow time for SQL to rebuild
        await Task.Delay(200);

        // Create the new schema and table in the database
        await CreateTableInDatabaseAsync(newSchemaName, "test_table_1");

        // Verify cache operations work with new schema
        const string newKey = "new-schema-key";
        const string newValue = "new-schema-value";

        await _distributedCache.SetStringAsync(newKey, newValue);
        var retrieved2 = await _distributedCache.GetStringAsync(newKey);
        retrieved2.ShouldBe(newValue, "Cache operations should work with new schema name");

        // Verify old schema data is not accessible
        var oldRetrieved = await _distributedCache.GetStringAsync(initialKey);
        oldRetrieved.ShouldBeNull("Old schema data should not be accessible after schema name change");
    }

    [Fact]
    public async Task BothTableAndSchemaNameChange_AtRuntime_ShouldWorkWithCacheOperations()
    {
        // Arrange - Verify cache works with initial table/schema
        const string initialKey = "initial-both-key";
        const string initialValue = "initial-both-value";

        await _distributedCache!.SetStringAsync(initialKey, initialValue);
        var retrieved1 = await _distributedCache.GetStringAsync(initialKey);
        retrieved1.ShouldBe(initialValue, "Cache should work with initial table/schema");

        // Act - Change both table and schema names at runtime
        const string newSchemaName = "test_schema_3";
        const string newTableName = "test_table_3";

        _optionsMonitor!.CurrentValue.Cache.SchemaNameObservable.Value = newSchemaName;
        await Task.Delay(100); // Allow first change to process

        _optionsMonitor.CurrentValue.Cache.TableNameObservable.Value = newTableName;
        await Task.Delay(200); // Allow second change to process

        // Create the new schema and table in the database
        await CreateTableInDatabaseAsync(newSchemaName, newTableName);

        // Verify cache operations work with new table/schema
        const string newKey = "new-both-key";
        const string newValue = "new-both-value";

        await _distributedCache.SetStringAsync(newKey, newValue);
        var retrieved2 = await _distributedCache.GetStringAsync(newKey);
        retrieved2.ShouldBe(newValue, "Cache operations should work with new table and schema names");

        // Verify old table/schema data is not accessible
        var oldRetrieved = await _distributedCache.GetStringAsync(initialKey);
        oldRetrieved.ShouldBeNull("Old table/schema data should not be accessible after both names change");
    }

    [Fact]
    public async Task TableNameChange_ShouldAllowOperationsOnBothOldAndNewTables()
    {
        // Arrange - Create data in initial table
        const string oldTableKey = "old-table-key";
        const string oldTableValue = "old-table-value";

        await _distributedCache!.SetStringAsync(oldTableKey, oldTableValue);
        var retrievedOld = await _distributedCache.GetStringAsync(oldTableKey);
        retrievedOld.ShouldBe(oldTableValue);

        // Act - Change table name and create new table
        const string newTableName = "test_table_4";
        _optionsMonitor!.CurrentValue.Cache.TableNameObservable.Value = newTableName;
        await Task.Delay(200);

        await CreateTableInDatabaseAsync("test_schema_1", newTableName);

        // Verify new table works
        const string newTableKey = "new-table-key";
        const string newTableValue = "new-table-value";

        await _distributedCache.SetStringAsync(newTableKey, newTableValue);
        var retrievedNew = await _distributedCache.GetStringAsync(newTableKey);
        retrievedNew.ShouldBe(newTableValue, "New table should work");

        // Verify old table data is still in database but not accessible through cache
        // (This verifies the cache is using the new table, not the old one)
        var oldRetrieved = await _distributedCache.GetStringAsync(oldTableKey);
        oldRetrieved.ShouldBeNull("Cache should use new table, so old table data should not be accessible");

        // Verify we can still access old data directly from database (proving it exists but cache uses new table)
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var checkOldCmd = new NpgsqlCommand(
            "SELECT value FROM test_schema_1.test_table_1 WHERE key = @key",
            connection);
        checkOldCmd.Parameters.AddWithValue("key", oldTableKey);
        var oldValueFromDb = await checkOldCmd.ExecuteScalarAsync() as byte[];
        oldValueFromDb.ShouldNotBeNull("Old table data should still exist in database");
        Encoding.UTF8.GetString(oldValueFromDb!).ShouldBe(oldTableValue);
    }
}
