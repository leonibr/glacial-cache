using System;

namespace GlacialCache.PostgreSQL.Models.CommandParameters;

/// <summary>
/// Parameters for GET cache entry operations.
/// </summary>
internal sealed record GetEntryParameters
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

