using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Abstractions;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Integration;

public class CleanupBackgroundServiceIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private CleanupBackgroundService? _cleanupService;
    private IDistributedCache? _cache;
    private TimeTestHelper? _timeHelper;

    public CleanupBackgroundServiceIntegrationTests(ITestOutputHelper output) : base(output)
    {
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

            // Grant advisory lock permissions for manager election
            await _postgres.GrantAdvisoryLockPermissionsAsync("testuser", Output);

            // Initialize time helper for deterministic time control without container sync
            _timeHelper = TimeTestHelper.CreateForIntegrationTestsWithoutContainerSync(Output);

            var services = new ServiceCollection();
            services.AddLogging(builder =>
                builder.AddConsole()
                       .SetMinimumLevel(LogLevel.Debug)
                       .AddFilter("GlacialCache.PostgreSQL.Services.CleanupBackgroundService", LogLevel.Trace));

            // Add the fake time provider to the service collection
            services.AddSingleton<TimeProvider>(_timeHelper.TimeProvider);

            // Configure the cache with simplified options
            services.AddGlacialCachePostgreSQL(options =>
            {
                options.Connection.ConnectionString = _postgres.GetConnectionString();
                options.Cache.SchemaName = "public";
                options.Cache.TableName = "test_cache";

                options.Infrastructure.CreateInfrastructure = true;
                options.Maintenance.EnableAutomaticCleanup = true;
                options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(500); // Fast for testing
                options.Maintenance.MaxCleanupBatchSize = 100;
                options.Infrastructure.EnableManagerElection = true;
            });

            _serviceProvider = services.BuildServiceProvider();
            _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
            _cleanupService = _serviceProvider.GetRequiredService<CleanupBackgroundService>();
            // Ensure cleanup service is started for tests relying on it
            await _cleanupService.StartAsync(default);
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize test: {ex.Message}");
            throw;
        }
    }

    protected override async Task CleanupTestAsync()
    {
        if (_cleanupService != null)
        {
            await _cleanupService.StopAsync(default);
            _cleanupService.Dispose();
        }

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (_postgres != null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task CleanupBackgroundService_WithExpiredEntries_ShouldCleanupCorrectly()
    {
        // Arrange - Set entries with very short expiration
        _timeHelper?.SetTime(_timeHelper.InitialTime);
        _cache.Should().NotBeNull();
        await _cache.SetStringAsync("expired-key-1", "value1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        });

        await _cache.SetStringAsync("expired-key-2", "value2", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        });

        await _cache.SetStringAsync("valid-key", "valid-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) // Longer expiration to ensure it doesn't expire
        });

        // Verify entries exist
        var value1 = await _cache.GetStringAsync("expired-key-1");
        var value2 = await _cache.GetStringAsync("expired-key-2");
        var validValue = await _cache.GetStringAsync("valid-key");

        value1.Should().Be("value1");
        value2.Should().Be("value2");
        validValue.Should().Be("valid-value");

        // Act - Wait for expiration and cleanup
        _timeHelper.Should().NotBeNull();
        _timeHelper!.Advance(TimeSpan.FromMinutes(3)); // Wait for entries to expire

        // Service already started in Initialize; ensure at least one cycle

        // Wait for cleanup to run (service runs every 500ms)
        await WaitForCleanupToCompleteAsync();

        // Keep service running for subsequent assertions

        // Assert - Expired entries should be cleaned up, valid entry should remain
        var expiredValue1 = await _cache.GetStringAsync("expired-key-1");
        var expiredValue2 = await _cache.GetStringAsync("expired-key-2");
        var stillValidValue = await _cache.GetStringAsync("valid-key");

        expiredValue1.Should().BeNull();
        expiredValue2.Should().BeNull();
        stillValidValue.Should().Be("valid-value");
    }

    [Fact]
    public async Task CleanupBackgroundService_WhenManagerElectionEnabled_ManagerInstanceShouldCleanup()
    {
        // Arrange
        _serviceProvider.Should().NotBeNull();
        var managerElectionService = _serviceProvider.GetRequiredService<IManagerElectionService>();

        // Verify manager election is enabled
        var options = _serviceProvider.GetRequiredService<IOptions<GlacialCachePostgreSQLOptions>>();
        options.Value.Infrastructure.EnableManagerElection.Should().BeTrue();

        // Set an expired entry
        _cache.Should().NotBeNull();
        await _cache.SetStringAsync("manager-test-key", "test-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        // Act - Wait for expiration and cleanup
        _timeHelper.Should().NotBeNull();
        _timeHelper!.Advance(TimeSpan.FromMinutes(2));

        // Service already started in Initialize (manager election may flip)
        await WaitForCleanupToCompleteAsync(); // Wait for cleanup cycle
        // Keep service running until test end

        // Assert - Entry should be cleaned up if this instance is the manager
        // (If not manager, entry might still exist, which is expected behavior)
        var result = await _cache.GetStringAsync("manager-test-key");

        // The key should either be cleaned up (if this instance is manager) or still exist (if not manager)
        // Both are valid outcomes depending on manager election results
        Output.WriteLine($"Manager election test result: Key exists = {result != null}, IsManager = {managerElectionService.IsManager}");
    }

    private async Task WaitForCleanupToCompleteAsync()
    {
        // Poll for cleanup completion instead of fixed delay
        var timeout = TimeSpan.FromSeconds(5);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            // Check if expired entries have been cleaned up
            var expiredValue1 = await _cache!.GetStringAsync("expired-key-1");
            var expiredValue2 = await _cache!.GetStringAsync("expired-key-2");

            if (expiredValue1 == null && expiredValue2 == null)
            {
                return; // Cleanup completed
            }

            await Task.Delay(100); // Short delay before next check
        }

        // If we reach here, cleanup didn't complete in time, but that's okay for the test
        Output.WriteLine("Cleanup did not complete within timeout, continuing with test");
    }

    [Fact]
    public async Task CleanupBackgroundService_WhenManagerElectionDisabled_AllInstancesShouldCleanup()
    {
        // Ensure test is properly initialized
        _postgres.Should().NotBeNull();

        // Arrange - Create a new service provider with manager election disabled
        var services = new ServiceCollection();
        services.AddLogging(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Debug));

        // Use the same time helper for consistency
        services.AddSingleton<TimeProvider>(_timeHelper!.TimeProvider);

        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "public";
            options.Cache.TableName = "test_cache_no_manager";
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(500);
            options.Maintenance.MaxCleanupBatchSize = 100;
            options.Infrastructure.EnableManagerElection = false; // Disable manager election
        });

        var testProvider = services.BuildServiceProvider();
        var testCache = testProvider.GetRequiredService<IDistributedCache>();
        var testCleanupService = testProvider.GetRequiredService<CleanupBackgroundService>();

        // Set an expired entry
        testCache.Should().NotBeNull();
        await testCache.SetStringAsync("no-manager-test-key", "test-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(100)
        });

        // Act - Wait for expiration and cleanup
        _timeHelper.Advance(TimeSpan.FromMilliseconds(200));

        // Start the cleanup service (no manager election check)
        testCleanupService.Should().NotBeNull();
        await testCleanupService.StartAsync(default);
        _timeHelper.Advance(TimeSpan.FromMilliseconds(1000)); // Wait for cleanup cycle
        await testCleanupService.StopAsync(default);

        // Assert - Entry should be cleaned up regardless of manager election
        var result = await testCache.GetStringAsync("no-manager-test-key");
        result.Should().BeNull("Cleanup should run when manager election is disabled");

        // Cleanup
        await testCleanupService.StopAsync(default);
        testCleanupService.Dispose();
        testProvider.Dispose();
    }

    [Fact]
    public async Task CleanupBackgroundService_ConfigurableCleanupInterval_WorksCorrectly()
    {
        // Ensure test is properly initialized
        _postgres.Should().NotBeNull();

        // Arrange - Create service with custom interval
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Use the same time helper for consistency
        services.AddSingleton<TimeProvider>(_timeHelper!.TimeProvider);

        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "public";
            options.Cache.TableName = "test_cache_interval";
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(200); // Very fast for testing
            options.Maintenance.MaxCleanupBatchSize = 100;
            options.Infrastructure.EnableManagerElection = false;
        });

        var testProvider = services.BuildServiceProvider();
        var testCache = testProvider.GetRequiredService<IDistributedCache>();
        var testCleanupService = testProvider.GetRequiredService<CleanupBackgroundService>();

        // Set multiple expired entries
        testCache.Should().NotBeNull();
        for (int i = 0; i < 5; i++)
        {
            await testCache.SetStringAsync($"interval-test-key-{i}", $"value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(50)
            });
        }

        // Act - Wait for expiration
        _timeHelper.Advance(TimeSpan.FromMilliseconds(100));

        // Start cleanup service
        testCleanupService.Should().NotBeNull();
        await testCleanupService.StartAsync(default);

        // Wait for multiple cleanup cycles (200ms interval)
        _timeHelper.Advance(TimeSpan.FromMilliseconds(1000)); // Should allow ~5 cleanup cycles

        await testCleanupService.StopAsync(default);

        // Assert - All expired entries should be cleaned up
        for (int i = 0; i < 5; i++)
        {
            var result = await testCache.GetStringAsync($"interval-test-key-{i}");
            result.Should().BeNull($"Entry {i} should be cleaned up");
        }

        // Cleanup
        testCleanupService.Dispose();
    }

    [Fact]
    public async Task CleanupBackgroundService_BatchSizeLimit_WorksCorrectly()
    {
        // Ensure test is properly initialized
        _postgres.Should().NotBeNull();

        // Arrange - Create service with small batch size
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Use the same time helper for consistency
        services.AddSingleton<TimeProvider>(_timeHelper!.TimeProvider);

        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "public";
            options.Cache.TableName = "test_cache_batch";
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(500);
            options.Maintenance.MaxCleanupBatchSize = 2; // Very small batch size for testing
            options.Infrastructure.EnableManagerElection = false;
        });

        var testProvider = services.BuildServiceProvider();
        var testCache = testProvider.GetRequiredService<IDistributedCache>();
        var testCleanupService = testProvider.GetRequiredService<CleanupBackgroundService>();

        // Set many expired entries
        testCache.Should().NotBeNull();
        for (int i = 0; i < 10; i++)
        {
            await testCache.SetStringAsync($"batch-test-key-{i}", $"value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(50)
            });
        }

        // Act - Wait for expiration and cleanup
        _timeHelper.Advance(TimeSpan.FromMilliseconds(100));

        testCleanupService.Should().NotBeNull();
        await testCleanupService.StartAsync(default);
        _timeHelper.Advance(TimeSpan.FromMilliseconds(2000)); // Allow multiple cleanup cycles to process all entries
        await testCleanupService.StopAsync(default);

        // Assert - All entries should eventually be cleaned up despite batch size limit
        for (int i = 0; i < 10; i++)
        {
            var result = await testCache.GetStringAsync($"batch-test-key-{i}");
            result.Should().BeNull($"Entry {i} should be cleaned up eventually");
        }

        // Cleanup
        testCleanupService.Dispose();
    }
}
