using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using System.Text;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Integration;
using Extensions;
using Shared;
using Configuration;
using Configuration.Resilience;
using Configuration.Infrastructure;

/// <summary>
/// Resilience patterns wiring verification. Functional coverage is provided by ComprehensiveValidationTests.
/// This test ensures that resilience configuration (retry, circuit breaker, timeouts) can be enabled without errors.
/// </summary>
public class ResiliencePatternsIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private IDistributedCache _cache = null;
    private IServiceProvider? _serviceProvider;

    public ResiliencePatternsIntegrationTests(ITestOutputHelper output) : base(output)
    {
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

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            services.AddGlacialCachePostgreSQL(options =>
            {
                options.Connection = new ConnectionOptions
                {
                    ConnectionString = _postgres.GetConnectionString()
                };
                options.Infrastructure = new InfrastructureOptions
                {
                    EnableManagerElection = false,
                    CreateInfrastructure = true
                };
                options.Resilience = new ResilienceOptions
                {
                    EnableResiliencePatterns = true,
                    Retry = new RetryOptions
                    {
                        MaxAttempts = 3,
                        BaseDelay = TimeSpan.FromMilliseconds(100)
                    },
                    CircuitBreaker = new CircuitBreakerOptions
                    {
                        Enable = true,
                        FailureThreshold = 2,
                        DurationOfBreak = TimeSpan.FromMilliseconds(500)
                    },
                    Timeouts = new TimeoutOptions
                    {
                        OperationTimeout = TimeSpan.FromSeconds(5)
                    },
                    Logging = new LoggingOptions
                    {
                        EnableResilienceLogging = true,
                        ConnectionFailureLogLevel = LogLevel.Warning
                    }
                };
            });

            _serviceProvider = services.BuildServiceProvider();
            _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}");
        }
    }

    protected override async Task CleanupTestAsync()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (_postgres != null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task ResiliencePatterns_WithEnabledConfiguration_ShouldInitializeAndWork()
    {
        // Arrange
        const string key = "resilience-wiring-test";
        var value = Encoding.UTF8.GetBytes("Resilience wiring test");

        // Act - Verify resilience-enabled cache can perform basic operations
        await _cache!.SetAsync(key, value);
        var retrievedValue = await _cache!.GetAsync(key);

        // Assert - Resilience patterns are wired correctly and don't interfere with normal operations
        retrievedValue.Should().NotBeNull();
        retrievedValue.Should().BeEquivalentTo(value);
    }

    [Fact]
    public async Task ResiliencePatterns_WithDisabledConfiguration_ShouldAlsoWork()
    {
        // Arrange - Create a new service with resilience disabled to verify optional wiring
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection = new ConnectionOptions
            {
                ConnectionString = _postgres!.GetConnectionString()
            };
            options.Infrastructure = new InfrastructureOptions
            {
                EnableManagerElection = false,
                CreateInfrastructure = true
            };
            options.Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = false // Verify cache works without resilience
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IDistributedCache>();

        // Act
        const string key = "no-resilience-wiring-test";
        var value = Encoding.UTF8.GetBytes("No resilience test");
        await cache.SetAsync(key, value);
        var result = await cache.GetAsync(key);

        // Assert - Cache works correctly without resilience patterns
        result.Should().BeEquivalentTo(value);

        // Cleanup
        if (serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
