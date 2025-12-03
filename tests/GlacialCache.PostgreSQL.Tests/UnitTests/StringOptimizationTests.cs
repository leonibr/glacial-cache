using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Abstractions;
using FluentAssertions;
using Xunit;
using GlacialCache.PostgreSQL.Serializers;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

/// <summary>
/// Tests for string serialization optimization to ensure it works correctly.
/// </summary>
public class StringOptimizationTests
{
    [Fact]
    public void OptimizedSerializer_String_ShouldRoundTrip()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var testString = "Hello, World! This is a test string for optimization.";

        // Act
        var serialized = serializer.Serialize(testString);
        var deserialized = serializer.Deserialize<string>(serialized);

        // Assert
        deserialized.Should().Be(testString);
    }

    [Fact]
    public void OptimizedSerializer_ByteArray_ShouldPassThrough()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var testBytes = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var serialized = serializer.Serialize(testBytes);
        var deserialized = serializer.Deserialize<byte[]>(serialized);

        // Assert
        serialized.Should().BeSameAs(testBytes); // Pass-through
        deserialized.Should().BeSameAs(testBytes); // Pass-through
    }

    [Fact]
    public void OptimizedSerializer_ComplexObject_ShouldUseMemoryPack()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var testObject = new TestData { Id = 42, Name = "Test", IsActive = true };

        // Act
        var serialized = serializer.Serialize(testObject);
        var deserialized = serializer.Deserialize<TestData>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Id.Should().Be(42);
        deserialized.Name.Should().Be("Test");
        deserialized.IsActive.Should().Be(true);
    }

    [Fact]
    public void JsonSerializer_String_ShouldRoundTrip()
    {
        // Arrange
        var serializer = new JsonCacheEntrySerializer();
        var testString = "Hello, JSON World!";

        // Act
        var serialized = serializer.Serialize(testString);
        var deserialized = serializer.Deserialize<string>(serialized);

        // Assert
        deserialized.Should().Be(testString);
    }

    [Fact]
    public void JsonSerializer_ComplexObject_ShouldUseJson()
    {
        // Arrange
        var serializer = new JsonCacheEntrySerializer();
        var testObject = new TestData { Id = 42, Name = "Test", IsActive = true };

        // Act
        var serialized = serializer.Serialize(testObject);
        var deserialized = serializer.Deserialize<TestData>(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Id.Should().Be(42);
        deserialized.Name.Should().Be("Test");
        deserialized.IsActive.Should().Be(true);
    }

    [Fact]
    public void OptimizedSerializer_EmptyString_ShouldRoundTrip()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var testString = "";

        // Act
        var serialized = serializer.Serialize(testString);
        var deserialized = serializer.Deserialize<string>(serialized);

        // Assert
        deserialized.Should().Be(testString);
    }

    [Fact]
    public void OptimizedSerializer_UnicodeString_ShouldRoundTrip()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var testString = "Hello 世界! 🌍 Test αβγ δεζ ñáéíóú";

        // Act
        var serialized = serializer.Serialize(testString);
        var deserialized = serializer.Deserialize<string>(serialized);

        // Assert
        deserialized.Should().Be(testString);
    }
}

[MemoryPack.MemoryPackable]
public partial class TestData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
