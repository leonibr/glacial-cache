using System;

namespace GlacialCache.PostgreSQL.Models.CommandParameters;

/// <summary>
/// Parameters for SET cache entry operations.
/// </summary>
internal sealed record SetEntryParameters
{
    /// <summary>
    /// The cache key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The cache value as byte array.
    /// </summary>
    public required byte[] Value { get; init; }

    /// <summary>
    /// The current UTC timestamp.
    /// </summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>
    /// Relative interval for absolute expiration (from now).
    /// </summary>
    public TimeSpan? RelativeInterval { get; init; }

    /// <summary>
    /// Sliding expiration interval.
    /// </summary>
    public TimeSpan? SlidingInterval { get; init; }

    /// <summary>
    /// Optional type information for the cached value.
    /// </summary>
    public string? ValueType { get; init; }
}

