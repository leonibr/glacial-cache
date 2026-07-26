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
    value_type TEXT,
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
| `JsonBytes`     | JSON serialization with optimizations (default) | High        | Interoperability, debugging, simple objects    |
| `MemoryPack`    | Fast binary serialization             | Highest     | High-performance applications, complex objects |

### String and Byte Array Optimization

Both serializers include automatic optimizations:

- **Strings**: Always use direct UTF-8 encoding (no serialization overhead)
- **Byte Arrays**: Pass-through without modification
- **Complex Objects**: Use configured serializer

### Configuration Examples

```csharp
// Use JsonBytes for compatibility (default)
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;
    options.Cache.Serializer = SerializerType.JsonBytes;
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

### Allocation-sensitive batch writes

When an application always reads the same keys immediately after a batch write, use the combined workflow instead of awaiting separate calls:

```csharp
var values = await cache.SetAndGetMultipleAsync(entries, cancellationToken);
```

For up to 1000 entries, `SetAndGetMultipleAsync` commits the complete write batch before performing the read in one PostgreSQL command exchange. A write failure rolls back the full batch; a read failure occurs after the writes have committed. Larger inputs safely fall back to the existing chunked write followed by the batch read.

The regular `SetMultipleAsync` overload for `ReadOnlyMemory<byte>` snapshots payloads and is the recommended default. When payload-copy allocations are a measured bottleneck, `SetMultipleDirectAsync` provides an explicit opt-in path:

```csharp
using var owner = MemoryPool<byte>.Shared.Rent(payloadLength);
FillPayload(owner.Memory.Span[..payloadLength]);

var entries = new Dictionary<string, (ReadOnlyMemory<byte>, DistributedCacheEntryOptions)>
{
    ["catalog:payload"] = (owner.Memory[..payloadLength], new DistributedCacheEntryOptions())
};

await cache.SetMultipleDirectAsync(entries, cancellationToken);
```

Keep every backing buffer alive, immutable, and undisposed until the awaited call completes, including cancellation and failure. Never return pooled memory or reuse its storage while the operation is still running. This API avoids explicit application payload copies but does not imply end-to-end zero-copy behavior inside Npgsql or PostgreSQL.

Use the regular snapshot overload when buffer ownership is uncertain; use the direct overload only after allocation measurements justify its stricter lifetime contract.

### Typed Cache Operations

GlacialCache provides strongly-typed operations for type safety and automatic serialization:

```csharp
public class CatalogService
{
    private readonly IGlacialCache _cache;

    public CatalogService(IGlacialCache cache)
    {
        _cache = cache;
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        var key = $"product:{id}";

        // Strongly-typed retrieval with rich metadata
        var entry = await _cache.GetEntryAsync<Product>(key);

        if (entry != null)
        {
            _logger.LogInformation(
                "Cache hit for {Key}. Size: {Size} bytes, Type: {Type}",
                entry.Key,
                entry.SizeInBytes,
                entry.BaseType);

            return entry.Value;
        }

        // Fetch and cache
        var product = await _database.GetProductAsync(id);
        if (product == null) return null;

        await _cache.SetEntryAsync(key, product, new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            SlidingExpiration = TimeSpan.FromMinutes(15)
        });

        return product;
    }

    public async Task<Dictionary<int, Product>> GetProductBatchAsync(int[] ids)
    {
        var keys = ids.Select(id => $"product:{id}");

        // Batch typed retrieval - single database round-trip
        var entries = await _cache.GetMultipleEntriesAsync<Product>(keys);

        return entries
            .Where(kvp => kvp.Value != null)
            .ToDictionary(
                kvp => int.Parse(kvp.Key.Split(':')[1]),
                kvp => kvp.Value!.Value);
    }
}
```

### Type Safety Benefits

```csharp
// Type mismatch protection
await cache.SetEntryAsync("key1", new Product { Id = 1 });

// Returns null - type mismatch detected
var order = await cache.GetEntryAsync<Order>("key1");

// Returns Product - correct type
var product = await cache.GetEntryAsync<Product>("key1");
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
// Install: Microsoft.Extensions.Configuration.AzureAppConfiguration
// Add Azure App Configuration provider
// Refer to Azure App Configuration documentation for exact API
builder.Configuration.AddAzureAppConfiguration(/* connection string or options */);

// Configure refresh for GlacialCache section
// The exact API depends on your package version
```

**Configuration in Azure App Configuration**:

```json
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

**Note**: Refer to the [Azure App Configuration documentation](https://learn.microsoft.com/azure/azure-app-configuration/) for the current API and setup instructions.

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

### How It Works: ObservableProperty Pattern

GlacialCache uses the `ObservableProperty<T>` pattern for internal change propagation:

```csharp
// Internal implementation (simplified for illustration)
public class CacheOptions
{
    private ObservableProperty<string> _tableName;

    public string TableName
    {
        get => _tableName.Value;
        set => _tableName.Value = value;
    }

    // When external config changes via IOptionsMonitor
    internal void SyncFromExternalChanges(CacheOptions newOptions, ILogger logger)
    {
        _tableName.Value = newOptions.TableName; // Triggers PropertyChanged event
    }
}
```

**Key Benefits**:

- **Automatic propagation**: Changes flow from `IOptionsMonitor` → `ObservableProperty` → dependent services
- **Resource recreation**: Services subscribe to property changes and recreate resources (connections, SQL queries)
- **Thread-safe**: All updates are synchronized
- **Logging**: Every change is logged for observability

### Monitoring Configuration Changes

Configuration changes are logged at Information level:

```
info: GlacialCache.PostgreSQL[0]
      Connection string changed from 'Host=old-db.example.com;Username=user;Password=***' to 'Host=new-db.example.com;Username=user;Password=***'

info: GlacialCache.PostgreSQL[0]
      Configuration property 'Cache.TableName' changed from 'old_table' to 'new_table', rebuilding SQL
```

**Security Note:** Connection strings are automatically masked in logs to prevent exposure of sensitive information like passwords and tokens.

### Troubleshooting Configuration Reloading

**Configuration not reloading?**

1. **Check your configuration provider supports reloading**:

   ```csharp
   // appsettings.json - enable reloadOnChange
   builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
   ```

2. **Verify IOptionsMonitor is being used** (not IOptions):

   ```csharp
   // ✅ Correct - supports reloading
   services.Configure<GlacialCachePostgreSQLOptions>(config.GetSection("GlacialCache"));

   // ❌ Wrong - snapshot only, no reloading
   services.Configure<GlacialCachePostgreSQLOptions>(options => { /* ... */ });
   ```

3. **Check logs for configuration change events**:

   ```json
   {
     "Logging": {
       "LogLevel": {
         "GlacialCache.PostgreSQL": "Information"
       }
     }
   }
   ```

4. **Non-reloadable properties**: Some properties require restart:
   - `Infrastructure.CreateInfrastructure`
   - `Infrastructure.EnableManagerElection`
   - `Cache.Serializer`

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
