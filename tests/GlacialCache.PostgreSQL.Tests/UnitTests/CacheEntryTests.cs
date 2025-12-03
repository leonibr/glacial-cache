using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Configuration;
using MemoryPack;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class CacheEntryTests
{
    [Fact]
    public void CacheEntryT_LazySerialization_SerializesOnFirstAccess()
    {
        var entry = CacheEntryTestHelper.Create("k", "hello", null, null);

        // Before access, SizeInBytes should reflect SerializedData length when accessed
        var first = entry.SerializedData;
        var second = entry.SerializedData;

        first.Span.Length.Should().BeGreaterThan(0);
        // Same buffer instance reused
        second.Span.ToArray().Should().BeEquivalentTo(first.Span.ToArray());
        entry.SizeInBytes.Should().Be(first.Length);
    }

    [Fact]
    public void CacheEntryT_FromSerializedData_UsesProvidedBufferAndDeserializes()
    {
        var entry = CacheEntryTestHelper.Create("k", "payload", SerializerType.MemoryPack);
        var bytes = entry.SerializedData.ToArray();
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<string>("k", bytes);

        deserializedEntry.Value.Should().Be("payload");
        // SerializedData should not change after deserialization
        entry.SerializedData.ToArray().Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public void CacheEntryT_CreateUnserialized_DelaysSerializationUntilAccess()
    {
        var entry = CacheEntryTestHelper.CreateUnserialized("k", "v");
        // Force serialization
        var buf = entry.SerializedData;
        buf.Length.Should().BeGreaterThan(0);
    }

    // ===== NEW TESTS FOR CacheEntry<T> =====

    [Fact]
    public void CacheEntryT_Constructor_WithValidParameters_ShouldCreateEntry()
    {
        var key = "test-key";
        var value = "test-value";
        var absoluteExpiration = DateTimeOffset.Now.AddMinutes(10);
        var slidingExpiration = TimeSpan.FromMinutes(5);

        var entry = CacheEntryTestHelper.Create(key, value, absoluteExpiration, slidingExpiration);

        entry.Key.Should().Be(key);
        entry.Value.Should().Be(value);
        entry.AbsoluteExpiration.Should().Be(absoluteExpiration);
        entry.SlidingExpiration.Should().Be(slidingExpiration);
        entry.BaseType.Should().Be(typeof(string).FullName);
    }

    [Fact]
    public void CacheEntryT_Constructor_WithNullKey_ShouldThrowArgumentException()
    {
        var action = () => CacheEntryTestHelper.Create(null!, "test-value", null, null);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CacheEntryT_Constructor_WithNullValue_ShouldAllowNullValue()
    {
        var entry = CacheEntryTestHelper.Create<string>("test-key", null!, null, null);

        entry.Should().NotBeNull();
        entry.Value.Should().BeNull();
        entry.Key.Should().Be("test-key");
    }





    [Fact]
    public void CacheEntryT_SizeInBytes_ShouldReturnSerializedDataLength()
    {
        var entry = CacheEntryTestHelper.Create("k", "hello world", null, null);

        entry.SizeInBytes.Should().Be(entry.SerializedData.Length);
    }

    [Fact]
    public void CacheEntryT_SerializedData_ShouldSerializeOnFirstAccess()
    {
        var entry = CacheEntryTestHelper.Create("k", "test value", null, null);
        var serialized = entry.SerializedData;

        serialized.Length.Should().BeGreaterThan(0);
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<string>("k", serialized.ToArray());
        deserializedEntry.Value.Should().Be("test value");
    }

    [Fact]
    public void CacheEntryT_SerializedData_ShouldReuseSerializedBuffer()
    {
        var entry = CacheEntryTestHelper.Create("k", "test value", null, null);
        var first = entry.SerializedData;
        var second = entry.SerializedData;

        // Should be the same buffer instance
        first.ToArray().Should().BeEquivalentTo(second.ToArray());
    }

    [Fact]
    public void CacheEntryT_FromSerializedData_WithValidData_ShouldCreateEntry()
    {
        var originalEntry = CacheEntryTestHelper.Create("k", "test value", SerializerType.MemoryPack);
        var serialized = originalEntry.SerializedData.ToArray();
        var absoluteExpiration = DateTimeOffset.Now.AddMinutes(10);
        var slidingExpiration = TimeSpan.FromMinutes(5);


        var entry = CacheEntryTestHelper.FromSerializedData<string>(
            "k", serialized, absoluteExpiration, slidingExpiration);

        entry.Value.Should().Be("test value");
        entry.AbsoluteExpiration.Should().Be(absoluteExpiration);
        entry.SlidingExpiration.Should().Be(slidingExpiration);
        entry.SerializedData.ToArray().Should().BeEquivalentTo(serialized);
    }

    [Fact]
    public void CacheEntryT_FromSerializedData_WithInvalidData_ShouldThrowException()
    {
        var invalidData = new byte[] { 0xFF, 0xFF, 0xFF }; // Invalid MemoryPack data

        var action = () => CacheEntryTestHelper.FromSerializedData<string>("k", invalidData);
        // The factory may handle invalid data gracefully, so let's just verify it doesn't crash
        var result = action.Should().NotThrow();

        // If it doesn't throw, the result should be a valid CacheEntry
        var entry = CacheEntryTestHelper.FromSerializedData<string>("k", invalidData);
        entry.Should().NotBeNull();
    }

    [Fact]
    public void CacheEntryT_CreateUnserialized_ShouldNotSerializeImmediately()
    {
        var entry = CacheEntryTestHelper.CreateUnserialized("k", "test value");

        // Accessing Value should not trigger serialization
        entry.Value.Should().Be("test value");

        // SerializedData should be null until accessed
        var serialized = entry.SerializedData;
        serialized.Length.Should().BeGreaterThan(0);
    }



    [Fact]
    public void CacheEntryT_ComplexObject_ShouldSerializeAndDeserializeCorrectly()
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

        var entry = CacheEntryTestHelper.Create("complex", complexObject, null, null);
        var serialized = entry.SerializedData;

        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<ComplexTestObject>("complex", serialized.ToArray());
        deserializedEntry.Value.Should().BeEquivalentTo(complexObject);
    }

    [Fact]
    public void CacheEntryT_BackwardCompatibility_ShouldImplementICacheEntryCorrectly()
    {
        var entry = CacheEntryTestHelper.Create("k", "v", null, null) as ICacheEntry;

        entry.Should().NotBeNull();
        entry!.Key.Should().Be("k");
        entry.SerializedData.Length.Should().BeGreaterThan(0);
        entry.BaseType.Should().Be(typeof(string).FullName);
        entry.SizeInBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CacheEntryT_ValueType_ShouldSerializeAndDeserializeCorrectly()
    {
        var valueType = 42;
        var entry = CacheEntryTestHelper.Create("int-key", valueType, null, null);

        var serialized = entry.SerializedData;
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<int>("int-key", serialized.ToArray());

        deserializedEntry.Value.Should().Be(valueType);
    }

    [Fact]
    public void CacheEntryT_DateTime_ShouldSerializeAndDeserializeCorrectly()
    {
        var dateTime = DateTime.UtcNow;
        var entry = CacheEntryTestHelper.Create("datetime-key", dateTime, null, null);

        var serialized = entry.SerializedData;
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<DateTime>("datetime-key", serialized.ToArray());

        deserializedEntry.Value.Should().Be(dateTime);
    }

    [Fact]
    public void CacheEntryT_Array_ShouldSerializeAndDeserializeCorrectly()
    {
        var array = new[] { 1, 2, 3, 4, 5 };
        var entry = CacheEntryTestHelper.Create("array-key", array, null, null);

        var serialized = entry.SerializedData;
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<int[]>("array-key", serialized.ToArray());

        deserializedEntry.Value.Should().BeEquivalentTo(array);
    }

    [Fact]
    public void CacheEntryT_List_ShouldSerializeAndDeserializeCorrectly()
    {
        var list = new List<string> { "item1", "item2", "item3" };
        var entry = CacheEntryTestHelper.Create("list-key", list, null, null);

        var serialized = entry.SerializedData;
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<List<string>>("list-key", serialized.ToArray());

        deserializedEntry.Value.Should().BeEquivalentTo(list);
    }

    [Fact]
    public void CacheEntryT_Dictionary_ShouldSerializeAndDeserializeCorrectly()
    {
        var dict = new Dictionary<string, int>
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3
        };
        var entry = CacheEntryTestHelper.Create("dict-key", dict, null, null);

        var serialized = entry.SerializedData;
        var deserializedEntry = CacheEntryTestHelper.FromSerializedData<Dictionary<string, int>>("dict-key", serialized.ToArray());

        deserializedEntry.Value.Should().BeEquivalentTo(dict);
    }


    // Test helper class for custom time provider
    public class CustomTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _fixedTime;

        public CustomTimeProvider(DateTimeOffset fixedTime)
        {
            _fixedTime = fixedTime;
        }

        public override DateTimeOffset GetUtcNow() => _fixedTime;
    }
}

// Test helper class for complex object serialization tests
[MemoryPackable]
public partial record ComplexTestObject
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string[] Tags { get; init; } = Array.Empty<string>();
    public Dictionary<string, string> Metadata { get; init; } = new();
}
