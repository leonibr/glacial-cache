using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace GlacialCache.PostgreSQL.Tests.UnitTests.ManagerElection;

public class VoluntaryYieldTests : IDisposable
{
    private readonly Mock<ILogger<ManagerElectionService>> _mockLogger;
    private readonly Mock<IPostgreSQLDataSource> _mockDataSource;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly ManagerElectionService _service;

    public VoluntaryYieldTests()
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
    public void VoluntaryYieldInterval_ShouldBeFiveMinutes()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var interval = GetVoluntaryYieldInterval(service);

        // Assert
        interval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void YieldWindow_ShouldBeFiveSeconds()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var window = GetYieldWindow(service);

        // Assert
        window.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MaxBackoff_ShouldBeOneMinute()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var maxBackoff = GetMaxBackoff(service);

        // Assert
        maxBackoff.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void BaseBackoff_ShouldBeFiveSeconds()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var baseBackoff = GetBaseBackoff(service);

        // Assert
        baseBackoff.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void JitterRange_ShouldBeOneSecond()
    {
        // Arrange
        var service = CreateServiceWithReflection();

        // Act
        var jitterRange = GetJitterRange(service);

        // Assert
        jitterRange.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Service_ShouldHaveCorrectInstanceId()
    {
        // Act
        var instanceId = GetInstanceId(_service);

        // Assert
        instanceId.Should().NotBeNullOrEmpty();
        instanceId.Should().Contain(Environment.MachineName);
        instanceId.Should().Contain(Environment.ProcessId.ToString());
    }

    private ManagerElectionService CreateServiceWithReflection()
    {
        var options = Options.Create(_options);
        return new ManagerElectionService(options, _mockLogger.Object, _mockDataSource.Object, "test-instance", _mockTimeProvider.Object);
    }

    private TimeSpan GetVoluntaryYieldInterval(ManagerElectionService service)
    {
        var field = typeof(ManagerElectionService).GetField("_voluntaryYieldInterval",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (TimeSpan)field!.GetValue(service)!;
    }

    private TimeSpan GetYieldWindow(ManagerElectionService service)
    {
        var field = typeof(ManagerElectionService).GetField("_yieldWindow",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (TimeSpan)field!.GetValue(service)!;
    }

    private TimeSpan GetMaxBackoff(ManagerElectionService service)
    {
        var field = typeof(ManagerElectionService).GetField("_maxBackoff",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (TimeSpan)field!.GetValue(service)!;
    }

    private TimeSpan GetBaseBackoff(ManagerElectionService service)
    {
        var field = typeof(ManagerElectionService).GetField("_baseBackoff",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (TimeSpan)field!.GetValue(service)!;
    }

    private TimeSpan GetJitterRange(ManagerElectionService service)
    {
        var field = typeof(ManagerElectionService).GetField("_jitterRange",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (TimeSpan)field!.GetValue(service)!;
    }

    private string GetInstanceId(ManagerElectionService service)
    {
        var field = typeof(ManagerElectionService).GetField("_instanceId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (string)field!.GetValue(service)!;
    }

    public void Dispose()
    {
        _service?.Dispose();
    }
}
