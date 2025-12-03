using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
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
        var _cacheContext = _sloopServiceProvider.GetRequiredService<IDbCacheContext>();
        _cacheContext.MigrateAsync().GetAwaiter().GetResult();



        // Pre-generate test data
        for (int i = 0; i < _testKeys.Length; i++)
        {
            _testKeys[i] = $"benchmark-key-{i:D6}";
            _testValues[i] = GenerateRandomValue();
        }

        // Pre-populate some data for Get benchmarks
        var populateOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(30)
        };

        // Populate GlacialCache
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


    // GlacialCache benchmarks
    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task FrostCache_SetAsync()
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

    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task FrostCache_GetAsync()
    {
        var key = GetRandomExistingKey();
        var result = await _glacialCache.GetAsync(key);

        if (result == null)
        {
            throw new InvalidOperationException("Expected to find cached value");
        }
    }

    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task FrostCache_SetAsync_Parallel()
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

    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task FrostCache_SetAsync_Scooped_Parallel()
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

    [Benchmark]
    [BenchmarkCategory("GlacialCache")]
    public async Task FrostCache_GetAsync_Parallel()
    {
        var tasks = new Task<byte[]?>[Parallelism];

        // Use sequential keys to ensure unique keys for each parallel operation
        for (int i = 0; i < Parallelism; i++)
        {
            var key = _testKeys[i % _testKeys.Length]; // Use modulo to cycle through available keys
            tasks[i] = _glacialCache.GetAsync(key);
        }

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            if (result == null)
            {
                throw new InvalidOperationException("Expected to find cached value");
            }
        }
    }


    [Benchmark]
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

    [Benchmark]
    [BenchmarkCategory("Sloop")]
    public async Task Sloop_GetAsync()
    {
        var key = GetRandomExistingKey();
        var result = await _sloopCache.GetAsync(key);

        if (result == null)
        {
            throw new InvalidOperationException("Expected to find cached value");
        }
    }

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

        foreach (var result in results)
        {
            if (result == null)
            {
                throw new InvalidOperationException("Expected to find cached value");
            }
        }
    }


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



