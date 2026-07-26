using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlacialCache.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGlacialCacheSqlServer(
        this IServiceCollection services,
        Action<GlacialCacheSqlServerOptions> setupAction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(setupAction);

        services.AddOptions<GlacialCacheSqlServerOptions>()
            .Configure(setupAction)
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Connection string is required")
            .ValidateOnStart();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<global::GlacialCache.Abstractions.ICacheEntrySerializer, global::GlacialCache.Abstractions.MemoryPackCacheEntrySerializer>();
        services.TryAddSingleton<global::GlacialCache.Abstractions.CacheEntryFactory>();
        services.TryAddSingleton<GlacialCacheSqlServer>();
        services.TryAddSingleton<global::GlacialCache.Abstractions.IGlacialCache>(sp => sp.GetRequiredService<GlacialCacheSqlServer>());
        services.TryAddSingleton<IDistributedCache>(sp => sp.GetRequiredService<GlacialCacheSqlServer>());
        return services;
    }

    public static IServiceCollection AddGlacialCacheSqlServer(this IServiceCollection services, string connectionString) =>
        services.AddGlacialCacheSqlServer(options => options.ConnectionString = connectionString);
}
