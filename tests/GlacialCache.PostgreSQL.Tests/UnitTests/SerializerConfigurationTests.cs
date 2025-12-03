
namespace GlacialCache.PostgreSQL.Tests.UnitTests;
using Models;
using Tests.Shared;
using GlacialCache.PostgreSQL.Configuration;
using MemoryPack;
using Serializers;

/// <summary>
/// Tests to verify that serializer configuration is properly respected throughout the system.
/// </summary>
public sealed class SerializerConfigurationTests
{
    [Fact]
    public void ConfiguredJsonSerializer_ShouldUseJsonForComplexObjects()
    {
        var complexObject = new ComplexTestObject
        {
            Id = 42,
            Name = "Test Object",
            Tags = new[] { "tag1", "tag2", "tag3" },
            Metadata = new Dictionary<string, string>
            {
                ["key1"] = "value1",
                ["key2"] = "123"
            }
        };

        var entry = CacheEntryTestHelper.Create("complex", complexObject, SerializerType.JsonBytes);
        var serialized = entry.SerializedData.ToArray();

        // Verify that JSON serialization was used (should contain JSON markers)
        var jsonString = System.Text.Encoding.UTF8.GetString(serialized);
        jsonString.Should().Contain("\"Id\":42");
        jsonString.Should().Contain("\"Name\":\"Test Object\"");
        jsonString.Should().Contain("\"Tags\":[\"tag1\",\"tag2\",\"tag3\"]");

        // Verify round-trip deserialization works
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<ComplexTestObject>("complex", serialized, SerializerType.JsonBytes);
        deserializedEntry.Value.Should().BeEquivalentTo(complexObject);
    }

    [Fact]
    public void ConfiguredMemoryPackSerializer_ShouldUseMemoryPackForComplexObjects()
    {
        var complexObject = new ComplexTestObject
        {
            Id = 42,
            Name = "Test Object",
            Tags = new[] { "tag1", "tag2", "tag3" },
            Metadata = new Dictionary<string, string>
            {
                ["key1"] = "value1",
                ["key2"] = "123"
            }
        };

        var entry = CacheEntryTestHelper.Create("complex", complexObject, SerializerType.MemoryPack);
        var serialized = entry.SerializedData.ToArray();

        // Verify that MemoryPack serialization was used (should be binary, not JSON)
        var jsonString = System.Text.Encoding.UTF8.GetString(serialized);
        jsonString.Should().NotContain("\"Id\":42"); // Should not contain JSON markers
        jsonString.Should().NotContain("\"Name\":\"Test Object\"");

        // Verify round-trip deserialization works
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<ComplexTestObject>("complex", serialized, SerializerType.MemoryPack);
        deserializedEntry.Value.Should().BeEquivalentTo(complexObject);
    }

    [Fact]
    public void ConfiguredJsonSerializer_ShouldUseUtf8ForStrings()
    {
        var testString = "Hello, World! 🌍";
        var entry = CacheEntryTestHelper.Create("string", testString, SerializerType.JsonBytes);
        var serialized = entry.SerializedData.ToArray();

        // Verify that UTF-8 encoding was used for strings (string optimization)
        var deserializedString = System.Text.Encoding.UTF8.GetString(serialized);
        deserializedString.Should().Be(testString);

        // Verify round-trip deserialization works
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<string>("string", serialized, SerializerType.JsonBytes);
        deserializedEntry.Value.Should().Be(testString);
    }

    [Fact]
    public void ConfiguredMemoryPackSerializer_ShouldUseUtf8ForStrings()
    {
        var testString = "Hello, World! 🌍";
        var entry = CacheEntryTestHelper.Create("string", testString, SerializerType.MemoryPack);
        var serialized = entry.SerializedData.ToArray();

        // Verify that UTF-8 encoding was used for strings (string optimization)
        var deserializedString = System.Text.Encoding.UTF8.GetString(serialized);
        deserializedString.Should().Be(testString);

        // Verify round-trip deserialization works
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<string>("string", serialized, SerializerType.MemoryPack);
        deserializedEntry.Value.Should().Be(testString);
    }

    [Fact]
    public void JsonSerializer_RoundTrip_ShouldPreserveDataIntegrity()
    {
        var testData = new TestData
        {
            Id = 123,
            Name = "Test Data",
            IsActive = true
        };

        var entry = CacheEntryTestHelper.Create("test", testData, SerializerType.JsonBytes);
        var serialized = entry.SerializedData.ToArray();

        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<TestData>("test", serialized, SerializerType.JsonBytes);
        deserializedEntry.Value.Should().BeEquivalentTo(testData);
    }

    [Fact]
    public void MemoryPackSerializer_RoundTrip_ShouldPreserveDataIntegrity()
    {
        var testData = new TestData
        {
            Id = 123,
            Name = "Test Data",
            IsActive = true
        };

        var entry = CacheEntryTestHelper.Create("test", testData, SerializerType.MemoryPack);
        var serialized = entry.SerializedData.ToArray();

        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<TestData>("test", serialized, SerializerType.MemoryPack);
        deserializedEntry.Value.Should().BeEquivalentTo(testData);
    }

