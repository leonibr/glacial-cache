using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;

namespace GlacialCache.Benchmarks;

/// <summary>
/// Verification test for batch operations functionality (NOT a benchmark).
/// This is a pre-benchmark verification tool to ensure batch operations work correctly
/// before running performance benchmarks. Useful for CI/CD validation.
/// Run this before benchmarks to ensure everything works correctly.
/// </summary>
public class BatchOperationsTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("🧪 Testing GlacialCache Batch Operations...");

        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .WithCleanUp(true)
            .Build();

        await postgres.StartAsync();

        // Setup GlacialCache
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = postgres.GetConnectionString();
            options.Cache.SchemaName = "test";
            options.Cache.TableName = "cache_entries";
        });

        using var serviceProvider = services.BuildServiceProvider();
        var GlacialCache = serviceProvider.GetRequiredService<IGlacialCache>();

        Console.WriteLine("✅ GlacialCache initialized");

        // Initialize the database schema by doing a simple operation first
        await GlacialCache.SetAsync("init-key", System.Text.Encoding.UTF8.GetBytes("init"), new DistributedCacheEntryOptions());
        await GlacialCache.RemoveAsync("init-key");
        Console.WriteLine("✅ Database schema initialized");

        // Test data
        var testData = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>
        {
            ["key1"] = (System.Text.Encoding.UTF8.GetBytes("value1"), new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) }),
            ["key2"] = (System.Text.Encoding.UTF8.GetBytes("value2"), new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) }),
            ["key3"] = (System.Text.Encoding.UTF8.GetBytes("value3"), new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) })
        };

        // Test batch operations
        Console.WriteLine("🔄 Testing SetMultipleAsync...");
        await GlacialCache.SetMultipleAsync(testData);
        Console.WriteLine("✅ SetMultipleAsync completed");

        Console.WriteLine("🔄 Testing GetMultipleAsync...");
        var results = await GlacialCache.GetMultipleAsync(testData.Keys);
        Console.WriteLine($"✅ GetMultipleAsync completed - Retrieved {results.Count} items");

        // Verify results
        foreach (var kvp in results)
        {
            var expectedValue = System.Text.Encoding.UTF8.GetString(testData[kvp.Key].value);
            var actualValue = System.Text.Encoding.UTF8.GetString(kvp.Value!);
            if (expectedValue == actualValue)
            {
                Console.WriteLine($"✅ {kvp.Key}: {actualValue} (correct)");
            }
            else
            {
                Console.WriteLine($"❌ {kvp.Key}: Expected '{expectedValue}', got '{actualValue}'");
                throw new Exception("Batch operation verification failed!");
            }
        }

        Console.WriteLine("🔄 Testing RemoveMultipleAsync...");
        var removeCount = await GlacialCache.RemoveMultipleAsync(new[] { "key1", "key2" });
        Console.WriteLine($"✅ RemoveMultipleAsync completed - Removed {removeCount} items");

        Console.WriteLine("🔄 Testing RefreshMultipleAsync...");
        var refreshCount = await GlacialCache.RefreshMultipleAsync(new[] { "key3" });
        Console.WriteLine($"✅ RefreshMultipleAsync completed - Refreshed {refreshCount} items");

        // Test bulk operations (scoped connection)
        Console.WriteLine("🔄 Testing Bulk Operations...");

        await GlacialCache.SetAsync("bulk-key1", System.Text.Encoding.UTF8.GetBytes("bulk-value1"),
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) });

        var bulkResult = await GlacialCache.GetAsync("bulk-key1");
        var bulkValue = System.Text.Encoding.UTF8.GetString(bulkResult!);

        if (bulkValue == "bulk-value1")
        {
            Console.WriteLine($"✅ Bulk operations: {bulkValue} (correct)");
        }
        else
        {
            throw new Exception("Bulk operation verification failed!");
        }

        // Test bulk with batch operations using unified interface
        Console.WriteLine("🔄 Testing Bulk + Batch combination...");
        var bulkBatchData = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>
        {
            ["bulk-batch-1"] = (System.Text.Encoding.UTF8.GetBytes("bulk-batch-value1"), new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) }),
            ["bulk-batch-2"] = (System.Text.Encoding.UTF8.GetBytes("bulk-batch-value2"), new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) })
        };

        await GlacialCache.SetMultipleAsync(bulkBatchData);
        var bulkBatchResults = await GlacialCache.GetMultipleAsync(bulkBatchData.Keys);
        Console.WriteLine($"✅ Bulk + Batch operations completed - Retrieved {bulkBatchResults.Count} items");

        Console.WriteLine("🎉 All batch operations tests passed!");
        Console.WriteLine("🚀 Ready to run benchmarks!");
    }
}