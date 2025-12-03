using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace GlacialCache.PostgreSQL.Extensions;
using Models;
using Services;
using Serializers;
using Abstractions;
using Configuration;

/// <summary>
/// Extension methods for setting up PostgreSQL distributed caching services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL distributed caching services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="setupAction">An <see cref="Action{GlacialCachePostgreSQLOptions}"/> to configure the provided <see cref="GlacialCachePostgreSQLOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddGlacialCachePostgreSQL(
        this IServiceCollection services,
        Action<GlacialCachePostgreSQLOptions> setupAction)
    {
        services.AddOptions();
        services.Configure(setupAction);

        // Register configuration services
        services.TryAddSingleton<IncrementalConfigurationValidator>();

        // Register domain services that depend on configuration
        services.TryAddSingleton<IDbNomenclature>(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<DbNomenclature>();

            // Initialize observable properties through SetLogger methods
            options.CurrentValue.Cache.SetLogger(logger);
            options.CurrentValue.Connection.SetLogger(logger);

            return new DbNomenclature(options, logger);
        });
        services.TryAddSingleton<IDbRawCommands>(sp =>
        {
            var dbNomenclature = sp.GetRequiredService<IDbNomenclature>();
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger<DbRawCommands>();
            return new DbRawCommands(dbNomenclature, options, logger);
        });
        // Ensure ILogger<PostgreSQLDataSource> is available
        services.TryAddTransient<ILogger<PostgreSQLDataSource>>(sp =>
            sp.GetService<ILoggerFactory>()?.CreateLogger<PostgreSQLDataSource>() ?? NullLogger<PostgreSQLDataSource>.Instance);

        services.TryAddSingleton<IPostgreSQLDataSource>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PostgreSQLDataSource>>();
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();

            return new PostgreSQLDataSource(logger, options);
        });

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        // Register serializer and factory services
        services.TryAddSingleton<ICacheEntrySerializer>(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var cacheOptions = options.CurrentValue.Cache;

            return cacheOptions.Serializer switch
            {
                SerializerType.JsonBytes => new JsonCacheEntrySerializer(),
                SerializerType.Custom => CreateCustomSerializer(sp, cacheOptions.CustomSerializerType),
                _ => new MemoryPackCacheEntrySerializer(),
            };
        });
        services.TryAddSingleton<GlacialCacheEntryFactory>();

        // Ensure ILogger<TimeConverterService> is available
        services.TryAddTransient<ILogger<TimeConverterService>>(sp =>
            sp.GetService<ILoggerFactory>()?.CreateLogger<TimeConverterService>() ?? NullLogger<TimeConverterService>.Instance);

        services.TryAddTransient<ITimeConverterService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TimeConverterService>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            return new TimeConverterService(logger, timeProvider, options);
        });

        services.TryAddSingleton<GlacialCachePostgreSQL>(sp =>
        {
            var opts = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var logger = sp.GetRequiredService<ILogger<GlacialCachePostgreSQL>>();
            var timeConverter = sp.GetRequiredService<ITimeConverterService>();
            var ds = sp.GetRequiredService<IPostgreSQLDataSource>();
            var dbRawCommands = sp.GetRequiredService<IDbRawCommands>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var entryFactory = sp.GetRequiredService<GlacialCacheEntryFactory>();
            return new GlacialCachePostgreSQL(opts, logger, timeConverter, ds, dbRawCommands, sp, timeProvider, entryFactory);
        });

        // Ensure ILogger<GlacialCachePostgreSQL> is available
        services.TryAddTransient<ILogger<GlacialCachePostgreSQL>>(sp =>
            sp.GetService<ILoggerFactory>()?.CreateLogger<GlacialCachePostgreSQL>() ?? NullLogger<GlacialCachePostgreSQL>.Instance);
        services.TryAddSingleton<IDistributedCache>(sp => sp.GetRequiredService<GlacialCachePostgreSQL>());
        services.TryAddSingleton<IGlacialCache>(sp => sp.GetRequiredService<GlacialCachePostgreSQL>());

        // Resilience patterns services
        services.TryAddSingleton<IPolicyFactory>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PolicyFactory>>();
            return new PolicyFactory(logger);
        });

        // Register SchemaManager for idempotent schema management
        services.TryAddSingleton<ISchemaManager>(sp =>
        {
            var dataSource = sp.GetRequiredService<IPostgreSQLDataSource>();
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var logger = sp.GetRequiredService<ILogger<SchemaManager>>();
            var nomeclature = sp.GetRequiredService<IDbNomenclature>();
            return new SchemaManager(dataSource, options.CurrentValue, logger, nomeclature);
        });

        services.TryAddSingleton<ElectionState>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ElectionState>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var instanceId = Environment.MachineName + "_" + Environment.ProcessId;
            return new ElectionState(logger, timeProvider, instanceId);
        });

        services.TryAddSingleton<IManagerElectionService>(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var logger = sp.GetRequiredService<ILogger<ManagerElectionService>>();
            var dataSource = sp.GetRequiredService<IPostgreSQLDataSource>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            return new ManagerElectionService(options, logger, dataSource, timeProvider);
        });

        services.AddHostedService<ElectionBackgroundService>();


        services.TryAddSingleton<CleanupBackgroundService>(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var logger = sp.GetRequiredService<ILogger<CleanupBackgroundService>>();
            var dataSource = sp.GetRequiredService<IPostgreSQLDataSource>();
            var dbRawCommands = sp.GetRequiredService<IDbRawCommands>();
            var electionState = sp.GetService<ElectionState>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            return new CleanupBackgroundService(options, logger, dataSource, dbRawCommands, electionState, timeProvider);
        });

        // Only register as hosted service if enabled
        services.AddHostedService(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            if (options.CurrentValue.Maintenance.EnableAutomaticCleanup)
            {
                return sp.GetRequiredService<CleanupBackgroundService>();
            }
            else
            {
                return new NullBackgroundService() as BackgroundService;
            }
        });


        // Auto-generate infrastructure lock key for each instance
        services.Configure<GlacialCachePostgreSQLOptions>(options =>
        {
            options.Infrastructure.Lock.GenerateLockKey(options.Cache.SchemaName, options.Cache.TableName);
        });

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL distributed caching services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddGlacialCachePostgreSQL(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        return services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = connectionString;
        });
    }

    /// <summary>
    /// Adds PostgreSQL distributed caching services to the specified <see cref="IServiceCollection"/> with custom table configuration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The name of the cache table.</param>
    /// <param name="schemaName">The schema name where the cache table resides.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddGlacialCachePostgreSQL(
        this IServiceCollection services,
        string connectionString,
        string tableName,
        string schemaName = "public")
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        if (string.IsNullOrEmpty(tableName))
            throw new ArgumentException("Table name cannot be null or empty.", nameof(tableName));
        if (string.IsNullOrEmpty(schemaName))
            throw new ArgumentException("Schema name cannot be null or empty.", nameof(schemaName));

        return services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = connectionString;
            options.Cache.TableName = tableName;
            options.Cache.SchemaName = schemaName;
        });
    }

    /// <summary>
    /// Helper method for creating custom serializer instances.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="customType">The custom serializer type.</param>
    /// <returns>An instance of the custom serializer.</returns>
    /// <exception cref="InvalidOperationException">Thrown when custom type is invalid.</exception>
    private static ICacheEntrySerializer CreateCustomSerializer(IServiceProvider sp, Type? customType)
    {
        if (customType == null)
            throw new InvalidOperationException("CustomSerializerType must be specified when using SerializerType.Custom");

        if (!typeof(ICacheEntrySerializer).IsAssignableFrom(customType))
            throw new InvalidOperationException($"Custom serializer type {customType.Name} must implement ICacheEntrySerializer");

        // Try to create instance using DI container first, fallback to Activator
        return (ICacheEntrySerializer)(sp.GetService(customType) ?? Activator.CreateInstance(customType)!);
    }
}
