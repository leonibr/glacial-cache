using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Serializers;

namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// Test helper for creating CacheEntry instances using the factory pattern.
/// </summary>
public static class CacheEntryTestHelper
{
    private static readonly GlacialCacheEntryFactory _memoryPackFactory = new(new MemoryPackCacheEntrySerializer());

    private static readonly GlacialCacheEntryFactory _jsonFactory = new(new JsonCacheEntrySerializer());
    /// <summary>
    /// Creates a CacheEntry using the MemoryPack factory (default for backward compatibility).
    /// </summary>
    public static CacheEntry<T> Create<T>(
        string key,
        T value,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null)
    {
        return _memoryPackFactory.Create(key, value, absoluteExpiration, slidingExpiration);
    }

    /// <summary>
    /// Creates a CacheEntry using the specified serializer type.
    /// </summary>
    public static CacheEntry<T> Create<T>(
        string key,
        T value,
        SerializerType serializerType,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null)
    {
        var factory = GetFactory(serializerType);
        return factory.Create(key, value, absoluteExpiration, slidingExpiration);
    }

    /// <summary>
    /// Creates a CacheEntry from serialized data using the MemoryPack factory (default for backward compatibility).
    /// </summary>
    public static CacheEntry<T> FromSerializedData<T>(
        string key,
        byte[] serializedValue,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        string? baseType = null)
    {
        return _memoryPackFactory.FromSerializedData<T>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);
    }

    /// <summary>
    /// Creates a CacheEntry from serialized data using the specified serializer type.
    /// </summary>
    public static CacheEntry<T> FromSerializedData<T>(
        string key,
        byte[] serializedValue,
        SerializerType serializerType,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        string? baseType = null)
    {
        var factory = GetFactory(serializerType);
        return factory.FromSerializedData<T>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);
    }

    /// <summary>
    /// Creates a CacheEntry from serialized data using the factory (string overload for convenience).
    /// </summary>
    public static CacheEntry<string> FromSerializedData(
        string key,
        byte[] serializedValue,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        string? baseType = null)
    {
        return _memoryPackFactory.FromSerializedData<string>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);
    }

    /// <summary>
    /// Creates a CacheEntry from serialized data using the specified serializer type (string overload for convenience).
    /// </summary>
    public static CacheEntry<string> FromSerializedData(
        string key,
        byte[] serializedValue,
        SerializerType serializerType,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        string? baseType = null)
    {
        var factory = GetFactory(serializerType);
        return factory.FromSerializedData<string>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);
    }

    /// <summary>
    /// Creates an unserialized CacheEntry (for backward compatibility with tests).
    /// This is equivalent to Create but with a different name for test clarity.
    /// </summary>
    public static CacheEntry<T> CreateUnserialized<T>(
        string key,
        T value,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null)
    {
        return _memoryPackFactory.Create(key, value, absoluteExpiration, slidingExpiration);
    }

    /// <summary>
    /// Gets the factory for the specified serializer type.
    /// </summary>
    private static GlacialCacheEntryFactory GetFactory(SerializerType serializerType)
    {
        return serializerType switch
        {
            SerializerType.MemoryPack => _memoryPackFactory,
            SerializerType.JsonBytes => _jsonFactory,
            _ => throw new ArgumentException($"Unsupported serializer type: {serializerType}")
        };
    }
}

