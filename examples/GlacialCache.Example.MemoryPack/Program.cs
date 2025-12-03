using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Models;
using System.Diagnostics;
using MemoryPack;
using System.Text.Json;
using GlacialCache.PostgreSQL.Services;

namespace GlacialCache.Example.MemoryPack;

[MemoryPackable]
public partial record UserProfile
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string[] Roles { get; init; } = Array.Empty<string>();
}

[MemoryPackable]
public partial record Product
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Category { get; init; } = string.Empty;
}

class Program
{
    static async Task Main(string[] args)
    {
        // Setup services
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Get connection string from environment variables or use default
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "cache_test";
        var username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";

        var connectionString = $"Host={host};Database={database};Username={username};Password={password}";

        Console.WriteLine($"🔧 Using connection string: {connectionString}");

        // Wait for PostgreSQL to be ready
        Console.WriteLine("⏳ Waiting for PostgreSQL to be ready...");
        await WaitForPostgreSQL(host, database, username, password);
        Console.WriteLine("✅ PostgreSQL is ready!");

        // Add GlacialCache PostgreSQL with MemoryPack support
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = connectionString;
            options.Cache.SchemaName = "cache";
            options.Cache.TableName = "entries";
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);

            // For demo purposes, disable manager election and force infrastructure creation
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Get the cache service
        var cache = serviceProvider.GetRequiredService<IGlacialCache>();
        var cacheEntryFactory = serviceProvider.GetRequiredService<GlacialCacheEntryFactory>();

        Console.WriteLine("🚀 GlacialCache PostgreSQL MemoryPack Example");
        Console.WriteLine("===========================================");

        // Run MemoryPack examples
        await RunBasicTypedOperationsAsync(cache);
        await RunComplexObjectSerializationAsync(cache);
        await RunPerformanceComparisonAsync(cache);
        await RunBatchOperationsAsync(cache);

