using GlacialCache.PostgreSQL;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add GlacialCache PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=cache_test;Username=postgres;Password=postgres";

builder.Services.AddGlacialCachePostgreSQL(options =>
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Basic cache endpoints
app.MapGet("/cache/string/{key}", async (string key, IDistributedCache cache) =>
{
    var value = await cache.GetStringAsync(key);
    return value is not null ? Results.Ok(new { key, value }) : Results.NotFound();
})
.WithName("GetCachedString");

app.MapPost("/cache/string/{key}", async (string key, string value, IDistributedCache cache) =>
{
    var options = new DistributedCacheEntryOptions
    {
        SlidingExpiration = TimeSpan.FromMinutes(10)
    };
    await cache.SetStringAsync(key, value, options);
    return Results.Ok(new { key, value, message = "Cached successfully" });
})
.WithName("SetCachedString");

app.MapDelete("/cache/{key}", async (string key, IDistributedCache cache) =>
{
    await cache.RemoveAsync(key);
    return Results.Ok(new { key, message = "Removed from cache" });
})
.WithName("RemoveCachedString");

// Batch operations
app.MapPost("/cache/batch", async (Dictionary<string, string> batchData, IDistributedCache cache) =>
{
    var options = new DistributedCacheEntryOptions
    {
        SlidingExpiration = TimeSpan.FromMinutes(10)
    };

    foreach (var (key, value) in batchData)
    {
        await cache.SetStringAsync(key, value, options);
    }

    return Results.Ok(new { count = batchData.Count, message = "Batch cached successfully" });
})
.WithName("SetBatchCache");

app.MapGet("/cache/batch", async (string[] keys, IDistributedCache cache) =>
{
    var results = new Dictionary<string, string?>();
    foreach (var key in keys)
    {
        results[key] = await cache.GetStringAsync(key);
    }

    return Results.Ok(results);
})
.WithName("GetBatchCache");

// Health check endpoint
app.MapGet("/health/cache", async (IDistributedCache cache, TimeProvider timeProvider) =>
{
    try
    {
        // Try to set and get a test value to verify connectivity
        var testKey = $"health-check-{Guid.NewGuid()}";
        var testValue = timeProvider.GetUtcNow().ToString("O");

        await cache.SetStringAsync(testKey, testValue, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var retrievedValue = await cache.GetStringAsync(testKey);

        if (retrievedValue == testValue)
        {
            await cache.RemoveAsync(testKey);
            return Results.Ok(new
            {
                status = "healthy",
                message = "Cache is working correctly",
                timestamp = timeProvider.GetUtcNow()
            });
        }
        else
        {
            return Results.Problem("Cache value mismatch", statusCode: 500);
        }
    }
    catch (Exception ex)
    {
        return Results.Problem($"Cache health check failed: {ex.Message}", statusCode: 500);
    }
})
.WithName("HealthCheckCache");

// Statistics endpoint
app.MapGet("/cache/stats", (IGlacialCache glacialCache) =>
{
    try
    {
        // This is a simplified stats endpoint - in a real application
        // you would implement proper metrics collection
        var stats = new
        {
            cacheType = "GlacialCache PostgreSQL",
            status = "operational",
            timestamp = DateTime.UtcNow
        };

        return Results.Ok(stats);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Stats retrieval failed: {ex.Message}", statusCode: 500);
    }
})
.WithName("GetCacheStats");

app.Run();




