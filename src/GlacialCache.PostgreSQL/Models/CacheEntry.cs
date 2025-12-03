namespace GlacialCache.PostgreSQL.Models;

/// <summary>
/// Interface for cache entries with optional serialization metadata.
/// </summary>
public interface ICacheEntry
{
    string Key { get; }

    ReadOnlyMemory<byte> SerializedData { get; }

    DateTimeOffset? AbsoluteExpiration { get; }

    TimeSpan? SlidingExpiration { get; }

    /// <summary>
    /// The base type of the cached value (for typed operations).
    /// </summary>
    string BaseType { get; }

    /// <summary>
    /// Size of the serialized value in bytes.
    /// </summary>
    int SizeInBytes { get; }
}

/// <summary>
/// Pure POCO cache entry with injected computed properties.
/// All serialization logic is handled by the factory.
/// </summary>
public sealed record CacheEntry<T> : ICacheEntry
{
    public string Key { get; init; } = null!;
    public T Value { get; init; } = default!;
    public DateTimeOffset? AbsoluteExpiration { get; init; }
    public TimeSpan? SlidingExpiration { get; init; }

    // ICacheEntry properties (injected by factory)
    public ReadOnlyMemory<byte> SerializedData { get; init; } = default;
    public string BaseType { get; init; } = null!;
    public int SizeInBytes { get; init; }
}