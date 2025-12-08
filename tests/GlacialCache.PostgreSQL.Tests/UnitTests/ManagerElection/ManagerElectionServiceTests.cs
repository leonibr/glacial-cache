using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace GlacialCache.PostgreSQL.Tests.UnitTests.ManagerElection;

public class ManagerElectionServiceTests
{
    private readonly Mock<ILogger<ManagerElectionService>> _mockLogger;
    private readonly Mock<IPostgreSQLDataSource> _mockDataSource;
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly ManagerElectionService _service;
    private readonly TimeTestHelper _time;

    public ManagerElectionServiceTests()
    {
        _mockLogger = new Mock<ILogger<ManagerElectionService>>();
        _mockDataSource = new Mock<IPostgreSQLDataSource>();

        _options = new GlacialCachePostgreSQLOptions
        {
            Infrastructure = new InfrastructureOptions
            {
                Lock = new LockOptions
                {
                    LockTimeout = TimeSpan.FromMinutes(5)
                }
            }
        };

        // Generate lock key
        _options.Infrastructure.Lock.GenerateLockKey("test_schema", "test_table");

        var optionsWrapper = new Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
        optionsWrapper.Setup(x => x.CurrentValue).Returns(_options);

        // Initialize TimeTestHelper first
        _time = TimeTestHelper.CreateForUnitTests();

        // Use FakeTimeProvider from TimeTestHelper instead of Mock<TimeProvider>
        _service = new ManagerElectionService(optionsWrapper.Object, _mockLogger.Object, _mockDataSource.Object, _time.TimeProvider);
    }

    [Fact]
    public void IsManager_ShouldReturnFalse_WhenNotElected()
    {
        // Act & Assert
        _service.IsManager.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireManagerRoleAsync_ShouldHandleDatabaseException()
    {
        // Arrange - Mock data source to throw exception
        _mockDataSource.Setup(ds => ds.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NpgsqlException("Database error"));

        // Act
        var result = await _service.TryAcquireManagerRoleAsync();

        // Assert
        result.Should().BeFalse();
        _service.IsManager.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireManagerRoleAsync_ShouldHandleCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _service.TryAcquireManagerRoleAsync(cts.Token));
    }

    [Fact]
    public async Task ReleaseManagerRoleAsync_ShouldHandleCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _service.ReleaseManagerRoleAsync(cts.Token));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ManagerElectionService(null!, _mockLogger.Object, _mockDataSource.Object, _time.TimeProvider));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Arrange
        var options = Options.Create(_options);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ManagerElectionService(options, null!, _mockDataSource.Object, "test-instance", _time.TimeProvider));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenDataSourceIsNull()
    {
        // Arrange
        var options = Options.Create(_options);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ManagerElectionService(options, _mockLogger.Object, null!, "test-instance", _time.TimeProvider));
    }

    [Fact]
    public async Task TryAcquireAdvisoryLockAsync_PermissionDenied_ReturnsFalseWithClearMessage()
    {
        // Arrange
        // Create a PostgresException with SQL state 42501 (permission denied)
        var postgresException = new PostgresException("permission denied for function pg_try_advisory_lock", "", "", "42501");

        // Mock the data source to throw the exception when getting connection
        _mockDataSource.Setup(ds => ds.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresException);

        // Act
        var result = await _service.TryAcquireManagerRoleAsync();

        // Assert
        result.Should().BeFalse();
        _service.IsManager.Should().BeFalse();

        // Verify that the specific error message was logged
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Advisory lock permission denied")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify that the manual coordination guidance was included
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Automatic coordination disabled")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify that the permission grant instructions were included
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("GRANT EXECUTE ON FUNCTION pg_try_advisory_lock")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify that the manual coordination options were included
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("EnableManagerElection=false")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task TryAcquireAdvisoryLockAsync_GenericException_ReturnsFalseWithWarningMessage()
    {
        // Arrange
        // Create a generic exception (not PostgresException with 42501)
        var genericException = new InvalidOperationException("Generic database error");

        // Mock the data source to throw the exception when getting connection
        _mockDataSource.Setup(ds => ds.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(genericException);

        // Act
        var result = await _service.TryAcquireManagerRoleAsync();

        // Assert
        result.Should().BeFalse();
        _service.IsManager.Should().BeFalse();

        // Verify that a warning was logged (not error)
        _mockLogger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error acquiring advisory lock")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify that the specific permission denied message was NOT logged
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Advisory lock permission denied")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task TryAcquireAdvisoryLockAsync_PostgresExceptionNon42501_ReturnsFalseWithWarningMessage()
    {
        // Arrange
        // Create a PostgresException with different SQL state (not 42501)
        var postgresException = new PostgresException("connection lost", "", "", "08006");

        // Mock the data source to throw the exception when getting connection
        _mockDataSource.Setup(ds => ds.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresException);

        // Act
        var result = await _service.TryAcquireManagerRoleAsync();

        // Assert
        result.Should().BeFalse();
        _service.IsManager.Should().BeFalse();

        // Verify that a warning was logged (not error)
        _mockLogger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error acquiring advisory lock")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify that the specific permission denied message was NOT logged
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Advisory lock permission denied")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
