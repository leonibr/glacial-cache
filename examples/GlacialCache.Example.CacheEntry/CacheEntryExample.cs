using Microsoft.Extensions.Caching.Distributed;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Services;
using System.Linq;

namespace GlacialCache.Example.CacheEntryExample;

/// <summary>
/// Example demonstrating how to use the new GetAsync and SetAsync methods with CacheEntry objects.
/// </summary>
public class CacheEntryExample
{
    private readonly IGlacialCache _cache;
    private readonly GlacialCacheEntryFactory _cacheEntryFactory;
    public CacheEntryExample(IGlacialCache cache, GlacialCacheEntryFactory cacheEntryFactory)
    {
        _cache = cache;
        _cacheEntryFactory = cacheEntryFactory;
    }

    /// <summary>
    /// Demonstrates using GetAsync and SetAsync with CacheEntry objects.
    /// </summary>
    public async Task RunExampleAsync()
    {
        Console.WriteLine("🚀 GlacialCache CacheEntry Example");
        Console.WriteLine("=================================");

        // Example 1: Using GetAsync with CacheEntry
        Console.WriteLine("\n📝 Example 1: GetAsync with CacheEntry");

        // First, set a cache entry using the traditional method
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };

        await _cache.SetAsync("user:123", System.Text.Encoding.UTF8.GetBytes("John Doe"), options);

        // Now retrieve it using the new GetEntryAsync method that returns CacheEntry
        CacheEntry<byte[]>? cacheEntry = await _cache.GetEntryAsync("user:123");

        if (cacheEntry != null)
        {
            Console.WriteLine($"✅ Retrieved CacheEntry:");
            Console.WriteLine($"   Key: {cacheEntry.Key}");
            Console.WriteLine($"   Value: {System.Text.Encoding.UTF8.GetString(cacheEntry.Value.ToArray())}");
            Console.WriteLine($"   AbsoluteExpiration: {cacheEntry.AbsoluteExpiration}");
            Console.WriteLine($"   SlidingExpiration: {cacheEntry.SlidingExpiration}");
        }
        else
        {
            Console.WriteLine("❌ Cache entry not found or expired");
        }

        // Example 2: Using SetAsync with CacheEntry and TimeProvider
        Console.WriteLine("\n📝 Example 2: SetAsync with CacheEntry and TimeProvider");

        // For examples, we'll use TimeProvider.System, but in production code
        // you should inject TimeProvider through dependency injection
        var timeProvider = TimeProvider.System;

        var newEntry = new CacheEntry<byte[]>()
        {
            Key = "user:456",
            Value = System.Text.Encoding.UTF8.GetBytes("Jane Smith"),
            AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(2),
            SlidingExpiration = TimeSpan.FromMinutes(15)
        };

        await _cache.SetAsync(newEntry);
        Console.WriteLine($"✅ Set CacheEntry for key: {newEntry.Key}");

        // Retrieve the newly set entry
        var retrievedEntry = await _cache.GetEntryAsync("user:456");
        if (retrievedEntry != null)
        {
            Console.WriteLine($"✅ Retrieved newly set CacheEntry:");
            Console.WriteLine($"   Value: {System.Text.Encoding.UTF8.GetString(retrievedEntry.Value.ToArray())}");
        }

        // Example 3: Working with IDistributedCache interface
        Console.WriteLine("\n📝 Example 3: Using IDistributedCache interface");

        if (_cache is IDistributedCache distributedCache)
        {
            // This will work with any IDistributedCache implementation
            var entryFromDistributedCache = await distributedCache.GetAsync("user:123");
            if (entryFromDistributedCache != null)
            {
                Console.WriteLine($"✅ Retrieved from IDistributedCache:");
                Console.WriteLine($"   Value: {System.Text.Encoding.UTF8.GetString(entryFromDistributedCache)}");
            }
        }

        // Example 4: Batch operations with CacheEntry
        Console.WriteLine("\n📝 Example 4: Batch operations with CacheEntry");

        var entries = new List<CacheEntry<byte[]>>
        {
            _cacheEntryFactory.Create<byte[]>("batch:1", System.Text.Encoding.UTF8.GetBytes("Batch Entry 1"),
                absoluteExpiration: timeProvider.GetUtcNow().AddHours(1)),
            _cacheEntryFactory.Create<byte[]>("batch:2", System.Text.Encoding.UTF8.GetBytes("Batch Entry 2"),
                slidingExpiration: TimeSpan.FromMinutes(30)),
            _cacheEntryFactory.Create<byte[]>("batch:3", System.Text.Encoding.UTF8.GetBytes("Batch Entry 3")),
            _cacheEntryFactory.Create<byte[]>("batch:4", System.Text.Encoding.UTF8.GetBytes("Batch Entry 4"),
                absoluteExpiration: timeProvider.GetUtcNow().AddHours(1),
                slidingExpiration: TimeSpan.FromMinutes(30)),
        };

        await _cache.SetMultipleEntriesAsync(entries);
        Console.WriteLine($"✅ Set {entries.Count} cache entries in batch");

        var keys = entries.Select(e => e.Key).ToList();
        var retrievedEntries = await _cache.GetMultipleEntriesAsync(keys);

        Console.WriteLine($"✅ Retrieved {retrievedEntries.Count} cache entries:");
        foreach (var (key, entry) in retrievedEntries)
        {
            if (entry != null)
            {
                Console.WriteLine($"   {key}: {System.Text.Encoding.UTF8.GetString(entry.Value.ToArray())}");
            }
        }

        Console.WriteLine("\n🎉 CacheEntry example completed successfully!");
    }
}