        Console.WriteLine("\n🎉 All MemoryPack examples completed successfully!");
        Console.WriteLine("\n💡 MemoryPack Benefits:");
        Console.WriteLine("   ✅ 10x faster serialization than System.Text.Json");
        Console.WriteLine("   ✅ 30-50% reduction in memory allocations");
        Console.WriteLine("   ✅ Zero-copy deserialization for optimal performance");
        Console.WriteLine("   ✅ Type safety at compile time");
        Console.WriteLine("   ✅ Cross-platform compatibility");
    }

    private static async Task RunBasicTypedOperationsAsync(IGlacialCache cache)
    {
        Console.WriteLine("\n📝 Example 1: Basic Typed Operations");
        Console.WriteLine("------------------------------------");

        var timeProvider = TimeProvider.System;

        // Create a typed cache entry
        var userEntry = new CacheEntry<string>()
        {
            Key = "user:name:123",
            Value = "John Doe",
            // timeProvider: timeProvider,
            SlidingExpiration = TimeSpan.FromMinutes(15)
        };

        await cache.SetEntryAsync(userEntry);
        Console.WriteLine($"✅ Set typed entry: {userEntry.Key} = {userEntry.Value}");

        // Retrieve with type safety
        var retrievedUser = await cache.GetEntryAsync<string>("user:name:123");
        if (retrievedUser != null)
        {
            Console.WriteLine($"✅ Retrieved typed entry: {retrievedUser.Key} = {retrievedUser.Value}");
            Console.WriteLine($"   Expires: {retrievedUser.AbsoluteExpiration}");
        }
    }

    private static async Task RunComplexObjectSerializationAsync(IGlacialCache cache)
    {
        Console.WriteLine("\n📝 Example 2: Complex Object Serialization");
        Console.WriteLine("------------------------------------------");

        var timeProvider = TimeProvider.System;

        // Create complex objects
        var user = new UserProfile
        {
            Id = 12345,
            Name = "Jane Smith",
            Roles = new[] { "user", "admin", "moderator" }
        };

        var product = new Product
        {
            Id = 67890,
            Name = "Wireless Headphones",
            Price = 199.99m,
            Category = "Electronics"
        };

        // Cache the user profile
        var userEntry = new CacheEntry<UserProfile>()
        {
            Key = $"user:profile:{user.Id}",
            Value = user,
            // timeProvider: timeProvider,
            AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(2)
        };

        await cache.SetEntryAsync(userEntry);
        Console.WriteLine($"✅ Cached user profile: {user.Name} with {user.Roles.Length} roles");

        // Cache the product
        var productEntry = new CacheEntry<Product>()
        {
            Key = $"product:{product.Id}",
            Value = product,
            //timeProvider: timeProvider,
            SlidingExpiration = TimeSpan.FromMinutes(30)
        };

        await cache.SetEntryAsync(productEntry);
        Console.WriteLine($"✅ Cached product: {product.Name} (${product.Price})");

        // Retrieve and verify
        var retrievedUser = await cache.GetEntryAsync<UserProfile>($"user:profile:{user.Id}");
        var retrievedProduct = await cache.GetEntryAsync<Product>($"product:{product.Id}");

        if (retrievedUser != null && retrievedProduct != null)
        {
            Console.WriteLine($"✅ Retrieved user: {retrievedUser.Value.Name}");
            Console.WriteLine($"✅ Retrieved product: {retrievedProduct.Value.Name} - ${retrievedProduct.Value.Price}");
        }
    }

    private static async Task RunPerformanceComparisonAsync(IGlacialCache cache)
    {
        Console.WriteLine("\n📝 Example 3: Performance Comparison");
        Console.WriteLine("-----------------------------------");

        var timeProvider = TimeProvider.System;

        // Create test data
        var largeObject = new UserProfile
        {
            Id = 99999,
            Name = "Performance Test User",
            Roles = Enumerable.Range(1, 100).Select(i => $"role_{i}").ToArray()
        };

        var iterations = 100;
        var stopwatch = Stopwatch.StartNew();

        // System.Text.Json.JsonSerializer operations (using separate keys to avoid conflicts)
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            await cache.SetAsync($"perf:json:{i}", JsonSerializer.SerializeToUtf8Bytes(largeObject), new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) });
        }
        var jsonSetTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var retrieved = await cache.GetAsync($"perf:json:{i}");
            if (retrieved != null)
            {
                var largeObjectDeserialized = JsonSerializer.Deserialize<UserProfile>(retrieved);
            }
        }
        var jsonGetTime = stopwatch.ElapsedMilliseconds;

        // MemoryPack operations (using separate keys to avoid conflicts)
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var entry = new CacheEntry<UserProfile>()
            {
                Key = $"perf:memorypack:{i}",
                Value = largeObject,
                SlidingExpiration = TimeSpan.FromMinutes(10)
            };
            await cache.SetEntryAsync(entry);
        }
        var memoryPackSetTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var retrieved = await cache.GetEntryAsync<UserProfile>($"perf:memorypack:{i}");
        }
        var memoryPackGetTime = stopwatch.ElapsedMilliseconds;

        Console.WriteLine($"✅ JSON Performance ({iterations} operations):");
        Console.WriteLine($"   Set operations: {jsonSetTime}ms (Avg: {jsonSetTime / (double)iterations:F2}ms/op)");
        Console.WriteLine($"   Get operations: {jsonGetTime}ms (Avg: {jsonGetTime / (double)iterations:F2}ms/op)");
        Console.WriteLine($"   Total: {jsonSetTime + jsonGetTime}ms");
        Console.WriteLine($"   Average per operation (GET + SET): {(jsonSetTime + jsonGetTime) / (iterations * 2.0):F2}ms");

        Console.WriteLine($"✅ MemoryPack Performance ({iterations} operations):");
        Console.WriteLine($"   Set operations: {memoryPackSetTime}ms ({(double)jsonSetTime / Math.Max(memoryPackSetTime, 1):F2}x faster)");
        Console.WriteLine($"   Get operations: {memoryPackGetTime}ms ({(double)jsonGetTime / Math.Max(memoryPackGetTime, 1):F2}x faster)");
        var totalJson = jsonSetTime + jsonGetTime;
        var totalMemoryPack = memoryPackSetTime + memoryPackGetTime;
        Console.WriteLine($"   Total: {totalMemoryPack}ms ({(double)totalJson / Math.Max(totalMemoryPack, 1):F2}x faster overall)");
        Console.WriteLine($"   Average per operation (GET + SET): {(double)totalMemoryPack / (iterations * 2.0):F2}ms");

    }

    private static async Task RunBatchOperationsAsync(IGlacialCache cache)
    {
        Console.WriteLine("\n📝 Example 4: Batch Operations with MemoryPack");
        Console.WriteLine("----------------------------------------------");

        var timeProvider = TimeProvider.System;

        // Create batch data
        var batchData = new Dictionary<string, (string value, DistributedCacheEntryOptions? options)>
        {
            ["batch:user:1"] = ("Alice Johnson", new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) }),
            ["batch:user:2"] = ("Bob Wilson", new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) }),
            ["batch:user:3"] = ("Carol Brown", new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) })
        };

        // Set multiple entries in batch
        await cache.SetMultipleEntriesAsync(batchData);
        Console.WriteLine($"✅ Set {batchData.Count} entries in batch using typed operations");

        // Get multiple entries in batch
        var keys = batchData.Keys.ToArray();
        var retrieved = await cache.GetMultipleEntriesAsync<string>(keys);
        Console.WriteLine($"✅ Retrieved {retrieved.Count} entries in batch:");

        foreach (var (key, entry) in retrieved)
        {
            if (entry != null)
            {
                Console.WriteLine($"   {key}: {entry.Value}");
            }
        }

        // Complex object batch operations
        var complexBatch = new List<CacheEntry<Product>>
        {
            new()
            {
                Key = "batch:product:1",
                Value = new Product { Id = 1, Name = "Laptop", Price = 999.99m, Category = "Electronics" },
            },
            new()
            {
                Key = "batch:product:2",
                Value = new Product { Id = 2, Name = "Mouse", Price = 29.99m, Category = "Electronics" },
            },
            new()
            {
                Key = "batch:product:3",
                Value = new Product { Id = 3, Name = "Keyboard", Price = 79.99m, Category = "Electronics" },
            }
        };

        await cache.SetMultipleEntriesAsync(complexBatch);
        Console.WriteLine($"✅ Set {complexBatch.Count} complex objects in batch");

        var productKeys = complexBatch.Select(e => e.Key).ToList();
        var retrievedProducts = await cache.GetMultipleEntriesAsync<Product>(productKeys);

        Console.WriteLine($"✅ Retrieved {retrievedProducts.Count} complex objects:");
        foreach (var (key, entry) in retrievedProducts)
        {
            if (entry != null)
            {
                Console.WriteLine($"   {key}: {entry.Value.Name} - ${entry.Value.Price}");
            }
        }
    }

    private static async Task WaitForPostgreSQL(string host, string database, string username, string password)
    {
        var maxAttempts = 30;
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            try
            {
                using var connection = new Npgsql.NpgsqlConnection($"Host={host};Database={database};Username={username};Password={password}");
                await connection.OpenAsync();
                await connection.CloseAsync();
                return; // Success
            }
            catch (Exception ex)
            {
                attempt++;
                Console.WriteLine($"⏳ Attempt {attempt}/{maxAttempts}: PostgreSQL not ready yet ({ex.Message})");
                if (attempt < maxAttempts)
                {
                    await Task.Delay(2000); // Wait 2 seconds before next attempt
                }
            }
        }

        throw new InvalidOperationException("PostgreSQL is not ready after maximum attempts");
    }
}




