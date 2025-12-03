using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;

namespace GlacialCache.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GlacialCacheBatchBenchmarks
{
    private PostgreSqlContainer _postgres = null!;
    private IGlacialCache _glacialCache = null!;
    private IServiceProvider _serviceProvider = null!;
    private readonly Random _random = new();

    // Test data for batch operations
    private readonly Dictionary<string, byte[]> _batchTestData = new();
    private readonly Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> _batchSetData = new();
    private readonly string[] _batchKeys = new string[100];

    [Params(5, 10, 25, 50, 100)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup PostgreSQL container
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("batchbench")
            .WithUsername("benchuser")
            .WithPassword("benchpass")
            .WithCleanUp(true)
            .Build();

        _postgres.StartAsync().GetAwaiter().GetResult();

        // Setup GlacialCache services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "glacial_batch";
            options.Cache.TableName = "cache_entries";
            options.Maintenance.CleanupInterval = TimeSpan.FromHours(1);
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        });

        _serviceProvider = services.BuildServiceProvider();
        _glacialCache = _serviceProvider.GetRequiredService<IGlacialCache>();

        // Pre-generate test data
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(30)
        };

        for (int i = 0; i < 100; i++)
        {
            var key = $"batch-key-{i:D3}";
            var value = GenerateRandomValue();

            _batchKeys[i] = key;
            _batchTestData[key] = value;
            _batchSetData[key] = (value, options);
        }

        // Pre-populate some data for batch Get benchmarks
        for (int i = 0; i < 50; i++)
        {
            var key = _batchKeys[i];
            _glacialCache.SetAsync(key, _batchTestData[key], options).GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _postgres?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private byte[] GenerateRandomValue()
    {
        var size = _random.Next(100, 1000); // 100B to 1KB
        var buffer = new byte[size];
        _random.NextBytes(buffer);
        return buffer;
    }

    #region Individual Operations (Baseline)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Individual")]
    public async Task Individual_SetAsync()
    {
        var keys = _batchKeys.Take(BatchSize).ToArray();
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) };

        var tasks = keys.Select(async key =>
        {
            await _glacialCache.SetAsync($"individual-set-{key}", _batchTestData[key], options);
        });

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Individual")]
    public async Task Individual_GetAsync()
    {
        var keys = _batchKeys.Take(BatchSize).ToArray();

        var tasks = keys.Select(async key =>
        {
            return await _glacialCache.GetAsync(key);
        });

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Individual")]
    public async Task Individual_RemoveAsync()
    {
        var keys = _batchKeys.Take(BatchSize).ToArray();

        var tasks = keys.Select(async key =>
        {
            await _glacialCache.RemoveAsync($"individual-remove-{key}");
        });

        await Task.WhenAll(tasks);
    }

    #endregion

    #region Batch Operations

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_SetMultipleAsync()
    {
        var batchData = _batchKeys.Take(BatchSize)
            .ToDictionary(
                key => $"batch-set-{key}",
                key => _batchSetData[key]);

        await _glacialCache.SetMultipleAsync(batchData);
    }

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_GetMultipleAsync()
    {
        var keys = _batchKeys.Take(BatchSize);

        await _glacialCache.GetMultipleAsync(keys);
    }

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_RemoveMultipleAsync()
    {
        var keys = _batchKeys.Take(BatchSize).Select(key => $"batch-remove-{key}");

        await _glacialCache.RemoveMultipleAsync(keys);
    }

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_RefreshMultipleAsync()
    {
        var keys = _batchKeys.Take(BatchSize);

        await _glacialCache.RefreshMultipleAsync(keys);
    }

    #endregion

    #region Bulk Operations (Scoped Connection)

    [Benchmark]
    [BenchmarkCategory("Bulk")]
    public async Task Bulk_SetAsync()
    {
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) };

        var keys = _batchKeys.Take(BatchSize).ToArray();

        var tasks = keys.Select(async key =>
        {
            await _glacialCache.SetAsync($"bulk-set-{key}", _batchTestData[key], options);
        });

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Bulk")]
    public async Task Bulk_GetAsync()
    {
        var keys = _batchKeys.Take(BatchSize).ToArray();

        var tasks = keys.Select(async key =>
        {
            return await _glacialCache.GetAsync(key);
        });

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Bulk")]
    public async Task Bulk_BatchOperations()
    {
        // Use the unified interface for both individual and batch operations
        var batchData = _batchKeys.Take(BatchSize)
            .ToDictionary(
                key => $"bulk-batch-{key}",
                key => _batchSetData[key]);

        await _glacialCache.SetMultipleAsync(batchData);

        var keys = _batchKeys.Take(BatchSize);
        await _glacialCache.GetMultipleAsync(keys);
    }

    #endregion
}