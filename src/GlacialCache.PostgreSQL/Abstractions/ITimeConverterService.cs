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
}
