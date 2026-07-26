namespace GlacialCache.Abstractions;

public interface ICacheEntry
{
    string Key { get; }
    ReadOnlyMemory<byte> SerializedData { get; }
    DateTimeOffset? AbsoluteExpiration { get; }
    TimeSpan? SlidingExpiration { get; }
    string BaseType { get; }
    int SizeInBytes { get; }
}

public record CacheEntry<T> : ICacheEntry
{
    public string Key { get; init; } = null!;
    public T Value { get; init; } = default!;
    public DateTimeOffset? AbsoluteExpiration { get; init; }
    public TimeSpan? SlidingExpiration { get; init; }
    public ReadOnlyMemory<byte> SerializedData { get; init; }
    public string BaseType { get; init; } = null!;
    public int SizeInBytes { get; init; }
}
