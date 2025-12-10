using MemoryPack;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Serializers;
using Xunit.Abstractions;
using System.Diagnostics;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Integration tests for serializer configuration options.
/// Tests verify that Serializer (MemoryPack, JsonBytes, Custom) works correctly
/// in real database scenarios with complex data types and performance verification.
/// </summary>
public class SerializerIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private IServiceProvider? _serviceProvider;
    private CleanupBackgroundService? _cleanupService;

    public SerializerIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    protected override async Task InitializeTestAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("testdb")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .WithCleanUp(true)
                .Build();

            await _postgres.StartWithRetryAsync(Output);
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}", ex);
        }
    }

    protected override async Task CleanupTestAsync()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            try
            {
                await (_cleanupService?.StopAsync(default) ?? Task.CompletedTask);
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Output.WriteLine($"⚠️ Warning: Error disposing service provider: {ex.Message}");
            }
        }

        if (_postgres != null)
        {
            try
            {
                await _postgres.DisposeAsync();
                Output.WriteLine("✅ PostgreSQL container disposed");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"⚠️ Warning: Error disposing container: {ex.Message}");
                // Don't throw - cleanup failures shouldn't fail tests
            }
            finally
            {
                _postgres = null;
            }
        }
    }

    private async Task<(IGlacialCache cache, ICacheEntrySerializer serializer)> SetupCacheAsync(Action<GlacialCachePostgreSQLOptions> configureOptions)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Explicitly register TimeProvider.System to ensure test isolation
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres!.GetConnectionString();
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
            options.Maintenance.EnableAutomaticCleanup = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(250);

            // Configure serializer
            configureOptions(options);
        });

        _serviceProvider = services.BuildServiceProvider();
        var cache = _serviceProvider.GetRequiredService<IGlacialCache>();
        var serializer = _serviceProvider.GetRequiredService<ICacheEntrySerializer>();
        _cleanupService = _serviceProvider.GetRequiredService<CleanupBackgroundService>();
        await _cleanupService.StartAsync(default);

        return (cache, serializer);
    }

    [Fact]
    public async Task Serializer_MemoryPack_ShouldSerializeComplexObjectsCorrectly()
    {
        // Arrange
        var (cache, serializer) = await SetupCacheAsync(options =>
        {
            options.Cache.Serializer = SerializerType.MemoryPack;
        });

        var testObject = CreateComplexTestObject("memorypack-test");

        // Act
        await cache.SetEntryAsync("complex-object-memorypack", testObject, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        var retrievedEntry = await cache.GetEntryAsync<TestDataObject>("complex-object-memorypack");
        var retrievedObject = retrievedEntry?.Value;

        // Assert
        serializer.ShouldBeOfType<MemoryPackCacheEntrySerializer>();
        retrievedObject.ShouldBeEquivalentTo(testObject);
        retrievedObject.Id.ShouldBe(testObject.Id);
        retrievedObject.Name.ShouldBe(testObject.Name);
        retrievedObject.Items.ShouldBeEquivalentTo(testObject.Items);
    }

    [Fact]
    public async Task Serializer_JsonBytes_ShouldSerializeComplexObjectsCorrectly()
    {
        // Arrange
        var (cache, serializer) = await SetupCacheAsync(options =>
        {
            options.Cache.Serializer = SerializerType.JsonBytes;
        });

        var testObject = CreateComplexTestObject("json-test");

        // Act
        await cache.SetEntryAsync("complex-object-json", testObject, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        var retrievedEntry = await cache.GetEntryAsync<TestDataObject>("complex-object-json");
        var retrievedObject = retrievedEntry?.Value;

        // Assert
        serializer.ShouldBeOfType<JsonCacheEntrySerializer>();
        retrievedObject.ShouldBeEquivalentTo(testObject);
        retrievedObject.Id.ShouldBe(testObject.Id);
        retrievedObject.Name.ShouldBe(testObject.Name);
        retrievedObject.Items.ShouldBeEquivalentTo(testObject.Items);
    }

    [Fact]
    public async Task Serializer_Custom_ShouldUseCustomSerializerImplementation()
    {
        // Arrange
        var (cache, serializer) = await SetupCacheAsync(options =>
        {
            options.Cache.Serializer = SerializerType.Custom;
            options.Cache.CustomSerializerType = typeof(TestCustomSerializer);
        });

        var testObject = CreateComplexTestObject("custom-test");

        // Act
        await cache.SetEntryAsync("complex-object-custom", testObject, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        var retrievedEntry = await cache.GetEntryAsync<TestDataObject>("complex-object-custom");
        var retrievedObject = retrievedEntry?.Value;
        retrievedObject.ShouldNotBeNull("Retrieved object should not be null");

        // Assert
        serializer.ShouldBeOfType<TestCustomSerializer>();
        retrievedObject.ShouldBeEquivalentTo(testObject);
        // Custom serializer adds a prefix during serialization but removes it during deserialization
        retrievedObject.Name.ShouldBe(testObject.Name);
    }

    [Fact]
    public async Task Serializer_AllTypes_ShouldHandleStringsAndByteArraysOptimally()
    {
        // Test all serializer types with string and byte array optimizations
        var testCases = new[]
        {
            new { Type = SerializerType.MemoryPack, ExpectedSerializerType = typeof(MemoryPackCacheEntrySerializer) },
            new { Type = SerializerType.JsonBytes, ExpectedSerializerType = typeof(JsonCacheEntrySerializer) },
            new { Type = SerializerType.Custom, ExpectedSerializerType = typeof(TestCustomSerializer) }
        };

        foreach (var testCase in testCases)
        {
            // Arrange
            var (cache, serializer) = await SetupCacheAsync(options =>
            {
                options.Cache.Serializer = testCase.Type;
                if (testCase.Type == SerializerType.Custom)
                    options.Cache.CustomSerializerType = typeof(TestCustomSerializer);
            });

            var testString = "Hello, World! Test string";
            var testBytes = new byte[] { 1, 2, 3, 4, 5, 255, 0, 128 };

            // Act & Assert - String operations
            await cache.SetStringAsync($"string-test-{testCase.Type}", testString, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });

            var retrievedString = await cache.GetStringAsync($"string-test-{testCase.Type}");
            retrievedString.ShouldBe(testString);

            // Act & Assert - Byte array operations
            await cache.SetAsync($"bytes-test-{testCase.Type}", testBytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });

            var retrievedEntry = await cache.GetEntryAsync<byte[]>($"bytes-test-{testCase.Type}");
            var retrievedBytes = retrievedEntry?.Value;
            retrievedBytes.ShouldBeEquivalentTo(testBytes);

            // Verify serializer type
            serializer.ShouldBeOfType(testCase.ExpectedSerializerType);
        }
    }

    [Fact]
    public async Task Serializer_PerformanceComparison_ShouldShowRelativePerformance()
    {
        // Arrange - Test data that will show performance differences
        var testObject = CreateLargeTestObject();
        var iterations = 10;

        var results = new Dictionary<string, TimeSpan>();

        foreach (var serializerType in new[] { SerializerType.MemoryPack, SerializerType.JsonBytes })
        {
            var (cache, _) = await SetupCacheAsync(options =>
            {
                options.Cache.Serializer = serializerType;
            });

            var stopwatch = Stopwatch.StartNew();

            // Perform multiple set/get operations
            for (int i = 0; i < iterations; i++)
            {
                var key = $"perf-test-{serializerType}-{i}";
                await cache.SetEntryAsync(key, testObject, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                });

                var retrievedEntry = await cache.GetEntryAsync<TestDataObject>(key);
                retrievedEntry.ShouldNotBeNull();
            }

            stopwatch.Stop();
            results[serializerType.ToString()] = stopwatch.Elapsed.TotalMilliseconds > 0 ? stopwatch.Elapsed : TimeSpan.FromTicks(1);
        }

        // Assert - MemoryPack should generally be faster than JSON
        // (Allow some tolerance for test environment variations)
        results[SerializerType.MemoryPack.ToString()].ShouldBeLessThan(
            results[SerializerType.JsonBytes.ToString()] * 1.5); // Allow 50% tolerance
    }

    [Fact]
    public async Task Serializer_DataIntegrity_ShouldPreserveComplexNestedStructures()
    {
        // Arrange
        var (cache, _) = await SetupCacheAsync(options =>
        {
            options.Cache.Serializer = SerializerType.MemoryPack; // Use fastest for integrity test
        });

        var complexObject = new TestDataObject
        {
            Id = Guid.NewGuid(),
            Name = "Complex Integrity Test",
            Count = int.MaxValue,
            Ratio = Math.PI,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Items = new[] { "item1", "item2", "item3" }
        };

        // Act
        await cache.SetEntryAsync("integrity-test", complexObject, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        var retrievedEntry = await cache.GetEntryAsync<TestDataObject>("integrity-test");
        var retrievedObject = retrievedEntry?.Value;

        // Assert - Every field should be preserved exactly
        retrievedObject.ShouldNotBeNull();
        retrievedObject.ShouldBeEquivalentTo(complexObject);

        // Explicit checks for critical fields
        retrievedObject.Id.ShouldBe(complexObject.Id);
        retrievedObject.Count.ShouldBe(complexObject.Count);
        retrievedObject.Ratio.ShouldBe(complexObject.Ratio);
        retrievedObject.IsActive.ShouldBe(complexObject.IsActive);
        retrievedObject.CreatedAt.ShouldBe(complexObject.CreatedAt);
        retrievedObject.Items.ShouldBeEquivalentTo(complexObject.Items);
    }

    [Fact]
    public async Task Serializer_EdgeCases_ShouldHandleNullAndEmptyValues()
    {
        // Arrange
        var (cache, _) = await SetupCacheAsync(options =>
        {
            options.Cache.Serializer = SerializerType.JsonBytes; // JSON handles nulls well
        });

        var edgeCaseObject = new TestDataObject
        {
            Id = Guid.Empty,
            Name = "",
            Count = 0,
            Ratio = 0.0,
            IsActive = false,
            CreatedAt = DateTime.MinValue,
            Items = Array.Empty<string>() // Empty array
        };

        // Act
        await cache.SetEntryAsync("edge-case-test", edgeCaseObject, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var retrievedEntry = await cache.GetEntryAsync<TestDataObject>("edge-case-test");
        var retrievedObject = retrievedEntry?.Value;
        // Assert
        retrievedObject.ShouldNotBeNull("Retrieved object should not be null");
        retrievedObject.ShouldBeEquivalentTo(edgeCaseObject);
        retrievedObject.Items.ShouldBeEmpty();
    }

    private static TestDataObject CreateComplexTestObject(string name)
    {
        return new TestDataObject
        {
            Id = Guid.NewGuid(),
            Name = name,
            Count = 42,
            Ratio = 3.14,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Items = new[] { "item1", "item2", "item3" }
        };
    }

    private static TestDataObject CreateLargeTestObject()
    {
        return new TestDataObject
        {
            Id = Guid.NewGuid(),
            Name = "Large Test Object with considerable data",
            Count = 1000,
            Ratio = Math.E,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Items = Enumerable.Range(1, 500).Select(i => $"Item {i} with some additional text to make it larger").ToArray()
        };
    }


    private class TestCustomSerializer : ICacheEntrySerializer
    {
        public byte[] Serialize<T>(T value) where T : notnull
        {
            // Handle byte arrays directly
            if (typeof(T) == typeof(byte[]))
            {
                return (byte[])(object)value;
            }

            // Add a prefix to verify custom serialization is working
            var json = System.Text.Json.JsonSerializer.Serialize(value);
            return System.Text.Encoding.UTF8.GetBytes("CUSTOM:" + json);
        }

        public T Deserialize<T>(byte[] data) where T : notnull
        {
            // Handle byte arrays directly
            if (typeof(T) == typeof(byte[]))
            {
                return (T)(object)data;
            }

            var str = System.Text.Encoding.UTF8.GetString(data);
            if (str.StartsWith("CUSTOM:"))
            {
                str = str.Substring(7);
            }
            return System.Text.Json.JsonSerializer.Deserialize<T>(str)!;
        }

        public bool IsByteArray<T>() => typeof(T) == typeof(byte[]);

        public string GetBaseType<T>() => typeof(T).FullName ?? typeof(T).Name;
    }
}
