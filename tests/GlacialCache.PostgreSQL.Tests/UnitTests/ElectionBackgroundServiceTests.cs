using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class ElectionBackgroundServiceTests
{
    private readonly Mock<ILogger<ElectionBackgroundService>> _mockLogger;
    private readonly Mock<IPostgreSQLDataSource> _mockDataSource;
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly ElectionState _electionState;
    private readonly TimeTestHelper _time;

    public ElectionBackgroundServiceTests()
    {
        _mockLogger = new Mock<ILogger<ElectionBackgroundService>>();
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

        // Initialize TimeTestHelper first
        _time = TimeTestHelper.CreateForUnitTests();

        _electionState = new ElectionState(
            Mock.Of<ILogger<ElectionState>>(),
            _time.TimeProvider,
            "test-instance-123");
    }

    [Fact]
    public async Task Constructor_InitializesCorrectly()
    {
        // Arrange
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
        _electionState.InstanceId.Should().Be("test-instance-123");
    }

    [Fact]
    public async Task Service_CanBeCreatedAndDisposed()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
        service.Invoking(s => s.Dispose()).Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteAsync_HandlesCancellationGracefully()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Act - Start the service and immediately cancel
        var executeTask = service.StartAsync(cts.Token);
        cts.Cancel();

        // Assert - Should complete without throwing
        await executeTask;
    }

    private ElectionBackgroundService CreateService()
    {
        return new ElectionBackgroundService(
            Options.Create(_options),
            _mockLogger.Object,
            _mockDataSource.Object,
            _electionState,
            _time.TimeProvider);
    }
}
