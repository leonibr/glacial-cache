using System;

namespace GlacialCache.PostgreSQL.Models.CommandParameters;

/// <summary>
/// Parameters for REFRESH cache entry operations.
/// </summary>
internal sealed record RefreshEntryParameters
{
    /// <summary>
    /// The cache key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The current UTC timestamp.
    /// </summary>
    public required DateTimeOffset Now { get; init; }
}

