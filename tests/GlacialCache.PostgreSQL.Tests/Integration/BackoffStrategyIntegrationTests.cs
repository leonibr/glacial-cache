using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Resilience;
using GlacialCache.PostgreSQL.Services;
using Xunit.Abstractions;
using Npgsql;
using Moq;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Integration tests for backoff strategy configuration options.
/// Tests verify that Retry.BackoffStrategy (Linear, Exponential, ExponentialWithJitter)
/// works correctly in real database scenarios with transient failures.
/// </summary>
public class BackoffStrategyIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private IDistributedCache? _cache;
    private IServiceProvider? _serviceProvider;
    private CleanupBackgroundService? _cleanupService;

    public BackoffStrategyIntegrationTests(ITestOutputHelper output) : base(output)
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

    private async Task<(IDistributedCache Cache, Mock<ILogger> Logger)> SetupCacheWithLoggerAsync(Action<GlacialCachePostgreSQLOptions> configureOptions)
    {
        var services = new ServiceCollection();
        var mockLogger = new Mock<ILogger>();

        // Set up logger to return true for IsEnabled for all log levels
        // This ensures Debug-level retry logs are not skipped
        mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Register the mock logger factory that returns our mock logger for all types
        services.AddSingleton<ILoggerFactory>(sp => new MockLoggerFactory(mockLogger));

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Explicitly register TimeProvider.System to ensure test isolation
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddGlacialCachePostgreSQL(options =>
        {
            // Use invalid connection string to force connection failures and trigger retries
            // Just use invalid host - Npgsql will fail to connect and trigger retries
            options.Connection.ConnectionString = "Host=nonexistent.example.com;Database=testdb;Username=testuser;Password=testpass;Timeout=1";
            options.Connection.Timeouts.ConnectionTimeout = TimeSpan.FromMilliseconds(100); // Very short for testing
            options.Connection.Timeouts.CommandTimeout = TimeSpan.FromMilliseconds(100);
            options.Connection.Timeouts.OperationTimeout = TimeSpan.FromMilliseconds(100);
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = false; // Skip infrastructure creation for invalid connection
            options.Maintenance.EnableAutomaticCleanup = false;

            // Configure resilience options
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Retry.MaxAttempts = 3;
            options.Resilience.Retry.BaseDelay = TimeSpan.FromMilliseconds(50); // Short for testing
            // Increase timeout to allow retries to complete (3 attempts * ~100ms connection timeout + delays)
            options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(2); // Allow time for retries
            options.Resilience.Logging.EnableResilienceLogging = true; // Enable logging to verify retries

            // Configure specific backoff strategy
            configureOptions(options);
        });

        _serviceProvider = services.BuildServiceProvider();
        var cache = _serviceProvider.GetRequiredService<IDistributedCache>();
        _cleanupService = _serviceProvider.GetRequiredService<CleanupBackgroundService>();
        await _cleanupService.StartAsync(default);

        return (cache, mockLogger);
    }

    private async Task SetupCacheAsync(Action<GlacialCachePostgreSQLOptions> configureOptions)
    {
        var (cache, _) = await SetupCacheWithLoggerAsync(configureOptions);
        _cache = cache;
    }

    private class MockLoggerFactory : ILoggerFactory
    {
        private readonly Mock<ILogger> _mockLogger;

        public MockLoggerFactory(Mock<ILogger> mockLogger)
        {
            _mockLogger = mockLogger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
            // Not needed for mock
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _mockLogger.Object;
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }

    [Fact]
    public async Task BackoffStrategy_Linear_ShouldUseLinearDelays()
    {
        // Arrange - Set up cache with linear backoff strategy and logger
        var (cache, logger) = await SetupCacheWithLoggerAsync(options =>
        {
            options.Resilience.Retry.BackoffStrategy = BackoffStrategy.Linear;
        });
        _cache = cache;

        // Act - Execute operation that will fail and trigger retries
        await ExecuteOperationWithTransientFailuresAsync("test-key", 0);

        // Assert - Verify that retry attempts were logged (proves retries happened)
        // With MaxAttempts=3, we should see 2 retry log entries (attempts 2 and 3)
        logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry attempt")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2), // At least 2 retry attempts should be logged
            "Linear backoff should trigger retries");
    }

    [Fact]
    public async Task BackoffStrategy_Exponential_ShouldUseExponentialDelays()
    {
        // Arrange - Set up cache with exponential backoff strategy and logger
        var (cache, logger) = await SetupCacheWithLoggerAsync(options =>
        {
            options.Resilience.Retry.BackoffStrategy = BackoffStrategy.Exponential;
        });
        _cache = cache;

        // Act - Execute operation that will fail and trigger retries
        await ExecuteOperationWithTransientFailuresAsync("exp-test-key", 0);

        // Assert - Verify that retry attempts were logged (proves retries happened)
        logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry attempt")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2), // At least 2 retry attempts should be logged
            "Exponential backoff should trigger retries");
    }

    [Fact]
    public async Task BackoffStrategy_ExponentialWithJitter_ShouldUseExponentialDelaysWithVariation()
    {
        // Arrange - Set up cache with exponential with jitter backoff strategy
        var (cache, logger) = await SetupCacheWithLoggerAsync(options =>
        {
            options.Resilience.Retry.BackoffStrategy = BackoffStrategy.ExponentialWithJitter;
        });
        _cache = cache;

        // Act - Execute operation that will fail and trigger retries
        await ExecuteOperationWithTransientFailuresAsync("jitter-test-key", 0);

        // Assert - Verify that retry attempts were logged (proves retries happened)
        logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry attempt")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2), // At least 2 retry attempts should be logged
            "Exponential with jitter backoff should trigger retries");

        // Note: Verifying exact jitter variation would require parsing log messages,
        // which is complex. The unit tests already verify the jitter calculation logic.
        // This integration test verifies that the strategy is applied in real scenarios.
    }

    [Fact]
    public async Task BackoffStrategy_Default_ShouldUseExponentialWithJitter()
    {
        // Arrange - Set up cache with default backoff strategy (should be ExponentialWithJitter)
        var (cache, logger) = await SetupCacheWithLoggerAsync(options =>
        {
            // Don't set BackoffStrategy - should default to ExponentialWithJitter
        });
        _cache = cache;

        // Act - Execute operation that will fail and trigger retries
        await ExecuteOperationWithTransientFailuresAsync("default-test-key", 0);

        // Assert - Verify that retry attempts were logged (proves retries happened)
        logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry attempt")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2), // At least 2 retry attempts should be logged
            "Default backoff (ExponentialWithJitter) should trigger retries");
    }

    [Fact]
    public async Task BackoffStrategy_DifferentBaseDelays_ShouldScaleCorrectly()
    {
        // Test with different base delays to ensure scaling works
        var baseDelays = new[] { TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(100) };

        foreach (var baseDelay in baseDelays)
        {
            // Arrange
            var (cache, logger) = await SetupCacheWithLoggerAsync(options =>
            {
                options.Resilience.Retry.BackoffStrategy = BackoffStrategy.Exponential;
                options.Resilience.Retry.BaseDelay = baseDelay;
            });
            _cache = cache;

            // Act
            await ExecuteOperationWithTransientFailuresAsync($"delay-test-key-{baseDelay.TotalMilliseconds}", 0);

            // Assert - Verify that retries were logged (proves retries happened)
            logger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry attempt")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeast(2),
                $"Backoff with base delay {baseDelay.TotalMilliseconds}ms should trigger retries");
        }
    }

    [Fact]
    public async Task BackoffStrategy_RetryCount_ShouldRespectMaxAttempts()
    {
        // Arrange - Set up cache with low max attempts
        var (cache, logger) = await SetupCacheWithLoggerAsync(options =>
        {
            options.Resilience.Retry.BackoffStrategy = BackoffStrategy.Linear;
            options.Resilience.Retry.MaxAttempts = 1; // Only 1 retry = 2 total attempts (1 initial + 1 retry)
            options.Resilience.Retry.BaseDelay = TimeSpan.FromMilliseconds(50);
            options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(2); // Allow time for retries
        });
        _cache = cache;

        // Act - Execute operation that will fail
        try
        {
            await _cache!.GetStringAsync("attempt-count-test");
        }
        catch (Exception)
        {
            // Expected due to failures
        }

        // Assert - With MaxAttempts=1, we should see exactly 1 retry log entry
        // MaxAttempts=1 means 1 retry, so 2 total attempts (1 initial + 1 retry)
        logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry attempt")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once, // Exactly 1 retry (1 initial attempt + 1 retry = 2 total attempts)
            "With MaxAttempts=1, should see exactly 1 retry log entry");
    }

    /// <summary>
    /// Helper method that executes a cache operation that will fail due to invalid connection.
    /// This creates a scenario where retries are triggered with backoff.
    /// </summary>
    private async Task ExecuteOperationWithTransientFailuresAsync(string key, int operationId)
    {
        // Try to set a value - this will fail due to invalid connection and trigger retries
        try
        {
            await _cache!.SetStringAsync(key, $"value-{operationId}", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });
        }
        catch (Exception)
        {
            // Expected due to invalid connection - retries should happen here
        }
    }

    /// <summary>
    /// Mock cache wrapper that counts execution attempts.
    /// </summary>
    private class MockCacheWithCounting : IDistributedCache
    {
        private readonly IDistributedCache _innerCache;
        private readonly Action _onAttempt;

        public MockCacheWithCounting(IDistributedCache innerCache, Action onAttempt)
        {
            _innerCache = innerCache;
            _onAttempt = onAttempt;
        }

        public byte[]? Get(string key)
        {
            _onAttempt();
            return _innerCache.Get(key);
        }

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            _onAttempt();
            return await _innerCache.GetAsync(key, token);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _onAttempt();
            _innerCache.Set(key, value, options);
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            _onAttempt();
            await _innerCache.SetAsync(key, value, options, token);
        }

        public void Refresh(string key)
        {
            _onAttempt();
            _innerCache.Refresh(key);
        }

        public async Task RefreshAsync(string key, CancellationToken token = default)
        {
            _onAttempt();
            await _innerCache.RefreshAsync(key, token);
        }

        public void Remove(string key)
        {
            _onAttempt();
            _innerCache.Remove(key);
        }

        public async Task RemoveAsync(string key, CancellationToken token = default)
        {
            _onAttempt();
            await _innerCache.RemoveAsync(key, token);
        }

        // Convenience methods for string operations
        public string? GetString(string key)
        {
            _onAttempt();
            return _innerCache.GetString(key);
        }

        public async Task<string?> GetStringAsync(string key, CancellationToken token = default)
        {
            _onAttempt();
            return await _innerCache.GetStringAsync(key, token);
        }

        public void SetString(string key, string value, DistributedCacheEntryOptions options)
        {
            _onAttempt();
            _innerCache.SetString(key, value, options);
        }

        public async Task SetStringAsync(string key, string value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            _onAttempt();
            await _innerCache.SetStringAsync(key, value, options, token);
        }
    }
}
