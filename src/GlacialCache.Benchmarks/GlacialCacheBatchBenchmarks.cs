using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;

namespace GlacialCache.Benchmarks;

/// <summary>
/// Benchmarks comparing batch operations (SetMultipleAsync/GetMultipleAsync) vs parallel individual operations.
/// Batch operations use a single connection with NpgsqlBatch for efficient multi-operation execution.
/// Individual operations use parallel SetAsync/GetAsync calls with Task.WhenAll.
/// </summary>
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

    // Test data for batch operations - pre-generated for all batch sizes
    private readonly Dictionary<string, byte[]> _batchTestData = new();
    private readonly Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> _batchSetData = new();
    private readonly string[] _batchKeys = new string[500]; // Support up to batch size 500

    /// <summary>
    /// Batch size parameter: 10 (small), 50 (medium), 100 (large), 500 (very large)
    /// </summary>
    [Params(10, 50, 100, 500)]
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
            options.Infrastructure.CreateInfrastructure = true;
            options.Maintenance.CleanupInterval = TimeSpan.FromHours(1);
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        });

        _serviceProvider = services.BuildServiceProvider();
        _glacialCache = _serviceProvider.GetRequiredService<IGlacialCache>();

        // Initialize database schema
        _glacialCache.SetAsync("init-key", new byte[] { 1 }, new DistributedCacheEntryOptions()).GetAwaiter().GetResult();
        _glacialCache.RemoveAsync("init-key").GetAwaiter().GetResult();

        // Pre-generate test data for all batch sizes (up to 500)
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(30)
        };

        for (int i = 0; i < _batchKeys.Length; i++)
        {
            var key = $"batch-key-{i:D3}";
            var value = GenerateRandomValue();

            _batchKeys[i] = key;
            _batchTestData[key] = value;
            _batchSetData[key] = (value, options);
        }

        // Pre-populate data for Get benchmarks (populate all keys that might be used)
        for (int i = 0; i < _batchKeys.Length; i++)
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

    /// <summary>
    /// Baseline: Parallel individual SetAsync operations using Task.WhenAll
    /// </summary>
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

    /// <summary>
    /// Baseline: Parallel individual GetAsync operations using Task.WhenAll
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Individual")]
    public async Task Individual_GetAsync()
    {
        var keys = _batchKeys.Take(BatchSize).ToArray();

        var tasks = keys.Select(async key =>
        {
            return await _glacialCache.GetAsync(key);
        });

        var results = await Task.WhenAll(tasks);

        // Consume results to prevent optimization
        foreach (var result in results)
        {
            if (result == null)
            {
                throw new InvalidOperationException("Expected to find cached value");
            }
        }
    }

    /// <summary>
    /// Baseline: Parallel individual RemoveAsync operations using Task.WhenAll
    /// </summary>
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

    /// <summary>
    /// Batch operation: SetMultipleAsync uses a single connection with NpgsqlBatch
    /// This should be significantly faster than parallel individual operations for larger batch sizes
    /// </summary>
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

    /// <summary>
    /// Batch operation: GetMultipleAsync uses a single connection with optimized query
    /// This should be significantly faster than parallel individual operations for larger batch sizes
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_GetMultipleAsync()
    {
        var keys = _batchKeys.Take(BatchSize);

        var results = await _glacialCache.GetMultipleAsync(keys);

        // Consume results to prevent optimization
        if (results.Count != BatchSize)
        {
            throw new InvalidOperationException($"Expected {BatchSize} results, got {results.Count}");
        }

        foreach (var result in results.Values)
        {
            if (result == null)
            {
                throw new InvalidOperationException("Expected to find cached value");
            }
        }
    }

    /// <summary>
    /// Batch operation: RemoveMultipleAsync uses a single connection with optimized query
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_RemoveMultipleAsync()
    {
        var keys = _batchKeys.Take(BatchSize).Select(key => $"batch-remove-{key}");

        var removedCount = await _glacialCache.RemoveMultipleAsync(keys);

        // Consume result to prevent optimization
        if (removedCount < 0)
        {
            throw new InvalidOperationException("Invalid remove count");
        }
    }

    /// <summary>
    /// Batch operation: RefreshMultipleAsync uses a single connection with optimized query
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_RefreshMultipleAsync()
    {
        var keys = _batchKeys.Take(BatchSize);

        var refreshedCount = await _glacialCache.RefreshMultipleAsync(keys);

        // Consume result to prevent optimization
        if (refreshedCount < 0)
        {
            throw new InvalidOperationException("Invalid refresh count");
        }
    }

    #endregion
}
