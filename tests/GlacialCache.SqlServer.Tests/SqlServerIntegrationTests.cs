using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using GlacialCache.Abstractions;
using MemoryPack;

namespace GlacialCache.SqlServer.Tests;

public sealed class SqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task Canonical_typed_entry_and_batch_operations_round_trip_metadata()
    {
        var connectionString = Environment.GetEnvironmentVariable("GLACIALCACHE_SQLSERVER_TEST_CONNECTION")!;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGlacialCacheSqlServer(options =>
        {
            options.ConnectionString = connectionString;
            options.TableName = $"glacial_typed_{Environment.ProcessId}";
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IGlacialCache>();
        var absolute = DateTimeOffset.UtcNow.AddMinutes(10);
        await cache.SetEntryAsync(new CacheEntry<TypedPayload>
        {
            Key = "typed:one", Value = new TypedPayload(42, "forty-two"),
            AbsoluteExpiration = absolute, SlidingExpiration = TimeSpan.FromMinutes(2)
        });

        var result = await cache.GetEntryAsync<TypedPayload>("typed:one");
        Assert.NotNull(result);
        Assert.Equal(42, result.Value.Number);
        Assert.Equal(typeof(TypedPayload).FullName, result.BaseType);
        Assert.Equal(TimeSpan.FromMinutes(2), result.SlidingExpiration);

        await cache.SetMultipleEntriesAsync(new Dictionary<string, (TypedPayload value, DistributedCacheEntryOptions? options)>
        {
            ["typed:A"] = (new TypedPayload(1, "A"), new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(1) }),
            ["typed:a"] = (new TypedPayload(2, "a"), null)
        });
        var batch = await cache.GetMultipleEntriesAsync<TypedPayload>(new[] { "typed:A", "typed:a" });
        Assert.Equal(1, batch["typed:A"]!.Value.Number);
        Assert.Equal(2, batch["typed:a"]!.Value.Number);
        Assert.Null(await cache.GetEntryAsync<string>("typed:A"));
    }

    [SqlServerFact]
    public async Task Core_and_batch_operations_preserve_case_sensitive_keys_and_expiration()
    {
        var connectionString = Environment.GetEnvironmentVariable("GLACIALCACHE_SQLSERVER_TEST_CONNECTION")!;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGlacialCacheSqlServer(options =>
        {
            options.ConnectionString = connectionString;
            options.TableName = $"glacial_cache_test_{Environment.ProcessId}".PadRight(128, 'x');
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<global::GlacialCache.Abstractions.IGlacialCache>();
        var ordinary = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) };

        await cache.SetAsync("Case", Encoding.UTF8.GetBytes("upper"), ordinary);
        await cache.SetAsync("case", Encoding.UTF8.GetBytes("lower"), ordinary);
        Assert.Equal("upper", Encoding.UTF8.GetString((await cache.GetAsync("Case"))!));
        Assert.Equal("lower", Encoding.UTF8.GetString((await cache.GetAsync("case"))!));

        var batch = Enumerable.Range(0, 1100).ToDictionary(
            index => $"batch-{index}",
            index => (Encoding.UTF8.GetBytes(index.ToString()), ordinary));
        await cache.SetMultipleAsync(batch);
        var fetched = await cache.GetMultipleAsync(batch.Keys);
        Assert.Equal(1100, fetched.Count);
        Assert.Equal("1099", Encoding.UTF8.GetString(fetched["batch-1099"]!));

        Assert.Equal(1100, await cache.RefreshMultipleAsync(batch.Keys));
        Assert.Equal(1100, await cache.RemoveMultipleAsync(batch.Keys));
        Assert.Empty(await cache.GetMultipleAsync(batch.Keys));

        await cache.SetAsync("expired", new byte[] { 1 }, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(100)
        });
        await Task.Delay(250);
        Assert.Null(await cache.GetAsync("expired"));
    }
}

[MemoryPackable]
public partial record TypedPayload(int Number, string Text);

internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GLACIALCACHE_SQLSERVER_TEST_CONNECTION")))
            Skip = "Set GLACIALCACHE_SQLSERVER_TEST_CONNECTION to run SQL Server integration tests.";
    }
}
