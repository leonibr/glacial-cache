# GlacialCache.PostgreSQL

A high-performance, pluggable distributed cache provider for .NET using PostgreSQL as the backend.  
Designed for modern, cloud-ready applications that need reliable, scalable caching with minimal configuration.

## Documentation

- **Getting started**: see `docs/getting-started.md` for a copy-pasteable ASP.NET Core setup.
- **Concepts**: see `docs/concepts.md` for data model, expiration semantics, and cleanup strategy.
- **Configuration**: see `docs/configuration.md` for a full breakdown of `GlacialCachePostgreSQLOptions`.
- **Architecture**: see `docs/architecture.md` for component and background service design.
- **Troubleshooting**: see `docs/troubleshooting.md` for common issues and concrete fixes.

## Features

✅ **Drop-in replacement** for `IDistributedCache`  
✅ **Advanced expiration support**: sliding and absolute expiration  
✅ **Binary data support**: Store any byte array efficiently  
✅ **Production-ready**: Comprehensive error handling and logging  
✅ **Auto-cleanup**: Automatic removal of expired entries  
✅ **High performance**: Optimized SQL queries with proper indexing  
✅ **Thread-safe**: Concurrent operations supported  
✅ **Multi-framework**: Supports .NET 6.0, 8.0, and 9.0  
✅ **Azure Managed Identity**: Automatic token refresh for Azure PostgreSQL  
✅ **Configurable serialization**: Choose between JSON and MemoryPack serializers

## Installation

```bash
dotnet add package GlacialCache.PostgreSQL
```

## Quick Start

### 1. Basic Configuration

```csharp
using GlacialCache.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

// Add GlacialCache with connection string
builder.Services.AddGlacialCachePostgreSQL(
    "Host=localhost;Database=myapp;Username=postgres;Password=mypassword");

var app = builder.Build();
```

### 2. Advanced Configuration

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = "Host=localhost;Database=myapp;Username=postgres;Password=mypassword";
    options.Cache.TableName = "my_cache_entries";
    options.Cache.SchemaName = "cache";

    // Simplified maintenance configuration
    options.Maintenance.EnableAutomaticCleanup = true;
    options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(15);
    options.Maintenance.MaxCleanupBatchSize = 500;

    options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(20);
    options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);

    // Serializer configuration
    options.Cache.Serializer = SerializerType.MemoryPack; // or SerializerType.JsonBytes
});
```

> **Migration note:** Previous previews exposed a `GlacialCachePostgreSQLBuilder` fluent API. Configure the cache by supplying an `Action<GlacialCachePostgreSQLOptions>` (as shown above) instead.

### 3. Using the Cache

```csharp
public class ProductService
{
    private readonly IDistributedCache _cache;

    public ProductService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        var key = $"product:{id}";

        // Try to get from cache
        var cachedBytes = await _cache.GetAsync(key);
        if (cachedBytes != null)
        {
            var json = Encoding.UTF8.GetString(cachedBytes);
            return JsonSerializer.Deserialize<Product>(json);
        }

        // Get from database
        var product = await _repository.GetProductAsync(id);
        if (product != null)
        {
            // Cache for 1 hour
            var productJson = JsonSerializer.Serialize(product);
            var bytes = Encoding.UTF8.GetBytes(productJson);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
                SlidingExpiration = TimeSpan.FromMinutes(15)
            };

            await _cache.SetAsync(key, bytes, options);
        }

        return product;
    }
}
```

## Configuration Options

| Option                                   | Description                  | Default               |
| ---------------------------------------- | ---------------------------- | --------------------- |
| `ConnectionString`                       | PostgreSQL connection string | _Required_            |
| `TableName`                              | Cache table name             | `glacial_cache_entries` |
| `SchemaName`                             | Database schema              | `public`              |
| `Maintenance.EnableAutomaticCleanup`     | Enable periodic cleanup      | `true`                |
| `Maintenance.CleanupInterval`            | Cleanup frequency            | 30 minutes            |
| `Maintenance.MaxCleanupBatchSize`        | Max items per cleanup batch  | 1000                  |
| `DefaultSlidingExpiration`               | Default sliding expiration   | `null`                |
| `DefaultAbsoluteExpirationRelativeToNow` | Default absolute expiration  | `null`                |

## Database Schema

GlacialCache automatically creates the following table structure:

```sql
CREATE TABLE public.glacial_cache_entries (
    key VARCHAR(900) PRIMARY KEY,
    value BYTEA NOT NULL,
    absolute_expiration TIMESTAMPTZ,
    sliding_interval INTERVAL,
    next_expiration TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    value_type VARCHAR(255),
    value_size INTEGER GENERATED ALWAYS AS (OCTET_LENGTH(value)) STORED
);