    [Fact]
    public void MixedSerializerTypes_ShouldNotBreakCompatibility()
    {
        var testString = "Compatibility Test";

        // Create with JSON serializer
        var jsonEntry = CacheEntryTestHelper.Create("json", testString, SerializerType.JsonBytes);
        var jsonSerialized = jsonEntry.SerializedData.ToArray();

        // Create with MemoryPack serializer
        var memoryPackEntry = CacheEntryTestHelper.Create("memorypack", testString, SerializerType.MemoryPack);
        var memoryPackSerialized = memoryPackEntry.SerializedData.ToArray();

        // Both should produce the same result for strings (UTF-8 optimization)
        jsonSerialized.Should().BeEquivalentTo(memoryPackSerialized);

        // Both should deserialize correctly
        var jsonDeserialized = CacheEntryTestHelper.FromSerializedData<string>("json", jsonSerialized, SerializerType.JsonBytes);
        var memoryPackDeserialized = CacheEntryTestHelper.FromSerializedData<string>("memorypack", memoryPackSerialized, SerializerType.MemoryPack);

        jsonDeserialized.Value.Should().Be(testString);
        memoryPackDeserialized.Value.Should().Be(testString);
    }

    [Fact]
    public void EmptyString_WithBothSerializers_ShouldWork()
    {
        var emptyString = "";

        var jsonEntry = CacheEntryTestHelper.Create("empty", emptyString, SerializerType.JsonBytes);
        var jsonDeserialized = CacheEntryTestHelper.FromSerializedData<string>("empty", jsonEntry.SerializedData.ToArray(), SerializerType.JsonBytes);
        jsonDeserialized.Value.Should().Be(emptyString);

        var memoryPackEntry = CacheEntryTestHelper.Create("empty", emptyString, SerializerType.MemoryPack);
        var memoryPackDeserialized = CacheEntryTestHelper.FromSerializedData<string>("empty", memoryPackEntry.SerializedData.ToArray(), SerializerType.MemoryPack);
        memoryPackDeserialized.Value.Should().Be(emptyString);
    }

    [Fact]
    public void UnicodeString_WithBothSerializers_ShouldWork()
    {
        var unicodeString = "Hello 世界 🌍 测试";

        var jsonEntry = CacheEntryTestHelper.Create("unicode", unicodeString, SerializerType.JsonBytes);
        var jsonDeserialized = CacheEntryTestHelper.FromSerializedData<string>("unicode", jsonEntry.SerializedData.ToArray(), SerializerType.JsonBytes);
        jsonDeserialized.Value.Should().Be(unicodeString);

        var memoryPackEntry = CacheEntryTestHelper.Create("unicode", unicodeString, SerializerType.MemoryPack);
        var memoryPackDeserialized = CacheEntryTestHelper.FromSerializedData<string>("unicode", memoryPackEntry.SerializedData.ToArray(), SerializerType.MemoryPack);
        memoryPackDeserialized.Value.Should().Be(unicodeString);
    }

    [Fact]
    public void ByteArray_WithBothSerializers_ShouldPassThrough()
    {
        var byteArray = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0xFE };

        var jsonEntry = CacheEntryTestHelper.Create("bytes", byteArray, SerializerType.JsonBytes);
        var jsonDeserialized = CacheEntryTestHelper.FromSerializedData<byte[]>("bytes", jsonEntry.SerializedData.ToArray(), SerializerType.JsonBytes);
        jsonDeserialized.Value.Should().BeEquivalentTo(byteArray);

        var memoryPackEntry = CacheEntryTestHelper.Create("bytes", byteArray, SerializerType.MemoryPack);
        var memoryPackDeserialized = CacheEntryTestHelper.FromSerializedData<byte[]>("bytes", memoryPackEntry.SerializedData.ToArray(), SerializerType.MemoryPack);
        memoryPackDeserialized.Value.Should().BeEquivalentTo(byteArray);
    }

    [Fact]
    public void SerializerConfiguration_ShouldBeRespectedInFactory()
    {
        var testObject = new TestData { Id = 42, Name = "Test", IsActive = true };

        // Test JSON serializer
        var jsonFactory = new GlacialCache.PostgreSQL.Services.GlacialCacheEntryFactory(new JsonCacheEntrySerializer());
        var jsonEntry = jsonFactory.Create("test", testObject);
        var jsonSerialized = jsonEntry.SerializedData.ToArray();

        // Test MemoryPack serializer
        var memoryPackFactory = new GlacialCache.PostgreSQL.Services.GlacialCacheEntryFactory(new MemoryPackCacheEntrySerializer());
        var memoryPackEntry = memoryPackFactory.Create("test", testObject);
        var memoryPackSerialized = memoryPackEntry.SerializedData.ToArray();

        // Verify different serialization results
        jsonSerialized.Should().NotBeEquivalentTo(memoryPackSerialized);

        // Verify both can deserialize correctly
        var jsonDeserialized = jsonFactory.Deserialize<TestData>(jsonSerialized);
        var memoryPackDeserialized = memoryPackFactory.Deserialize<TestData>(memoryPackSerialized);

        jsonDeserialized.Should().NotBeNull();
        memoryPackDeserialized.Should().NotBeNull();
        jsonDeserialized.Should().BeEquivalentTo(testObject);
        memoryPackDeserialized.Should().BeEquivalentTo(testObject);
    }
}

// Test helper classes are already defined in other test files
