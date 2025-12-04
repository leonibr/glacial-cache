using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using Sloop.Extensions;
using Sloop.Abstractions;

namespace GlacialCache.Benchmarks;

/// <summary>
/// Head-to-head comparison benchmarks between GlacialCache and Sloop.
/// Both implementations use the same PostgreSQL database with identical test conditions.
/// Tests individual operations and parallel operations at different concurrency levels.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GlacialCacheVsSloopBenchmarks
{
    private PostgreSqlContainer _postgres = null!;
    private IGlacialCache _glacialCache = null!;
    private IDistributedCache _sloopCache = null!;
    private IServiceProvider _glacialServiceProvider = null!;
    private IServiceProvider _sloopServiceProvider = null!;
    private readonly Random _random = new();
    private readonly string[] _testKeys = new string[1000];
    private readonly byte[][] _testValues = new byte[1000][];

    /// <summary>
    /// Parallelism parameter: 1 (sequential), 10 (medium concurrency), 50 (high concurrency)
    /// </summary>
    [Params(1, 10, 50)]
    public int Parallelism { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup shared PostgreSQL container
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("benchdb")
            .WithUsername("benchuser")
            .WithPassword("benchpass")
            .WithCleanUp(true)
            .Build();

        _postgres.StartAsync().GetAwaiter().GetResult();

        // Setup GlacialCache services
        var glacialServices = new ServiceCollection();
        glacialServices.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        glacialServices.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "glacial";
            options.Cache.TableName = "cache_entries";
            options.Maintenance.CleanupInterval = TimeSpan.FromHours(1);
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            options.Infrastructure.CreateInfrastructure = true;
            options.Infrastructure.EnableManagerElection = false;
            options.Resilience.EnableResiliencePatterns = false;
        });

        _glacialServiceProvider = glacialServices.BuildServiceProvider();
        _glacialCache = _glacialServiceProvider.GetRequiredService<IGlacialCache>();

        // Initialize GlacialCache database schema by doing a simple operation first
        _glacialCache.SetAsync("init-key", new byte[] { 1 }, new DistributedCacheEntryOptions()).GetAwaiter().GetResult();
        _glacialCache.RemoveAsync("init-key").GetAwaiter().GetResult();

        // Setup Sloop services
        var sloopServices = new ServiceCollection();
        sloopServices.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        sloopServices.AddCache(options =>
        {
            options.UseConnectionString(_postgres.GetConnectionString());
            options.SchemaName = "sloop";
            options.TableName = "cache_entries";
            options.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
            options.DefaultAbsoluteExpiration = TimeSpan.FromHours(1);
            options.CleanupInterval = TimeSpan.FromHours(1);
            options.CreateInfrastructure = true;
        });

        _sloopServiceProvider = sloopServices.BuildServiceProvider();
        _sloopCache = _sloopServiceProvider.GetRequiredService<IDistributedCache>();
        var cacheContext = _sloopServiceProvider.GetRequiredService<IDbCacheContext>();
        cacheContext.MigrateAsync().GetAwaiter().GetResult();

        // Pre-generate test data
        for (int i = 0; i < _testKeys.Length; i++)
        {
            _testKeys[i] = $"benchmark-key-{i:D6}";
            _testValues[i] = GenerateRandomValue();
        }

        // Pre-populate data for Get benchmarks
        var populateOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(30)
        };

        // Populate both caches with identical data
        for (int i = 0; i < _testKeys.Length; i++)
        {
            _glacialCache.SetAsync(_testKeys[i], _testValues[i], populateOptions).GetAwaiter().GetResult();
            _sloopCache.SetAsync(_testKeys[i], _testValues[i], populateOptions).GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_glacialServiceProvider is IDisposable glacialDisposable)
        {
            glacialDisposable.Dispose();
        }

        if (_sloopServiceProvider is IDisposable sloopDisposable)
        {
            sloopDisposable.Dispose();
        }

        _postgres?.DisposeAsync().GetAwaiter().GetResult();
    }

    #region GlacialCache Benchmarks

    /// <summary>
    /// Baseline: GlacialCache single SetAsync operation
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GlacialCache")]
    public async Task GlacialCache_SetAsync()
    {
        var key = GetRandomKey();
        var value = GenerateRandomValue();
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };

        await _glacialCache.SetAsync(key, value, options);
    }

    /// <summary>
    /// GlacialCache single GetAsync operation
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task GlacialCache_GetAsync()
    {
        var key = GetRandomExistingKey();
        var result = await _glacialCache.GetAsync(key);

        // Consume result to prevent optimization
        if (result == null)
        {
            throw new InvalidOperationException("Expected to find cached value");
        }
    }

    /// <summary>
    /// GlacialCache parallel SetAsync operations using Task.WhenAll
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task GlacialCache_SetAsync_Parallel()
    {
        var tasks = new Task[Parallelism];

        for (int i = 0; i < Parallelism; i++)
        {
            var key = GetRandomKey();
            var value = GenerateRandomValue();
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            };

            tasks[i] = _glacialCache.SetAsync(key, value, options);
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// GlacialCache batch operation: SetMultipleAsync uses a single connection with NpgsqlBatch
    /// This is more efficient than parallel individual operations for multiple items
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task GlacialCache_SetMultipleAsync()
    {
        var entries = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>();

        // Use sequential keys to avoid duplicates
        for (int i = 0; i < Parallelism; i++)
        {
            var key = $"benchmark-key-{i:D6}";
            var value = GenerateRandomValue();
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            };
            entries.Add(key, (value, options));
        }

        await _glacialCache.SetMultipleAsync(entries);
    }

    /// <summary>
    /// GlacialCache parallel GetAsync operations using Task.WhenAll
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task GlacialCache_GetAsync_Parallel()
    {
        var tasks = new Task<byte[]?>[Parallelism];

        // Use sequential keys to ensure unique keys for each parallel operation
        for (int i = 0; i < Parallelism; i++)
        {
            var key = _testKeys[i % _testKeys.Length]; // Use modulo to cycle through available keys
            tasks[i] = _glacialCache.GetAsync(key);
        }

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

    #endregion

    #region Sloop Benchmarks

    /// <summary>
    /// Baseline: Sloop single SetAsync operation
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Sloop")]
    public async Task Sloop_SetAsync()
    {
        var key = GetRandomKey();
        var value = GenerateRandomValue();
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };

        await _sloopCache.SetAsync(key, value, options);
    }

    /// <summary>
    /// Sloop single GetAsync operation
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sloop")]
    public async Task Sloop_GetAsync()
    {
        var key = GetRandomExistingKey();
        var result = await _sloopCache.GetAsync(key);

        // Consume result to prevent optimization
        if (result == null)
        {
            throw new InvalidOperationException("Expected to find cached value");
        }
    }

    /// <summary>
    /// Sloop parallel SetAsync operations using Task.WhenAll
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sloop")]
    public async Task Sloop_SetAsync_Parallel()
    {
        var tasks = new Task[Parallelism];

        for (int i = 0; i < Parallelism; i++)
        {
            var key = GetRandomKey();
            var value = GenerateRandomValue();
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            };

            tasks[i] = _sloopCache.SetAsync(key, value, options);
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sloop parallel GetAsync operations using Task.WhenAll
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Sloop")]
    public async Task Sloop_GetAsync_Parallel()
    {
        var tasks = new Task<byte[]?>[Parallelism];

        for (int i = 0; i < Parallelism; i++)
        {
            var key = GetRandomExistingKey();
            tasks[i] = _sloopCache.GetAsync(key);
        }

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

    #endregion

    private string GetRandomKey()
    {
        return $"benchmark-key-{_random.Next(0, 1000000):D6}";
    }

    private string GetRandomExistingKey()
    {
        return _testKeys[_random.Next(0, _testKeys.Length)];
    }

    private byte[] GenerateRandomValue()
    {
        var size = _random.Next(100, 1000); // Random size between 100B and 1KB
        var value = new byte[size];
        _random.NextBytes(value);
        return value;
    }
}
