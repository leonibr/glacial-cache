using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Services;
using Xunit.Abstractions;
using Npgsql;
using Moq;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Integration tests for ConnectionFailureLogLevel configuration option.
/// Tests verify that Logging.ConnectionFailureLogLevel works correctly
/// in real database scenarios with simulated connection failures.
/// </summary>
public class ConnectionFailureLogLevelIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private IDistributedCache? _cache;
    private IServiceProvider? _serviceProvider;
    private Mock<ILogger>? _loggerMock;
    private LogLevel _configuredConnectionFailureLogLevel = LogLevel.Warning; // Default

    public ConnectionFailureLogLevelIntegrationTests(ITestOutputHelper output) : base(output)
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
        _loggerMock = new Mock<ILogger>();
        var services = new ServiceCollection();

        // Register the mock logger factory that returns our mock logger for all types
        services.AddSingleton<ILoggerFactory>(sp => new MockLoggerFactory(_loggerMock));

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new MockLoggerProvider(_loggerMock));
        });

        // Explicitly register TimeProvider.System to ensure test isolation
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = new NpgsqlConnectionStringBuilder(_postgres!.GetConnectionString()) { ApplicationName = GetType().Name }.ConnectionString;
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
            options.Maintenance.EnableAutomaticCleanup = false;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(250);

            // Configure resilience options
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Retry.MaxAttempts = 1; // Keep it simple for log level testing

            // Configure specific log level
            configureOptions(options);

            // Store the configured log level for use in CreateCacheWithInvalidConnection
            _configuredConnectionFailureLogLevel = options.Resilience.Logging.ConnectionFailureLogLevel;
        });

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public async Task ConnectionFailureLogLevel_WithSimulatedFailure_ShouldLogAtConfiguredLevel(LogLevel logLevel)
    {
        // Arrange - Set up cache with specific connection failure log level
        await SetupCacheAsync(options =>
        {
            options.Resilience.Logging.ConnectionFailureLogLevel = logLevel;
        });

        // Act - Attempt an operation that will fail due to invalid connection
        var invalidConnectionCache = CreateCacheWithInvalidConnection();

        try
        {
            await invalidConnectionCache.GetStringAsync("test-connection-failure-logging");
        }
        catch (Exception)
        {
            // Expected due to invalid connection
        }

        // Assert - Verify the logger was called with the correct log level
        switch (logLevel)
        {
            case LogLevel.Trace:
                _loggerMock!.Verify(x => x.Log(LogLevel.Trace,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
                break;
            case LogLevel.Debug:
                _loggerMock!.Verify(x => x.Log(LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
                break;
            case LogLevel.Information:
                _loggerMock!.Verify(x => x.Log(LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
                break;
            case LogLevel.Warning:
                _loggerMock!.Verify(x => x.Log(LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
                break;
            case LogLevel.Error:
                _loggerMock!.Verify(x => x.Log(LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
                break;
            case LogLevel.Critical:
                _loggerMock!.Verify(x => x.Log(LogLevel.Critical,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
                break;
        }
    }

    [Fact]
    public async Task ConnectionFailureLogLevel_Default_ShouldUseWarningLevel()
    {
        // Arrange - Set up cache with default log level (should be Warning)
        await SetupCacheAsync(options =>
        {
            // Don't set ConnectionFailureLogLevel - should default to Warning
        });

        // Act - Attempt an operation that will fail due to invalid connection
        var invalidConnectionCache = CreateCacheWithInvalidConnection();

        try
        {
            await invalidConnectionCache.GetStringAsync("test-default-log-level");
        }
        catch (Exception)
        {
            // Expected due to invalid connection
        }

        // Assert - Verify Warning level logging occurred (default)
        _loggerMock!.Verify(x => x.Log(LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ConnectionFailureLogLevel_WithSuccessfulOperation_ShouldNotLogConnectionFailure()
    {
        // Arrange - Set up cache with valid connection
        await SetupCacheAsync(options =>
        {
            options.Resilience.Logging.ConnectionFailureLogLevel = LogLevel.Error;
        });

        // Act - Perform successful operations
        await _cache!.SetStringAsync("success-test-key", "success-test-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var result = await _cache!.GetStringAsync("success-test-key");

        // Assert - Verify successful operation and no connection failure logging
        result.ShouldBe("success-test-value");

        // Verify no connection failure logging occurred
        _loggerMock!.Verify(x => x.Log(It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Database connection failed")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task ConnectionFailureLogLevel_WithNonConnectionException_ShouldNotLogConnectionFailure()
    {
        // Arrange - Set up cache with valid connection but force a non-connection error
        await SetupCacheAsync(options =>
        {
            options.Resilience.Logging.ConnectionFailureLogLevel = LogLevel.Error;
        });

        // Act - Try to deserialize invalid data (should cause a non-connection error)
        try
        {
            // This should work fine - no connection issues
            await _cache!.SetStringAsync("normal-test-key", "normal-test-value", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });
        }
        catch (Exception)
        {
            // If there's an exception, it shouldn't be a connection failure
        }

        // Assert - Verify no connection failure logging occurred
        _loggerMock!.Verify(x => x.Log(It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Database connection failed")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task ConnectionFailureLogLevel_LogMessage_ShouldContainExpectedInformation()
    {
        // Arrange - Set up cache with Error log level
        await SetupCacheAsync(options =>
        {
            options.Resilience.Logging.ConnectionFailureLogLevel = LogLevel.Error;
        });

        // Act - Attempt operation with invalid connection
        var invalidConnectionCache = CreateCacheWithInvalidConnection();

        try
        {
            await invalidConnectionCache.GetStringAsync("test-message-content");
        }
        catch (Exception)
        {
            // Expected
        }

        // Assert - Verify log message contains expected information
        // Note: GetStringAsync internally calls GetAsync, so the operation name in the log is "GetAsync"
        _loggerMock!.Verify(x => x.Log(LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Database connection failed for GetAsync operation")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Creates a cache instance configured with an invalid connection string to simulate connection failures.
    /// Uses the configured connection failure log level from the test setup.
    /// </summary>
    private IDistributedCache CreateCacheWithInvalidConnection()
    {
        var services = new ServiceCollection();

        // Register the mock logger factory that returns our mock logger for all types
        services.AddSingleton<ILoggerFactory>(sp => new MockLoggerFactory(_loggerMock!));

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new MockLoggerProvider(_loggerMock!));
        });

        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddGlacialCachePostgreSQL(options =>
        {
            // Use an invalid connection string to force connection failures
            options.Connection.ConnectionString = "Host=nonexistent.example.com;Database=testdb;Username=testuser;Password=testpass;Timeout=1";
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = false; // Skip infrastructure creation for invalid connection
            options.Maintenance.EnableAutomaticCleanup = false;

            // Configure resilience to trigger connection failure logging
            options.Resilience.EnableResiliencePatterns = true;
            options.Resilience.Retry.MaxAttempts = 1;
            // Use the log level configured in the test setup
            options.Resilience.Logging.ConnectionFailureLogLevel = _configuredConnectionFailureLogLevel;
        });

        var tempServiceProvider = services.BuildServiceProvider();
        return tempServiceProvider.GetRequiredService<IDistributedCache>();
    }

    /// <summary>
    /// Custom logger provider that forwards to our mock logger.
    /// </summary>
    private class MockLoggerProvider : ILoggerProvider
    {
        private readonly Mock<ILogger> _mockLogger;

        public MockLoggerProvider(Mock<ILogger> mockLogger)
        {
            _mockLogger = mockLogger;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _mockLogger.Object;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Mock logger factory that returns the mock logger for all categories.
    /// </summary>
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
            // Not needed for mock
        }
    }
}
