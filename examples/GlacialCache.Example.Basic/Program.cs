using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Models;
using System.Diagnostics;

namespace GlacialCache.Example.Basic;

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

        // Add GlacialCache PostgreSQL with standard connection
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Cache.SchemaName = "";
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

        // Clean API: No casting required!
        var cache = serviceProvider.GetRequiredService<IGlacialCache>();

        Console.WriteLine("🚀 %GlacialCache PostgreSQL Example (Basic)");
        Console.WriteLine("=============================================");

        // Standard IDistributedCache operations
        Console.WriteLine("\n📝 Standard Operations:\n--------------------------------\n Setting key1 with sliding expiration of 5 seconds");
        await MeasureOperationAsync("Set key1", async () =>
            await cache.SetAsync("key1", System.Text.Encoding.UTF8.GetBytes("value1"),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromSeconds(5) }));

        Console.WriteLine("--------------------------------");
        Console.WriteLine("Trying to Get key1 while it is still in the cache while waiting for 2 second");

        var continueWaiting = 0;
        while (continueWaiting < 5)
        {
            Console.WriteLine("--------------------------------");
            var valueWhileWaiting = await MeasureOperationAsync("Try to Get key1", async () => await cache.GetAsync("key1"));
            if (valueWhileWaiting != null)
            {
                Console.WriteLine($"✅ Retrieved: {System.Text.Encoding.UTF8.GetString(valueWhileWaiting)}");
            }
            else
            {
                Console.WriteLine("✅ Correctly failed to retrieve value");
            }
            Console.WriteLine("Waiting for 1 second...");
            await Task.Delay(1000);
            continueWaiting++;
        }
        Console.WriteLine("--------------------------------");


        var value1 = await MeasureOperationAsync("Try to Get key1", async () => await cache.GetAsync("key1"));
        if (value1 != null)
        {
            Console.WriteLine($"✅ Retrieved: {System.Text.Encoding.UTF8.GetString(value1)}");
        }
        else
        {
            Console.WriteLine("✅ Correctly failed to retrieve value");
        }
        Console.WriteLine("--------------------------------");
        Console.WriteLine("Waiting for 6 seconds...");
        await Task.Delay(6000);
        var value1Again = await MeasureOperationAsync("Get key1 again", async () => await cache.GetAsync("key1"));
        if (value1Again != null)
        {
            Console.WriteLine($"✅ Retrieved: {System.Text.Encoding.UTF8.GetString(value1Again)}");
        }
        else
        {
            Console.WriteLine("❌ Failed to retrieve value");
        }


        // ✅ Batch operations (no casting needed!)
        Console.WriteLine("\n📦 Batch Operations:");
        var batchData = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>
        {
            ["batch-key1"] = (System.Text.Encoding.UTF8.GetBytes("batch-value1"),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) }),
            ["batch-key2"] = (System.Text.Encoding.UTF8.GetBytes("batch-value2"),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) }),
            ["batch-key3"] = (System.Text.Encoding.UTF8.GetBytes("batch-value3"),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) })
        };

        await MeasureOperationAsync("Set multiple entries in batch", async () => await cache.SetMultipleAsync(batchData));

        var batchResults = await MeasureOperationAsync("Get multiple entries in batch",
            async () => await cache.GetMultipleAsync(new[] { "batch-key1", "batch-key2", "batch-key3" }));
        Console.WriteLine($"✅ Retrieved {batchResults.Count} entries in batch:");
        foreach (var kvp in batchResults)
        {
            if (kvp.Value != null)
            {
                Console.WriteLine($"   {kvp.Key}: {System.Text.Encoding.UTF8.GetString(kvp.Value)}");
            }
        }

        // ✅ Standard cache operations (no casting needed!)
        Console.WriteLine("\n🔗 Standard Cache Operations:");
        await MeasureOperationAsync("Set cache key", async () =>
            await cache.SetAsync("cache-key1", System.Text.Encoding.UTF8.GetBytes("cache-value1"),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) }));

        var cacheValue = await MeasureOperationAsync("Get cache key", async () => await cache.GetAsync("cache-key1"));
        if (cacheValue != null)
        {
            Console.WriteLine($"✅ Retrieved from cache operation: {System.Text.Encoding.UTF8.GetString(cacheValue)}");
        }

        // Batch removal
        var removedCount = await MeasureOperationAsync("Remove multiple entries",
            async () => await cache.RemoveMultipleAsync(new[] { "batch-key1", "batch-key2" }));
        Console.WriteLine($"✅ Removed {removedCount} entries in batch");

        // Batch refresh
        var refreshedCount = await MeasureOperationAsync("Refresh multiple entries",
            async () => await cache.RefreshMultipleAsync(new[] { "batch-key3" }));
        Console.WriteLine($"✅ Refreshed {refreshedCount} entries in batch");

        // ✅ Basic CacheEntry operations
        Console.WriteLine("\n🎯 CacheEntry Operations:");
        await RunBasicCacheEntryOperationsAsync(cache);

        Console.WriteLine("\n🎉 All operations completed successfully!");
    }

    private static async Task<T> MeasureOperationAsync<T>(string operationName, Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await operation();
        stopwatch.Stop();
        Console.WriteLine($"✅ {operationName} [{stopwatch.ElapsedMilliseconds} ms]");
        return result;
    }

    private static async Task MeasureOperationAsync(string operationName, Func<Task> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        await operation();
        stopwatch.Stop();
        Console.WriteLine($"✅ {operationName} [{stopwatch.ElapsedMilliseconds} ms]");
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

    private static async Task RunBasicCacheEntryOperationsAsync(IGlacialCache cache)
    {
        var timeProvider = TimeProvider.System;

        // Create a cache entry with metadata
        var cacheEntry = new CacheEntry<byte[]>()
        {
            Key = "cache-entry-demo",
            Value = System.Text.Encoding.UTF8.GetBytes("CacheEntry Demo Value"),
            AbsoluteExpiration = timeProvider.GetUtcNow().AddMinutes(5),
            SlidingExpiration = TimeSpan.FromMinutes(2)
        };

        await cache.SetAsync(cacheEntry);
        Console.WriteLine($"✅ Set CacheEntry: {cacheEntry.Key}");

        // Retrieve the cache entry with metadata
        var retrieved = await cache.GetEntryAsync("cache-entry-demo");
        if (retrieved != null)
        {
            Console.WriteLine($"✅ Retrieved CacheEntry:");
            Console.WriteLine($"   Key: {retrieved.Key}");
            Console.WriteLine($"   Value: {System.Text.Encoding.UTF8.GetString(retrieved.Value.ToArray())}");
            Console.WriteLine($"   Absolute Expiration: {retrieved.AbsoluteExpiration}");
            Console.WriteLine($"   Sliding Expiration: {retrieved.SlidingExpiration}");
        }

        // Demonstrate batch operations with CacheEntry
        var batchEntries = new List<CacheEntry<byte[]>>
        {
            new CacheEntry<byte[]>()
            {
                Key = "batch-entry-1",
                Value = System.Text.Encoding.UTF8.GetBytes("Batch Value 1")
            },
            new CacheEntry<byte[]>()
            {
                Key = "batch-entry-2",
                Value = System.Text.Encoding.UTF8.GetBytes("Batch Value 2")
            },
            new CacheEntry<byte[]>()
            {
                Key = "batch-entry-3",
                Value = System.Text.Encoding.UTF8.GetBytes("Batch Value 3")
            }
        };

        await cache.SetMultipleEntriesAsync(batchEntries);
        Console.WriteLine($"✅ Set {batchEntries.Count} CacheEntry items in batch");

        var retrievedBatch = await cache.GetMultipleEntriesAsync(new[] { "batch-entry-1", "batch-entry-2", "batch-entry-3" });
        Console.WriteLine($"✅ Retrieved {retrievedBatch.Count} CacheEntry items from batch");
    }
}
