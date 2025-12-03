using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Extensions;

namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// Base class for unit-style integration tests that don't require Docker containers.
/// Provides automatic service provider setup and disposal.
/// </summary>
public abstract class UnitIntegrationTestBase
{
    private const string DefaultConnectionString = "Server=localhost;Database=testdb;User Id=testuser;Password=testpass;";

    /// <summary>
    /// Executes a test action with a properly configured and disposed ServiceProvider.
    /// </summary>
    /// <param name="testAction">The test logic to execute with the ServiceProvider</param>
    /// <param name="configureOptions">Optional action to configure GlacialCachePostgreSQLOptions</param>
    /// <param name="configureServices">Optional action to configure additional services</param>
    protected void ExecuteWithServiceProvider(
        Action<ServiceProvider> testAction,
        Action<GlacialCachePostgreSQLOptions>? configureOptions = null,
        Action<ServiceCollection>? configureServices = null)
    {
        var serviceProvider = CreateServiceProvider(configureOptions, configureServices);
        try
        {
            testAction(serviceProvider);
        }
        finally
        {
            serviceProvider?.Dispose();
        }
    }

    /// <summary>
    /// Executes an async test action with a properly configured and disposed ServiceProvider.
    /// </summary>
    /// <param name="testAction">The async test logic to execute with the ServiceProvider</param>
    /// <param name="configureOptions">Optional action to configure GlacialCachePostgreSQLOptions</param>
    /// <param name="configureServices">Optional action to configure additional services</param>
    protected async Task ExecuteWithServiceProviderAsync(
        Func<ServiceProvider, Task> testAction,
        Action<GlacialCachePostgreSQLOptions>? configureOptions = null,
        Action<ServiceCollection>? configureServices = null)
    {
        var serviceProvider = CreateServiceProvider(configureOptions, configureServices);
        try
        {
            await testAction(serviceProvider);
        }
        finally
        {
            serviceProvider?.Dispose();
        }
    }

    /// <summary>
    /// Creates a ServiceProvider with GlacialCache configured.
    /// Caller is responsible for disposal.
    /// </summary>
    private ServiceProvider CreateServiceProvider(
        Action<GlacialCachePostgreSQLOptions>? configureOptions = null,
        Action<ServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = DefaultConnectionString;
            configureOptions?.Invoke(options);
        });

        configureServices?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Helper method to initialize observable properties with logging.
    /// </summary>
    protected void InitializeObservableProperties<T>(ServiceProvider serviceProvider, GlacialCachePostgreSQLOptions options)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<T>>();
        options.Cache.SetLogger(logger);
        options.Connection.SetLogger(logger);
    }
}
