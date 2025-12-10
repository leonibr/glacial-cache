using GlacialCache.PostgreSQL.Serializers;
using Xunit;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

/// <summary>
/// Tests for serializer memory efficiency optimizations.
/// These tests verify that serializers avoid unnecessary memory copies, particularly for byte arrays.
/// </summary>
public class SerializerMemoryEfficiencyTests
{
    /// <summary>
    /// Verifies that byte arrays are passed through without copying, which is an important memory efficiency optimization.
    /// This optimization avoids unnecessary memory allocations and copies, especially critical for large byte arrays.
    /// 
    /// Note: This tests an implementation detail (reference equality) rather than correctness (value equality).
    /// The correctness contract (that byte arrays serialize/deserialize correctly) is covered by other tests.
    /// However, this optimization is important for memory efficiency in a caching library.
    /// </summary>
    [Fact]
    public void OptimizedSerializer_ByteArray_ShouldPassThrough()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var testBytes = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var serialized = serializer.Serialize(testBytes);
        var deserialized = serializer.Deserialize<byte[]>(serialized);

        // Assert - Verify correctness first
        deserialized.ShouldBeEquivalentTo(testBytes);

        // Assert - Verify memory efficiency optimization (pass-through without copying)
        // This is an implementation detail but important for memory efficiency
        serialized.ShouldBeSameAs(testBytes, "Serializer should pass-through byte arrays to avoid unnecessary copies");
        deserialized.ShouldBeSameAs(testBytes, "Deserializer should pass-through byte arrays to avoid unnecessary copies");
    }
}
