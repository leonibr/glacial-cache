using GlacialCache.PostgreSQL.Serializers;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Tests.Shared;
using MemoryPack;
using Xunit;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Single integration test to verify serializer factory/wiring works correctly.
/// Detailed serializer behavior is tested in unit tests.
/// </summary>
public sealed class SerializerIntegrationTests
{
    [Fact]
    public void SerializerFactory_Wiring_ShouldWorkCorrectly()
    {
        // Test that both serializers can be instantiated and work
        var jsonFactory = new GlacialCacheEntryFactory(new JsonCacheEntrySerializer());
        var memoryPackFactory = new GlacialCacheEntryFactory(new MemoryPackCacheEntrySerializer());

        var testData = new TestData { Id = 42, Name = "Test", IsActive = true };

        // Test JSON serializer
        var jsonEntry = jsonFactory.Create("test", testData);
        var jsonSerialized = jsonEntry.SerializedData.ToArray();
        var jsonDeserialized = jsonFactory.Deserialize<TestData>(jsonSerialized);
        jsonDeserialized.Should().NotBeNull();
        jsonDeserialized.Id.Should().Be(42);

        // Test MemoryPack serializer
        var memoryPackEntry = memoryPackFactory.Create("test", testData);
        var memoryPackSerialized = memoryPackEntry.SerializedData.ToArray();
        var memoryPackDeserialized = memoryPackFactory.Deserialize<TestData>(memoryPackSerialized);
        memoryPackDeserialized.Should().NotBeNull();
        memoryPackDeserialized.Id.Should().Be(42);

        // Verify different serialization results (proving they're different serializers)
        jsonSerialized.Should().NotBeEquivalentTo(memoryPackSerialized);
    }
}

// Test helper class
[MemoryPackable]
public partial record TestData
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}
