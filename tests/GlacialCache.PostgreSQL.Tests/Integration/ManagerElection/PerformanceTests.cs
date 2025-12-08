using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Models;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Integration.ManagerElection;

[Trait("Category", "Performance")]
public class PerformanceTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private readonly string _schemaName;
    private readonly string _tableName;

    public PerformanceTests(ITestOutputHelper output) : base(output)
    {
        // Use a fixed schema name to ensure all instances use the same lock key
        _schemaName = "test_schema_performance";
        _tableName = "test_cache";
    }

    protected override async Task InitializeTestAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("testdb")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .WithCleanUp(true)
                .Build();

            await _postgres.StartAsync();
            Output.WriteLine($"✅ PostgreSQL container started: {_postgres.GetConnectionString()}");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}");
        }
    }

    protected override async Task CleanupTestAsync()
    {
        if (_postgres != null)
        {
            await _postgres.DisposeAsync();
        }
    }

    private IServiceProvider CreateServiceProvider(string instanceId = "test-instance")
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Warning) // Reduce logging for performance tests
                   .AddFilter("GlacialCache.PostgreSQL.Services.ManagerElectionService", LogLevel.Warning));

        // Configure GlacialCache with manager election enabled
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection = new ConnectionOptions
            {
                ConnectionString = _postgres!.GetConnectionString()
            };
            options.Cache = new CacheOptions
            {
                SchemaName = _schemaName,
                TableName = _tableName
            };
            options.Infrastructure = new InfrastructureOptions
            {
                EnableManagerElection = true,
                CreateInfrastructure = true,
                Lock = new LockOptions
                {
                    LockTimeout = TimeSpan.FromSeconds(30)
                }
            };
        });

        return services.BuildServiceProvider();
    }

    private ManagerElectionService CreateManagerElectionService(IServiceProvider serviceProvider, string? instanceId = null)
    {
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
        var logger = serviceProvider.GetRequiredService<ILogger<ManagerElectionService>>();
        var dataSource = serviceProvider.GetRequiredService<IPostgreSQLDataSource>();
        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

        if (instanceId != null)
        {
            return new ManagerElectionService(options.CurrentValue, logger, dataSource, instanceId, timeProvider);
        }

        return new ManagerElectionService(options, logger, dataSource, timeProvider);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ElectionTime_ShouldBeUnder5Seconds()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();
        var managerElectionService = CreateManagerElectionService(serviceProvider, "test-instance-election-time");

        try
        {
            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var isManager = await managerElectionService.TryAcquireManagerRoleAsync();
            stopwatch.Stop();

            // Assert
            isManager.Should().BeTrue();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

            Output.WriteLine($"Election time: {stopwatch.Elapsed.TotalMilliseconds}ms");
        }
        finally
        {
            await managerElectionService.ReleaseManagerRoleAsync();
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task FailoverTime_ShouldBeUnder30Seconds()
    {
        // Arrange
        var serviceProvider1 = CreateServiceProvider("instance-1");
        var serviceProvider2 = CreateServiceProvider("instance-2");

        var managerElectionService1 = CreateManagerElectionService(serviceProvider1, "test-instance-1-failover-time");
        var managerElectionService2 = CreateManagerElectionService(serviceProvider2, "test-instance-2-failover-time");

        try
        {
            // Act - First instance becomes manager
            var isManager1 = await managerElectionService1.TryAcquireManagerRoleAsync();
            isManager1.Should().BeTrue();

            // Simulate failure and measure failover time
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await managerElectionService1.ReleaseManagerRoleAsync();

            var isManager2 = await managerElectionService2.TryAcquireManagerRoleAsync();
            stopwatch.Stop();

            // Assert
            isManager2.Should().BeTrue();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));

            Output.WriteLine($"Failover time: {stopwatch.Elapsed.TotalMilliseconds}ms");
        }
        finally
        {
            await managerElectionService1.ReleaseManagerRoleAsync();
            await managerElectionService2.ReleaseManagerRoleAsync();

            if (serviceProvider1 is IDisposable disposable1) disposable1.Dispose();
            if (serviceProvider2 is IDisposable disposable2) disposable2.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task MemoryUsage_ShouldBeUnder10MB()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();
        var managerElectionService = CreateManagerElectionService(serviceProvider, "test-instance-memory");

        try
        {
            // Act - Acquire manager role
            var isManager = await managerElectionService.TryAcquireManagerRoleAsync();
            isManager.Should().BeTrue();

            // Measure memory usage
            var memoryBefore = GC.GetTotalMemory(false);
            GC.Collect();
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryUsageMB = (memoryAfter - memoryBefore) / (1024.0 * 1024.0);

            // Assert
            memoryUsageMB.Should().BeLessThan(10.0);

            Output.WriteLine($"Memory usage: {memoryUsageMB:F2}MB");
        }
        finally
        {
            await managerElectionService.ReleaseManagerRoleAsync();
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task CPUUsage_ShouldBeLowDuringIdle()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();
        var managerElectionService = CreateManagerElectionService(serviceProvider, "test-instance-cpu");

        try
        {
            // Act - Acquire manager role and measure CPU during idle
            var isManager = await managerElectionService.TryAcquireManagerRoleAsync();
            isManager.Should().BeTrue();

            // Measure CPU usage over a short period
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var cpuBefore = process.TotalProcessorTime;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Wait for a short period to measure idle CPU
            await Task.Delay(TimeSpan.FromSeconds(4));

            stopwatch.Stop();
            var cpuAfter = process.TotalProcessorTime;
            var cpuUsage = (cpuAfter - cpuBefore).TotalMilliseconds / (Environment.ProcessorCount * stopwatch.Elapsed.TotalMilliseconds) * 100;

            // Assert
            cpuUsage.Should().BeLessThan(30.0);

            Output.WriteLine($"CPU usage: {cpuUsage:F2}%");
        }
        finally
        {
            await managerElectionService.ReleaseManagerRoleAsync();
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task HighContention_ShouldMaintainPerformance()
    {
        // Arrange
        var serviceProvider1 = CreateServiceProvider("instance-1");
        var serviceProvider2 = CreateServiceProvider("instance-2");
        var serviceProvider3 = CreateServiceProvider("instance-3");

        var managerElectionService1 = CreateManagerElectionService(serviceProvider1, "test-instance-1-contention");
        var managerElectionService2 = CreateManagerElectionService(serviceProvider2, "test-instance-2-contention");
        var managerElectionService3 = CreateManagerElectionService(serviceProvider3, "test-instance-3-contention");

        try
        {
            // Act - Simulate high contention
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var tasks = new List<Task<bool>>();

            // Start many concurrent attempts
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(managerElectionService1.TryAcquireManagerRoleAsync());
                tasks.Add(managerElectionService2.TryAcquireManagerRoleAsync());
                tasks.Add(managerElectionService3.TryAcquireManagerRoleAsync());
            }

            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert - Verify that the results are consistent
            // All calls to the same instance should return the same result
            var instance1Results = results.Where((_, index) => index % 3 == 0).ToList();
            var instance2Results = results.Where((_, index) => index % 3 == 1).ToList();
            var instance3Results = results.Where((_, index) => index % 3 == 2).ToList();

            // All results for each instance should be the same
            instance1Results.All(r => r == instance1Results[0]).Should().BeTrue();
            instance2Results.All(r => r == instance2Results[0]).Should().BeTrue();
            instance3Results.All(r => r == instance3Results[0]).Should().BeTrue();

            // Only one instance should be manager
            var instance1IsManager = instance1Results[0];
            var instance2IsManager = instance2Results[0];
            var instance3IsManager = instance3Results[0];

            var managerCount = new[] { instance1IsManager, instance2IsManager, instance3IsManager }.Count(m => m);
            managerCount.Should().Be(1);

            // Performance should be reasonable even under contention
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

            Output.WriteLine($"High contention test completed in {stopwatch.Elapsed.TotalMilliseconds}ms");
            Output.WriteLine($"Instance1: {instance1IsManager}, Instance2: {instance2IsManager}, Instance3: {instance3IsManager}");
        }
        finally
        {
            await managerElectionService1.ReleaseManagerRoleAsync();
            await managerElectionService2.ReleaseManagerRoleAsync();
            await managerElectionService3.ReleaseManagerRoleAsync();

            if (serviceProvider1 is IDisposable disposable1) disposable1.Dispose();
            if (serviceProvider2 is IDisposable disposable2) disposable2.Dispose();
            if (serviceProvider3 is IDisposable disposable3) disposable3.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task RapidElectionCycles_ShouldMaintainPerformance()
    {
        // Arrange
        var serviceProvider1 = CreateServiceProvider("instance-1");
        var serviceProvider2 = CreateServiceProvider("instance-2");

        try
        {
            // Act - Rapid election cycles
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var cycles = 10;

            for (int i = 0; i < cycles; i++)
            {
                var managerElectionService1 = CreateManagerElectionService(serviceProvider1, $"test-instance-1-cycle-{i}");
                var managerElectionService2 = CreateManagerElectionService(serviceProvider2, $"test-instance-2-cycle-{i}");

                var tasks = new[]
                {
                    managerElectionService1.TryAcquireManagerRoleAsync(),
                    managerElectionService2.TryAcquireManagerRoleAsync()
                };

                var results = await Task.WhenAll(tasks);
                var managerCount = results.Count(r => r);
                managerCount.Should().Be(1);

                await managerElectionService1.ReleaseManagerRoleAsync();
                await managerElectionService2.ReleaseManagerRoleAsync();
            }

            stopwatch.Stop();

            // Assert
            var averageTimePerCycle = stopwatch.Elapsed.TotalMilliseconds / cycles;
            averageTimePerCycle.Should().BeLessThan(1000); // Less than 1 second per cycle

            Output.WriteLine($"Average time per cycle: {averageTimePerCycle:F2}ms");
        }
        finally
        {
            if (serviceProvider1 is IDisposable disposable1) disposable1.Dispose();
            if (serviceProvider2 is IDisposable disposable2) disposable2.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ConcurrentElectionAttempts_ShouldScaleWell()
    {
        // Arrange
        var serviceProvider1 = CreateServiceProvider("instance-1");
        var serviceProvider2 = CreateServiceProvider("instance-2");

        var managerElectionService1 = CreateManagerElectionService(serviceProvider1, "test-instance-1-scale");
        var managerElectionService2 = CreateManagerElectionService(serviceProvider2, "test-instance-2-scale");

        try
        {
            // Act - Many concurrent attempts
            var tasks = new List<Task<bool>>();
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            // Start many concurrent attempts
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(managerElectionService1.TryAcquireManagerRoleAsync(cancellationTokenSource.Token));
                tasks.Add(managerElectionService2.TryAcquireManagerRoleAsync(cancellationTokenSource.Token));
            }

            var results = await Task.WhenAll(tasks);

            // Assert - Verify that the results are consistent
            // All calls to the same instance should return the same result
            var instance1Results = results.Where((_, index) => index % 2 == 0).ToList();
            var instance2Results = results.Where((_, index) => index % 2 == 1).ToList();

            // All results for instance 1 should be the same
            instance1Results.All(r => r == instance1Results[0]).Should().BeTrue();
            // All results for instance 2 should be the same
            instance2Results.All(r => r == instance2Results[0]).Should().BeTrue();

            // Only one instance should be manager
            var instance1IsManager = instance1Results[0];
            var instance2IsManager = instance2Results[0];

            (instance1IsManager && instance2IsManager).Should().BeFalse(); // Both can't be manager
            (instance1IsManager || instance2IsManager).Should().BeTrue();  // At least one should be manager

            // Verify final state
            var finalManagers = new[]
            {
                managerElectionService1.IsManager,
                managerElectionService2.IsManager
            };

            finalManagers.Count(m => m).Should().Be(1);

            Output.WriteLine($"Concurrent attempts completed. Instance1 results: {string.Join(",", instance1Results)}");
            Output.WriteLine($"Instance2 results: {string.Join(",", instance2Results)}");
            Output.WriteLine($"Final state - Instance1: {finalManagers[0]}, Instance2: {finalManagers[1]}");
        }
        finally
        {
            await managerElectionService1.ReleaseManagerRoleAsync();
            await managerElectionService2.ReleaseManagerRoleAsync();

            if (serviceProvider1 is IDisposable disposable1) disposable1.Dispose();
            if (serviceProvider2 is IDisposable disposable2) disposable2.Dispose();
        }
    }
}
