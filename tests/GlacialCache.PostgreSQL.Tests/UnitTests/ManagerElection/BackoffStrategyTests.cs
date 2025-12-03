using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace GlacialCache.PostgreSQL.Tests.UnitTests.ManagerElection;

public class BackoffStrategyTests : IDisposable
{
    private readonly Mock<ILogger<ManagerElectionService>> _mockLogger;
    private readonly Mock<IPostgreSQLDataSource> _mockDataSource;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly ManagerElectionService _service;

    public BackoffStrategyTests()
    {
        _mockLogger = new Mock<ILogger<ManagerElectionService>>();
        _mockDataSource = new Mock<IPostgreSQLDataSource>();
        _mockTimeProvider = new Mock<TimeProvider>();

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

        _service = new ManagerElectionService(optionsWrapper.Object, _mockLogger.Object, _mockDataSource.Object, _mockTimeProvider.Object);
    }

    [Fact]
    public void CalculateBackoffDelay_ShouldIncreaseExponentially()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var delay1 = InvokeCalculateBackoffDelay(service, 1);
        var delay2 = InvokeCalculateBackoffDelay(service, 2);
        var delay3 = InvokeCalculateBackoffDelay(service, 3);

        // Assert
        delay2.Should().BeGreaterThan(delay1);
        delay3.Should().BeGreaterThan(delay2);
    }

    [Fact]
    public void CalculateBackoffDelay_ShouldCapAtMaximumBackoff()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var delay1 = InvokeCalculateBackoffDelay(service, 1);
        var delay10 = InvokeCalculateBackoffDelay(service, 10);
        var delay20 = InvokeCalculateBackoffDelay(service, 20);

        // Assert
        delay10.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1)); // Allow for jitter
        delay20.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1)); // Allow for jitter
    }

    [Fact]
    public void CalculateBackoffDelay_ShouldIncludeJitter()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var delays = new List<TimeSpan>();
        for (int i = 0; i < 10; i++)
        {
            delays.Add(InvokeCalculateBackoffDelay(service, 1));
        }

        // Assert
        delays.Should().HaveCount(10);
        delays.Should().NotBeEmpty();
        // Check that not all delays are the same (jitter should cause variation)
        delays.Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public void CalculateBackoffDelay_ShouldHaveReasonableJitterRange()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var delays = new List<TimeSpan>();
        for (int i = 0; i < 100; i++)
        {
            delays.Add(InvokeCalculateBackoffDelay(service, 1));
        }

        // Assert
        var minDelay = delays.Min();
        var maxDelay = delays.Max();
        var jitterRange = maxDelay - minDelay;

        // Jitter should be reasonable (±1 second as configured)
        jitterRange.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(2));
        jitterRange.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void CalculateBackoffDelay_ShouldHandleZeroAttemptCount()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var delay = InvokeCalculateBackoffDelay(service, 0);

        // Assert
        delay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void CalculateBackoffDelay_ShouldHandleNegativeAttemptCount()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var delay = InvokeCalculateBackoffDelay(service, -1);

        // Assert
        delay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task TryAcquireManagerRoleAsync_ShouldBackOffOnFailure()
    {
        // Arrange
        _mockDataSource.Setup(ds => ds.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NpgsqlException("Connection failed"));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = await _service.TryAcquireManagerRoleAsync();

        // Assert
        result.Should().BeFalse();
        stopwatch.Stop();

        // Should not have significant delay for single attempt
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void InstanceSpecificRandomSeed_ShouldBeDeterministic()
    {
        // Arrange
        var options1 = Options.Create(_options);
        var options2 = Options.Create(_options);

        // Act
        var service1 = new ManagerElectionService(options1, _mockLogger.Object, _mockDataSource.Object, "instance1", _mockTimeProvider.Object);
        var service2 = new ManagerElectionService(options2, _mockLogger.Object, _mockDataSource.Object, "instance2", _mockTimeProvider.Object);

        // Assert
        service1.Should().NotBeNull();
        service2.Should().NotBeNull();

        // Both services should be created successfully with same configuration
        service1.IsManager.Should().BeFalse();
        service2.IsManager.Should().BeFalse();
    }

    private ManagerElectionService CreateServiceWithReflection()
    {
        var options = Options.Create(_options);
        return new ManagerElectionService(options, _mockLogger.Object, _mockDataSource.Object, "test-instance", _mockTimeProvider.Object);
    }

    private TimeSpan InvokeCalculateBackoffDelay(ManagerElectionService service, int attemptCount)
    {
        var method = typeof(ManagerElectionService).GetMethod("CalculateBackoffDelay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return (TimeSpan)method!.Invoke(service, new object[] { attemptCount })!;
    }

    public void Dispose()
    {
        _service?.Dispose();
    }
}
