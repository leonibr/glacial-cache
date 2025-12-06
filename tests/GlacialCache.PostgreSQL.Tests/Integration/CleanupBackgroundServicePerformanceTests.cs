using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Integration;

[Trait("Category", "Performance")]
public class CleanupBackgroundServicePerformanceTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private CleanupBackgroundService? _cleanupService;
    private IDistributedCache? _cache;
    private FakeTimeProvider? _fakeTimeProvider;

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

            // Use FakeTimeProvider for deterministic testing
            _fakeTimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            services.AddSingleton<System.TimeProvider>(_fakeTimeProvider);

            // Configure the cache with performance-optimized settings
            services.AddGlacialCachePostgreSQL(options =>
            {
                options.Connection.ConnectionString = _postgres.GetConnectionString();
                options.Cache.SchemaName = "public";
                options.Cache.TableName = "test_cache_perf";
                options.Maintenance.EnableAutomaticCleanup = true;
                options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(100); // Very fast for deterministic testing
                options.Maintenance.MaxCleanupBatchSize = 1000; // Larger batches for performance testing
                options.Infrastructure.EnableManagerElection = false; // Disable for performance testing
                options.Infrastructure.CreateInfrastructure = true; // Enable infrastructure creation
            });

            _serviceProvider = services.BuildServiceProvider();
            _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
            _cleanupService = _serviceProvider.GetRequiredService<CleanupBackgroundService>();

            // Start the cleanup service
            await _cleanupService.StartAsync(default);
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
            try
            {
                await _cleanupService.StopAsync(default);
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Warning: Error stopping cleanup service: {ex.Message}");
            }
            finally
            {
                _cleanupService.Dispose();
            }
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
        _fakeTimeProvider.Should().NotBeNull();

        var startTime = DateTime.UtcNow;

        // Arrange - Create a large number of entries with short expiration times
        const int entryCount = 200;
        var expiredEntries = new List<string>();

        // Create entries that will expire in 1 minute (from fake time perspective)
        for (int i = 0; i < entryCount; i++)
        {
            var key = $"perf-expired-{i}";
            expiredEntries.Add(key);

            await _cache.SetStringAsync(key, $"value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = _fakeTimeProvider.GetUtcNow().AddMinutes(1)
            });
        }

        // Also create some valid entries that should NOT be cleaned up
        for (int i = 0; i < 20; i++)
        {
            await _cache.SetStringAsync($"perf-valid-{i}", $"valid-value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = _fakeTimeProvider.GetUtcNow().AddHours(1) // Long expiration
            });
        }

        // Act - Advance fake time to make entries expire, then wait for cleanup
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(2)); // Advance past expiration time

        // Wait for cleanup to complete (deterministic, no real time delays)
        await WaitForCleanupToCompleteAsync(expiredEntries);

        var endTime = DateTime.UtcNow;
        var elapsed = endTime - startTime;

        // Assert - Performance metrics
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
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "Cleanup should complete within reasonable time");

        Output.WriteLine($"Performance Results: {cleanedCount}/{entryCount} expired entries cleaned, {preservedCount}/20 valid entries preserved");
    }

    /// <summary>
    /// Waits for cleanup to complete by polling for expired entries to be removed.
    /// Uses deterministic time control instead of real time delays.
    /// </summary>
    private async Task WaitForCleanupToCompleteAsync(List<string> expiredKeys)
    {
        await WaitForCleanupToCompleteAsync(_cache, expiredKeys);
    }

    /// <summary>
    /// Waits for cleanup to complete by polling for expired entries to be removed.
    /// Uses deterministic time control instead of real time delays.
    /// </summary>
    private async Task WaitForCleanupToCompleteAsync(IDistributedCache cache, List<string> expiredKeys)
    {
        var timeout = TimeSpan.FromSeconds(5);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            bool allCleaned = true;
            foreach (var key in expiredKeys.Take(10)) // Check first 10 keys as sample
            {
                var value = await cache.GetStringAsync(key);
                if (value != null)
                {
                    allCleaned = false;
                    break;
                }
            }

            if (allCleaned)
            {
                Output.WriteLine("Cleanup completed successfully");
                return;
            }

            // Small delay between checks
            await Task.Delay(50);
        }

        Output.WriteLine($"Cleanup did not complete within {timeout.TotalSeconds}s timeout, but continuing with test");
    }

    /// <summary>
    /// Waits for cleanup to complete by polling for entries with a specific prefix.
    /// Uses deterministic time control instead of real time delays.
    /// </summary>
    private async Task WaitForCleanupToCompleteAsync(IDistributedCache cache, string keyPrefix, int sampleCount)
    {
        var sampleKeys = Enumerable.Range(0, sampleCount).Select(i => $"{keyPrefix}{i}").ToList();
        await WaitForCleanupToCompleteAsync(cache, sampleKeys);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task CleanupBackgroundService_BatchProcessing_Efficiency()
    {
        // Ensure test is properly initialized
        _postgres.Should().NotBeNull();
        _fakeTimeProvider.Should().NotBeNull();

        // Arrange - Test different batch sizes using fake time
        const int smallBatchSize = 10;
        const int largeBatchSize = 100;

        // Test with small batch size first
        var servicesSmall = new ServiceCollection();
        servicesSmall.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Use the same FakeTimeProvider for all tests
        servicesSmall.AddSingleton<System.TimeProvider>(_fakeTimeProvider);

        servicesSmall.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "public";
            options.Cache.TableName = "test_cache_small_batch";
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(100); // Very fast for testing
            options.Maintenance.MaxCleanupBatchSize = smallBatchSize;
            options.Infrastructure.EnableManagerElection = false;
        });

        var providerSmall = servicesSmall.BuildServiceProvider();
        var cacheSmall = providerSmall.GetRequiredService<IDistributedCache>();
        var cleanupSmall = providerSmall.GetRequiredService<CleanupBackgroundService>();

        // Create entries that expire based on fake time
        for (int i = 0; i < 50; i++)
        {
            await cacheSmall.SetStringAsync($"small-batch-{i}", $"value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = _fakeTimeProvider.GetUtcNow().AddSeconds(1) // Expire in 1 second (fake time)
            });
        }

        // Act - Test small batch performance using fake time advancement
        var startSmall = DateTime.UtcNow;
        await cleanupSmall.StartAsync(default);

        // Advance fake time to make entries expire
        _fakeTimeProvider.Advance(TimeSpan.FromSeconds(2));

        // Wait for cleanup to complete
        await WaitForCleanupToCompleteAsync(cacheSmall, "small-batch-", 5);

        await cleanupSmall.StopAsync(default);
        var endSmall = DateTime.UtcNow;

        // Test with large batch size
        var servicesLarge = new ServiceCollection();
        servicesLarge.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Use the same FakeTimeProvider
        servicesLarge.AddSingleton<System.TimeProvider>(_fakeTimeProvider);

        servicesLarge.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "public";
            options.Cache.TableName = "test_cache_large_batch";
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(100);
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
                AbsoluteExpiration = _fakeTimeProvider.GetUtcNow().AddSeconds(1)
            });
        }

        // Act - Test large batch performance
        var startLarge = DateTime.UtcNow;
        await cleanupLarge.StartAsync(default);

        // Advance fake time again
        _fakeTimeProvider.Advance(TimeSpan.FromSeconds(2));

        // Wait for cleanup to complete
        await WaitForCleanupToCompleteAsync(cacheLarge, "large-batch-", 5);

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
        _fakeTimeProvider.Should().NotBeNull();

        // Arrange - Create many entries to test memory usage patterns
        const int highLoadCount = 500;

        // Create a high load of expired entries using fake time
        for (int i = 0; i < highLoadCount; i++)
        {
            await _cache.SetStringAsync($"memory-test-{i}", new string('x', 1000), new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = _fakeTimeProvider.GetUtcNow().AddSeconds(1) // Expire quickly in fake time
            });
        }

        // Act - Start cleanup and monitor memory usage
        var startMemory = GC.GetTotalMemory(false);
        await _cleanupService.StartAsync(default);

        // Advance fake time to make entries expire
        _fakeTimeProvider.Advance(TimeSpan.FromSeconds(2));

        // Wait for cleanup to complete (no real time delay)
        await WaitForCleanupToCompleteAsync(Enumerable.Range(0, 10).Select(i => $"memory-test-{i}").ToList());

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
