using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL;
using GlacialCache.PostgreSQL.Extensions;
namespace GlacialCache.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class GlacialCacheBenchmarks
{
    private PostgreSqlContainer _postgres = null!;
    private IDistributedCache _cache = null!;
    private IServiceProvider _serviceProvider = null!;
    private readonly Random _random = new();
    private readonly string[] _testKeys = new string[1000];
    private readonly byte[][] _testValues = new byte[1000][];

    [Params(1, 10, 50)]
    public int Parallelism { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Setup PostgreSQL container
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("benchdb")
            .WithUsername("benchuser")
            .WithPassword("benchpass")
            .WithCleanUp(true)
            .Build();

        _postgres.StartAsync().GetAwaiter().GetResult();

        // Setup services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Maintenance.CleanupInterval = TimeSpan.FromHours(1); // Reduce cleanup frequency for benchmarks
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        });

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<IDistributedCache>();

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

        for (int i = 0; i < _testKeys.Length; i++)
        {
            _cache.SetAsync(_testKeys[i], _testValues[i], populateOptions).GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _postgres?.DisposeAsync().GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task SetAsync()
    {
        var key = GetRandomKey();
        var value = GenerateRandomValue();
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };

        await _cache.SetAsync(key, value, options);
    }

    [Benchmark]
    public async Task GetAsync()
    {
        var key = GetRandomExistingKey();
        var result = await _cache.GetAsync(key);

        // Ensure the result is consumed to prevent optimization
        if (result == null)
        {
            throw new InvalidOperationException("Expected to find cached value");
        }
    }

    [Benchmark]
    public async Task SetAsync_Parallel()
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

            tasks[i] = _cache.SetAsync(key, value, options);
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task GetAsync_Parallel()
    {
        var tasks = new Task<byte[]?>[Parallelism];

        for (int i = 0; i < Parallelism; i++)
        {
            var key = GetRandomExistingKey();
            tasks[i] = _cache.GetAsync(key);
        }

        var results = await Task.WhenAll(tasks);

        // Ensure all results are consumed to prevent optimization
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


