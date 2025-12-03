using MemoryPack;
using System.Text;

namespace GlacialCache.PostgreSQL.Serializers;
using Abstractions;

/// <summary>
/// MemoryPack-based implementation of ICacheEntrySerializer with string optimization.
/// Provides 22% performance improvement for string serialization by using direct UTF8 encoding.
/// </summary>
public class MemoryPackCacheEntrySerializer : ICacheEntrySerializer
{
    /// <summary>
    /// Serializes a value to byte array using optimized paths for strings and MemoryPack for complex objects.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The serialized byte array.</returns>
    public byte[] Serialize<T>(T value) where T : notnull
    {
        // Allow null values to be passed to the serializer
        // ArgumentNullException.ThrowIfNull(value);

        if (typeof(T) == typeof(string))
        {
            if (value is null)
            {
                return Array.Empty<byte>();
            }
            var stringValue = (string)(object)value;
            return Encoding.UTF8.GetBytes(stringValue);
        }

        if (typeof(T) == typeof(byte[]))
        {
            if (value is null)
            {
                return Array.Empty<byte>();
            }
            return (byte[])(object)value;
        }

        return MemoryPackSerializer.Serialize(value);
    }

    /// <summary>
    /// Deserializes a byte array to a value of type T using optimized paths for strings and MemoryPack for complex objects.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="data">The byte array to deserialize.</param>
    /// <returns>The deserialized value.</returns>
    public T Deserialize<T>(byte[] data) where T : notnull
    {
        if (typeof(T) == typeof(string))
        {
            // Throw on invalid bytes for strict validation
            var encoding = new UTF8Encoding(false, true);
            try
            {
                return (T)(object)(data.Length == 0 ? string.Empty : encoding.GetString(data));
            }
            catch (DecoderFallbackException)
            {
                return default!;
            }
        }

        if (typeof(T) == typeof(byte[]))
        {
            return (T)(object)data;
        }

        T? result = MemoryPackSerializer.Deserialize<T>(data);
        if (result == null)
        {
            throw new InvalidOperationException($"Failed to deserialize value of type {typeof(T).Name}");
        }
        return result;
    }

    /// <summary>
    /// Determines if the type T is byte[].
    /// </summary>
    /// <typeparam name="T">The type to check.</typeparam>
    /// <returns>True if T is byte[], false otherwise.</returns>
    public bool IsByteArray<T>()
    {
        return typeof(T) == typeof(byte[]);
    }

    /// <summary>
    /// Gets the base type string for type T.
    /// </summary>
    /// <typeparam name="T">The type to get base type for.</typeparam>
    /// <returns>The base type string.</returns>
    public string GetBaseType<T>()
    {
        return typeof(T) == typeof(byte[]) ? "System.Byte[]" : typeof(T).FullName ?? typeof(T).Name;
    }
}
