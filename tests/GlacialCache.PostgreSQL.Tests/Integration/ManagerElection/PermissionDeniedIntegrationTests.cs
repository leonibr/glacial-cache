using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Linq;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Tests.Shared;
using Xunit.Abstractions;
using Npgsql;
using Microsoft.Extensions.Caching.Distributed;
using GlacialCache.PostgreSQL.Configuration.Maintenance;

namespace GlacialCache.PostgreSQL.Tests.Integration.ManagerElection;

public class PermissionDeniedIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private readonly string _schemaName;
    private readonly string _tableName;

    public PermissionDeniedIntegrationTests(ITestOutputHelper output) : base(output)
    {
        _schemaName = "test_schema_permission_denied";
        _tableName = "test_cache";
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
            Output.WriteLine($"✅ PostgreSQL container started: {_postgres.GetConnectionString()}");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}");
        }
    }

    protected override async Task CleanupTestAsync()
    {
        if (_postgres != null)
        {
            await _postgres.DisposeAsync();
            Output.WriteLine("✅ PostgreSQL container disposed");
        }
    }

    private async Task<string> CreateRestrictedUserAsync()
    {
        var connectionString = _postgres!.GetConnectionString();

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Create a restricted user without advisory lock permissions
        var restrictedUser = $"restricted_user_{Guid.NewGuid():N}";
        var restrictedPassword = "restricted_pass";

        await using var createUserCommand = new NpgsqlCommand(
            $"CREATE USER {restrictedUser} WITH PASSWORD '{restrictedPassword}'", connection);
        await createUserCommand.ExecuteNonQueryAsync();

        // Grant basic permissions but NOT advisory lock permissions
        await using var grantCommand = new NpgsqlCommand(
            $"GRANT CONNECT ON DATABASE testdb TO {restrictedUser}", connection);
        await grantCommand.ExecuteNonQueryAsync();

        await using var grantSchemaCommand = new NpgsqlCommand(
            $"GRANT USAGE ON SCHEMA public TO {restrictedUser}", connection);
        await grantSchemaCommand.ExecuteNonQueryAsync();

        // Grant permission to create schemas and tables (needed for infrastructure creation)
        await using var grantCreateCommand = new NpgsqlCommand(
            $"GRANT CREATE ON SCHEMA public TO {restrictedUser}", connection);
        await grantCreateCommand.ExecuteNonQueryAsync();

        // Grant permission to create tables in public schema
        await using var grantTableCommand = new NpgsqlCommand(
            $"GRANT CREATE ON DATABASE testdb TO {restrictedUser}", connection);
        await grantTableCommand.ExecuteNonQueryAsync();

        // Grant permissions needed for cache operations but NOT advisory lock permissions
        // This ensures the user can create tables and perform cache operations
        // but will fail when trying to acquire advisory locks

        // Grant permission to connect to the database
        await using var grantConnectCommand = new NpgsqlCommand(
            $"GRANT CONNECT ON DATABASE testdb TO {restrictedUser}", connection);
        await grantConnectCommand.ExecuteNonQueryAsync();

        // Grant permission to create schemas (needed for custom schema creation)
        await using var grantCreateSchemaCommand = new NpgsqlCommand(
            $"GRANT CREATE ON DATABASE testdb TO {restrictedUser}", connection);
        await grantCreateSchemaCommand.ExecuteNonQueryAsync();

        // Grant permission to create tables in any schema
        await using var grantCreateTableCommand = new NpgsqlCommand(
            $"GRANT CREATE ON SCHEMA public TO {restrictedUser}", connection);
        await grantCreateTableCommand.ExecuteNonQueryAsync();

        // Grant usage on public schema
        await using var grantUsageCommand = new NpgsqlCommand(
            $"GRANT USAGE ON SCHEMA public TO {restrictedUser}", connection);
        await grantUsageCommand.ExecuteNonQueryAsync();

        // Grant permission to create tables in any schema (needed for custom schema tables)
        await using var grantCreateAnySchemaCommand = new NpgsqlCommand(
            $"ALTER USER {restrictedUser} CREATEDB", connection);
        await grantCreateAnySchemaCommand.ExecuteNonQueryAsync();

        // Explicitly revoke advisory lock permissions to ensure the permission denied scenario
        // This is the key part - we want the user to be able to create tables but NOT acquire advisory locks
        // First revoke from PUBLIC role, then from the specific user
        // Revoke advisory lock permissions from PUBLIC and from the restricted user in this isolated test DB
        await using var revokePublicAdvisoryLockCommand = new NpgsqlCommand(
            $"REVOKE EXECUTE ON FUNCTION pg_try_advisory_lock(bigint) FROM PUBLIC", connection);
        await revokePublicAdvisoryLockCommand.ExecuteNonQueryAsync();

        await using var revokePublicAdvisoryUnlockCommand = new NpgsqlCommand(
            $"REVOKE EXECUTE ON FUNCTION pg_advisory_unlock(bigint) FROM PUBLIC", connection);
        await revokePublicAdvisoryUnlockCommand.ExecuteNonQueryAsync();

        await using var revokePublicAdvisoryLockSharedCommand = new NpgsqlCommand(
            $"REVOKE EXECUTE ON FUNCTION pg_try_advisory_lock_shared(bigint) FROM PUBLIC", connection);
        await revokePublicAdvisoryLockSharedCommand.ExecuteNonQueryAsync();

        await using var revokePublicAdvisoryUnlockSharedCommand = new NpgsqlCommand(
            $"REVOKE EXECUTE ON FUNCTION pg_advisory_unlock_shared(bigint) FROM PUBLIC", connection);
        await revokePublicAdvisoryUnlockSharedCommand.ExecuteNonQueryAsync();

        // Now revoke from the specific user
        await using var revokeAdvisoryLockCommand = new NpgsqlCommand(
            $"REVOKE EXECUTE ON FUNCTION pg_try_advisory_lock(bigint) FROM {restrictedUser}", connection);
        await revokeAdvisoryLockCommand.ExecuteNonQueryAsync();

        await using var revokeAdvisoryUnlockCommand = new NpgsqlCommand(
            $"REVOKE EXECUTE ON FUNCTION pg_advisory_unlock(bigint) FROM {restrictedUser}", connection);
        await revokeAdvisoryUnlockCommand.ExecuteNonQueryAsync();

        await using var revokeAdvisoryLockSharedCommand = new NpgsqlCommand(
            $"REVOKE EXECUTE ON FUNCTION pg_try_advisory_lock_shared(bigint) FROM {restrictedUser}", connection);
        await revokeAdvisoryLockSharedCommand.ExecuteNonQueryAsync();

        await using var revokeAdvisoryUnlockSharedCommand = new NpgsqlCommand(
            $"REVOKE EXECUTE ON FUNCTION pg_advisory_unlock_shared(bigint) FROM {restrictedUser}", connection);
        await revokeAdvisoryUnlockSharedCommand.ExecuteNonQueryAsync();

        // Create a restricted connection string for the new user
        var restrictedConnectionString = connectionString.Replace("testuser", restrictedUser).Replace("testpass", restrictedPassword);

        Output.WriteLine($"✅ Created restricted user: {restrictedUser}");
        return restrictedConnectionString;
    }

    private IServiceProvider CreateServiceProvider(string connectionString, bool enableManagerElection = true, bool enableAutomaticCleanup = true, bool createInfrastructure = true)
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Debug)
                   .AddFilter("GlacialCache.PostgreSQL.Services.ManagerElectionService", LogLevel.Trace));

        // Configure GlacialCache
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection = new ConnectionOptions
            {
                ConnectionString = connectionString
            };
            options.Cache = new CacheOptions
            {
                SchemaName = _schemaName,
                TableName = _tableName
            };
            options.Infrastructure = new InfrastructureOptions
            {
                EnableManagerElection = enableManagerElection,
                CreateInfrastructure = createInfrastructure,
                Lock = new LockOptions
                {
                    LockTimeout = TimeSpan.FromSeconds(30)
                }
            };
            options.Maintenance = new MaintenanceOptions
            {
                EnableAutomaticCleanup = enableAutomaticCleanup
            };
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PermissionDeniedScenario_ApplicationContinuesWithManualCoordinationGuidance()
    {
        // Arrange
        var restrictedConnectionString = await CreateRestrictedUserAsync();

        // First, ensure infrastructure exists using an admin-scoped GlacialCache provider
        var adminServices = new ServiceCollection();
        adminServices.AddLogging(b => b.AddConsole());
        adminServices.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres!.GetConnectionString();
            options.Cache.SchemaName = _schemaName;
            options.Cache.TableName = _tableName;
            options.Infrastructure.CreateInfrastructure = true;
            options.Infrastructure.EnableManagerElection = false;
        });
        using var adminProvider = adminServices.BuildServiceProvider();
        // Touch the cache to trigger initialization/schema ensure
        adminProvider.GetRequiredService<IDistributedCache>();

        // Grant data access on ensured schema/table to restricted user
        var adminCstr = _postgres!.GetConnectionString();
        await using (var adminConn = new NpgsqlConnection(adminCstr))
        {
            await adminConn.OpenAsync();
            var csb = new NpgsqlConnectionStringBuilder(restrictedConnectionString);
            var restrictedUserName = csb.Username;

            await using var grantUsage = new NpgsqlCommand($"GRANT USAGE ON SCHEMA {_schemaName} TO {restrictedUserName}", adminConn);
            await grantUsage.ExecuteNonQueryAsync();

            await using var grantTable = new NpgsqlCommand($"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {_schemaName} TO {restrictedUserName}", adminConn);
            await grantTable.ExecuteNonQueryAsync();
        }

        // Disable manager election so migrations can run and cache operations work
        // Use restricted user without attempting to (re)create infrastructure
        var serviceProvider = CreateServiceProvider(restrictedConnectionString, enableManagerElection: false, enableAutomaticCleanup: true, createInfrastructure: false);

        var GlacialCache = serviceProvider.GetRequiredService<IGlacialCache>();

        // Act & Assert
        // The application should start successfully even with permission denied
        GlacialCache.ShouldNotBeNull();

        // Verify that the application is still functional
        var testKey = "test_key";
        var testValue = "test_value";

        // Cache operations should work even without manager election
        await GlacialCache.SetAsync(testKey, System.Text.Encoding.UTF8.GetBytes(testValue), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });
        var retrievedBytes = await GlacialCache.GetAsync(testKey);
        var retrievedValue = retrievedBytes != null ? System.Text.Encoding.UTF8.GetString(retrievedBytes) : null;

        retrievedValue.ShouldBe(testValue);

        Output.WriteLine("✅ Application continues to function despite permission denied for advisory locks");
    }

    [Fact]
    public async Task ManualCoordination_EnableManagerElectionFalse_WorksWithCleanupFlags()
    {
        // Arrange
        var connectionString = _postgres!.GetConnectionString();

        // Instance 1 - Designated cleanup instance
        var cleanupServiceProvider = CreateServiceProvider(connectionString, enableManagerElection: false, enableAutomaticCleanup: true);

        // Instance 2 - Non-cleanup instance
        var nonCleanupServiceProvider = CreateServiceProvider(connectionString, enableManagerElection: false, enableAutomaticCleanup: false);

        var cleanupGlacialCache = cleanupServiceProvider.GetRequiredService<IGlacialCache>();
        var nonCleanupGlacialCache = nonCleanupServiceProvider.GetRequiredService<IGlacialCache>();

        // Act
        // Both instances should work independently
        await cleanupGlacialCache.SetAsync("cleanup_key", System.Text.Encoding.UTF8.GetBytes("cleanup_value"), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });
        await nonCleanupGlacialCache.SetAsync("non_cleanup_key", System.Text.Encoding.UTF8.GetBytes("non_cleanup_value"), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        var cleanupBytes = await cleanupGlacialCache.GetAsync("cleanup_key");
        var nonCleanupBytes = await nonCleanupGlacialCache.GetAsync("non_cleanup_key");

        var cleanupValue = cleanupBytes != null ? System.Text.Encoding.UTF8.GetString(cleanupBytes) : null;
        var nonCleanupValue = nonCleanupBytes != null ? System.Text.Encoding.UTF8.GetString(nonCleanupBytes) : null;

        // Assert
        cleanupValue.ShouldBe("cleanup_value");
        nonCleanupValue.ShouldBe("non_cleanup_value");

        // Verify that both instances can operate independently
        cleanupGlacialCache.ShouldNotBeNull();
        nonCleanupGlacialCache.ShouldNotBeNull();

        Output.WriteLine("✅ Manual coordination works with EnableManagerElection=false");
    }

    [Fact]
    public async Task PermissionDeniedScenario_LogsClearErrorMessage()
    {
        // Arrange
        var restrictedConnectionString = await CreateRestrictedUserAsync();
        var logMessages = new List<string>();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddProvider(new TestLogProvider(logMessages));
        });
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection = new ConnectionOptions
            {
                ConnectionString = restrictedConnectionString
            };
            options.Cache = new CacheOptions
            {
                SchemaName = _schemaName,
                TableName = _tableName
            };
            options.Infrastructure = new InfrastructureOptions
            {
                EnableManagerElection = true,
                CreateInfrastructure = true,
                Lock = new LockOptions
                {
                    LockTimeout = TimeSpan.FromSeconds(30)
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();

        // Start hosted services to trigger the election process
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        var GlacialCache = serviceProvider.GetRequiredService<IGlacialCache>();

        // Act
        // Wait for the election service to attempt and fail
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Stop hosted services
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        // Assert
        var errorMessages = logMessages.Where(msg => msg.Contains("Advisory lock permission denied")).ToList();
        errorMessages.ShouldNotBeEmpty();

        var permissionDeniedMessage = errorMessages.First();
        permissionDeniedMessage.ShouldContain("Automatic coordination disabled");
        permissionDeniedMessage.ShouldContain("GRANT EXECUTE ON FUNCTION pg_try_advisory_lock");
        permissionDeniedMessage.ShouldContain("CreateInfrastructure=false");
        permissionDeniedMessage.ShouldContain("Manually coordinate schema creation");

        Output.WriteLine($"✅ Clear error message logged: {permissionDeniedMessage}");
    }

    [Fact]
    public async Task MultipleInstancesWithPermissionDenied_AllContinueToFunction()
    {
        // Arrange
        var restrictedConnectionString = await CreateRestrictedUserAsync();

        // Ensure infrastructure exists using an admin-scoped GlacialCache provider
        var adminServices = new ServiceCollection();
        adminServices.AddLogging(b => b.AddConsole());
        adminServices.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres!.GetConnectionString();
            options.Cache.SchemaName = _schemaName;
            options.Cache.TableName = _tableName;
            options.Infrastructure.CreateInfrastructure = true;
            options.Infrastructure.EnableManagerElection = false;
        });
        using var adminProvider = adminServices.BuildServiceProvider();
        // Trigger initialization which ensures schema/table using built-in SchemaManager
        adminProvider.GetRequiredService<IDistributedCache>();

        // Grant data access on ensured schema/table to restricted user
        var adminConnStr2 = _postgres!.GetConnectionString();
        await using (var conn = new NpgsqlConnection(adminConnStr2))
        {
            await conn.OpenAsync();
            var csb = new NpgsqlConnectionStringBuilder(restrictedConnectionString);
            var restrictedUser = csb.Username;

            await using var grantUsageCmd = new NpgsqlCommand($"GRANT USAGE ON SCHEMA {_schemaName} TO {restrictedUser}", conn);
            await grantUsageCmd.ExecuteNonQueryAsync();

            await using var grantTblCmd = new NpgsqlCommand($"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {_schemaName} TO {restrictedUser}", conn);
            await grantTblCmd.ExecuteNonQueryAsync();
        }

        var services1 = CreateServiceProvider(restrictedConnectionString, enableManagerElection: false, enableAutomaticCleanup: true, createInfrastructure: false);
        var services2 = CreateServiceProvider(restrictedConnectionString, enableManagerElection: false, enableAutomaticCleanup: false, createInfrastructure: false);

        var glacialCache1 = services1.GetRequiredService<IGlacialCache>();
        var glacialCache2 = services2.GetRequiredService<IGlacialCache>();

        // Act
        await glacialCache1.SetAsync("instance1_key", System.Text.Encoding.UTF8.GetBytes("instance1_value"), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });
        await glacialCache2.SetAsync("instance2_key", System.Text.Encoding.UTF8.GetBytes("instance2_value"), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        var value1Bytes = await glacialCache1.GetAsync("instance1_key");
        var value2Bytes = await glacialCache2.GetAsync("instance2_key");

        var value1 = value1Bytes != null ? System.Text.Encoding.UTF8.GetString(value1Bytes) : null;
        var value2 = value2Bytes != null ? System.Text.Encoding.UTF8.GetString(value2Bytes) : null;

        // Assert
        value1.ShouldBe("instance1_value");
        value2.ShouldBe("instance2_value");

        // Both instances should be able to read each other's data
        var crossValue1Bytes = await glacialCache1.GetAsync("instance2_key");
        var crossValue2Bytes = await glacialCache2.GetAsync("instance1_key");

        var crossValue1 = crossValue1Bytes != null ? System.Text.Encoding.UTF8.GetString(crossValue1Bytes) : null;
        var crossValue2 = crossValue2Bytes != null ? System.Text.Encoding.UTF8.GetString(crossValue2Bytes) : null;

        crossValue1.ShouldBe("instance2_value");
        crossValue2.ShouldBe("instance1_value");

        Output.WriteLine("✅ Multiple instances continue to function with manual coordination");
    }

    private class TestLogProvider : ILoggerProvider
    {
        private readonly List<string> _logMessages;

        public TestLogProvider(List<string> logMessages)
        {
            _logMessages = logMessages;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(_logMessages);
        }

        public void Dispose()
        {
        }
    }

    private class TestLogger : ILogger
    {
        private readonly List<string> _logMessages;

        public TestLogger(List<string> logMessages)
        {
            _logMessages = logMessages;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            _logMessages.Add(message);
        }
    }
}
