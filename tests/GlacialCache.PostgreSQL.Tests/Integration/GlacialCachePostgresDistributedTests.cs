using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Tests.Shared;
using System.Text;
using Xunit.Abstractions;
using GlacialCache.PostgreSQL.Services;

namespace GlacialCache.PostgreSQL.Tests.IntegrationTests;

/// <summary>
/// Minimal smoke tests for IDistributedCache interface compliance.
/// See ComprehensiveValidationTests for thorough coverage of edge cases, batch operations, and serialization.
/// </summary>
public class GlacialCachePostgresDistributedTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private IDistributedCache? _cache;
    private IGlacialCache? _glacial;
    private IServiceProvider? _serviceProvider;
    private CleanupBackgroundService? _cleanupService;

    public GlacialCachePostgresDistributedTests(ITestOutputHelper output) : base(output)
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

            // Explicitly register TimeProvider.System to ensure test isolation
            services.AddSingleton<TimeProvider>(TimeProvider.System);

            services.AddGlacialCachePostgreSQL(options =>
            {
                options.Connection.ConnectionString = _postgres.GetConnectionString();
                options.Infrastructure.EnableManagerElection = false;
                options.Infrastructure.CreateInfrastructure = true;
                options.Maintenance.EnableAutomaticCleanup = true;
                options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(250);
            });

            _serviceProvider = services.BuildServiceProvider();
            _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
            _glacial = _serviceProvider.GetRequiredService<IGlacialCache>();
            _cleanupService = _serviceProvider.GetRequiredService<CleanupBackgroundService>();
            await _cleanupService.StartAsync(default);
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
    public async Task SetAndGet_ShouldStoreAndRetrieveValue()
    {
        // Arrange
        const string key = "test-key";
        var value = Encoding.UTF8.GetBytes("Hello, World!");
        var options = new DistributedCacheEntryOptions();

        // Act
        await _cache!.SetAsync(key, value, options);
        var retrievedValue = await _cache.GetAsync(key);

        // Assert
        retrievedValue.Should().NotBeNull();
        retrievedValue.Should().BeEquivalentTo(value);
    }

    [Fact]
    public async Task Get_NonExistentKey_ShouldReturnNull()
    {
        // Arrange
        const string key = "non-existent-key";

        // Act
        var result = await _cache!.GetAsync(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Remove_ShouldDeleteValue()
    {
        // Arrange
        const string key = "remove-key";
        var value = Encoding.UTF8.GetBytes("Value to remove");
        var options = new DistributedCacheEntryOptions();

        // Act
        await _cache!.SetAsync(key, value, options);
        await _cache!.RemoveAsync(key);
        var retrievedValue = await _cache.GetAsync(key);

        // Assert
        retrievedValue.Should().BeNull();
    }
}
