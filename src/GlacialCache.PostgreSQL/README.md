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
✅ **Multi-framework**: Supports .NET 8.0, 9.0, and 10.0  
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

| Option                                   | Description                  | Default         |
| ---------------------------------------- | ---------------------------- | --------------- |
| `Connection.ConnectionString`            | PostgreSQL connection string | _Required_      |
| `TableName`                              | Cache table name             | `glacial_cache` |
| `SchemaName`                             | Database schema              | `public`        |
| `Maintenance.EnableAutomaticCleanup`     | Enable periodic cleanup      | `true`          |
| `Maintenance.CleanupInterval`            | Cleanup frequency            | 30 minutes      |
| `Maintenance.MaxCleanupBatchSize`        | Max items per cleanup batch  | 1000            |
| `DefaultSlidingExpiration`               | Default sliding expiration   | `null`          |
| `DefaultAbsoluteExpirationRelativeToNow` | Default absolute expiration  | `null`          |

## Database Schema

GlacialCache automatically creates the following table structure:

```sql
CREATE TABLE public.glacial_cache (
    key VARCHAR(900) PRIMARY KEY,
    value BYTEA NOT NULL,
    absolute_expiration TIMESTAMPTZ,
    sliding_interval INTERVAL,
    next_expiration TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    value_type VARCHAR(255),
    value_size INTEGER GENERATED ALWAYS AS (OCTET_LENGTH(value)) STORED
);

-- Indexes for efficient cleanup
CREATE INDEX idx_glacial_cache_absolute_expiration
ON public.glacial_cache (absolute_expiration)
WHERE absolute_expiration IS NOT NULL;

CREATE INDEX idx_glacial_cache_next_expiration
ON public.glacial_cache (next_expiration);

CREATE INDEX idx_glacial_cache_value_type
ON public.glacial_cache (value_type)
WHERE value_type IS NOT NULL;

CREATE INDEX idx_glacial_cache_value_size
ON public.glacial_cache (value_size);
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

## Reloadable Configuration

GlacialCache supports runtime configuration changes without requiring application restarts. The cache automatically reloads when configuration values change, using `IOptionsMonitor` for external configuration changes and `ObservableProperty<T>` for observable properties.

### Supported Reloadable Properties

- **Connection String**: Automatically recreates the database connection pool (masked in logs for security)
- **Table Name**: Rebuilds SQL queries for cache operations
- **Schema Name**: Rebuilds SQL queries for cache operations
- **Connection Pool Settings**: Updates pool size limits and pruning behavior
- **Cleanup Settings**: Adjusts maintenance intervals and batch sizes

### Real-World Use Cases

#### 1. Database Failover and Disaster Recovery

Switch to a backup database when the primary fails without downtime:

```csharp
// Configuration that can be changed at runtime
{
  "GlacialCache": {
    "Connection": {
      "ConnectionString": "Host=primary-db.example.com;Database=cache;Username=user;Password=pass"
    }
  }
}

// Update to backup during failover (via Azure App Configuration, environment variables, etc.)
{
  "GlacialCache": {
    "Connection": {
      "ConnectionString": "Host=backup-db.example.com;Database=cache;Username=user;Password=pass"
    }
  }
}
```

#### 2. Security Credential Rotation

Update connection strings during password rotation policies:

```csharp
// In appsettings.json or Azure App Configuration
{
  "GlacialCache": {
    "Connection": {
      "ConnectionString": "Host=db.example.com;Database=cache;Username=user;Password=current-password"
    }
  }
}

// Rotate password without restart - update the configuration source
// GlacialCache automatically reconnects with new credentials
```

#### 3. Azure App Configuration Integration

Use Azure App Configuration for centralized cache management across microservices:

```csharp
// Program.cs
builder.Configuration.AddAzureAppConfiguration(options =>
{
    options.Connect("Endpoint=https://my-app-config.azconfig.io;...");
    options.ConfigureRefresh(refreshOptions =>
    {
        refreshOptions.Register("GlacialCache", refreshAll: true);
    });
});

// Configuration in Azure App Configuration
{
  "GlacialCache": {
    "Cache": {
      "TableName": "shared_cache",
      "SchemaName": "cache_schema",
      "DefaultSlidingExpiration": "00:30:00"
    },
    "Maintenance": {
      "CleanupInterval": "00:15:00",
      "MaxCleanupBatchSize": 500
    }
  }
}
```

### Security Configuration

Configure connection string masking behavior:

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Configure connection string masking in logs (default: enabled)
    options.Security.ConnectionString.MaskInLogs = true; // Mask sensitive parameters
    options.Security.ConnectionString.SensitiveParameters = new[] { "Password", "Token", "Key" };
});
```

### Configuration Providers

Reloadable configuration works with any .NET configuration provider:

- **appsettings.json**: File-based configuration
- **Environment Variables**: Container and deployment environments
- **Azure App Configuration**: Centralized cloud configuration
- **Key Vault**: Secure credential management
- **User Secrets**: Development-time secrets

### Monitoring Configuration Changes

Configuration changes are logged at Information level:

```
info: GlacialCache.PostgreSQL[0]
      Connection string changed from 'Host=old-db.example.com;Username=user;Password=***' to 'Host=new-db.example.com;Username=user;Password=***'

info: GlacialCache.PostgreSQL[0]
      Configuration property 'Cache.TableName' changed from 'old_table' to 'new_table', rebuilding SQL
```

**Security Note:** Connection strings are automatically masked in logs to prevent exposure of sensitive information like passwords and tokens.

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
