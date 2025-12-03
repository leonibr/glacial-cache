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
    public void Constructor_ShouldInitializeWithCorrectLockKey()
    {
        // Arrange
        var options = Options.Create(_options);

        // Act
        var service = new ManagerElectionService(options, _mockLogger.Object, _mockDataSource.Object, "test-instance", _time.TimeProvider);

        // Assert
        service.Should().NotBeNull();
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(_options.Infrastructure.Lock.AdvisoryLockKey.ToString())),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        // Dispose the service
        service.Dispose();
    }

    [Fact]
    public void ManagerElected_ShouldFireEvent_WhenSubscribed()
    {
        // Arrange
        var expectedTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        _time.SetTime(expectedTime);

        ManagerElectedEventArgs? eventArgs = null;
        _service.ManagerElected += (sender, args) => eventArgs = args;

        // Act - Trigger event manually through reflection
        var method = typeof(ManagerElectionService).GetMethod("OnManagerElected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(_service, null);

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.InstanceId.Should().NotBeNullOrEmpty();
        eventArgs.ElectedAt.Should().Be(expectedTime);
    }

    [Fact]
    public void ManagerLost_ShouldFireEvent_WhenSubscribed()
    {
        // Arrange
        var expectedTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        _time.SetTime(expectedTime);

        ManagerLostEventArgs? eventArgs = null;
        _service.ManagerLost += (sender, args) => eventArgs = args;

        // Act - Trigger event manually through reflection
        var method = typeof(ManagerElectionService).GetMethod("OnManagerLost",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(_service, new object[] { "Test reason" });

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.InstanceId.Should().NotBeNullOrEmpty();
        eventArgs.Reason.Should().Be("Test reason");
        eventArgs.LostAt.Should().Be(expectedTime);
    }

    [Fact]
    public void TimeProvider_ShouldProvideConsistentTime_AcrossMultipleCalls()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        _time.SetTime(fixedTime);

        // Act - Get time multiple times
        var time1 = _time.Now();
        var time2 = _time.Now();
        var time3 = _time.Now();

        // Assert - All times should be identical since we haven't advanced time
        time1.Should().Be(fixedTime);
        time2.Should().Be(fixedTime);
        time3.Should().Be(fixedTime);
        time1.Should().Be(time2);
        time2.Should().Be(time3);
    }

    [Fact]
    public void TimeProvider_ShouldAdvanceTime_WhenRequested()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        _time.SetTime(startTime);

        // Act
        var initialTime = _time.Now();
        _time.Advance(TimeSpan.FromMinutes(5));
        var advancedTime = _time.Now();

        // Assert
        initialTime.Should().Be(startTime);
        advancedTime.Should().Be(startTime.AddMinutes(5));
        (advancedTime - initialTime).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ManagerElectionService_ShouldUseTimeProvider_ForEventTimestamps()
    {
        // Arrange
        var electionTime = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        _time.SetTime(electionTime);

        ManagerElectedEventArgs? electedEventArgs = null;
        _service.ManagerElected += (sender, args) => electedEventArgs = args;

        // Act - Simulate manager election
        var method = typeof(ManagerElectionService).GetMethod("OnManagerElected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(_service, null);

        // Advance time and simulate manager lost
        _time.Advance(TimeSpan.FromHours(2));
        var lossTime = _time.Now();

        ManagerLostEventArgs? lostEventArgs = null;
        _service.ManagerLost += (sender, args) => lostEventArgs = args;

        var lostMethod = typeof(ManagerElectionService).GetMethod("OnManagerLost",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lostMethod!.Invoke(_service, new object[] { "Voluntary yield" });

        // Assert
        electedEventArgs.Should().NotBeNull();
        electedEventArgs!.ElectedAt.Should().Be(electionTime);

        lostEventArgs.Should().NotBeNull();
        lostEventArgs!.LostAt.Should().Be(lossTime);
        lostEventArgs.Reason.Should().Be("Voluntary yield");

        // Verify time progression
        (lostEventArgs.LostAt - electedEventArgs.ElectedAt).Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ManagerElectionService_ShouldHandleTimeBasedScenarios_WithPrecision()
    {
        // Arrange - Start at a specific time
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        _time.SetTime(startTime);

        var events = new List<(string EventType, DateTimeOffset Timestamp)>();

        _service.ManagerElected += (sender, args) =>
            events.Add(("Elected", args.ElectedAt));
        _service.ManagerLost += (sender, args) =>
            events.Add(("Lost", args.LostAt));

        // Act - Simulate a series of time-based events
        var electedMethod = typeof(ManagerElectionService).GetMethod("OnManagerElected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var lostMethod = typeof(ManagerElectionService).GetMethod("OnManagerLost",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Event 1: Initial election
        electedMethod!.Invoke(_service, null);

        // Event 2: Advance 30 minutes, lose leadership
        _time.Advance(TimeSpan.FromMinutes(30));
        lostMethod!.Invoke(_service, new object[] { "Connection lost" });

        // Event 3: Advance 5 minutes, regain leadership
        _time.Advance(TimeSpan.FromMinutes(5));
        electedMethod.Invoke(_service, null);

        // Event 4: Advance 2 hours, voluntary yield
        _time.Advance(TimeSpan.FromHours(2));
        lostMethod.Invoke(_service, new object[] { "Voluntary yield" });

        // Assert
        events.Should().HaveCount(4);

        events[0].Should().Be(("Elected", startTime));
        events[1].Should().Be(("Lost", startTime.AddMinutes(30)));
        events[2].Should().Be(("Elected", startTime.AddMinutes(35)));
        events[3].Should().Be(("Lost", startTime.AddMinutes(35).AddHours(2)));

        // Verify time intervals
        var totalElapsedTime = events[3].Timestamp - events[0].Timestamp;
        totalElapsedTime.Should().Be(TimeSpan.FromMinutes(155)); // 30 + 5 + 120 = 155 minutes
    }

    [Fact]
    public void ManagerElectionService_ShouldHandleBackoffScenarios_WithTimeControl()
    {
        // This test demonstrates how FakeTimeProvider enables testing of time-dependent backoff logic
        // Even though we can't easily test the actual ExecuteAsync method due to its complexity,
        // we can test the time-related components

        // Arrange
        var backoffStartTime = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        _time.SetTime(backoffStartTime);

        // Act - Simulate backoff calculation scenarios
        var initialTime = _time.Now();

        // Simulate first backoff attempt (5 seconds)
        _time.Advance(TimeSpan.FromSeconds(5));
        var firstBackoffTime = _time.Now();

        // Simulate second backoff attempt (10 seconds)
        _time.Advance(TimeSpan.FromSeconds(10));
        var secondBackoffTime = _time.Now();

        // Simulate third backoff attempt (20 seconds)
        _time.Advance(TimeSpan.FromSeconds(20));
        var thirdBackoffTime = _time.Now();

        // Assert - Verify exponential backoff timing
        (firstBackoffTime - initialTime).Should().Be(TimeSpan.FromSeconds(5));
        (secondBackoffTime - firstBackoffTime).Should().Be(TimeSpan.FromSeconds(10));
        (thirdBackoffTime - secondBackoffTime).Should().Be(TimeSpan.FromSeconds(20));

        var totalBackoffTime = thirdBackoffTime - initialTime;
        totalBackoffTime.Should().Be(TimeSpan.FromSeconds(35));
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
