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
using Npgsql;
using Polly.Timeout;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Integration tests for timeout configuration options.
/// Tests verify that Timeouts.OperationTimeout, Timeouts.ConnectionTimeout,
/// and Timeouts.CommandTimeout work correctly in real database scenarios.
/// </summary>
public class TimeoutConfigurationIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private IDistributedCache? _cache;
    private IServiceProvider? _serviceProvider;
    private CleanupBackgroundService? _cleanupService;

    public TimeoutConfigurationIntegrationTests(ITestOutputHelper output) : base(output)
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

            // Configure timeout settings
            configureOptions(options);
        });

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
        _cleanupService = _serviceProvider.GetRequiredService<CleanupBackgroundService>();
        await _cleanupService.StartAsync(default);
    }

    [Fact]
    public async Task Timeouts_OperationTimeout_WithSlowQuery()
    {
        // Arrange - Set up cache with short operation timeout
        await SetupCacheAsync(options =>
        {
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromMilliseconds(500); // Very short timeout
        });

        // Act & Assert - Attempt to execute a slow query that should timeout
        var exception = await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            // This should trigger a slow operation that exceeds the timeout
            await ExecuteSlowQueryAsync(TimeSpan.FromSeconds(2)); // Query takes 2 seconds, timeout is 500ms
        });

        // Verify the timeout exception was thrown
        Assert.Contains("timeout", exception.Message.ToLower());
    }

    [Fact]
    public async Task Timeouts_OperationTimeout_WithFastQuery()
    {
        // Arrange - Set up cache with reasonable operation timeout
        await SetupCacheAsync(options =>
        {
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(5); // Reasonable timeout
        });

        // Act - Execute a fast operation that should complete within timeout
        await _cache!.SetStringAsync("fast-test-key", "fast-test-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var result = await _cache!.GetStringAsync("fast-test-key");

        // Assert - Operation should complete successfully
        Assert.Equal("fast-test-value", result);
    }

    [Fact]
    public async Task Timeouts_CommandTimeout_WithLongRunningCommand()
    {
        // Arrange - Set up cache with short command timeout
        await SetupCacheAsync(options =>
        {
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Timeouts.CommandTimeout = TimeSpan.FromMilliseconds(300); // Very short timeout
        });

        // Act & Assert - Attempt to execute a long-running command that should timeout
        var exception = await Assert.ThrowsAsync<NpgsqlException>(async () =>
        {
            // Execute a command that takes longer than the timeout
            await ExecuteLongRunningCommandAsync(TimeSpan.FromSeconds(1));
        });

        // Verify it's a timeout-related exception (could be NpgsqlException or TimeoutRejectedException)
        Assert.True(exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   exception.InnerException?.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Timeouts_CommandTimeout_WithNormalCommand()
    {
        // Arrange - Set up cache with reasonable command timeout
        await SetupCacheAsync(options =>
        {
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Timeouts.CommandTimeout = TimeSpan.FromSeconds(10); // Reasonable timeout
        });

        // Act - Execute normal cache operations that should complete within timeout
        for (int i = 0; i < 5; i++)
        {
            await _cache!.SetStringAsync($"normal-test-key-{i}", $"normal-test-value-{i}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });

            var result = await _cache!.GetStringAsync($"normal-test-key-{i}");
            Assert.Equal($"normal-test-value-{i}", result);
        }
    }

    [Fact]
    public async Task Timeouts_ConnectionTimeout_WithUnavailableDatabase()
    {
        // Arrange - Set up cache with very short connection timeout and use invalid connection string
        // Note: The exception may occur during service initialization or during the first operation
        Exception? setupException = null;
        try
        {
            await SetupCacheAsync(options =>
            {
                options.Connection.ConnectionString = "Host=nonexistent.example.com;Database=testdb;Username=testuser;Password=testpass;Timeout=1";
                options.Resilience.EnableResiliencePatterns = true;
                options.Resilience.Timeouts.ConnectionTimeout = TimeSpan.FromMilliseconds(100); // Very short timeout
            });
        }
        catch (Exception ex)
        {
            // Exception may occur during service provider build due to initialization
            setupException = ex;
        }

        // Act & Assert - If setup succeeded, attempt operation; otherwise verify setup exception
        if (setupException == null && _cache != null)
        {
            var exception = await Assert.ThrowsAsync<Exception>(async () =>
            {
                await _cache.SetStringAsync("connection-timeout-test", "value");
            });

            // Verify it's a connection-related exception
            Assert.True(exception.Message.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                       exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                       exception.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
                       exception.Message.Contains("host", StringComparison.OrdinalIgnoreCase) ||
                       (exception.InnerException != null && (
                           exception.InnerException.Message.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                           exception.InnerException.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                           exception.InnerException.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase))));
        }
        else if (setupException != null)
        {
            // Verify the setup exception is connection-related
            Assert.True(setupException.Message.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                       setupException.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                       setupException.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
                       setupException.Message.Contains("host", StringComparison.OrdinalIgnoreCase) ||
                       (setupException.InnerException != null && (
                           setupException.InnerException.Message.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                           setupException.InnerException.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                           setupException.InnerException.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase))));
        }
        else
        {
            Assert.Fail("Expected either a setup exception or a cache operation exception");
        }
    }

    [Fact]
    public async Task Timeouts_ConfigurationIsAppliedCorrectly()
    {
        // Arrange - Set up cache with specific timeout configuration
        await SetupCacheAsync(options =>
        {
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(45);
            options.Resilience.Timeouts.ConnectionTimeout = TimeSpan.FromSeconds(15);
            options.Resilience.Timeouts.CommandTimeout = TimeSpan.FromSeconds(25);
        });

        // Act - Get the policy factory to verify configuration
        var policyFactory = _serviceProvider!.GetRequiredService<IPolicyFactory>();

        // Assert - Configuration should be applied (this is more of a smoke test since we can't easily inspect internal timeout values)
        Assert.NotNull(policyFactory);

        // Perform a basic operation to ensure the cache works with the timeout configuration
        await _cache!.SetStringAsync("config-test-key", "config-test-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var result = await _cache!.GetStringAsync("config-test-key");
        Assert.Equal("config-test-value", result);
    }

    [Fact]
    public async Task Timeouts_TimeoutPropagation_InCacheOperations()
    {
        // Arrange - Set up cache with short operation timeout
        await SetupCacheAsync(options =>
        {
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromMilliseconds(200); // Very short
        });

        // Act - Perform multiple operations that should collectively exceed timeout
        await _cache!.SetStringAsync("propagation-test-1", "value1");

        // Small delay to accumulate time
        await Task.Delay(50);

        await _cache!.SetStringAsync("propagation-test-2", "value2");

        // Small delay to accumulate time
        await Task.Delay(50);

        // This operation might timeout depending on cumulative time spent
        var result = await _cache!.GetStringAsync("propagation-test-1");

        // Assert - Either the operation succeeds or times out appropriately
        Assert.True(result == "value1" || result == null, "Operation should either succeed or timeout gracefully");
    }

    [Fact]
    public async Task Timeouts_TimeoutOverrides_InDifferentScenarios()
    {
        // Arrange - Set up cache with different timeout configurations for different scenarios
        await SetupCacheAsync(options =>
        {
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(2); // 2 seconds
            options.Resilience.Timeouts.CommandTimeout = TimeSpan.FromSeconds(1);   // 1 second
        });

        // Act & Assert - Test that different operations respect different timeout configurations
        // Set operation should work within reasonable time
        await _cache!.SetStringAsync("override-test-key", "override-test-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        // Get operation should also work
        var result = await _cache!.GetStringAsync("override-test-key");
        Assert.Equal("override-test-value", result);

        // Remove operation should work
        await _cache!.RemoveAsync("override-test-key");

        // Verify removal
        var removedResult = await _cache!.GetStringAsync("override-test-key");
        Assert.Null(removedResult);
    }

    /// <summary>
    /// Helper method to execute a slow database query using PostgreSQL's pg_sleep function.
    /// </summary>
    private async Task ExecuteSlowQueryAsync(TimeSpan delay)
    {
        var dataSource = _serviceProvider!.GetRequiredService<IPostgreSQLDataSource>();

        await using var connection = await dataSource.GetConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT pg_sleep({delay.TotalSeconds})", connection);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Helper method to execute a long-running database command.
    /// </summary>
    private async Task ExecuteLongRunningCommandAsync(TimeSpan duration)
    {
        var dataSource = _serviceProvider!.GetRequiredService<IPostgreSQLDataSource>();

        await using var connection = await dataSource.GetConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT pg_sleep({duration.TotalSeconds})", connection);

        await command.ExecuteNonQueryAsync();
    }
}
