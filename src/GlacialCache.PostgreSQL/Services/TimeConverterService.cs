using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Services;

/// <summary>
/// Service for converting absolute expiration times to relative time intervals.
/// Uses TimeProvider for testable and mockable time operations.
/// Handles edge cases like very short/long intervals and past times.
/// </summary>
internal sealed class TimeConverterService : ITimeConverterService
{
    private readonly ILogger<TimeConverterService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<GlacialCachePostgreSQLOptions> _options;

    public TimeConverterService(
        ILogger<TimeConverterService> logger,
        TimeProvider timeProvider,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public TimeSpan? ConvertToRelativeInterval(DateTimeOffset? absoluteExpiration)
    {
        return ConvertToRelativeInterval(absoluteExpiration, _timeProvider.GetUtcNow());
    }

    public TimeSpan? ConvertToRelativeInterval(DateTimeOffset? absoluteExpiration, DateTimeOffset now)
    {
        if (!absoluteExpiration.HasValue)
        {
            return null; // No expiration
        }

        var cacheOptions = _options.CurrentValue.Cache;
        var relativeInterval = absoluteExpiration.Value - now;

        // Handle past times - convert to immediate expiration
        if (relativeInterval <= TimeSpan.Zero)
        {
            if (cacheOptions.EnableEdgeCaseLogging)
            {
                _logger.LogWarning(
                    "Absolute expiration time {AbsoluteExpiration} is in the past (current time: {CurrentTime}). Converting to immediate expiration.",
                    absoluteExpiration.Value, now);
            }
            return cacheOptions.MinimumExpirationInterval; // Configurable immediate expiration
        }

        // Handle very short intervals
        if (relativeInterval < cacheOptions.MinimumExpirationInterval)
        {
            if (cacheOptions.EnableEdgeCaseLogging)
            {
                _logger.LogWarning(
                    "Relative interval {RelativeInterval} is shorter than minimum allowed interval {MinimumInterval}. Clamping to minimum.",
                    relativeInterval, cacheOptions.MinimumExpirationInterval);
            }
            return cacheOptions.MinimumExpirationInterval;
        }

        // Handle very long intervals
        if (relativeInterval > cacheOptions.MaximumExpirationInterval)
        {
            if (cacheOptions.EnableEdgeCaseLogging)
            {
                _logger.LogWarning(
                    "Relative interval {RelativeInterval} exceeds maximum allowed interval {MaximumInterval}. Clamping to maximum.",
                    relativeInterval, cacheOptions.MaximumExpirationInterval);
            }
            return cacheOptions.MaximumExpirationInterval;
        }

        // Handle edge case: extremely small positive intervals that might cause issues
        if (relativeInterval.TotalMilliseconds < 10)
        {
            if (cacheOptions.EnableEdgeCaseLogging)
            {
                _logger.LogInformation(
                    "Very small relative interval detected: {RelativeInterval}. This may cause rapid expiration.",
                    relativeInterval);
            }
        }

        return relativeInterval;
    }
}
