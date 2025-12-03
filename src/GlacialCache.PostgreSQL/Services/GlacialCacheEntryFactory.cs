using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Models;

namespace GlacialCache.PostgreSQL.Services;

/// <summary>
/// Factory for creating CacheEntry instances with injected serialization properties.
/// </summary>
public class GlacialCacheEntryFactory
{
    private readonly ICacheEntrySerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the GlacialCacheEntryFactory class.
    /// </summary>
    /// <param name="serializer">The serializer to use for serialization operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when serializer is null.</exception>
    public GlacialCacheEntryFactory(ICacheEntrySerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Creates a CacheEntry with injected computed properties.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="absoluteExpiration">Optional absolute expiration time.</param>
    /// <param name="slidingExpiration">Optional sliding expiration time.</param>
    /// <returns>A new CacheEntry instance with injected properties.</returns>
    /// <exception cref="ArgumentNullException">Thrown when key or value is null.</exception>
    public CacheEntry<T> Create<T>(
        string key,
        T value,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null)
    {
        // Compute values using serializer
        var serializedData = _serializer.Serialize(value);
        var baseType = _serializer.GetBaseType<T>();
        var sizeInBytes = serializedData.Length;

        return new CacheEntry<T>
        {
            Key = key ?? throw new ArgumentNullException(nameof(key)),
            Value = value, // Allow null values
            AbsoluteExpiration = absoluteExpiration,
            SlidingExpiration = slidingExpiration,

            // Inject computed properties
            SerializedData = serializedData,
            BaseType = baseType,
            SizeInBytes = sizeInBytes
        };
    }

    /// <summary>
    /// Creates a CacheEntry from serialized data with injected computed properties.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="serializedValue">The serialized value data.</param>
    /// <param name="absoluteExpiration">Optional absolute expiration time.</param>
    /// <param name="slidingExpiration">Optional sliding expiration time.</param>
    /// <param name="baseType">Optional base type override.</param>
    /// <returns>A new CacheEntry instance with injected properties.</returns>
    /// <exception cref="ArgumentNullException">Thrown when key or serializedValue is null.</exception>
    public CacheEntry<T> FromSerializedData<T>(
        string key,
        byte[] serializedValue,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        string? baseType = null)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (serializedValue == null) throw new ArgumentNullException(nameof(serializedValue));

        if (_serializer.IsByteArray<T>())
        {
            var byteEntry = new CacheEntry<byte[]>
            {
                Key = key,
                Value = (byte[])(object)serializedValue,
                AbsoluteExpiration = absoluteExpiration,
                SlidingExpiration = slidingExpiration,

                // Inject computed properties
                SerializedData = serializedValue,
                BaseType = baseType ?? string.Empty, // Use empty string for null BaseType to preserve backward compatibility
                SizeInBytes = serializedValue.Length
            };
            return (CacheEntry<T>)(object)byteEntry;
        }

        var deserializedValue = _serializer.Deserialize<T>(serializedValue);
        return new CacheEntry<T>
        {
            Key = key,
            Value = deserializedValue,
            AbsoluteExpiration = absoluteExpiration,
            SlidingExpiration = slidingExpiration,

            // Inject computed properties
            SerializedData = serializedValue,
            BaseType = baseType ?? _serializer.GetBaseType<T>(),
            SizeInBytes = serializedValue.Length
        };
    }

    /// <summary>
    /// Gets the serialized data for a CacheEntry.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="entry">The cache entry.</param>
    /// <returns>The serialized data.</returns>
    public ReadOnlyMemory<byte> GetSerializedData<T>(CacheEntry<T> entry)
    {
        return entry.SerializedData;
    }

    /// <summary>
    /// Gets the base type for a given type T.
    /// </summary>
    /// <typeparam name="T">The type to get base type for.</typeparam>
    /// <returns>The base type string.</returns>
    public string GetBaseType<T>()
    {
        return _serializer.GetBaseType<T>();
    }

    /// <summary>
    /// Gets the size in bytes for a CacheEntry.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="entry">The cache entry.</param>
    /// <returns>The size in bytes.</returns>
    public int GetSizeInBytes<T>(CacheEntry<T> entry)
    {
        return entry.SizeInBytes;
    }

    /// <summary>
    /// Deserializes data using the configured serializer.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="data">The serialized data to deserialize.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails.</exception>
    public T Deserialize<T>(byte[] data) where T : notnull
    {
        return _serializer.Deserialize<T>(data) ?? throw new InvalidOperationException($"Failed to deserialize value of type {typeof(T).Name}");
    }
}