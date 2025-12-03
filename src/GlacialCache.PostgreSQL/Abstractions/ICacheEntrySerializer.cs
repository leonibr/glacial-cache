namespace GlacialCache.PostgreSQL.Abstractions;

/// <summary>
/// Interface for serializing and deserializing cache entry values.
/// </summary>
public interface ICacheEntrySerializer
{
    /// <summary>
    /// Serializes a value to byte array.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The serialized byte array.</returns>
    byte[] Serialize<T>(T value) where T : notnull;

    /// <summary>
    /// Deserializes a byte array to a value of type T.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="data">The byte array to deserialize.</param>
    /// <returns>The deserialized value.</returns>
    T Deserialize<T>(byte[] data) where T : notnull;

    /// <summary>
    /// Determines if the type T is byte[].
    /// </summary>
    /// <typeparam name="T">The type to check.</typeparam>
    /// <returns>True if T is byte[], false otherwise.</returns>
    bool IsByteArray<T>();

    /// <summary>
    /// Gets the base type string for type T.
    /// </summary>
    /// <typeparam name="T">The type to get base type for.</typeparam>
    /// <returns>The base type string.</returns>
    string GetBaseType<T>();
}
