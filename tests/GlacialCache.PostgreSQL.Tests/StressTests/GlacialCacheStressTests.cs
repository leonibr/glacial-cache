using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Tests.Shared;

namespace GlacialCache.PostgreSQL.Tests.StressTests;

/// <summary>
/// Stress tests for GlacialCache main operations including concurrent access, memory usage, and performance baselines.
/// </summary>
public sealed class GlacialCacheStressTests : UnitIntegrationTestBase, IAsyncDisposable
{
    private PostgreSqlContainer _postgres = null!;

    [Fact]
    [Trait("Category", "Stress")]
    public async Task ConcurrentAccess_ShouldBeThreadSafe()
    {
        await SetupPostgresAsync();
        const int concurrentTasks = 50;
        const int operationsPerTask = 50;

        await ExecuteWithServiceProviderAsync(async serviceProvider =>
        {
            var cache = serviceProvider.GetRequiredService<IDistributedCache>();

            var tasks = Enumerable.Range(0, concurrentTasks).Select(async taskId =>
            {
                for (int i = 0; i < operationsPerTask; i++)
                {
                    var key = $"concurrent-key-{taskId}-{i}";
                    var value = Encoding.UTF8.GetBytes($"value-{taskId}-{i}");

                    await cache.SetAsync(key, value);
                    var retrieved = await cache.GetAsync(key);

                    retrieved.Should().BeEquivalentTo(value);
                }
            });

            await Task.WhenAll(tasks);
        }, options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(1);
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(5);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Retry.MaxAttempts = 3;
            options.Resilience.CircuitBreaker.Enable = true;
        });