-- Indexes for efficient cleanup
CREATE INDEX idx_glacial_cache_entries_absolute_expiration
ON public.glacial_cache_entries (absolute_expiration)
WHERE absolute_expiration IS NOT NULL;

CREATE INDEX idx_glacial_cache_entries_next_expiration
ON public.glacial_cache_entries (next_expiration);

CREATE INDEX idx_glacial_cache_entries_value_type
ON public.glacial_cache_entries (value_type)
WHERE value_type IS NOT NULL;

CREATE INDEX idx_glacial_cache_entries_value_size
ON public.glacial_cache_entries (value_size);
```

## Serialization Options

GlacialCache supports two serialization strategies for complex objects while maintaining optimal performance for strings and byte arrays:

### Serializer Types

| Serializer Type | Description                           | Performance | Use Case                                       |
| --------------- | ------------------------------------- | ----------- | ---------------------------------------------- |
| `MemoryPack`    | Fast binary serialization (default)   | Highest     | High-performance applications, complex objects |
| `JsonBytes`     | JSON serialization with optimizations | High        | Interoperability, debugging, simple objects    |

### String and Byte Array Optimization

Both serializers include automatic optimizations:

- **Strings**: Always use direct UTF-8 encoding (no serialization overhead)
- **Byte Arrays**: Pass-through without modification
- **Complex Objects**: Use configured serializer

### Configuration Examples

```csharp
// Use MemoryPack for maximum performance (default)
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;
    options.Cache.Serializer = SerializerType.MemoryPack;
});

// Use JSON for better interoperability
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;
    options.Cache.Serializer = SerializerType.JsonBytes;
});
```

### Performance Characteristics

- **MemoryPack**: ~22% faster serialization, smaller payload size
- **JSON**: Human-readable, better debugging, cross-platform compatibility
- **String Optimization**: Both serializers use UTF-8 encoding for strings
- **Byte Array Pass-through**: Both serializers pass byte arrays unchanged

## Performance Considerations

- **Connection Pooling**: Uses Npgsql's built-in connection pooling
- **Async Operations**: All operations are fully async
- **Efficient Cleanup**: Background cleanup with configurable intervals
- **Optimized Queries**: Uses prepared statements and proper indexing
- **Binary Storage**: Direct byte array storage without unnecessary serialization

## Examples

### Simple String Caching

```csharp
// Store a string
await _cache.SetStringAsync("greeting", "Hello, World!", TimeSpan.FromMinutes(5));

// Retrieve a string
var greeting = await _cache.GetStringAsync("greeting");
```

### Object Caching with JSON

```csharp
public static class DistributedCacheExtensions
{
    public static async Task SetObjectAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        DistributedCacheEntryOptions? options = null)
    {
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        await cache.SetAsync(key, bytes, options ?? new DistributedCacheEntryOptions());
    }

    public static async Task<T?> GetObjectAsync<T>(
        this IDistributedCache cache,
        string key)
    {
        var bytes = await cache.GetAsync(key);
        if (bytes == null) return default;

        var json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<T>(json);
    }
}

// Usage
await _cache.SetObjectAsync("user:123", user, new DistributedCacheEntryOptions
{
    SlidingExpiration = TimeSpan.FromMinutes(30)
});

var user = await _cache.GetObjectAsync<User>("user:123");
```

### Custom Expiration Policies

```csharp
// Absolute expiration
var absoluteOptions = new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
};

// Sliding expiration
var slidingOptions = new DistributedCacheEntryOptions
{
    SlidingExpiration = TimeSpan.FromMinutes(15)
};

