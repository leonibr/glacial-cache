namespace GlacialCache.PostgreSQL.Abstractions;

/// <summary>
/// Service for converting absolute expiration times to relative time intervals.
/// </summary>
public interface ITimeConverterService
{
    /// <summary>
    /// Converts an absolute expiration time to a relative time interval from now.
    /// </summary>
    /// <param name="absoluteExpiration">The absolute expiration time, or null for no expiration.</param>
    /// <returns>The relative time interval, or null if no expiration.</returns>
    TimeSpan? ConvertToRelativeInterval(DateTimeOffset? absoluteExpiration);

    /// <summary>
    /// Converts an absolute expiration time to a relative time interval from the provided current time.
    /// </summary>
    /// <param name="absoluteExpiration">The absolute expiration time, or null for no expiration.</param>
    /// <param name="now">The current UTC timestamp to use for the conversion.</param>
    /// <returns>The relative time interval, or null if no expiration.</returns>
    TimeSpan? ConvertToRelativeInterval(DateTimeOffset? absoluteExpiration, DateTimeOffset now);
}
