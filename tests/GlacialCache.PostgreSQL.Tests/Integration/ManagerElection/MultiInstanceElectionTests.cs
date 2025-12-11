using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Models;
using Xunit.Abstractions;
using Npgsql;

namespace GlacialCache.PostgreSQL.Tests.Integration.ManagerElection;

public class MultiInstanceElectionTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private readonly string _schemaName;
    private readonly string _tableName;

    public MultiInstanceElectionTests(ITestOutputHelper output) : base(output)
    {
        // Use a fixed schema name to ensure all instances use the same lock key
        _schemaName = "test_schema_multi_instance";
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

            // Grant advisory lock permissions for manager election
            await _postgres.GrantAdvisoryLockPermissionsAsync("testuser", Output);
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
                   .SetMinimumLevel(LogLevel.Debug)
                   .AddFilter("GlacialCache.PostgreSQL.Services.ManagerElectionService", LogLevel.Trace));

        // Configure GlacialCache with manager election enabled
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection = new ConnectionOptions
            {
                ConnectionString = new NpgsqlConnectionStringBuilder(_postgres!.GetConnectionString()) { ApplicationName = GetType().Name }.ConnectionString
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
    public async Task SingleInstance_ShouldBecomeManager()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();
        var managerElectionService = CreateManagerElectionService(serviceProvider, "test-single-instance");

        try
        {
            // Act
            var isManager = await managerElectionService.TryAcquireManagerRoleAsync();

            // Assert
            isManager.ShouldBeTrue();
            managerElectionService.IsManager.ShouldBeTrue();
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
    public async Task MultipleInstances_ShouldElectSingleManager()
    {
        // Arrange
        var serviceProvider1 = CreateServiceProvider("instance-1");
        var serviceProvider2 = CreateServiceProvider("instance-2");
        var serviceProvider3 = CreateServiceProvider("instance-3");

        var managerElectionService1 = CreateManagerElectionService(serviceProvider1, "test-instance-1");
        var managerElectionService2 = CreateManagerElectionService(serviceProvider2, "test-instance-2");
        var managerElectionService3 = CreateManagerElectionService(serviceProvider3, "test-instance-3");

        try
        {
            // Act - All instances try to become manager simultaneously
            var tasks = new[]
            {
                managerElectionService1.TryAcquireManagerRoleAsync(),
                managerElectionService2.TryAcquireManagerRoleAsync(),
                managerElectionService3.TryAcquireManagerRoleAsync()
            };

            var results = await Task.WhenAll(tasks);

            // Assert - Only one should be manager
            var managerCount = results.Count(r => r);
            managerCount.ShouldBe(1);

            // Verify state consistency
            var isManager1 = managerElectionService1.IsManager;
            var isManager2 = managerElectionService2.IsManager;
            var isManager3 = managerElectionService3.IsManager;

            var totalManagers = new[] { isManager1, isManager2, isManager3 }.Count(m => m);
            totalManagers.ShouldBe(1);

            Output.WriteLine($"Manager election results: Instance1={isManager1}, Instance2={isManager2}, Instance3={isManager3}");
        }
        finally
        {
            // Cleanup
            await managerElectionService1.ReleaseManagerRoleAsync();
            await managerElectionService2.ReleaseManagerRoleAsync();
            await managerElectionService3.ReleaseManagerRoleAsync();

            if (serviceProvider1 is IDisposable disposable1) disposable1.Dispose();
            if (serviceProvider2 is IDisposable disposable2) disposable2.Dispose();
            if (serviceProvider3 is IDisposable disposable3) disposable3.Dispose();
        }
    }

    [Fact]
    public async Task ManagerFailure_ShouldTriggerFailover()
    {
        // Arrange
        var serviceProvider1 = CreateServiceProvider("instance-1");
        var serviceProvider2 = CreateServiceProvider("instance-2");

        var managerElectionService1 = CreateManagerElectionService(serviceProvider1, "test-instance-1-failover");
        var managerElectionService2 = CreateManagerElectionService(serviceProvider2, "test-instance-2-failover");

        try
        {
            // Act - First instance becomes manager
            var isManager1 = await managerElectionService1.TryAcquireManagerRoleAsync();
            isManager1.ShouldBeTrue();

            // Second instance should not be manager
            var isManager2 = await managerElectionService2.TryAcquireManagerRoleAsync();
            isManager2.ShouldBeFalse();

            // Simulate failure of first instance
            await managerElectionService1.ReleaseManagerRoleAsync();

            // Second instance should now be able to become manager
            isManager2 = await managerElectionService2.TryAcquireManagerRoleAsync();
            isManager2.ShouldBeTrue();

            Output.WriteLine("Failover test completed successfully");
        }
        finally
        {
            // Cleanup
            await managerElectionService1.ReleaseManagerRoleAsync();
            await managerElectionService2.ReleaseManagerRoleAsync();

            if (serviceProvider1 is IDisposable disposable1) disposable1.Dispose();
            if (serviceProvider2 is IDisposable disposable2) disposable2.Dispose();
        }
    }

    [Fact]
    public async Task VoluntaryYield_ShouldAllowOtherInstancesToBecomeManager()
    {
        // Arrange
        var serviceProvider1 = CreateServiceProvider("instance-1");
        var serviceProvider2 = CreateServiceProvider("instance-2");

        var managerElectionService1 = CreateManagerElectionService(serviceProvider1, "test-instance-1-yield");
        var managerElectionService2 = CreateManagerElectionService(serviceProvider2, "test-instance-2-yield");

        try
        {
            // Act - First instance becomes manager
            var isManager1 = await managerElectionService1.TryAcquireManagerRoleAsync();
            isManager1.ShouldBeTrue();

            // Second instance should not be manager
            var isManager2 = await managerElectionService2.TryAcquireManagerRoleAsync();
            isManager2.ShouldBeFalse();

            // First instance voluntarily yields
            await managerElectionService1.ReleaseManagerRoleAsync();

            // Second instance should now be able to become manager
            isManager2 = await managerElectionService2.TryAcquireManagerRoleAsync();
            isManager2.ShouldBeTrue();

            Output.WriteLine("Voluntary yield test completed successfully");
        }
        finally
        {
            // Cleanup
            await managerElectionService1.ReleaseManagerRoleAsync();
            await managerElectionService2.ReleaseManagerRoleAsync();

            if (serviceProvider1 is IDisposable disposable1) disposable1.Dispose();
            if (serviceProvider2 is IDisposable disposable2) disposable2.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentAccess_ShouldMaintainSingleManager()
    {
        // Arrange
        var serviceProvider1 = CreateServiceProvider("instance-1");
        var serviceProvider2 = CreateServiceProvider("instance-2");

        var managerElectionService1 = CreateManagerElectionService(serviceProvider1, "test-instance-1-concurrent");
        var managerElectionService2 = CreateManagerElectionService(serviceProvider2, "test-instance-2-concurrent");

        try
        {
            // Act - Simulate concurrent access attempts
            var tasks = new List<Task<bool>>();
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Start multiple concurrent attempts
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
            instance1Results.All(r => r == instance1Results[0]).ShouldBeTrue();
            // All results for instance 2 should be the same
            instance2Results.All(r => r == instance2Results[0]).ShouldBeTrue();

            // Only one instance should be manager
            var instance1IsManager = instance1Results[0];
            var instance2IsManager = instance2Results[0];

            (instance1IsManager && instance2IsManager).ShouldBeFalse(); // Both can't be manager
            (instance1IsManager || instance2IsManager).ShouldBeTrue();  // At least one should be manager

            Output.WriteLine($"Concurrent access test completed. Instance1: {instance1IsManager}, Instance2: {instance2IsManager}");
        }
        finally
        {
            // Cleanup
            await managerElectionService1.ReleaseManagerRoleAsync();
            await managerElectionService2.ReleaseManagerRoleAsync();

            if (serviceProvider1 is IDisposable disposable1) disposable1.Dispose();
            if (serviceProvider2 is IDisposable disposable2) disposable2.Dispose();
        }
    }

    [Fact]
    public async Task Events_ShouldFireCorrectly()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();
        var managerElectionService = CreateManagerElectionService(serviceProvider, "test-instance-events");

        var electedEvents = new List<ManagerElectedEventArgs>();
        var lostEvents = new List<ManagerLostEventArgs>();

        managerElectionService.ManagerElected += (sender, args) => electedEvents.Add(args);
        managerElectionService.ManagerLost += (sender, args) => lostEvents.Add(args);

        try
        {
            // Act
            var startTime = DateTimeOffset.UtcNow;
            var isManager = await managerElectionService.TryAcquireManagerRoleAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(100)); // Allow event to fire

            var afterAcquireTime = DateTimeOffset.UtcNow;
            await managerElectionService.ReleaseManagerRoleAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(100)); // Allow event to fire
            var endTime = DateTimeOffset.UtcNow;

            // Assert
            isManager.ShouldBeTrue();
            electedEvents.Count.ShouldBe(1);
            lostEvents.Count.ShouldBe(1);

            electedEvents[0].InstanceId.ShouldNotBeNullOrEmpty();
            electedEvents[0].ElectedAt.ShouldBeGreaterThanOrEqualTo(startTime);
            electedEvents[0].ElectedAt.ShouldBeLessThanOrEqualTo(afterAcquireTime);

            lostEvents[0].InstanceId.ShouldNotBeNullOrEmpty();
            lostEvents[0].LostAt.ShouldBeGreaterThanOrEqualTo(afterAcquireTime);
            lostEvents[0].LostAt.ShouldBeLessThanOrEqualTo(endTime);
            lostEvents[0].Reason.ShouldBe("Manual release");
        }
        finally
        {
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
