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
/// Benchmarks comparing scoped connection strategy vs connection pool strategy
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ConnectionStrategyBenchmarks
{
    private PostgreSqlContainer _postgres = null!;
    private IGlacialCache _glacialCache = null!;
    private IServiceProvider _serviceProvider = null!;
    private readonly Random _random = new();
    private readonly string[] _testKeys = new string[1000];
    private readonly byte[][] _testValues = new byte[1000][];

    [Params(50)]
    public int OperationsPerScope { get; set; }

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
            options.Maintenance.CleanupInterval = TimeSpan.FromHours(1);
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        });

        _serviceProvider = services.BuildServiceProvider();
        _glacialCache = _serviceProvider.GetRequiredService<IGlacialCache>();

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
            _glacialCache.SetAsync(_testKeys[i], _testValues[i], populateOptions).GetAwaiter().GetResult();
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

    /// <summary>
    /// Strategy 1: Connection Pool - Each operation gets its own connection from the pool
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Connection Pool")]
    public async Task ConnectionPool_IndividualOperations()
    {
        for (int i = 0; i < OperationsPerScope; i++)
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
    }

    /// <summary>
    /// Strategy 2: Scoped Connection - All operations share the same connection
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Scoped Connection")]
    public async Task ScopedConnection_SharedOperations()
    {
        for (int i = 0; i < OperationsPerScope; i++)
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
    }

    /// <summary>
    /// Strategy 3: Connection Pool - Mixed Get/Set operations
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Connection Pool")]
    public async Task ConnectionPool_MixedOperations()
    {
        for (int i = 0; i < OperationsPerScope; i++)
        {
            if (i % 2 == 0)
            {
                // Set operation
                var key = GetRandomKey();
                var value = GenerateRandomValue();
                var options = new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                };

                await _glacialCache.SetAsync(key, value, options);
            }
            else
            {
                // Get operation
                var key = GetRandomExistingKey();
                await _glacialCache.GetAsync(key);
            }
        }
    }

    /// <summary>
    /// Strategy 4: Scoped Connection - Mixed Get/Set operations
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Scoped Connection")]
    public async Task ScopedConnection_MixedOperations()
    {
        for (int i = 0; i < OperationsPerScope; i++)
        {
            if (i % 2 == 0)
            {
                // Set operation
                var key = GetRandomKey();
                var value = GenerateRandomValue();
                var options = new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                };

                await _glacialCache.SetAsync(key, value, options);
            }
            else
            {
                // Get operation
                var key = GetRandomExistingKey();
                await _glacialCache.GetAsync(key);
            }
        }
    }

    /// <summary>
    /// Strategy 5: Connection Pool - Read-heavy workload
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Connection Pool")]
    public async Task ConnectionPool_ReadHeavy()
    {
        for (int i = 0; i < OperationsPerScope; i++)
        {
            var key = GetRandomExistingKey();
            await _glacialCache.GetAsync(key);
        }
    }

    /// <summary>
    /// Strategy 6: Scoped Connection - Read-heavy workload
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Scoped Connection")]
    public async Task ScopedConnection_ReadHeavy()
    {
        for (int i = 0; i < OperationsPerScope; i++)
        {
            var key = GetRandomExistingKey();
            await _glacialCache.GetAsync(key);
        }
    }

    /// <summary>
    /// Strategy 7: Connection Pool - Write-heavy workload
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Connection Pool")]
    public async Task ConnectionPool_WriteHeavy()
    {
        for (int i = 0; i < OperationsPerScope; i++)
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
    }

    /// <summary>
    /// Strategy 8: Scoped Connection - Write-heavy workload
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Scoped Connection")]
    public async Task ScopedConnection_WriteHeavy()
    {
        for (int i = 0; i < OperationsPerScope; i++)
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
    }

    /// <summary>
    /// Strategy 9: Batch Operations - Using the batch interface
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch Operations")]
    public async Task BatchOperations_MultipleSets()
    {
        var entries = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>();

        for (int i = 0; i < OperationsPerScope; i++)
        {
            var key = GetRandomKey();
            var value = GenerateRandomValue();
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            };

            entries[key] = (value, options);
        }

        await _glacialCache.SetMultipleAsync(entries);
    }

    /// <summary>
    /// Strategy 10: Batch Operations - Using the bulk operations interface
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Batch Operations")]
    public async Task BulkOperations_MultipleSets()
    {
        var entries = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>();

        for (int i = 0; i < OperationsPerScope; i++)
        {
            var key = GetRandomKey();
            var value = GenerateRandomValue();
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            };

            entries[key] = (value, options);
        }

        await _glacialCache.SetMultipleAsync(entries);
    }

    private string GetRandomKey()
    {
        return $"benchmark-key-{Guid.NewGuid():N}";
    }

    private string GetRandomExistingKey()
    {
        return _testKeys[_random.Next(_testKeys.Length)];
    }

    private byte[] GenerateRandomValue()
    {
        var size = _random.Next(100, 1024); // 100B to 1KB
        var value = new byte[size];
        _random.NextBytes(value);
        return value;
    }
}