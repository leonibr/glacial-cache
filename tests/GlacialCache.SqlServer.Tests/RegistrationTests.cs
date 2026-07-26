using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace GlacialCache.SqlServer.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void Registration_exposes_same_neutral_and_distributed_cache_instance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGlacialCacheSqlServer(options =>
        {
            options.ConnectionString = "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true";
            options.CreateInfrastructure = false;
        });

        using var provider = services.BuildServiceProvider();
        var neutral = provider.GetRequiredService<global::GlacialCache.Abstractions.IGlacialCache>();
        var distributed = provider.GetRequiredService<IDistributedCache>();

        Assert.IsType<GlacialCacheSqlServer>(neutral);
        Assert.Same(neutral, distributed);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("[quoted]")]
    public void Registration_rejects_unsafe_identifiers(string tableName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGlacialCacheSqlServer(options =>
        {
            options.ConnectionString = "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true";
            options.TableName = tableName;
            options.CreateInfrastructure = false;
        });

        using var provider = services.BuildServiceProvider();
        Assert.Throws<ArgumentException>(() => provider.GetRequiredService<GlacialCacheSqlServer>());
    }

    [Fact]
    public void Provider_rejects_keys_longer_than_900_characters_before_connecting()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGlacialCacheSqlServer(options =>
        {
            options.ConnectionString = "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true";
            options.CreateInfrastructure = false;
        });

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<global::GlacialCache.Abstractions.IGlacialCache>();
        Assert.Throws<ArgumentException>(() => cache.Get(new string('k', 901)));
    }
}
