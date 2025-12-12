using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Polly;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Configuration.Resilience;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Configuration.Maintenance;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

/// <summary>
/// Unit tests for GlacialCachePostgreSQL focusing on configuration options.
/// </summary>
public class GlacialCachePostgreSQLTests
{
    private readonly Mock<ILogger<GlacialCachePostgreSQL>> _logger = new();
    private readonly Mock<ITimeConverterService> _timeConverter = new();
    private readonly Mock<IPostgreSQLDataSource> _dataSource = new();
    private readonly Mock<IDbRawCommands> _dbRawCommands = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<TimeProvider> _timeProvider = new();
    private readonly Mock<ICacheEntrySerializer> _serializer = new();
    private readonly GlacialCacheEntryFactory _entryFactory;
    private readonly Mock<IPolicyFactory> _policyFactory = new();
    private readonly Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>> _optionsMonitor = new();
    private readonly Mock<ISchemaManager> _schemaManager = new();

    public GlacialCachePostgreSQLTests()
    {
        _entryFactory = new GlacialCacheEntryFactory(_serializer.Object);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public async Task ExecuteWithResilienceAsync_WithConnectionFailure_ShouldLogAtConfiguredLevel(LogLevel logLevel)
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=testpass"
            },
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Logging = new LoggingOptions
                {
                    ConnectionFailureLogLevel = logLevel
                }
            }
        };

        // Set up logger to return true for IsEnabled for all log levels
        _logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Mock the data source to throw a connection failure exception
        // This will be caught by ExecuteWithResilienceAsync and logged
        _dataSource.Setup(ds => ds.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateConnectionException());

        var mockPolicy = new Mock<IAsyncPolicy>();
        // The policy will execute the operation, which will throw from GetConnectionAsync
        // GetAsync calls ExecuteWithResilienceAsync<byte[]?>, so we need to mock Func<Task<byte[]?>>
        // The policy should just execute the function and let exceptions propagate naturally
        mockPolicy
            .Setup(p => p.ExecuteAsync(It.IsAny<Func<Context, Task<byte[]?>>>(), It.IsAny<Context>()))
            .Returns<Func<Context, Task<byte[]?>>, Context>((func, ctx) => func(ctx));

        _policyFactory.Setup(p => p.CreateResiliencePolicy(options)).Returns(mockPolicy.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(IPolicyFactory))).Returns(_policyFactory.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(ISchemaManager))).Returns(_schemaManager.Object);
        _optionsMonitor.Setup(o => o.CurrentValue).Returns(options);

        var cache = new GlacialCachePostgreSQL(
            _optionsMonitor.Object,
            _logger.Object,
            _timeConverter.Object,
            _dataSource.Object,
            _dbRawCommands.Object,
            _serviceProvider.Object,
            _timeProvider.Object,
            _entryFactory);

        // Act
        var result = await cache.GetStringAsync("test-key");

        // Assert - Result should be null or empty string (depending on implementation)
        result.ShouldBeNullOrEmpty();

        // Verify the logger was called with the correct log level using ILogger.Log
        _logger.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database connection failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public async Task ExecuteWithResilienceAsync_Void_WithConnectionFailure_ShouldLogAtConfiguredLevel(LogLevel logLevel)
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=testpass"
            },
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Logging = new LoggingOptions
                {
                    ConnectionFailureLogLevel = logLevel
                }
            }
        };

        // Set up logger to return true for IsEnabled for all log levels
        _logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Mock the data source to throw a connection failure exception
        // This will be caught by ExecuteWithResilienceAsync and logged
        _dataSource.Setup(ds => ds.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateConnectionException());

        var mockPolicy = new Mock<IAsyncPolicy>();
        // The policy will execute the operation, which will throw from GetConnectionAsync
        // We need to make sure the exception propagates through the policy
        mockPolicy
            .Setup(p => p.ExecuteAsync(It.IsAny<Func<Context, Task>>(), It.IsAny<Context>()))
            .Returns<Func<Context, Task>, Context>((func, ctx) => func(ctx));

        _policyFactory.Setup(p => p.CreateResiliencePolicy(options)).Returns(mockPolicy.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(IPolicyFactory))).Returns(_policyFactory.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(ISchemaManager))).Returns(_schemaManager.Object);
        _optionsMonitor.Setup(o => o.CurrentValue).Returns(options);

        var cache = new GlacialCachePostgreSQL(
            _optionsMonitor.Object,
            _logger.Object,
            _timeConverter.Object,
            _dataSource.Object,
            _dbRawCommands.Object,
            _serviceProvider.Object,
            _timeProvider.Object,
            _entryFactory);

        // Act & Assert - This should not throw, but should log at the configured level
        await cache.RemoveAsync("test-key");

        // Verify the logger was called with the correct log level using ILogger.Log
        _logger.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database connection failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_WithNonConnectionFailure_ShouldNotLogConnectionFailure()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=testpass"
            },
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Logging = new LoggingOptions
                {
                    ConnectionFailureLogLevel = LogLevel.Error
                }
            }
        };

        // Set up logger to return true for IsEnabled for all log levels
        _logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Mock the data source to throw a non-connection failure exception
        // This should NOT be caught as a connection failure
        _dataSource.Setup(ds => ds.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Some other error"));

        var mockPolicy = new Mock<IAsyncPolicy>();
        // The policy will execute the operation, which will throw from GetConnectionAsync
        // GetAsync calls ExecuteWithResilienceAsync<byte[]?>, so we need to mock Func<Task<byte[]?>>
        // The policy should just execute the function and let exceptions propagate naturally
        mockPolicy
            .Setup(p => p.ExecuteAsync(It.IsAny<Func<Context, Task<byte[]?>>>(), It.IsAny<Context>()))
            .Returns<Func<Context, Task<byte[]?>>, Context>((func, ctx) => func(ctx));

        _policyFactory.Setup(p => p.CreateResiliencePolicy(options)).Returns(mockPolicy.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(IPolicyFactory))).Returns(_policyFactory.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(ISchemaManager))).Returns(_schemaManager.Object);
        _optionsMonitor.Setup(o => o.CurrentValue).Returns(options);

        var cache = new GlacialCachePostgreSQL(
            _optionsMonitor.Object,
            _logger.Object,
            _timeConverter.Object,
            _dataSource.Object,
            _dbRawCommands.Object,
            _serviceProvider.Object,
            _timeProvider.Object,
            _entryFactory);

        // Act & Assert - Non-connection failures should propagate as exceptions
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetStringAsync("test-key"));

        Assert.Equal("Some other error", exception.Message);

        // Verify no connection failure logging occurred (check for the specific message)
        _logger.Verify(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Database connection failed")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    private static Exception CreateConnectionException()
    {
        // Create an exception that will be detected as a connection failure
        // IsConnectionFailure checks for InvalidOperationException with message containing "connection"
        // The check is case-sensitive, so we need lowercase "connection" in the message
        return new InvalidOperationException("connection failed: unable to connect to database");
    }
}
