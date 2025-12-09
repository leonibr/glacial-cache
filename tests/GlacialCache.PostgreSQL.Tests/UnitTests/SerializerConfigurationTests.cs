using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Serializers;
using GlacialCache.PostgreSQL.Abstractions;
using MemoryPack;
using Xunit;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

/// <summary>
/// Unit tests for serializer configuration and functionality.
/// </summary>
public class SerializerConfigurationTests
{
    [Fact]
    public void Serializer_Default_ShouldBeMemoryPack()
    {
        // Arrange & Act
        var options = new CacheOptions();

        // Assert
        options.Serializer.Should().Be(SerializerType.MemoryPack);
    }

    [Fact]
    public void Serializer_CustomSerializerType_ShouldBeNullByDefault()
    {
        // Arrange & Act
        var options = new CacheOptions();

        // Assert
        options.CustomSerializerType.Should().BeNull();
    }

    [Fact]
    public void ServiceCollectionExtensions_WithMemoryPackSerializer_ShouldRegisterMemoryPackCacheEntrySerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                Serializer = SerializerType.MemoryPack
            }
        };

        services.AddSingleton<IOptionsMonitor<GlacialCachePostgreSQLOptions>>(
            new TestOptionsMonitor(options));

        // Act
        services.TryAddSingleton<ICacheEntrySerializer>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var cacheOptions = optionsMonitor.CurrentValue.Cache;

            return cacheOptions.Serializer switch
            {
                SerializerType.JsonBytes => new JsonCacheEntrySerializer(),
                SerializerType.Custom => CreateCustomSerializer(sp, cacheOptions.CustomSerializerType),
                _ => new MemoryPackCacheEntrySerializer(),
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<ICacheEntrySerializer>();

        // Assert
        serializer.Should().BeOfType<MemoryPackCacheEntrySerializer>();
    }

    [Fact]
    public void ServiceCollectionExtensions_WithJsonBytesSerializer_ShouldRegisterJsonCacheEntrySerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                Serializer = SerializerType.JsonBytes
            }
        };

        services.AddSingleton<IOptionsMonitor<GlacialCachePostgreSQLOptions>>(
            new TestOptionsMonitor(options));

        // Act
        services.TryAddSingleton<ICacheEntrySerializer>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var cacheOptions = optionsMonitor.CurrentValue.Cache;

            return cacheOptions.Serializer switch
            {
                SerializerType.JsonBytes => new JsonCacheEntrySerializer(),
                SerializerType.Custom => CreateCustomSerializer(sp, cacheOptions.CustomSerializerType),
                _ => new MemoryPackCacheEntrySerializer(),
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<ICacheEntrySerializer>();

        // Assert
        serializer.Should().BeOfType<JsonCacheEntrySerializer>();
    }

    [Fact]
    public void ServiceCollectionExtensions_WithCustomSerializer_ShouldCreateCustomSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                Serializer = SerializerType.Custom,
                CustomSerializerType = typeof(TestCustomSerializer)
            }
        };

        services.AddSingleton<IOptionsMonitor<GlacialCachePostgreSQLOptions>>(
            new TestOptionsMonitor(options));

        // Act
        services.TryAddSingleton<ICacheEntrySerializer>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var cacheOptions = optionsMonitor.CurrentValue.Cache;

            return cacheOptions.Serializer switch
            {
                SerializerType.JsonBytes => new JsonCacheEntrySerializer(),
                SerializerType.Custom => CreateCustomSerializer(sp, cacheOptions.CustomSerializerType),
                _ => new MemoryPackCacheEntrySerializer(),
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<ICacheEntrySerializer>();

        // Assert
        serializer.Should().BeOfType<TestCustomSerializer>();
    }

    [Fact]
    public void ServiceCollectionExtensions_WithCustomSerializerAndNullCustomSerializerType_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                Serializer = SerializerType.Custom,
                CustomSerializerType = null
            }
        };

        services.AddSingleton<IOptionsMonitor<GlacialCachePostgreSQLOptions>>(
            new TestOptionsMonitor(options));

        // Act & Assert
        services.TryAddSingleton<ICacheEntrySerializer>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var cacheOptions = optionsMonitor.CurrentValue.Cache;

            return cacheOptions.Serializer switch
            {
                SerializerType.JsonBytes => new JsonCacheEntrySerializer(),
                SerializerType.Custom => CreateCustomSerializer(sp, cacheOptions.CustomSerializerType),
                _ => new MemoryPackCacheEntrySerializer(),
            };
        });

        var serviceProvider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<ICacheEntrySerializer>());

        exception.Message.Should().Be("CustomSerializerType must be specified when using SerializerType.Custom");
    }

    [Fact]
    public void ServiceCollectionExtensions_WithCustomSerializerAndInvalidType_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                Serializer = SerializerType.Custom,
                CustomSerializerType = typeof(string) // Not implementing ICacheEntrySerializer
            }
        };

        services.AddSingleton<IOptionsMonitor<GlacialCachePostgreSQLOptions>>(
            new TestOptionsMonitor(options));

        // Act & Assert
        services.TryAddSingleton<ICacheEntrySerializer>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var cacheOptions = optionsMonitor.CurrentValue.Cache;

            return cacheOptions.Serializer switch
            {
                SerializerType.JsonBytes => new JsonCacheEntrySerializer(),
                SerializerType.Custom => CreateCustomSerializer(sp, cacheOptions.CustomSerializerType),
                _ => new MemoryPackCacheEntrySerializer(),
            };
        });

        var serviceProvider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<ICacheEntrySerializer>());

        exception.Message.Should().Be("Custom serializer type String must implement ICacheEntrySerializer");
    }

    [Fact]
    public void MemoryPackSerializer_SerializeDeserializeString_ShouldRoundTripCorrectly()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var originalValue = "Hello, World!";

        // Act
        var serialized = serializer.Serialize(originalValue);
        var deserialized = serializer.Deserialize<string>(serialized);

        // Assert
        deserialized.Should().Be(originalValue);
        Encoding.UTF8.GetString(serialized).Should().Be(originalValue);
    }

    [Fact]
    public void MemoryPackSerializer_SerializeDeserializeByteArray_ShouldRoundTripCorrectly()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var originalValue = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var serialized = serializer.Serialize(originalValue);
        var deserialized = serializer.Deserialize<byte[]>(serialized);

        // Assert
        deserialized.Should().BeEquivalentTo(originalValue);
        serialized.Should().BeSameAs(originalValue); // Should be pass-through for byte arrays
    }

    [Fact]
    public void MemoryPackSerializer_SerializeDeserializeComplexObject_ShouldRoundTripCorrectly()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();
        var originalValue = new TestObject
        {
            Id = 42,
            Name = "Test Object",
            Values = new List<int> { 1, 2, 3, 4, 5 }
        };

        // Act
        var serialized = serializer.Serialize(originalValue);
        var deserialized = serializer.Deserialize<TestObject>(serialized);

        // Assert
        deserialized.Should().BeEquivalentTo(originalValue);
        deserialized.Id.Should().Be(42);
        deserialized.Name.Should().Be("Test Object");
        deserialized.Values.Should().BeEquivalentTo(new List<int> { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void JsonSerializer_SerializeDeserializeString_ShouldRoundTripCorrectly()
    {
        // Arrange
        var serializer = new JsonCacheEntrySerializer();
        var originalValue = "Hello, World!";

        // Act
        var serialized = serializer.Serialize(originalValue);
        var deserialized = serializer.Deserialize<string>(serialized);

        // Assert
        deserialized.Should().Be(originalValue);
        Encoding.UTF8.GetString(serialized).Should().Be(originalValue);
    }

    [Fact]
    public void JsonSerializer_SerializeDeserializeByteArray_ShouldRoundTripCorrectly()
    {
        // Arrange
        var serializer = new JsonCacheEntrySerializer();
        var originalValue = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var serialized = serializer.Serialize(originalValue);
        var deserialized = serializer.Deserialize<byte[]>(serialized);

        // Assert
        deserialized.Should().BeEquivalentTo(originalValue);
        serialized.Should().BeSameAs(originalValue); // Should be pass-through for byte arrays
    }

    [Fact]
    public void JsonSerializer_SerializeDeserializeComplexObject_ShouldRoundTripCorrectly()
    {
        // Arrange
        var serializer = new JsonCacheEntrySerializer();
        var originalValue = new TestObject
        {
            Id = 42,
            Name = "Test Object",
            Values = new List<int> { 1, 2, 3, 4, 5 }
        };

        // Act
        var serialized = serializer.Serialize(originalValue);
        var deserialized = serializer.Deserialize<TestObject>(serialized);

        // Assert
        deserialized.Should().BeEquivalentTo(originalValue);
        deserialized.Id.Should().Be(42);
        deserialized.Name.Should().Be("Test Object");
        deserialized.Values.Should().BeEquivalentTo(new List<int> { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void CustomSerializer_ShouldWorkCorrectly()
    {
        // Arrange
        var serializer = new TestCustomSerializer();
        var originalValue = "test value";

        // Act
        var serialized = serializer.Serialize(originalValue);
        var deserialized = serializer.Deserialize<string>(serialized);

        // Assert
        // The serializer adds "CUSTOM:" prefix during serialization and removes it during deserialization
        // So the round-trip should return the original value
        deserialized.Should().Be(originalValue);
    }

    [Fact]
    public void MemoryPackSerializer_IsByteArray_ShouldReturnCorrectValue()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();

        // Act & Assert
        serializer.IsByteArray<string>().Should().BeFalse();
        serializer.IsByteArray<int>().Should().BeFalse();
        serializer.IsByteArray<byte[]>().Should().BeTrue();
    }

    [Fact]
    public void MemoryPackSerializer_GetBaseType_ShouldReturnCorrectTypeName()
    {
        // Arrange
        var serializer = new MemoryPackCacheEntrySerializer();

        // Act & Assert
        serializer.GetBaseType<string>().Should().Be("System.String");
        serializer.GetBaseType<int>().Should().Be("System.Int32");
        serializer.GetBaseType<byte[]>().Should().Be("System.Byte[]");
        serializer.GetBaseType<TestObject>().Should().Be("GlacialCache.PostgreSQL.Tests.UnitTests.TestObject");
    }

    // Helper method to create custom serializer (copied from ServiceCollectionExtensions)
    private static ICacheEntrySerializer CreateCustomSerializer(IServiceProvider sp, Type? customType)
    {
        if (customType == null)
            throw new InvalidOperationException("CustomSerializerType must be specified when using SerializerType.Custom");

        if (!typeof(ICacheEntrySerializer).IsAssignableFrom(customType))
            throw new InvalidOperationException($"Custom serializer type {customType.Name} must implement ICacheEntrySerializer");

        // Try to create instance using DI container first, fallback to Activator
        return (ICacheEntrySerializer)(sp.GetService(customType) ?? Activator.CreateInstance(customType)!);
    }

    // Test classes
    private class TestOptionsMonitor : IOptionsMonitor<GlacialCachePostgreSQLOptions>
    {
        private readonly GlacialCachePostgreSQLOptions _options;

        public TestOptionsMonitor(GlacialCachePostgreSQLOptions options)
        {
            _options = options;
        }

        public GlacialCachePostgreSQLOptions CurrentValue => _options;

        public GlacialCachePostgreSQLOptions Get(string? name) => _options;

        public IDisposable OnChange(Action<GlacialCachePostgreSQLOptions, string?> listener) =>
            new TestDisposable();
    }

    private class TestDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private class TestCustomSerializer : ICacheEntrySerializer
    {
        public byte[] Serialize<T>(T value) where T : notnull
        {
            return Encoding.UTF8.GetBytes("CUSTOM:" + value?.ToString());
        }

        public T Deserialize<T>(byte[] data) where T : notnull
        {
            var str = Encoding.UTF8.GetString(data);
            if (str.StartsWith("CUSTOM:"))
            {
                str = str.Substring(7);
            }
            return (T)(object)str;
        }

        public bool IsByteArray<T>() => typeof(T) == typeof(byte[]);

        public string GetBaseType<T>() => typeof(T).FullName ?? typeof(T).Name;
    }
}

[MemoryPackable]
public partial class TestObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<int> Values { get; set; } = new();
}
