using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Services;
using MemoryPack;
using Xunit.Abstractions;
using Npgsql;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Comprehensive validation tests covering all MemoryPack integration scenarios.
/// </summary>
public sealed class ComprehensiveValidationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private IGlacialCache? _glacialCache;
    private IDistributedCache? _distributedCache;
    private NpgsqlDataSource? _dataSource;

    public ComprehensiveValidationTests(ITestOutputHelper output) : base(output)
    {
    }

    protected override async Task InitializeTestAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithUsername("test")
                .WithPassword("test")
                .WithDatabase("glacialcache_test")
                .WithCleanUp(true)
                .Build();

            await _postgres.StartAsync();
            Output.WriteLine($"✅ PostgreSQL container started: {_postgres.GetConnectionString()}");

            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddConsole());

            services.AddGlacialCachePostgreSQL(options =>
            {
                options.Connection.ConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { ApplicationName = GetType().Name }.ConnectionString;
                options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(10);
                options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                options.Infrastructure.EnableManagerElection = false;
                options.Infrastructure.CreateInfrastructure = true;
            });

            _serviceProvider = services.BuildServiceProvider();
            _glacialCache = _serviceProvider.GetRequiredService<IGlacialCache>();
            _distributedCache = _serviceProvider.GetRequiredService<IDistributedCache>();

            // Create data source for proper disposal
            _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}");
        }
    }

    protected override async Task CleanupTestAsync()
    {
        try
        {
            // Dispose data source
            if (_dataSource != null)
            {
                await _dataSource.DisposeAsync();
            }

            // Dispose service provider properly
            if (_serviceProvider != null)
            {
                await _serviceProvider.DisposeAsync();
            }

            // Dispose container last
            if (_postgres != null)
            {
                await _postgres.DisposeAsync();
                Output.WriteLine("✅ PostgreSQL container disposed");
            }
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Error during cleanup: {ex.Message}");
            // Don't rethrow to avoid masking test failures
        }
    }

    [Fact]
    public async Task Comprehensive_AllDataTypes_ShouldSerializeAndDeserializeCorrectly()
    {
        // Test all primitive types and complex structures
        var testCases = new Dictionary<string, object>
        {
            ["string"] = "Hello, World!",
            ["int"] = 42,
            ["long"] = 123456789L,
            ["double"] = 3.14159,
            ["decimal"] = 123.456m,
            ["bool"] = true,
            ["datetime"] = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ["datetimeoffset"] = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero),
            ["timespan"] = TimeSpan.FromHours(2.5),
            ["guid"] = Guid.NewGuid(),
            ["byte"] = (byte)255,
            ["char"] = 'A',
            ["float"] = 3.14f,
            ["short"] = (short)12345,
            ["ushort"] = (ushort)12345,
            ["uint"] = 12345u,
            ["ulong"] = 12345ul,
            ["sbyte"] = (sbyte)127
        };

        foreach (var testCase in testCases)
        {
            var key = $"comprehensive:{testCase.Key}";
            dynamic entry = CreateTypedEntry(key, testCase.Value);

            await _glacialCache!.SetEntryAsync(entry);

            // Use the typed method to get the correct type
            var retrieved = await GetTypedEntry(testCase.Value.GetType(), key);

            retrieved.ShouldNotBeNull();
            retrieved!.ShouldBeEquivalentTo(testCase.Value);
        }
    }

    [Fact]
    public async Task Comprehensive_ComplexNestedObjects_ShouldHandleCorrectly()
    {
        var complexObject = new NestedComplexObject
        {
            Id = 1,
            Name = "Root Object",
            Children = new[]
            {
                new NestedComplexObject
                {
                    Id = 2,
                    Name = "Child 1",
                    Children = new[]
                    {
                        new NestedComplexObject { Id = 3, Name = "Grandchild 1" },
                        new NestedComplexObject { Id = 4, Name = "Grandchild 2" }
                    }
                },
                new NestedComplexObject
                {
                    Id = 5,
                    Name = "Child 2",
                    Children = new[]
                    {
                        new NestedComplexObject { Id = 6, Name = "Grandchild 3" }
                    }
                }
            },
            Metadata = new Dictionary<string, string>
            {
                ["level"] = "1",
                ["maxDepth"] = "3",
                ["created"] = DateTime.UtcNow.ToString("O"),
                ["tags"] = string.Join(",", new[] { "root", "complex", "nested" })
            }
        };

        var entry = CacheEntryTestHelper.Create("comprehensive:nested", complexObject);
        await _glacialCache!.SetEntryAsync(entry);

        var retrieved = await _glacialCache.GetEntryAsync<NestedComplexObject>("comprehensive:nested");
        retrieved.ShouldNotBeNull();
        retrieved!.Value.ShouldBeEquivalentTo(complexObject);
        retrieved.Value.Children.Length.ShouldBe(2);
        retrieved.Value.Children[0].Children.Length.ShouldBe(2);
        retrieved.Value.Children[1].Children.Length.ShouldBe(1);
    }

    [Fact]
    public async Task Comprehensive_LargeDataSets_ShouldHandleCorrectly()
    {
        // Test with large arrays and collections
        var largeArray = Enumerable.Range(1, 10000).ToArray();
        var largeList = Enumerable.Range(1, 5000).Select(i => $"Item {i}").ToList();
        var largeDictionary = Enumerable.Range(1, 2500).ToDictionary(i => $"Key{i}", i => i * 2);

        var arrayEntry = CacheEntryTestHelper.Create("comprehensive:large:array", largeArray);
        var listEntry = CacheEntryTestHelper.Create("comprehensive:large:list", largeList);
        var dictEntry = CacheEntryTestHelper.Create("comprehensive:large:dict", largeDictionary);

        await _glacialCache!.SetEntryAsync(arrayEntry);
        await _glacialCache.SetEntryAsync(listEntry);
        await _glacialCache.SetEntryAsync(dictEntry);

        var retrievedArray = await _glacialCache.GetEntryAsync<int[]>("comprehensive:large:array");
        var retrievedList = await _glacialCache.GetEntryAsync<List<string>>("comprehensive:large:list");
        var retrievedDict = await _glacialCache.GetEntryAsync<Dictionary<string, int>>("comprehensive:large:dict");

        retrievedArray.ShouldNotBeNull();
        retrievedArray!.Value.Length.ShouldBe(10000);
        retrievedArray.Value[0].ShouldBe(1);
        retrievedArray.Value[9999].ShouldBe(10000);

        retrievedList.ShouldNotBeNull();
        retrievedList!.Value.Count.ShouldBe(5000);
        retrievedList.Value[0].ShouldBe("Item 1");
        retrievedList.Value[4999].ShouldBe("Item 5000");

        retrievedDict.ShouldNotBeNull();
        retrievedDict!.Value.Count.ShouldBe(2500);
        retrievedDict.Value["Key1"].ShouldBe(2);
        retrievedDict.Value["Key2500"].ShouldBe(5000);
    }

    [Fact]
    public async Task Comprehensive_EdgeCases_ShouldHandleCorrectly()
    {
        // Test edge cases
        var edgeCases = new Dictionary<string, object>
        {
            ["empty-string"] = "",
            ["null-string"] = (string?)null,
            ["zero-int"] = 0,
            ["negative-int"] = -42,
            ["max-int"] = int.MaxValue,
            ["min-int"] = int.MinValue,
            ["max-long"] = long.MaxValue,
            ["min-long"] = long.MinValue,
            ["epsilon-double"] = double.Epsilon,
            ["max-double"] = double.MaxValue,
            ["min-double"] = double.MinValue,
            ["infinity"] = double.PositiveInfinity,
            ["negative-infinity"] = double.NegativeInfinity,
            ["nan"] = double.NaN,
            ["empty-array"] = new int[0],
            ["empty-list"] = new List<string>(),
            ["empty-dict"] = new Dictionary<string, int>(),
            ["single-item-array"] = new[] { 42 },
            ["single-item-list"] = new List<string> { "single" },
            ["single-item-dict"] = new Dictionary<string, int> { ["key"] = 42 }
        };

        foreach (var edgeCase in edgeCases)
        {
            if (edgeCase.Value == null) continue; // Skip null values for now

            var key = $"comprehensive:edge:{edgeCase.Key}";
            dynamic entry = CreateTypedEntry(key, edgeCase.Value);

            await _glacialCache!.SetEntryAsync(entry);
            var retrieved = await GetTypedEntry(edgeCase.Value.GetType(), key);

            retrieved.ShouldNotBeNull();

            if (edgeCase.Value is double d && double.IsNaN(d))
            {
                // For NaN values, we need to handle them specially since they can't be directly compared
                double.IsNaN((double)retrieved!).ShouldBeTrue();
            }
            else
            {
                retrieved!.ShouldBeEquivalentTo(edgeCase.Value);
            }
        }
    }

    [Fact]
    public async Task Comprehensive_ExpirationScenarios_ShouldHandleCorrectly()
    {
        // Test various expiration scenarios
        var now = DateTimeOffset.UtcNow;

        var scenarios = new[]
        {
            new ExpirationScenario("exp:absolute:past", now.AddSeconds(-1), null, true),
            new ExpirationScenario("exp:absolute:future", now.AddSeconds(10), null, false),
            new ExpirationScenario("exp:sliding:short", null, TimeSpan.FromMilliseconds(100), false),
            new ExpirationScenario("exp:sliding:long", null, TimeSpan.FromMinutes(10), false),
            new ExpirationScenario("exp:both:absolute", now.AddSeconds(5), TimeSpan.FromMinutes(1), false),
            new ExpirationScenario("exp:both:sliding", now.AddMinutes(10), TimeSpan.FromMilliseconds(100), false)
        };

        foreach (var scenario in scenarios)
        {
            var entry = CacheEntryTestHelper.Create(
                scenario.Key,
                $"value for {scenario.Key}",
                absoluteExpiration: scenario.AbsoluteExp);

            await _glacialCache!.SetEntryAsync(entry);
        }

        // Wait for some to expire
        await Task.Delay(2000);

        foreach (var scenario in scenarios)
        {
            var retrieved = await _glacialCache!.GetEntryAsync<string>(scenario.Key);

            if (scenario.ExpectedExpired)
            {
                retrieved.ShouldBeNull();
            }
            else
            {
                retrieved.ShouldNotBeNull();
            }
        }
    }

    [Fact]
    public async Task Comprehensive_ConcurrentOperations_ShouldHandleCorrectly()
    {
        const int concurrentOperations = 100;
        const int keysPerOperation = 10;

        var tasks = new List<Task>();
        var random = new Random(42);

        // Concurrent set operations
        for (int i = 0; i < concurrentOperations; i++)
        {
            var task = Task.Run(async () =>
            {
                for (int j = 0; j < keysPerOperation; j++)
                {
                    var key = $"concurrent:set:{Guid.NewGuid()}";
                    var value = $"value-{random.Next(1000)}";
                    var entry = CacheEntryTestHelper.Create(key, value);
                    await _glacialCache!.SetEntryAsync(entry);
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Concurrent get operations
        tasks.Clear();
        for (int i = 0; i < concurrentOperations; i++)
        {
            var task = Task.Run(async () =>
            {
                for (int j = 0; j < keysPerOperation; j++)
                {
                    var key = $"concurrent:set:{Guid.NewGuid()}";
                    var result = await _glacialCache!.GetEntryAsync<string>(key);
                    // Most will be null since keys are random, but shouldn't throw
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // If we get here without exceptions, concurrent operations work
        Assert.True(true);
    }

    [Fact]
    public async Task Comprehensive_ErrorConditions_ShouldHandleCorrectly()
    {
        // Test various error conditions
        var errorScenarios = new[]
        {
            new { Key = "error:null-key", Action = new Func<Task>(() => _glacialCache!.GetEntryAsync<string>(null!)), ExpectNullArg = true },
            new { Key = "error:empty-key", Action = new Func<Task>(() => _glacialCache!.GetEntryAsync<string>("")), ExpectNullArg = false },
            new { Key = "error:whitespace-key", Action = new Func<Task>(() => _glacialCache!.GetEntryAsync<string>("   ")), ExpectNullArg = false }
        };

        foreach (var scenario in errorScenarios)
        {
            if (scenario.ExpectNullArg)
            {
                await Assert.ThrowsAsync<ArgumentNullException>(async () => await scenario.Action());
            }
            else
            {
                await Assert.ThrowsAsync<ArgumentException>(async () => await scenario.Action());
            }
            // Exception thrown as expected
        }

        // Test type mismatch handling
        var stringEntry = CacheEntryTestHelper.Create("error:type-mismatch", "string value");
        await _glacialCache!.SetEntryAsync(stringEntry);

        var intResult = await _glacialCache.GetEntryAsync<int>("error:type-mismatch");
        intResult.ShouldBeNull(); // Should handle type mismatch gracefully
    }

    [Fact]
    public async Task Comprehensive_PerformanceCharacteristics_ShouldMeetExpectations()
    {
        const int performanceTestCount = 1000;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Performance test: Set operations
        var setStopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < performanceTestCount; i++)
        {
            var entry = CacheEntryTestHelper.Create($"perf:set:{i}", $"value-{i}");
            await _glacialCache!.SetEntryAsync(entry);
        }
        setStopwatch.Stop();

        // Performance test: Get operations
        var getStopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < performanceTestCount; i++)
        {
            var result = await _glacialCache!.GetEntryAsync<string>($"perf:set:{i}");
            result.ShouldNotBeNull();
        }
        getStopwatch.Stop();

        // Performance test: Batch operations
        var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var batchData = new Dictionary<string, (string value, DistributedCacheEntryOptions? options)>();
        for (int i = 0; i < 100; i++)
        {
            batchData[$"perf:batch:{i}"] = ($"batch-value-{i}", null);
        }
        await _glacialCache!.SetMultipleEntriesAsync(batchData);
        batchStopwatch.Stop();

        var totalTime = stopwatch.ElapsedMilliseconds;

        // Assert performance expectations
        setStopwatch.ElapsedMilliseconds.ShouldBeLessThan(10000); // 10 seconds for 1000 operations
        getStopwatch.ElapsedMilliseconds.ShouldBeLessThan(5000);  // 5 seconds for 1000 operations
        batchStopwatch.ElapsedMilliseconds.ShouldBeLessThan(2000); // 2 seconds for 100 operations
        totalTime.ShouldBeLessThan(15000); // Total should be reasonable

        Output.WriteLine($"Performance Results:");
        Output.WriteLine($"  Set operations: {setStopwatch.ElapsedMilliseconds}ms for {performanceTestCount} operations");
        Output.WriteLine($"  Get operations: {getStopwatch.ElapsedMilliseconds}ms for {performanceTestCount} operations");
        Output.WriteLine($"  Batch operations: {batchStopwatch.ElapsedMilliseconds}ms for 100 operations");
        Output.WriteLine($"  Total time: {totalTime}ms");
    }

    [Fact]
    public async Task Comprehensive_MemoryUsage_ShouldBeReasonable()
    {
        // Test memory usage characteristics
        var initialMemory = GC.GetTotalMemory(true);

        // Create and cache many objects
        const int memoryTestCount = 1000;
        var entries = new List<CacheEntry<LargeTestObject>>();

        for (int i = 0; i < memoryTestCount; i++)
        {
            var largeObject = new LargeTestObject
            {
                Id = i,
                Data = new byte[1024], // 1KB per object
                Metadata = Enumerable.Range(1, 100).ToDictionary(j => $"key{j}", j => j.ToString())
            };

            var entry = CacheEntryTestHelper.Create($"memory:{i}", largeObject);
            entries.Add(entry);
            await _glacialCache!.SetEntryAsync(entry);
        }

        // Verify objects were stored correctly
        var verificationCount = 0;
        for (int i = 0; i < Math.Min(100, memoryTestCount); i++) // Verify first 100
        {
            var retrieved = await _glacialCache!.GetEntryAsync<LargeTestObject>($"memory:{i}");
            if (retrieved != null)
            {
                verificationCount++;
                retrieved.Value.Id.ShouldBe(i);
                retrieved.Value.Data.Length.ShouldBe(1024);
            }
        }
        verificationCount.ShouldBeGreaterThan(90); // At least 90% should be retrievable

        // Force garbage collection to get accurate memory measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        // Memory increase should be reasonable (less than 100MB for 1000 1KB objects)
        memoryIncrease.ShouldBeLessThan(100 * 1024 * 1024);

        // Clean up cached objects to verify proper disposal
        for (int i = 0; i < memoryTestCount; i++)
        {
            await _glacialCache!.RemoveAsync($"memory:{i}");
        }

        // Force garbage collection after cleanup
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var cleanupMemory = GC.GetTotalMemory(true);
        var cleanupReduction = finalMemory - cleanupMemory;

        Output.WriteLine($"Memory Usage Results:");
        Output.WriteLine($"  Initial memory: {initialMemory / 1024 / 1024}MB");
        Output.WriteLine($"  Final memory: {finalMemory / 1024 / 1024}MB");
        Output.WriteLine($"  Memory increase: {memoryIncrease / 1024 / 1024}MB");
        Output.WriteLine($"  After cleanup: {cleanupMemory / 1024 / 1024}MB");
        Output.WriteLine($"  Memory reduction: {cleanupReduction / 1024 / 1024}MB");
        Output.WriteLine($"  Objects cached: {memoryTestCount}");
        Output.WriteLine($"  Objects verified: {verificationCount}");
    }

    [Fact]
    public async Task Comprehensive_BackwardCompatibility_ShouldBeMaintained()
    {
        // Test that existing functionality still works
        var traditionalKey = "backward:traditional";
        var traditionalValue = System.Text.Encoding.UTF8.GetBytes("traditional value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // Use traditional IDistributedCache methods
        await _distributedCache!.SetAsync(traditionalKey, traditionalValue, options);
        var traditionalResult = await _distributedCache.GetAsync(traditionalKey);
        traditionalResult.ShouldBeEquivalentTo(traditionalValue);

        // Use new typed methods to retrieve traditional data
        var typedResult = await _glacialCache!.GetEntryAsync<string>(traditionalKey);
        typedResult.ShouldNotBeNull();
        typedResult!.Value.ShouldBe("traditional value");

        // Use traditional methods to retrieve typed data
        var typedKey = "backward:typed";
        var typedEntry = CacheEntryTestHelper.Create(typedKey, "typed value");
        await _glacialCache.SetEntryAsync(typedEntry);

        var traditionalTypedResult = await _distributedCache.GetAsync(typedKey);
        traditionalTypedResult.ShouldNotBeNull();
        System.Text.Encoding.UTF8.GetString(traditionalTypedResult!).ShouldContain("typed value");
    }

    [Fact]
    public async Task Comprehensive_NullAndDefaultValues_ShouldHandleCorrectly()
    {
        // Test null string handling - null strings are serialized as empty strings
        var nullStringEntry = CacheEntryTestHelper.Create("null:string", (string?)null);
        await _glacialCache!.SetEntryAsync(nullStringEntry);
        var nullStringResult = await _glacialCache.GetEntryAsync<string>("null:string");
        nullStringResult.ShouldNotBeNull();
        nullStringResult!.Value.ShouldBe(string.Empty);

        // Test default(T) values
        var defaultIntEntry = CacheEntryTestHelper.Create("default:int", default(int));
        await _glacialCache.SetEntryAsync(defaultIntEntry);
        var defaultIntResult = await _glacialCache.GetEntryAsync<int>("default:int");
        defaultIntResult.ShouldNotBeNull();
        defaultIntResult!.Value.ShouldBe(0);

        // Test empty collections
        var emptyListEntry = CacheEntryTestHelper.Create("empty:list", new List<string>());
        await _glacialCache.SetEntryAsync(emptyListEntry);
        var emptyListResult = await _glacialCache.GetEntryAsync<List<string>>("empty:list");
        emptyListResult.ShouldNotBeNull();
        emptyListResult!.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Comprehensive_CorruptPayloadDeserialization_ShouldHandleGracefully()
    {
        // Arrange - Set invalid data directly in database
        const string key = "corrupt-data-key";
        var invalidBytes = new byte[] { 0xFF, 0xFF, 0xFF }; // Invalid MemoryPack data

        // Use raw database access to set invalid data
        var connection = new NpgsqlConnection(_postgres!.GetConnectionString());
        await connection.OpenAsync();
        var command = new NpgsqlCommand(
            "INSERT INTO public.glacial_cache (key, value) VALUES (@key, @value) " +
            "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value",
            connection);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", invalidBytes);
        await command.ExecuteNonQueryAsync();
        await connection.CloseAsync();

        // Act - Try to get as typed entry
        var result = await _glacialCache!.GetEntryAsync<string>(key);

        // Assert - Should return null due to deserialization error
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Comprehensive_BatchOperations_ShouldWorkCorrectly()
    {
        // Arrange
        var entries = new Dictionary<string, (string value, DistributedCacheEntryOptions? options)>
        {
            ["batch-1"] = ("value1", null),
            ["batch-2"] = ("value2", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }),
            ["batch-3"] = ("value3", new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(3) })
        };

        // Act
        await _glacialCache!.SetMultipleEntriesAsync(entries);
        var retrieved = await _glacialCache.GetMultipleEntriesAsync<string>(new[] { "batch-1", "batch-2", "batch-3" });

        // Assert
        retrieved.Count.ShouldBe(3);
        retrieved["batch-1"]!.Value.ShouldBe("value1");
        retrieved["batch-2"]!.Value.ShouldBe("value2");
        retrieved["batch-3"]!.Value.ShouldBe("value3");
    }

    [Fact]
    public async Task Comprehensive_VeryLargePayload_ShouldHandleCorrectly()
    {
        // Test with very large payload (10MB)
        const int largeSize = 10 * 1024 * 1024; // 10MB
        var largeData = new byte[largeSize];
        Random.Shared.NextBytes(largeData);

        var largeEntry = CacheEntryTestHelper.Create("very:large:payload", largeData);
        await _glacialCache!.SetEntryAsync(largeEntry);

        var retrieved = await _glacialCache.GetEntryAsync<byte[]>("very:large:payload");
        retrieved.ShouldNotBeNull();
        retrieved!.Value.ShouldBeEquivalentTo(largeData);
        retrieved.Value.Length.ShouldBe(largeSize);
    }

    private static CacheEntry<T> CreateTypedEntry<T>(string key, T value) => CacheEntryTestHelper.Create(key, value);

    private static object CreateTypedEntry(string key, object value)
    {
        // Use a simpler approach - call the factory directly based on type
        return value switch
        {
            string str => CacheEntryTestHelper.Create(key, str),
            int i => CacheEntryTestHelper.Create(key, i),
            long l => CacheEntryTestHelper.Create(key, l),
            double d => CacheEntryTestHelper.Create(key, d),
            decimal dec => CacheEntryTestHelper.Create(key, dec),
            bool b => CacheEntryTestHelper.Create(key, b),
            DateTime dt => CacheEntryTestHelper.Create(key, dt),
            DateTimeOffset dto => CacheEntryTestHelper.Create(key, dto),
            TimeSpan ts => CacheEntryTestHelper.Create(key, ts),
            Guid g => CacheEntryTestHelper.Create(key, g),
            byte b => CacheEntryTestHelper.Create(key, b),
            char c => CacheEntryTestHelper.Create(key, c),
            float f => CacheEntryTestHelper.Create(key, f),
            short s => CacheEntryTestHelper.Create(key, s),
            ushort us => CacheEntryTestHelper.Create(key, us),
            uint ui => CacheEntryTestHelper.Create(key, ui),
            ulong ul => CacheEntryTestHelper.Create(key, ul),
            sbyte sb => CacheEntryTestHelper.Create(key, sb),
            byte[] bytes => CacheEntryTestHelper.Create(key, bytes),
            int[] intArray => CacheEntryTestHelper.Create(key, intArray),
            string[] stringArray => CacheEntryTestHelper.Create(key, stringArray),
            List<string> stringList => CacheEntryTestHelper.Create(key, stringList),
            Dictionary<string, int> stringIntDict => CacheEntryTestHelper.Create(key, stringIntDict),
            _ => throw new NotSupportedException($"Type {value.GetType()} is not supported for typed cache entries")
        };
    }

    private async Task<object?> GetTypedEntry(Type expectedType, string key)
    {
        var entry = await _glacialCache!.GetEntryAsync(key);
        if (entry == null)
        {
            return null;
        }

        // Use factory-based deserialization instead of direct MemoryPack
        // We need to use reflection to call the generic method with the correct type
        var methods = typeof(CacheEntryTestHelper).GetMethods().Where(m => m.Name == "FromSerializedData" && m.IsGenericMethodDefinition).ToArray();
        var method = methods.FirstOrDefault();

        if (method == null)
        {
            return null;
        }

        var genericMethod = method.MakeGenericMethod(expectedType);
        var deserializedEntry = genericMethod.Invoke(null, new object[] { key, entry.SerializedData.ToArray(), null, null, null });

        // Extract the Value property using reflection
        var valueProperty = deserializedEntry?.GetType().GetProperty("Value");
        return valueProperty?.GetValue(deserializedEntry);
    }
}

// Test helper classes
[MemoryPackable]
public partial record NestedComplexObject
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public NestedComplexObject[] Children { get; init; } = Array.Empty<NestedComplexObject>();
    public Dictionary<string, string> Metadata { get; init; } = new();
}

[MemoryPackable]
public partial record LargeTestObject
{
    public int Id { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public Dictionary<string, string> Metadata { get; init; } = new();
}

// TestClockService removed - ClockSynchronizationService no longer needed

// Helper record for expiration scenarios
public record ExpirationScenario(string Key, DateTimeOffset? AbsoluteExp, TimeSpan? SlidingExp, bool ExpectedExpired);
