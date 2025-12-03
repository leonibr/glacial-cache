namespace GlacialCache.PostgreSQL.Models.CommandParameters;

/// <summary>
/// Parameters for REMOVE cache entry operations.
/// </summary>
internal sealed record RemoveEntryParameters
{
    /// <summary>
    /// The cache key.
    /// </summary>
    public required string Key { get; init; }
}

