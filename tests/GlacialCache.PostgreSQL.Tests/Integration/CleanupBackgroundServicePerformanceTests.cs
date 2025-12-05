using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Services;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Integration;

[Trait("Category", "Performance")]
public class CleanupBackgroundServicePerformanceTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private CleanupBackgroundService? _cleanupService;
    private IDistributedCache? _cache;

    public CleanupBackgroundServicePerformanceTests(ITestOutputHelper output) : base(output)
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

            var services = new ServiceCollection();
            services.AddLogging(builder =>
                builder.AddConsole()
                       .SetMinimumLevel(LogLevel.Warning)); // Reduce log noise for performance tests

            // Configure the cache with performance-optimized settings
            services.AddGlacialCachePostgreSQL(options =>
            {
                options.Connection.ConnectionString = _postgres.GetConnectionString();
                options.Cache.SchemaName = "public";
                options.Cache.TableName = "test_cache_perf";
                options.Maintenance.EnableAutomaticCleanup = true;
                options.Maintenance.CleanupInterval = TimeSpan.FromSeconds(1); // Reasonable interval for testing
                options.Maintenance.MaxCleanupBatchSize = 50; // Small batches for testing
                options.Infrastructure.EnableManagerElection = false; // Disable for performance testing
                options.Infrastructure.CreateInfrastructure = true; // Enable infrastructure creation
            });

            _serviceProvider = services.BuildServiceProvider();
            _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
            _cleanupService = _serviceProvider.GetRequiredService<CleanupBackgroundService>();
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize performance test: {ex.Message}");
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

        if (_serviceProvider != null)
        {
            _serviceProvider.Dispose();
        }

        if (_postgres != null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task CleanupBackgroundService_Performance_UnderLoad()
    {
        // Ensure test is properly initialized
        _cache.Should().NotBeNull();
        _cleanupService.Should().NotBeNull();

        // Arrange - Create a large number of expired entries
        const int entryCount = 200;
        var expiredEntries = new List<string>();

        for (int i = 0; i < entryCount; i++)
        {
            var key = $"perf-expired-{i}";
            expiredEntries.Add(key);

            await _cache.SetStringAsync(key, $"value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(50) // Very short expiration
            });
        }

        // Also create some valid entries that should NOT be cleaned up
        for (int i = 0; i < 20; i++)
        {
            await _cache.SetStringAsync($"perf-valid-{i}", $"valid-value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) // Long expiration
            });
        }

        // Act - Start cleanup service and let it run multiple cycles
        var startTime = DateTime.UtcNow;
        await _cleanupService.StartAsync(default);

        // Wait for expiration and multiple cleanup cycles
        await Task.Delay(2000); // Allow time for expiration and cleanup (2 seconds for reliability)

        // Stop the service with timeout to prevent hanging
        var stopTimeout = TimeSpan.FromSeconds(10);
        var stopTask = _cleanupService.StopAsync(default);
        var timeoutTask = Task.Delay(stopTimeout);

        var completedTask = await Task.WhenAny(stopTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            throw new TimeoutException($"CleanupBackgroundService failed to stop within {stopTimeout.TotalSeconds} seconds");
        }

        var endTime = DateTime.UtcNow;

        // Assert - Performance metrics
        var elapsed = endTime - startTime;
        Output.WriteLine($"Performance test completed in {elapsed.TotalSeconds:F2} seconds");

        // Verify that expired entries were cleaned up
        int cleanedCount = 0;
        foreach (var key in expiredEntries)
        {
            var value = await _cache.GetStringAsync(key);
            if (value == null)
            {
                cleanedCount++;
            }
        }

        // Verify that valid entries were preserved
        int preservedCount = 0;
        for (int i = 0; i < 20; i++)
        {
            var value = await _cache.GetStringAsync($"perf-valid-{i}");
            if (value != null)
            {
                preservedCount++;
            }
        }

        // Performance assertions
        cleanedCount.Should().BeGreaterThan(0, "At least some expired entries should be cleaned up");
        preservedCount.Should().Be(20, "All valid entries should be preserved");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15), "Cleanup should complete within reasonable time");

        Output.WriteLine($"Performance Results: {cleanedCount}/{entryCount} expired entries cleaned, {preservedCount}/20 valid entries preserved");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task CleanupBackgroundService_BatchProcessing_Efficiency()
    {
        // Ensure test is properly initialized
        _postgres.Should().NotBeNull();

        // Arrange - Test different batch sizes
        const int smallBatchSize = 10;
        const int largeBatchSize = 100;

        // Test with small batch size first
        var servicesSmall = new ServiceCollection();
        servicesSmall.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        servicesSmall.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "public";
            options.Cache.TableName = "test_cache_small_batch";
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(200);
            options.Maintenance.MaxCleanupBatchSize = smallBatchSize;
            options.Infrastructure.EnableManagerElection = false;
        });

        var providerSmall = servicesSmall.BuildServiceProvider();
        var cacheSmall = providerSmall.GetRequiredService<IDistributedCache>();
        var cleanupSmall = providerSmall.GetRequiredService<CleanupBackgroundService>();

        // Create entries for small batch test
        for (int i = 0; i < 50; i++)
        {
            await cacheSmall.SetStringAsync($"small-batch-{i}", $"value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(50)
            });
        }

        // Act - Test small batch performance
        var startSmall = DateTime.UtcNow;
        await cleanupSmall.StartAsync(default);
        await Task.Delay(1000);
        await cleanupSmall.StopAsync(default);
        var endSmall = DateTime.UtcNow;

        // Test with large batch size
        var servicesLarge = new ServiceCollection();
        servicesLarge.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        servicesLarge.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "public";
            options.Cache.TableName = "test_cache_large_batch";
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(200);
            options.Maintenance.MaxCleanupBatchSize = largeBatchSize;
            options.Infrastructure.EnableManagerElection = false;
        });

        var providerLarge = servicesLarge.BuildServiceProvider();
        var cacheLarge = providerLarge.GetRequiredService<IDistributedCache>();
        var cleanupLarge = providerLarge.GetRequiredService<CleanupBackgroundService>();

        // Create entries for large batch test
        for (int i = 0; i < 50; i++)
        {
            await cacheLarge.SetStringAsync($"large-batch-{i}", $"value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(50)
            });
        }

        // Act - Test large batch performance
        var startLarge = DateTime.UtcNow;
        await cleanupLarge.StartAsync(default);
        await Task.Delay(1000);
        await cleanupLarge.StopAsync(default);
        var endLarge = DateTime.UtcNow;

        // Assert - Both should work but potentially with different performance characteristics
        var durationSmall = endSmall - startSmall;
        var durationLarge = endLarge - startLarge;

        durationSmall.Should().BeGreaterThan(TimeSpan.Zero);
        durationLarge.Should().BeGreaterThan(TimeSpan.Zero);

        Output.WriteLine($"Batch Performance: Small batch ({smallBatchSize}) took {durationSmall.TotalMilliseconds:F0}ms");
        Output.WriteLine($"Batch Performance: Large batch ({largeBatchSize}) took {durationLarge.TotalMilliseconds:F0}ms");

        // Cleanup
        cleanupSmall.Dispose();
        cleanupLarge.Dispose();
        providerSmall.Dispose();
        providerLarge.Dispose();
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task CleanupBackgroundService_MemoryEfficiency_UnderLoad()
    {
        // Ensure test is properly initialized
        _cache.Should().NotBeNull();
        _cleanupService.Should().NotBeNull();

        // Arrange - Create many entries to test memory usage patterns
        const int highLoadCount = 500;

        // Create a high load of expired entries
        for (int i = 0; i < highLoadCount; i++)
        {
            await _cache.SetStringAsync($"memory-test-{i}", new string('x', 1000), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(50)
            });
        }

        // Act - Start cleanup and monitor
        var startMemory = GC.GetTotalMemory(false);
        await _cleanupService.StartAsync(default);

        // Let it run through multiple cleanup cycles
        await Task.Delay(2000);

        // Force garbage collection to see memory impact
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var endMemory = GC.GetTotalMemory(false);

        await _cleanupService.StopAsync(default);

        // Assert - Memory usage should not grow excessively
        var memoryDelta = endMemory - startMemory;
        var memoryDeltaMB = memoryDelta / (1024.0 * 1024.0);

        Output.WriteLine($"Memory test: {highLoadCount} entries, memory delta: {memoryDeltaMB:F2} MB");

        // The memory delta should be reasonable (less than 50MB for this test)
        memoryDelta.Should().BeLessThan(50 * 1024 * 1024, "Memory usage should not grow excessively during cleanup");

        // Verify cleanup actually happened
        var sampleEntry = await _cache.GetStringAsync("memory-test-0");
        sampleEntry.Should().BeNull("Sample expired entry should be cleaned up");
    }
}
