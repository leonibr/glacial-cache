namespace GlacialCache.PostgreSQL.Abstractions;

/// <summary>
/// Service responsible for periodic cleanup of expired cache entries.
/// </summary>
internal interface ICleanupBackgroundService
{
    void Dispose();
}