        await DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task MemoryUsage_ShouldNotGrowUnbounded()
    {
        await SetupPostgresAsync();

        await ExecuteWithServiceProviderAsync(async serviceProvider =>
        {
            var cache = serviceProvider.GetRequiredService<IDistributedCache>();

            static async Task<long> RunWorkloadAsync(IDistributedCache cache, string keyPrefix)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);

                var initialMemory = GC.GetTotalMemory(true);

                for (int i = 0; i < 10000; i++)
                {
                    var key = $"{keyPrefix}-{i}";
                    var value = new byte[1024];
                    await cache.SetAsync(key, value);
                    await cache.GetAsync(key);
                }

                GC.Collect();
                var finalMemory = GC.GetTotalMemory(true);

                return finalMemory - initialMemory;
            }

            // First pass may include one-time allocations (JIT, static caches, etc.).
            // The second pass should reflect steady-state behavior without unbounded growth.
            var firstPassGrowth = await RunWorkloadAsync(cache, "memory-test-pass1");
            var secondPassGrowth = await RunWorkloadAsync(cache, "memory-test-pass2");

            secondPassGrowth.Should().BeLessThan(10 * 1024 * 1024);
        }, options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(1);
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(5);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        });

        await DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task Performance_ShouldMeetBaseline()
    {
        await SetupPostgresAsync();

        await ExecuteWithServiceProviderAsync(async serviceProvider =>
        {
            var cache = serviceProvider.GetRequiredService<IDistributedCache>();
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 200; i++)
            {
                var key = $"perf-test-{i}";
                var value = Encoding.UTF8.GetBytes($"value-{i}");
                await cache.SetAsync(key, value);
                await cache.GetAsync(key);
            }

            stopwatch.Stop();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);
        }, options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        await DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task MultiInstance_ShouldSynchronizeCorrectly()
    {
        await SetupPostgresAsync();

        var cache1 = CreateCacheInstance();
        var cache2 = CreateCacheInstance();

        var key = "multi-instance-test";
        var value = Encoding.UTF8.GetBytes("test-value");
        await cache1.SetAsync(key, value);

        var retrieved = await cache2.GetAsync(key);
        retrieved.Should().BeEquivalentTo(value);
        await DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task LargeValueStress_ShouldHandleSuccessfully()
    {
        await SetupPostgresAsync();
        const int largeValueSize = 1024 * 1024; // 1MB
        var largeValue = new byte[largeValueSize];
        new Random().NextBytes(largeValue);

        await ExecuteWithServiceProviderAsync(async serviceProvider =>
        {
            var cache = serviceProvider.GetRequiredService<IDistributedCache>();
            var key = "large-value-stress-test";
            await cache.SetAsync(key, largeValue);
            var retrieved = await cache.GetAsync(key);
            retrieved.Should().BeEquivalentTo(largeValue);
        }, options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        await DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task MixedOperationsStress_ShouldHandleSuccessfully()
    {
        await SetupPostgresAsync();
        const int operationCount = 500;
        var random = new Random();
        await ExecuteWithServiceProviderAsync(async serviceProvider =>
        {
            var cache = serviceProvider.GetRequiredService<IDistributedCache>();
            for (int i = 0; i < operationCount; i++)
            {
                var key = $"mixed-ops-{i}";
                var value = Encoding.UTF8.GetBytes($"value-{i}");

                switch (random.Next(4))
                {
                    case 0:
                        await cache.SetAsync(key, value);
                        break;
                    case 1:
                        await cache.GetAsync(key);
                        break;
                    case 2:
                        await cache.RemoveAsync(key);
                        break;
                    case 3:
                        await cache.RefreshAsync(key);
                        break;
                }
            }
        }, options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        await DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task ExpirationStress_ShouldHandleCorrectly()
    {
        await SetupPostgresAsync();
        const int itemCount = 100;
        await ExecuteWithServiceProviderAsync(async serviceProvider =>
        {
            var cache = serviceProvider.GetRequiredService<IDistributedCache>();
            for (int i = 0; i < itemCount; i++)
            {
                var key = $"expiration-stress-{i}";
                var value = Encoding.UTF8.GetBytes($"value-{i}");
                var entryOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(100)
                };

                await cache.SetAsync(key, value, entryOptions);
            }

            await Task.Delay(800);

            for (int i = 0; i < itemCount; i++)
            {
                var key = $"expiration-stress-{i}";
                var retrieved = await cache.GetAsync(key);
                retrieved.Should().BeNull();
            }
        }, options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        await DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task SlidingExpirationStress_ShouldHandleCorrectly()
    {
        await SetupPostgresAsync();
        const int itemCount = 50;
        await ExecuteWithServiceProviderAsync(async serviceProvider =>
        {
            var cache = serviceProvider.GetRequiredService<IDistributedCache>();
            for (int i = 0; i < itemCount; i++)
            {
                var key = $"sliding-stress-{i}";
                var value = Encoding.UTF8.GetBytes($"value-{i}");
                var entryOptions = new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromSeconds(5)
                };

                await cache.SetAsync(key, value, entryOptions);
            }

            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < itemCount; i++)
                {
                    var key = $"sliding-stress-{i}";
                    await cache.GetAsync(key);
                }
                await Task.Delay(500);
            }

            for (int i = 0; i < itemCount; i++)
            {
                var key = $"sliding-stress-{i}";
                var retrieved = await cache.GetAsync(key);
                retrieved.Should().NotBeNull();
            }
        }, options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        await DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task ConnectionPoolStress_ShouldHandleHighConcurrency()
    {
        await SetupPostgresAsync();
        const int concurrentConnections = 50;
        await ExecuteWithServiceProviderAsync(async serviceProvider =>
        {
            var cache = serviceProvider.GetRequiredService<IDistributedCache>();
            var tasks = Enumerable.Range(0, concurrentConnections).Select(async i =>
            {
                var key = $"connection-pool-{i}";
                var value = Encoding.UTF8.GetBytes($"value-{i}");

                await cache.SetAsync(key, value);
                var retrieved = await cache.GetAsync(key);

                retrieved.Should().BeEquivalentTo(value);
            });

            await Task.WhenAll(tasks);
        }, options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        await DisposeAsync();
    }

    private async Task SetupPostgresAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("stresstest")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .WithCleanUp(true)
            .Build();

        await _postgres.StartAsync();
    }

    private IDistributedCache CreateCacheInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(1);
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(5);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        });

        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IDistributedCache>();
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgres != null)
        {
            await _postgres.DisposeAsync();
        }
    }
}
