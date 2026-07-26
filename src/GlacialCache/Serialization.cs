using MemoryPack;
using System.Text;

namespace GlacialCache.Abstractions;

public interface ICacheEntrySerializer
{
    byte[] Serialize<T>(T value) where T : notnull;
    T Deserialize<T>(byte[] data) where T : notnull;
    bool IsByteArray<T>();
    string GetBaseType<T>();
}

public class MemoryPackCacheEntrySerializer : ICacheEntrySerializer
{
    public byte[] Serialize<T>(T value) where T : notnull
    {
        if (typeof(T) == typeof(string))
            return value is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes((string)(object)value);
        if (typeof(T) == typeof(byte[]))
            return value is null ? Array.Empty<byte>() : (byte[])(object)value;
        return MemoryPackSerializer.Serialize(value);
    }

    public T Deserialize<T>(byte[] data) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(data);
        if (typeof(T) == typeof(string))
        {
            try { return (T)(object)new UTF8Encoding(false, true).GetString(data); }
            catch (DecoderFallbackException) { return default!; }
        }
        if (typeof(T) == typeof(byte[]))
            return (T)(object)data;
        return MemoryPackSerializer.Deserialize<T>(data)
            ?? throw new InvalidOperationException($"Failed to deserialize value of type {typeof(T).Name}.");
    }

    public bool IsByteArray<T>() => typeof(T) == typeof(byte[]);
    public string GetBaseType<T>() => typeof(T).FullName ?? typeof(T).Name;
}

public sealed class CacheEntryFactory
{
    private readonly ICacheEntrySerializer _serializer;
    public CacheEntryFactory(ICacheEntrySerializer serializer) => _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

    public CacheEntry<T> Create<T>(string key, T value, DateTimeOffset? absoluteExpiration = null, TimeSpan? slidingExpiration = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        var bytes = _serializer.Serialize(value);
        return new CacheEntry<T> { Key = key, Value = value, AbsoluteExpiration = absoluteExpiration, SlidingExpiration = slidingExpiration,
            SerializedData = bytes, BaseType = _serializer.GetBaseType<T>(), SizeInBytes = bytes.Length };
    }

    public CacheEntry<T> FromSerializedData<T>(string key, byte[] bytes, DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null, string? baseType = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(bytes);
        var value = _serializer.IsByteArray<T>() ? (T)(object)bytes : _serializer.Deserialize<T>(bytes);
        return new CacheEntry<T> { Key = key, Value = value, AbsoluteExpiration = absoluteExpiration, SlidingExpiration = slidingExpiration,
            SerializedData = bytes, BaseType = baseType ?? (_serializer.IsByteArray<T>() ? string.Empty : _serializer.GetBaseType<T>()), SizeInBytes = bytes.Length };
    }

    public T Deserialize<T>(byte[] data) where T : notnull => _serializer.Deserialize<T>(data);
    public string GetBaseType<T>() => _serializer.GetBaseType<T>();

    public CacheEntry<T> PrepareForStorage<T>(CacheEntry<T> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.SerializedData.IsEmpty
            ? Create(entry.Key, entry.Value, entry.AbsoluteExpiration, entry.SlidingExpiration)
            : entry;
    }

    public bool TryFromSerializedData<T>(CacheEntry<byte[]> entry, out CacheEntry<T>? result, out Exception? error)
    {
        ArgumentNullException.ThrowIfNull(entry);
        result = null;
        error = null;
        if (!string.IsNullOrWhiteSpace(entry.BaseType) &&
            !string.Equals(entry.BaseType, GetBaseType<T>(), StringComparison.Ordinal))
            return false;

        try
        {
            result = FromSerializedData<T>(entry.Key, entry.SerializedData.ToArray(), entry.AbsoluteExpiration,
                entry.SlidingExpiration, entry.BaseType);
            if (result.Value is null)
            {
                result = null;
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception;
            return false;
        }
    }
}
