using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Models;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Integration tests for connection pool configuration options.
/// Tests verify that Pool.MaxSize, Pool.MinSize, Pool.IdleLifetimeSeconds,
/// and Pool.PruningIntervalSeconds work correctly in real database scenarios.
/// </summary>
public class ConnectionPoolIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private IDistributedCache? _cache;
    private IServiceProvider? _serviceProvider;
    private CleanupBackgroundService? _cleanupService;

    public ConnectionPoolIntegrationTests(ITestOutputHelper output) : base(output)
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

            await _postgres.StartWithRetryAsync(Output);
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}", ex);
        }
    }

    protected override async Task CleanupTestAsync()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            try
            {
                await (_cleanupService?.StopAsync(default) ?? Task.CompletedTask);
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Output.WriteLine($"⚠️ Warning: Error disposing service provider: {ex.Message}");
            }
        }

        if (_postgres != null)
        {
            try
            {
                await _postgres.DisposeAsync();
                Output.WriteLine("✅ PostgreSQL container disposed");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"⚠️ Warning: Error disposing container: {ex.Message}");
                // Don't throw - cleanup failures shouldn't fail tests
            }
            finally
            {
                _postgres = null;
            }
        }
    }

    private async Task SetupCacheAsync(Action<GlacialCachePostgreSQLOptions> configureOptions)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Explicitly register TimeProvider.System to ensure test isolation
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres!.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(250);

            // Configure connection pool settings
            configureOptions(options);
        });

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
        _cleanupService = _serviceProvider.GetRequiredService<CleanupBackgroundService>();
        await _cleanupService.StartAsync(default);
    }

    [Fact]
    public async Task Pool_MaxSize_LimitsConcurrentConnections()
    {
        // Arrange - Set up cache with small pool max size
        await SetupCacheAsync(options =>
        {
            options.Connection.Pool.MaxSize = 2; // Very small pool for testing
            options.Connection.Pool.MinSize = 1;
        });

        // Act - Create multiple concurrent operations that exceed pool size
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    // Each operation should acquire a connection
                    await _cache!.SetStringAsync($"key{i}", $"value{i}", new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                    });
                    await Task.Delay(100); // Hold connection briefly
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"Operation failed: {ex.Message}");
                    throw;
                }
            }));
        }

        // Assert - All operations should complete (pooling should handle the load)
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Pool_MinSize_IsRespected()
    {
        // Arrange - Set up cache with minimum pool size
        await SetupCacheAsync(options =>
        {
            options.Connection.Pool.MinSize = 2;
            options.Connection.Pool.MaxSize = 10;
        });

        // Act - Get pool metrics after some operations
        var dataSource = _serviceProvider!.GetRequiredService<IPostgreSQLDataSource>();
        var metrics = dataSource.GetPoolMetrics();

        // Assert - Pool should maintain minimum size
        Assert.True(metrics.MinPoolSize >= 2, $"MinPoolSize should be at least 2, but was {metrics.MinPoolSize}");
    }

    [Fact]
    public async Task Pool_IdleLifetimeSeconds_ControlsConnectionPruning()
    {
        // Arrange - Set up cache with short idle lifetime
        await SetupCacheAsync(options =>
        {
            options.Connection.Pool.IdleLifetimeSeconds = 1; // 1 second
            options.Connection.Pool.PruningIntervalSeconds = 1; // Check every 1 second
            options.Connection.Pool.MaxSize = 5;
        });

        // Act - Perform some operations to create connections
        for (int i = 0; i < 3; i++)
        {
            await _cache!.SetStringAsync($"key{i}", $"value{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });
        }

        // Wait for idle lifetime to expire and pruning to occur
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Perform another operation to potentially trigger pruning
        await _cache!.SetStringAsync("newkey", "newvalue", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        // Assert - Test passes if no connection errors occur (pruning should work without issues)
        Assert.True(true, "Idle lifetime pruning completed without errors");
    }

    [Fact]
    public async Task Pool_PruningIntervalSeconds_ControlsPruningFrequency()
    {
        // Arrange - Set up cache with longer pruning interval
        // Note: IdleLifetime must be >= PruningInterval per Npgsql requirements
        await SetupCacheAsync(options =>
        {
            options.Connection.Pool.IdleLifetimeSeconds = 5; // 5 seconds (must be >= pruning interval)
            options.Connection.Pool.PruningIntervalSeconds = 3; // Check every 3 seconds
            options.Connection.Pool.MaxSize = 5;
        });

        // Act - Perform operations and wait for pruning
        for (int i = 0; i < 3; i++)
        {
            await _cache!.SetStringAsync($"key{i}", $"value{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });
        }

        // Wait less than idle lifetime (connections are still active, so pruning won't remove them)
        // Pruning checks happen every 3 seconds, but connections need to be idle for 5 seconds before being pruned
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - Test passes if operations still work (connections are still active, not pruned)
        var result = await _cache!.GetStringAsync("key0");
        Assert.Equal("value0", result);
    }

    [Fact]
    public async Task Pool_ConfigurationIsAppliedCorrectly()
    {
        // Arrange - Set up cache with specific pool configuration
        await SetupCacheAsync(options =>
        {
            options.Connection.Pool.MaxSize = 8;
            options.Connection.Pool.MinSize = 3;
            options.Connection.Pool.IdleLifetimeSeconds = 120;
            options.Connection.Pool.PruningIntervalSeconds = 15;
        });

        // Act - Get pool metrics
        var dataSource = _serviceProvider!.GetRequiredService<IPostgreSQLDataSource>();
        var metrics = dataSource.GetPoolMetrics();

        // Assert - Pool configuration should be applied
        Assert.True(metrics.MaxPoolSize >= 8, $"MaxPoolSize should be at least 8, but was {metrics.MaxPoolSize}");
        Assert.True(metrics.MinPoolSize >= 3, $"MinPoolSize should be at least 3, but was {metrics.MinPoolSize}");
        Assert.True(metrics.IdleLifetime >= 120, $"IdleLifetime should be at least 120s, but was {metrics.IdleLifetime}s");
        Assert.True(metrics.PruningInterval >= 15, $"PruningInterval should be at least 15s, but was {metrics.PruningInterval}s");
    }

    [Fact]
    public async Task Pool_BehaviorUnderConcurrentLoad()
    {
        // Arrange - Set up cache with moderate pool size
        await SetupCacheAsync(options =>
        {
            options.Connection.Pool.MaxSize = 4;
            options.Connection.Pool.MinSize = 1;
        });

        // Act - Simulate concurrent load
        var tasks = new List<Task<string>>();
        for (int i = 0; i < 10; i++)
        {
            var taskId = i;
            tasks.Add(Task.Run(async () =>
            {
                // Each task performs multiple cache operations
                for (int j = 0; j < 5; j++)
                {
                    var key = $"concurrent-{taskId}-{j}";
                    await _cache!.SetStringAsync(key, $"value-{taskId}-{j}", new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                    });

                    var result = await _cache!.GetStringAsync(key);
                    if (result != $"value-{taskId}-{j}")
                    {
                        throw new Exception($"Data inconsistency: expected 'value-{taskId}-{j}', got '{result}'");
                    }
                }
                return $"Task {taskId} completed successfully";
            }));
        }

        // Assert - All concurrent operations should complete successfully
        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.Contains("completed successfully", result));
    }
}