// Combined expiration
var combinedOptions = new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4),
    SlidingExpiration = TimeSpan.FromMinutes(30)
};
```

## Logging

GlacialCache uses `Microsoft.Extensions.Logging` for comprehensive logging:

```csharp
builder.Services.AddLogging(config =>
{
    config.AddConsole().SetMinimumLevel(LogLevel.Information);
});
```

Log levels:

- **Information**: Successful operations, cleanup statistics
- **Warning**: Non-critical errors (cleanup failures, access time updates)
- **Error**: Critical failures (connection issues, query failures)

## Azure Managed Identity Support

GlacialCache supports Azure Managed Identity for secure PostgreSQL connections without storing credentials. This is especially useful for Azure-hosted applications where tokens expire every 24 hours.

### Basic Azure Managed Identity Setup

```csharp
// Simple configuration
builder.Services.AddGlacialCachePostgreSQLWithAzureManagedIdentity(
    baseConnectionString: "Host=your-server.postgres.database.azure.com;Database=yourdb;Username=your-username@your-server",
    resourceId: "https://ossrdbms-aad.database.windows.net"
);
```

### Advanced Azure Managed Identity Configuration

```csharp
builder.Services.AddGlacialCachePostgreSQLWithAzureManagedIdentity(
    azureOptions =>
    {
        azureOptions.BaseConnectionString = "Host=your-server.postgres.database.azure.com;Database=yourdb;Username=your-username@your-server";
        azureOptions.ResourceId = "https://ossrdbms-aad.database.windows.net";
        azureOptions.ClientId = "your-user-assigned-managed-identity-client-id"; // Optional
        azureOptions.TokenRefreshBuffer = TimeSpan.FromHours(1); // Refresh token 1 hour before expiration
        azureOptions.MaxRetryAttempts = 3;
        azureOptions.RetryDelay = TimeSpan.FromSeconds(1);
    },
    cacheOptions =>
    {
        cacheOptions.TableName = "app_cache";
        cacheOptions.SchemaName = "public";
        cacheOptions.Maintenance.CleanupInterval = TimeSpan.FromMinutes(5);
        cacheOptions.Maintenance.MaxCleanupBatchSize = 200;
        cacheOptions.DefaultSlidingExpiration = TimeSpan.FromMinutes(15);
    }
);
```

### Azure Managed Identity Requirements

1. **Base Connection String**: Must NOT include a password/token
2. **Azure Setup**: Enable system-assigned or user-assigned managed identity
3. **Permissions**: Grant managed identity access to PostgreSQL server
4. **Environment**: Must run on Azure (App Service, VM, AKS, etc.)
5. **Network**: Access to Azure Instance Metadata Service (IMDS)

### Token Refresh Behavior

- **Automatic Refresh**: Tokens are refreshed 1 hour before expiration (configurable)
- **Retry Logic**: Failed token acquisition is retried with exponential backoff
- **Connection Pool**: Pool is recreated when tokens are refreshed
- **Monitoring**: Token refresh events are logged at Information level

### Health Check Example

```csharp
app.MapGet("/health/azure-cache", async (IDistributedCache cache) =>
{
    try
    {
        var testKey = $"health-check-{Guid.NewGuid()}";
        var testValue = DateTime.UtcNow.ToString("O");

        await cache.SetStringAsync(testKey, testValue, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var retrievedValue = await cache.GetStringAsync(testKey);

        if (retrievedValue == testValue)
        {
            await cache.RemoveAsync(testKey);
            return Results.Ok(new { status = "healthy", message = "Azure Managed Identity cache is working" });
        }

        return Results.Problem("Cache value mismatch", statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Azure Managed Identity cache health check failed: {ex.Message}", statusCode: 500);
    }
});
```

## Testing

Use the provided test container setup for integration tests:

```csharp
[Fact]
public async Task CustomTest()
{
    using var postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    await postgres.StartAsync();

    var services = new ServiceCollection();
    services.AddGlacialCachePostgreSQL(postgres.GetConnectionString());

    var provider = services.BuildServiceProvider();
    var cache = provider.GetRequiredService<IDistributedCache>();

    // Your test logic here
}
```

## License

MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.



