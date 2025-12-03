using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Models;
using System.Diagnostics;
using GlacialCache.PostgreSQL.Services;

namespace GlacialCache.Example.CacheEntryExample;

class Program
{
    static async Task Main(string[] args)
    {
        // Setup services
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Get connection string from environment variables or use default
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "cache_test";
        var username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";

        var connectionString = $"Host={host};Database={database};Username={username};Password={password}";

        Console.WriteLine($"🔧 Using connection string: {connectionString}");

        // Wait for PostgreSQL to be ready
        Console.WriteLine("⏳ Waiting for PostgreSQL to be ready...");
        await WaitForPostgreSQL(host, database, username, password);
        Console.WriteLine("✅ PostgreSQL is ready!");

        // Add GlacialCache PostgreSQL
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = connectionString;
            options.Cache.SchemaName = "cache";
            options.Cache.TableName = "entries";
            options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);

            // For demo purposes, disable manager election and force infrastructure creation
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Get the cache service
        var cache = serviceProvider.GetRequiredService<IGlacialCache>();

        Console.WriteLine("🚀 GlacialCache PostgreSQL CacheEntry Example");
        Console.WriteLine("============================================");

        // Run CacheEntry examples
        var cacheEntryExample = new CacheEntryExample(cache, serviceProvider.GetRequiredService<GlacialCacheEntryFactory>());
        await cacheEntryExample.RunExampleAsync();

        Console.WriteLine("\n🎉 All CacheEntry examples completed successfully!");
    }

    private static async Task<T> MeasureOperationAsync<T>(string operationName, Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await operation();
        stopwatch.Stop();
        Console.WriteLine($"✅ {operationName} [{stopwatch.ElapsedMilliseconds} ms]");
        return result;
    }

    private static async Task MeasureOperationAsync(string operationName, Func<Task> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        await operation();
        stopwatch.Stop();
        Console.WriteLine($"✅ {operationName} [{stopwatch.ElapsedMilliseconds} ms]");
    }

    private static async Task WaitForPostgreSQL(string host, string database, string username, string password)
    {
        var maxAttempts = 30;
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            try
            {
                using var connection = new Npgsql.NpgsqlConnection($"Host={host};Database={database};Username={username};Password={password}");
                await connection.OpenAsync();
                await connection.CloseAsync();
                return; // Success
            }
            catch (Exception ex)
            {
                attempt++;
                Console.WriteLine($"⏳ Attempt {attempt}/{maxAttempts}: PostgreSQL not ready yet ({ex.Message})");
                if (attempt < maxAttempts)
                {
                    await Task.Delay(2000); // Wait 2 seconds before next attempt
                }
            }
        }

        throw new InvalidOperationException("PostgreSQL is not ready after maximum attempts");
    }
}



