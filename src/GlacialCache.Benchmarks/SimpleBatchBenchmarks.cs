using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
namespace GlacialCache.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SimpleBatchBenchmarks
{
    private PostgreSqlContainer _postgres = null!;
    private IGlacialCache _glacialCache = null!;
    private IServiceProvider _serviceProvider = null!;
    private readonly Random _random = new();

    // Test data - keep it small and simple
    private readonly Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> _testData10 = new();
    private readonly Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> _testData25 = new();
    private readonly string[] _keys10 = new string[10];
    private readonly string[] _keys25 = new string[25];

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine("🚀 Setting up Simple Batch Benchmarks...");

        // Setup PostgreSQL container with optimized settings
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("simplebench")
            .WithUsername("benchuser")
            .WithPassword("benchpass")
            .WithCleanUp(true)
            .Build();

        _postgres.StartAsync().GetAwaiter().GetResult();
        Console.WriteLine("✅ PostgreSQL container ready");

        // Setup GlacialCache with optimized connection pool
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Error)); // Reduce logging overhead
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.SchemaName = "simple";
            options.Cache.TableName = "cache_entries";
            options.Maintenance.CleanupInterval = TimeSpan.FromHours(24); // Reduce cleanup frequency
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromHours(1);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
        });

        _serviceProvider = services.BuildServiceProvider();
        _glacialCache = _serviceProvider.GetRequiredService<IGlacialCache>();

        // Initialize database schema
        _glacialCache.SetAsync("init", new byte[] { 1 }, new DistributedCacheEntryOptions()).GetAwaiter().GetResult();
        _glacialCache.RemoveAsync("init").GetAwaiter().GetResult();
        Console.WriteLine("✅ Database schema initialized");

        // Pre-generate test data (keep it simple)
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) };

        for (int i = 0; i < 10; i++)
        {
            var key = $"key-{i:D2}";
            var value = GenerateSmallValue();
            _keys10[i] = key;
            _testData10[key] = (value, options);
        }

        for (int i = 0; i < 25; i++)
        {
            var key = $"key-{i:D2}";
            var value = GenerateSmallValue();
            _keys25[i] = key;
            _testData25[key] = (value, options);
        }

        // Pre-populate some data for Get benchmarks
        _glacialCache.SetAsync(_keys10[0], _testData10[_keys10[0]].value, _testData10[_keys10[0]].options).GetAwaiter().GetResult();
        _glacialCache.SetAsync(_keys25[0], _testData25[_keys25[0]].value, _testData25[_keys25[0]].options).GetAwaiter().GetResult();

        Console.WriteLine("✅ Test data prepared");
        Console.WriteLine("🎯 Ready for benchmarks!");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine("🧹 Cleaning up...");
        (_serviceProvider as IDisposable)?.Dispose();
        _postgres?.DisposeAsync().GetAwaiter().GetResult();
        Console.WriteLine("✅ Cleanup complete");
    }

    private byte[] GenerateSmallValue()
    {
        var size = _random.Next(50, 200); // Small values: 50-200 bytes
        var buffer = new byte[size];
        _random.NextBytes(buffer);
        return buffer;
    }

    #region Individual Operations (Baseline)

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Individual")]
    public async Task Individual_Set_10_Keys()
    {
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) };
        var tasks = new Task[10];

        for (int i = 0; i < 10; i++)
        {
            var key = $"ind-set-{i}";
            var value = _testData10[_keys10[i]].value;
            tasks[i] = _glacialCache.SetAsync(key, value, options);
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Individual")]
    public async Task Individual_Get_10_Keys()
    {
        var tasks = new Task<byte[]?>[10];

        for (int i = 0; i < 10; i++)
        {
            tasks[i] = _glacialCache.GetAsync(_keys10[i]);
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Individual")]
    public async Task Individual_Set_25_Keys()
    {
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) };
        var tasks = new Task[25];

        for (int i = 0; i < 25; i++)
        {
            var key = $"ind-set-{i}";
            var value = _testData25[_keys25[i]].value;
            tasks[i] = _glacialCache.SetAsync(key, value, options);
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Individual")]
    public async Task Individual_Get_25_Keys()
    {
        var tasks = new Task<byte[]?>[25];

        for (int i = 0; i < 25; i++)
        {
            tasks[i] = _glacialCache.GetAsync(_keys25[i]);
        }

        await Task.WhenAll(tasks);
    }

    #endregion

    #region Batch Operations

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_Set_10_Keys()
    {
        await _glacialCache.SetMultipleAsync(_testData10);
    }

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_Get_10_Keys()
    {
        await _glacialCache.GetMultipleAsync(_keys10);
    }

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_Set_25_Keys()
    {
        await _glacialCache.SetMultipleAsync(_testData25);
    }

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch_Get_25_Keys()
    {
        await _glacialCache.GetMultipleAsync(_keys25);
    }

    #endregion

    #region Bulk Operations (Scoped Connection)

    [Benchmark]
    [BenchmarkCategory("Bulk")]
    public async Task Bulk_Set_10_Keys()
    {
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) };
        var tasks = new Task[10];

        for (int i = 0; i < 10; i++)
        {
            var key = $"bulk-set-{i}";
            var value = _testData10[_keys10[i]].value;
            tasks[i] = _glacialCache.SetAsync(key, value, options);
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Bulk")]
    public async Task Bulk_Get_10_Keys()
    {
        var tasks = new Task<byte[]?>[10];

        for (int i = 0; i < 10; i++)
        {
            tasks[i] = _glacialCache.GetAsync(_keys10[i]);
        }

        await Task.WhenAll(tasks);
    }

    #endregion
}