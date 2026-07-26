using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.Abstractions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Serializers;

namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// Test helper for creating CacheEntry instances using the helper pattern.
/// </summary>
public static class CacheEntryTestHelper
{
    private static readonly CacheEntryHelper _memoryPackHelper = new(new MemoryPackCacheEntrySerializer());

    private static readonly CacheEntryHelper _jsonHelper = new(new JsonCacheEntrySerializer());
    /// <summary>
    /// Creates a CacheEntry using the MemoryPack helper (default for backward compatibility).
    /// </summary>
    public static CacheEntry<T> Create<T>(
        string key,
        T value,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null)
    {
        return _memoryPackHelper.Create(key, value, absoluteExpiration, slidingExpiration);
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
        var helper = GetHelper(serializerType);
        return helper.Create(key, value, absoluteExpiration, slidingExpiration);
    }

    /// <summary>
    /// Creates a CacheEntry from serialized data using the MemoryPack helper (default for backward compatibility).
    /// </summary>
    public static CacheEntry<T> FromSerializedData<T>(
        string key,
        byte[] serializedValue,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        string? baseType = null)
    {
        return _memoryPackHelper.FromSerializedData<T>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);
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
        var helper = GetHelper(serializerType);
        return helper.FromSerializedData<T>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);
    }

    /// <summary>
    /// Creates a CacheEntry from serialized data using the helper (string overload for convenience).
    /// </summary>
    public static CacheEntry<string> FromSerializedData(
        string key,
        byte[] serializedValue,
        DateTimeOffset? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        string? baseType = null)
    {
        return _memoryPackHelper.FromSerializedData<string>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);
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
        var helper = GetHelper(serializerType);
        return helper.FromSerializedData<string>(key, serializedValue, absoluteExpiration, slidingExpiration, baseType);
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
        return _memoryPackHelper.Create(key, value, absoluteExpiration, slidingExpiration);
    }

    /// <summary>
    /// Gets the helper for the specified serializer type.
    /// </summary>
    private static CacheEntryHelper GetHelper(SerializerType serializerType)
    {
        return serializerType switch
        {
            SerializerType.MemoryPack => _memoryPackHelper,
            SerializerType.JsonBytes => _jsonHelper,
            _ => throw new ArgumentException($"Unsupported serializer type: {serializerType}")
        };
    }
}
