using GlacialCache.Abstractions;

namespace GlacialCache.PostgreSQL.Services;

/// <summary>Compatibility facade over the provider-neutral cache-entry factory.</summary>
public sealed class CacheEntryHelper
{
    private readonly CacheEntryFactory _factory;
    public CacheEntryHelper(ICacheEntrySerializer serializer) => _factory = new CacheEntryFactory(serializer);

    public CacheEntry<T> Create<T>(string key, T value, DateTimeOffset? absoluteExpiration = null, TimeSpan? slidingExpiration = null) =>
        _factory.Create(key, value, absoluteExpiration, slidingExpiration);

    public CacheEntry<T> FromSerializedData<T>(string key, byte[] serializedValue, DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null, string? baseType = null) =>
        _factory.FromSerializedData<T>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);

    public ReadOnlyMemory<byte> GetSerializedData<T>(CacheEntry<T> entry) => entry.SerializedData;
    public string GetBaseType<T>() => _factory.GetBaseType<T>();
    public int GetSizeInBytes<T>(CacheEntry<T> entry) => entry.SizeInBytes;
    public T Deserialize<T>(byte[] data) where T : notnull => _factory.Deserialize<T>(data);
    public CacheEntry<T> PrepareForStorage<T>(CacheEntry<T> entry) => _factory.PrepareForStorage(entry);
    public bool TryFromSerializedData<T>(CacheEntry<byte[]> entry, out CacheEntry<T>? result, out Exception? error) =>
        _factory.TryFromSerializedData(entry, out result, out error);
}
