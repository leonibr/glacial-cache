using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Configuration;
using Moq;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class TimeConverterServiceTests
{
    private readonly Mock<ILogger<TimeConverterService>> _mockLogger = new();
    private readonly Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>> _mockOptions = new();
    private readonly Mock<TimeProvider> _mockTimeProvider = new();
    private readonly TimeConverterService _service;
    private readonly GlacialCachePostgreSQLOptions _options;

    private void SetupTimeProvider(DateTimeOffset time)
    {
        _mockTimeProvider.Setup(tp => tp.GetUtcNow()).Returns(time);
    }

    public TimeConverterServiceTests()
    {
        // Setup default options
        _options = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                MinimumExpirationInterval = TimeSpan.FromMilliseconds(1),
                MaximumExpirationInterval = TimeSpan.FromDays(365),
                EnableEdgeCaseLogging = true
            }
        };
        _mockOptions.Setup(x => x.CurrentValue).Returns(_options);

        // Setup TimeProvider mock with default time
        _mockTimeProvider.Setup(tp => tp.GetUtcNow())
            .Returns(new DateTimeOffset(2025, 9, 4, 12, 0, 0, TimeSpan.Zero));

        // Create service with mocked TimeProvider
        _service = new TimeConverterService(_mockLogger.Object, _mockTimeProvider.Object, _mockOptions.Object);
    }


    // ─────────────────────────────────────────────────────────────────────────────
    //  Construction
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithValidParameters_CreatesService()
    {
        var service = new TimeConverterService(_mockLogger.Object, _mockTimeProvider.Object, _mockOptions.Object);
        service.ShouldNotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new TimeConverterService(null!, _mockTimeProvider.Object, _mockOptions.Object);
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("logger");
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var act = () => new TimeConverterService(_mockLogger.Object, null!, _mockOptions.Object);
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("timeProvider");
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var act = () => new TimeConverterService(_mockLogger.Object, _mockTimeProvider.Object, null!);
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("options");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Null Absolute Expiration
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConvertToRelativeInterval_WithNullAbsoluteExpiration_ReturnsNull()
    {
        var result = _service.ConvertToRelativeInterval(null);
        result.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Future Absolute Expiration
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConvertToRelativeInterval_WithFutureAbsoluteExpiration_ReturnsCorrectRelativeInterval()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime.AddHours(2); // 2 hours in future
        var expectedRelativeInterval = TimeSpan.FromHours(2);

        SetupTimeProvider(currentTime);

        // Act
        var result = _service.ConvertToRelativeInterval(absoluteExpiration);

        // Assert
        result.ShouldBe(expectedRelativeInterval);
    }


    #region Past Absolute Expiration

    [Fact]
    public void ConvertToRelativeInterval_WithPastAbsoluteExpiration_ReturnsImmediateExpiration()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime.AddHours(-1); // 1 hour in past
        var expectedRelativeInterval = TimeSpan.FromMilliseconds(1); // Immediate expiration

        SetupTimeProvider(currentTime);

        // Act
        var result = _service.ConvertToRelativeInterval(absoluteExpiration);

        // Assert
        result.ShouldBe(expectedRelativeInterval);
    }

    #endregion

    #region Current Time (Zero Interval)

    [Fact]
    public void ConvertToRelativeInterval_WithCurrentTime_ReturnsImmediateExpiration()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime; // Exactly current time
        var expectedRelativeInterval = TimeSpan.FromMilliseconds(1); // Immediate expiration

        SetupTimeProvider(currentTime);

        // Act
        var result = _service.ConvertToRelativeInterval(absoluteExpiration);

        // Assert
        result.ShouldBe(expectedRelativeInterval);
    }

    [Fact]
    public void ConvertToRelativeInterval_WithZeroIntervalAfterSubtraction_ReturnsImmediateExpiration()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime.AddTicks(1); // Very small positive interval that becomes zero due to precision

        SetupTimeProvider(currentTime);

        // Act
        var result = _service.ConvertToRelativeInterval(absoluteExpiration);

        // Assert - should handle zero or negative intervals as immediate expiration
        result.ShouldBe(TimeSpan.FromMilliseconds(1));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ConvertToRelativeInterval_WithVeryLargeFutureExpiration_ReturnsCorrectInterval()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime.AddDays(365); // 1 year in future
        var expectedRelativeInterval = TimeSpan.FromDays(365);

        SetupTimeProvider(currentTime);

        // Act
        var result = _service.ConvertToRelativeInterval(absoluteExpiration);

        // Assert
        result.ShouldBe(expectedRelativeInterval);
    }

    #endregion
    #region TimeProvider Integration


    [Fact]
    public void ConvertToRelativeInterval_UsesTimeProviderGetUtcNow()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime.AddHours(1);

        SetupTimeProvider(currentTime);

        // Act
        var result = _service.ConvertToRelativeInterval(absoluteExpiration);

        // Assert - Verify the service correctly uses the FakeTimeProvider time
        result.ShouldBe(TimeSpan.FromHours(1));
    }

    #endregion

    #region Very Short Intervals

    [Fact]
    public void ConvertToRelativeInterval_WithVeryShortInterval_ClampsToMinimum()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime.AddMilliseconds(0.5); // Very short interval
        var expectedMinimum = TimeSpan.FromMilliseconds(1);

        SetupTimeProvider(currentTime);

        // Act
        var result = _service.ConvertToRelativeInterval(absoluteExpiration);

        // Assert
        result.ShouldBe(expectedMinimum);
    }


    #endregion

    #region Very Long Intervals

    [Fact]
    public void ConvertToRelativeInterval_WithVeryLongInterval_ClampsToMaximum()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime.AddDays(400); // Very long interval (over 1 year)
        var expectedMaximum = TimeSpan.FromDays(365);

        SetupTimeProvider(currentTime);

        // Act
        var result = _service.ConvertToRelativeInterval(absoluteExpiration);

        // Assert
        result.ShouldBe(expectedMaximum);
    }


    #endregion

    #region Configuration-Based Behavior

    [Fact]
    public void ConvertToRelativeInterval_UsesConfiguredMinimumInterval()
    {
        // Arrange
        var currentTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var absoluteExpiration = currentTime.AddMilliseconds(10); // Below configured minimum
        var customMinimum = TimeSpan.FromMilliseconds(100);

        // Setup custom options
        var customOptions = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                MinimumExpirationInterval = customMinimum,
                MaximumExpirationInterval = TimeSpan.FromDays(365),
                EnableEdgeCaseLogging = true
            }
        };
        var mockCustomOptions = new Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
        mockCustomOptions.Setup(x => x.CurrentValue).Returns(customOptions);

        var customService = new TimeConverterService(_mockLogger.Object, _mockTimeProvider.Object, mockCustomOptions.Object);

        SetupTimeProvider(currentTime);

        // Act
        var result = customService.ConvertToRelativeInterval(absoluteExpiration);

        // Assert
        result.ShouldBe(customMinimum);
    }


    #endregion
}
