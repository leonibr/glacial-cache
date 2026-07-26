# GlacialCache PostgreSQL Configuration Reference

`GlacialCachePostgreSQLOptions` is the central configuration class for GlacialCache PostgreSQL. It groups all settings into logical sections and provides comprehensive validation.

## Quick Start

Configure GlacialCache in your `Program.cs`:

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = builder.Configuration.GetConnectionString("GlacialCache")
        ?? "Host=localhost;Database=glacialcache;Username=postgres;Password=postgres";

    // Using default schema and table names
    options.Cache.SchemaName = "public";       // default
    options.Cache.TableName = "glacial_cache";   // default
});
```

## Configuration Overview

`GlacialCachePostgreSQLOptions` contains these main sections:

- **`Connection`**: Database connectivity, connection pooling, and timeouts
- **`Cache`**: Table/schema names, expiration defaults, and serialization
- **`Maintenance`**: Background cleanup behavior
- **`Resilience`**: Retry policies, circuit breakers, and fault tolerance
- **`Infrastructure`**: Schema creation and multi-instance coordination
- **`Security`**: Connection string masking and audit logging ⚠️
- **`Monitoring`**: Metrics collection and health checks ⚠️

**⚠️ Implementation Status**: Some features are planned but not yet implemented. See [Implementation Status](#implementation-status--planned-features) below.

---

## Connection Options

**Type**: `ConnectionOptions`  
**Path**: `options.Connection.*`  
**Purpose**: Database connectivity, connection pooling, and timeout behavior

### Properties

| Property                      | Type       | Default      | Validation                        | Reloadable | Status     |
| ----------------------------- | ---------- | ------------ | --------------------------------- | ---------- | ---------- |
| `ConnectionString`            | `string`   | _(required)_ | Min 10 chars, valid Npgsql format | ✅ Yes     | ✅ Working |
| `Pool.MaxSize`                | `int`      | `50`         | 1-1000                            | ✅ Yes     | ✅ Working |
| `Pool.MinSize`                | `int`      | `5`          | 1-100, ≤ MaxSize                  | ✅ Yes     | ✅ Working |
| `Pool.IdleLifetimeSeconds`    | `int`      | `300`        | > 0                               | ✅ Yes     | ✅ Working |
| `Pool.PruningIntervalSeconds` | `int`      | `10`         | > 0                               | ✅ Yes     | ✅ Working |
| `Timeouts.OperationTimeout`   | `TimeSpan` | `00:00:30`   | > 0                               | ❌ No      | ✅ Working |
| `Timeouts.ConnectionTimeout`  | `TimeSpan` | `00:00:30`   | > 0                               | ❌ No      | ✅ Working |
| `Timeouts.CommandTimeout`     | `TimeSpan` | `00:00:30`   | > 0                               | ❌ No      | ✅ Working |

### Detailed Property Reference

#### `ConnectionString` ✅ **REQUIRED**

- **Type**: `string`
- **Default**: _(none - required)_
- **Validation**: Must be at least 10 characters, valid Npgsql connection string format
- **When to Use**: Always required. Use configuration providers for different environments
- **Impact**: Determines database target. Reloadable for failover scenarios
- **Example**: `"Host=localhost;Database=cache;Username=postgres;Password=secret"`

#### `Pool.MaxSize` ✅

- **Type**: `int`
- **Default**: `50`
- **Validation**: 1-1000, must be ≥ MinSize
- **When to Use**: High-traffic applications. Increase for better concurrency
- **Impact**: Higher values increase memory usage but improve performance under load
- **Example**: `100` for production workloads

#### `Pool.MinSize` ✅

- **Type**: `int`
- **Default**: `5`
- **Validation**: 1-100, must be ≤ MaxSize
- **When to Use**: Applications with steady load. Reduces connection churn
- **Impact**: Keeps connections ready, reduces latency for first requests
- **Example**: `10` for production environments

#### `Pool.IdleLifetimeSeconds` ✅

- **Type**: `int`
- **Default**: `300` (5 minutes)
- **Validation**: Must be positive
- **When to Use**: Long-running applications. Adjust based on connection limits
- **Impact**: Balances memory usage vs connection availability
- **Example**: `600` for applications with infrequent requests

#### `Pool.PruningIntervalSeconds` ✅

- **Type**: `int`
- **Default**: `10`
- **Validation**: Must be positive
- **When to Use**: Fine-tune connection pool maintenance frequency
- **Impact**: Lower values = more frequent pruning = better memory management
- **Example**: `30` for reduced maintenance overhead

#### `Timeouts.OperationTimeout` ✅

- **Type**: `TimeSpan`
- **Default**: `00:00:30` (30 seconds)
- **Validation**: Must be positive
- **When to Use**: High-latency networks or slow queries
- **Impact**: Maximum time for cache operations (separate from resilience timeouts)
- **Example**: `TimeSpan.FromSeconds(5)` for fast networks

#### `Timeouts.ConnectionTimeout` ✅

- **Type**: `TimeSpan`
- **Default**: `00:00:30` (30 seconds)
- **Validation**: Must be positive
- **When to Use**: Slow network connections
- **Impact**: How long to wait for initial database connection
- **Example**: `TimeSpan.FromSeconds(10)` for reliable networks

#### `Timeouts.CommandTimeout` ✅

- **Type**: `TimeSpan`
- **Default**: `00:00:30` (30 seconds)
- **Validation**: Must be positive
- **When to Use**: Complex queries or slow database performance
- **Impact**: Maximum execution time for individual SQL commands
- **Example**: `TimeSpan.FromSeconds(15)` for complex operations

### Configuration Examples

#### Development Setup

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = "Host=localhost;Database=glacialcache;Username=postgres;Password=postgres";

    // Smaller pool for development
    options.Connection.Pool.MinSize = 1;
    options.Connection.Pool.MaxSize = 10;

    // Longer timeouts for debugging
    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromMinutes(1);
    options.Connection.Timeouts.CommandTimeout = TimeSpan.FromMinutes(1);
});
```

#### Production Setup

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = builder.Configuration.GetConnectionString("GlacialCache")
        ?? throw new InvalidOperationException("GlacialCache connection string is required");

    // Larger pool for production
    options.Connection.Pool.MinSize = 10;
    options.Connection.Pool.MaxSize = 100;
    options.Connection.Pool.IdleLifetimeSeconds = 600; // 10 minutes

    // Tighter timeouts for performance
    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(5);
    options.Connection.Timeouts.CommandTimeout = TimeSpan.FromSeconds(5);
    options.Connection.Timeouts.ConnectionTimeout = TimeSpan.FromSeconds(5);
});
```

#### High-Performance Setup

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Large pool for high throughput
    options.Connection.Pool.MinSize = 20;
    options.Connection.Pool.MaxSize = 200;

    // Short idle lifetime for rapid scaling
    options.Connection.Pool.IdleLifetimeSeconds = 60; // 1 minute
    options.Connection.Pool.PruningIntervalSeconds = 5; // More frequent pruning

    // Fast timeouts for low latency
    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(2);
    options.Connection.Timeouts.CommandTimeout = TimeSpan.FromSeconds(2);
});
```

---

## Cache Options

**Type**: `CacheOptions`  
**Path**: `options.Cache.*`  
**Purpose**: Database schema/table configuration, expiration defaults, and serialization settings

### Properties

| Property                                 | Type             | Default                     | Validation                             | Reloadable | Status     |
| ---------------------------------------- | ---------------- | --------------------------- | -------------------------------------- | ---------- | ---------- |
| `TableName`                              | `string`         | `"glacial_cache"`           | Valid PostgreSQL identifier            | ✅ Yes     | ✅ Working |
| `SchemaName`                             | `string`         | `"public"`                  | Valid PostgreSQL identifier            | ✅ Yes     | ✅ Working |
| `DefaultSlidingExpiration`               | `TimeSpan?`      | `null`                      | ≥ MinExpirationInterval                | ❌ No      | ✅ Working |
| `DefaultAbsoluteExpirationRelativeToNow` | `TimeSpan?`      | `null`                      | ≥ MinExpirationInterval                | ❌ No      | ✅ Working |
| `MinimumExpirationInterval`              | `TimeSpan`       | `00:00:00.001`              | > 0                                    | ❌ No      | ✅ Working |
| `MaximumExpirationInterval`              | `TimeSpan`       | `365.00:00:00`              | > MinExpirationInterval                | ❌ No      | ✅ Working |
| `EnableEdgeCaseLogging`                  | `bool`           | `true`                      | -                                      | ❌ No      | ✅ Working |
| `Serializer`                             | `SerializerType` | `SerializerType.MemoryPack` | -                                      | ❌ No      | ✅ Working |
| `CustomSerializerType`                   | `Type?`          | `null`                      | Must implement `ICacheEntrySerializer` | ❌ No      | ✅ Working |

### Detailed Property Reference

#### `TableName` ✅

- **Type**: `string`
- **Default**: `"glacial_cache"`
- **Validation**: Valid PostgreSQL identifier (starts with letter/underscore, contains only letters/digits/underscores, max 63 bytes)
- **When to Use**: Multi-tenant applications, custom naming conventions, or avoiding conflicts
- **Impact**: Reloadable - SQL queries regenerate automatically when changed
- **Example**: `"cache_entries"` or `"tenant_cache"`

#### `SchemaName` ✅

- **Type**: `string`
- **Default**: `"public"`
- **Validation**: Valid PostgreSQL identifier (same rules as TableName)
- **When to Use**: Organizing cache tables in separate schemas for security or organization
- **Impact**: Reloadable - SQL queries regenerate automatically when changed
- **Example**: `"cache_schema"` or `"app_cache"`

#### `DefaultSlidingExpiration` ✅

- **Type**: `TimeSpan?`
- **Default**: `null` (no sliding expiration)
- **Validation**: Must be ≥ MinimumExpirationInterval if set
- **When to Use**: Entries should expire after periods of inactivity
- **Impact**: Applied to entries that don't specify their own sliding expiration
- **Example**: `TimeSpan.FromMinutes(20)` for 20-minute sliding expiration

#### `DefaultAbsoluteExpirationRelativeToNow` ✅

- **Type**: `TimeSpan?`
- **Default**: `null` (no absolute expiration)
- **Validation**: Must be ≥ MinimumExpirationInterval if set
- **When to Use**: Entries should expire at a specific time regardless of access
- **Impact**: Applied to entries that don't specify their own absolute expiration
- **Example**: `TimeSpan.FromHours(1)` for 1-hour absolute expiration

#### `MinimumExpirationInterval` ✅

- **Type**: `TimeSpan`
- **Default**: `00:00:00.001` (1 millisecond)
- **Validation**: Must be > 0 and < MaximumExpirationInterval
- **When to Use**: Prevent accidentally setting very short expirations that could cause performance issues
- **Impact**: Very short expiration values are clamped to this minimum
- **Example**: `TimeSpan.FromSeconds(1)` to prevent sub-second expirations

#### `MaximumExpirationInterval` ✅

- **Type**: `TimeSpan`
- **Default**: `365.00:00:00` (1 year)
- **Validation**: Must be > MinimumExpirationInterval
- **When to Use**: Prevent accidentally setting very long expirations that could waste storage
- **Impact**: Very long expiration values are clamped to this maximum
- **Example**: `TimeSpan.FromDays(30)` for maximum 30-day expiration

#### `EnableEdgeCaseLogging` ✅

- **Type**: `bool`
- **Default**: `true`
- **Validation**: None
- **When to Use**: Monitor when expiration values are being clamped to min/max values
- **Impact**: Logs warnings when expiration intervals are adjusted due to guardrails
- **Example**: `false` to reduce log noise in production

#### `Serializer` ✅

- **Type**: `SerializerType` enum
- **Default**: `SerializerType.JsonBytes`
- **Validation**: Must be valid enum value
- **When to Use**:
  - `JsonBytes`: Interoperability with other languages/tools (default)
  - `MemoryPack`: High performance, .NET-focused applications
  - `Custom`: Specialized serialization requirements
- **Impact**: Affects storage size, performance, and compatibility. Not reloadable.
- **Example**: `SerializerType.MemoryPack` for maximum performance

#### `CustomSerializerType` ✅

- **Type**: `Type?`
- **Default**: `null`
- **Validation**: Must implement `ICacheEntrySerializer` interface when Serializer is Custom
- **When to Use**: Custom serialization logic required (encryption, compression, etc.)
- **Impact**: Allows complete control over how objects are serialized/deserialized
- **Example**: `typeof(MyEncryptedSerializer)`

### Configuration Examples

#### Basic Configuration

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Use default schema/table (recommended for simple apps)
    // options.Cache.SchemaName = "public";  // default
    // options.Cache.TableName = "glacial_cache";  // default
});
```

#### Custom Schema/Table with Expiration

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Custom schema and table
    options.Cache.SchemaName = "cache_schema";
    options.Cache.TableName = "entries";

    // Default expirations for entries that don't specify their own
    options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
    options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2);

    // Guardrails for expiration values
    options.Cache.MinimumExpirationInterval = TimeSpan.FromSeconds(5);
    options.Cache.MaximumExpirationInterval = TimeSpan.FromDays(7);
});
```

#### JSON Serialization for Debugging

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Use JSON for easier debugging and cross-platform compatibility
    options.Cache.Serializer = SerializerType.JsonBytes;

    // Custom schema for organization
    options.Cache.SchemaName = "cache";
    options.Cache.TableName = "entries";
});
```

#### Custom Serializer Implementation

```csharp
// Custom serializer implementation
public class MyCustomSerializer : ICacheEntrySerializer
{
    public byte[] Serialize<T>(T value) => /* custom logic */;
    public T Deserialize<T>(byte[] data) => /* custom logic */;
}

builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Use custom serializer
    options.Cache.Serializer = SerializerType.Custom;
    options.Cache.CustomSerializerType = typeof(MyCustomSerializer);
});
```

---

## Maintenance Options

**Type**: `MaintenanceOptions`  
**Path**: `options.Maintenance.*`  
**Purpose**: Background cleanup and maintenance of expired cache entries

### Properties

| Property                 | Type       | Default             | Validation | Reloadable | Status     |
| ------------------------ | ---------- | ------------------- | ---------- | ---------- | ---------- |
| `EnableAutomaticCleanup` | `bool`     | `true`              | -          | ✅ Yes     | ✅ Working |
| `CleanupInterval`        | `TimeSpan` | `00:30:00` (30 min) | > 0        | ✅ Yes     | ✅ Working |
| `MaxCleanupBatchSize`    | `int`      | `1000`              | 1-10000    | ✅ Yes     | ✅ Working |

### Detailed Property Reference

#### `EnableAutomaticCleanup` ✅

- **Type**: `bool`
- **Default**: `true`
- **Validation**: None
- **When to Use**:
  - `true`: Automatic background cleanup (recommended for production)
  - `false`: Manual cleanup or external cleanup processes
- **Impact**: Controls whether expired entries are automatically removed
- **Example**: `false` for development or when using external cleanup jobs

#### `CleanupInterval` ✅

- **Type**: `TimeSpan`
- **Default**: `00:30:00` (30 minutes)
- **Validation**: Must be positive
- **When to Use**: Balance between cleanup frequency and database load
- **Impact**: How often expired entries are removed. Reloadable at runtime.
- **Example**: `TimeSpan.FromMinutes(5)` for aggressive cleanup

#### `MaxCleanupBatchSize` ✅

- **Type**: `int`
- **Default**: `1000`
- **Validation**: 1-10000
- **When to Use**: Control cleanup batch size to manage database load
- **Impact**: Larger batches = faster cleanup but higher database load
- **Example**: `500` for less aggressive cleanup batches

### Configuration Examples

#### Default Maintenance (Recommended)

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Use default maintenance settings (recommended)
    // Automatic cleanup enabled, runs every 30 minutes, 1000 entries per batch
});
```

#### Aggressive Cleanup

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // More frequent cleanup for time-sensitive data
    options.Maintenance.EnableAutomaticCleanup = true;
    options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(5);
    options.Maintenance.MaxCleanupBatchSize = 500; // Smaller batches
});
```

#### Conservative Cleanup

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Less frequent cleanup to reduce database load
    options.Maintenance.EnableAutomaticCleanup = true;
    options.Maintenance.CleanupInterval = TimeSpan.FromHours(2);
    options.Maintenance.MaxCleanupBatchSize = 2000; // Larger batches
});
```

#### Manual Cleanup (Development)

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Disable automatic cleanup for development
    // Use manual cleanup or external processes
    options.Maintenance.EnableAutomaticCleanup = false;
});
```

### How Maintenance Works

1. **Background Service**: Runs on a timer based on `CleanupInterval`
2. **Manager Election**: In multi-instance setups, only one instance performs cleanup
3. **Batch Processing**: Deletes expired entries in batches of `MaxCleanupBatchSize`
4. **Continuation**: Continues until all expired entries are removed
5. **Logging**: Logs cleanup progress and any errors

### Best Practices

- **Production**: Keep automatic cleanup enabled with reasonable intervals
- **High-Traffic**: Use shorter intervals and smaller batch sizes
- **Low-Traffic**: Use longer intervals and larger batch sizes
- **Development**: Consider disabling automatic cleanup for manual control

---

## Resilience Options

**Type**: `ResilienceOptions`  
**Path**: `options.Resilience.*`  
**Purpose**: Fault tolerance, retry policies, and circuit breaker patterns using Polly

### Properties

| Property                            | Type              | Default                 | Validation | Reloadable | Status     |
| ----------------------------------- | ----------------- | ----------------------- | ---------- | ---------- | ---------- |
| `EnableResiliencePatterns`          | `bool`            | `true`                  | -          | ❌ No      | ✅ Working |
| `Retry.MaxAttempts`                 | `int`             | `3`                     | 0-10       | ❌ No      | ✅ Working |
| `Retry.BaseDelay`                   | `TimeSpan`        | `00:00:01`              | > 0        | ❌ No      | ✅ Working |
| `Retry.BackoffStrategy`             | `BackoffStrategy` | `ExponentialWithJitter` | -          | ❌ No      | ✅ Working |
| `CircuitBreaker.Enable`             | `bool`            | `true`                  | -          | ❌ No      | ✅ Working |
| `CircuitBreaker.FailureThreshold`   | `int`             | `5`                     | 1-100      | ❌ No      | ✅ Working |
| `CircuitBreaker.DurationOfBreak`    | `TimeSpan`        | `00:01:00`              | > 0        | ❌ No      | ✅ Working |
| `Timeouts.OperationTimeout`         | `TimeSpan`        | `00:00:30`              | > 0        | ❌ No      | ✅ Working |
| `Logging.EnableResilienceLogging`   | `bool`            | `true`                  | -          | ❌ No      | ✅ Working |
| `Logging.ConnectionFailureLogLevel` | `LogLevel`        | `Warning`               | -          | ❌ No      | ✅ Working |

### Detailed Property Reference

#### `EnableResiliencePatterns` ✅

- **Type**: `bool`
- **Default**: `true`
- **Validation**: None
- **When to Use**:
  - `true`: Enable Polly-based resilience patterns (recommended)
  - `false`: Disable all resilience features for debugging
- **Impact**: Master switch for all resilience behaviors
- **Example**: `false` for development debugging

#### `Retry.MaxAttempts` ✅

- **Type**: `int`
- **Default**: `3`
- **Validation**: 0-10
- **When to Use**: Control retry behavior for transient failures
- **Impact**: Higher values = more resilient but potentially slower
- **Example**: `5` for unreliable networks

#### `Retry.BaseDelay` ✅

- **Type**: `TimeSpan`
- **Default**: `00:00:01` (1 second)
- **Validation**: Must be positive
- **When to Use**: Set base delay between retry attempts
- **Impact**: Affects retry frequency and overall operation timeout
- **Example**: `TimeSpan.FromMilliseconds(500)` for faster retries

#### `Retry.BackoffStrategy` ✅

- **Type**: `BackoffStrategy` enum
- **Default**: `ExponentialWithJitter`
- **Validation**: Must be valid enum value
- **When to Use**:
  - `ExponentialWithJitter`: Prevents thundering herd (recommended)
  - `Linear`: Simple linear backoff
  - `Exponential`: Exponential backoff without jitter
- **Impact**: How retry delays increase over attempts
- **Example**: `BackoffStrategy.ExponentialWithJitter` (default)

#### `CircuitBreaker.Enable` ✅

- **Type**: `bool`
- **Default**: `true`
- **Validation**: None
- **When to Use**:
  - `true`: Protect database from cascading failures
  - `false`: Disable circuit breaker for debugging
- **Impact**: Prevents overwhelming failing services
- **Example**: `true` for production resilience

#### `CircuitBreaker.FailureThreshold` ✅

- **Type**: `int`
- **Default**: `5`
- **Validation**: 1-100
- **When to Use**: Control sensitivity of circuit breaker
- **Impact**: Higher values = more tolerant of failures before opening
- **Example**: `10` for high-traffic applications

#### `CircuitBreaker.DurationOfBreak` ✅

- **Type**: `TimeSpan`
- **Default**: `00:01:00` (1 minute)
- **Validation**: Must be positive
- **When to Use**: Control how long circuit stays open after failures
- **Impact**: Recovery time after service issues
- **Example**: `TimeSpan.FromMinutes(2)` for slower recovery

#### `Timeouts.OperationTimeout` ✅

- **Type**: `TimeSpan`
- **Default**: `00:00:30` (30 seconds)
- **Validation**: Must be positive
- **When to Use**: Overall timeout for cache operations at resilience layer
- **Impact**: Maximum time including retries (separate from connection timeouts)
- **Example**: `TimeSpan.FromSeconds(10)` for faster failure detection

#### `Logging.EnableResilienceLogging` ✅

- **Type**: `bool`
- **Default**: `true`
- **Validation**: None
- **When to Use**:
  - `true`: Log resilience events for observability
  - `false`: Reduce log noise
- **Impact**: Controls logging of retry attempts and circuit breaker state changes
- **Example**: `false` for high-volume logging reduction

#### `Logging.ConnectionFailureLogLevel` ✅

- **Type**: `LogLevel`
- **Default**: `Warning`
- **Validation**: Must be valid LogLevel
- **When to Use**: Control log level for connection failures
- **Impact**: How connection issues are logged
- **Example**: `LogLevel.Error` for production environments

### BackoffStrategy Enum

```csharp
public enum BackoffStrategy
{
    Linear,              // delay * attempt
    Exponential,         // delay * 2^attempt
    ExponentialWithJitter // exponential + randomization
}
```

### Configuration Examples

#### Default Resilience (Recommended)

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Use default resilience settings (recommended for most applications)
    // Resilience enabled, 3 retries, circuit breaker, 30s timeout
});
```

#### High-Resilience Production Setup

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Aggressive retry behavior for unreliable infrastructure
    options.Resilience.EnableResiliencePatterns = true;
    options.Resilience.Retry.MaxAttempts = 5;
    options.Resilience.Retry.BaseDelay = TimeSpan.FromMilliseconds(500);
    options.Resilience.Retry.BackoffStrategy = BackoffStrategy.ExponentialWithJitter;

    // Protective circuit breaker
    options.Resilience.CircuitBreaker.Enable = true;
    options.Resilience.CircuitBreaker.FailureThreshold = 10;
    options.Resilience.CircuitBreaker.DurationOfBreak = TimeSpan.FromMinutes(2);

    // Reasonable operation timeout
    options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(15);

    // Enable resilience logging for monitoring
    options.Resilience.Logging.EnableResilienceLogging = true;
    options.Resilience.Logging.ConnectionFailureLogLevel = LogLevel.Error;
});
```

#### Minimal Resilience (Development)

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Minimal resilience for development/debugging
    options.Resilience.EnableResiliencePatterns = true;
    options.Resilience.Retry.MaxAttempts = 1; // Quick failure
    options.Resilience.Retry.BaseDelay = TimeSpan.FromMilliseconds(100);

    // Keep circuit breaker for safety
    options.Resilience.CircuitBreaker.Enable = true;
    options.Resilience.CircuitBreaker.FailureThreshold = 3;

    // Longer timeout for debugging
    options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromMinutes(1);
});
```

#### No Resilience (Debugging Only)

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Disable resilience for pure debugging scenarios
    options.Resilience.EnableResiliencePatterns = false;
});
```

### How Resilience Works

1. **Retry Policy**: Automatically retries transient failures
2. **Circuit Breaker**: Opens when failure threshold exceeded, preventing further calls
3. **Timeout Policy**: Overall timeout including all retry attempts
4. **Logging**: Events logged for observability and debugging

### Best Practices

- **Production**: Enable resilience with reasonable retry counts and circuit breakers
- **Development**: Reduce retry counts for faster failure detection
- **High-Traffic**: Use exponential backoff with jitter to prevent thundering herd
- **Unreliable Networks**: Increase retry attempts and base delays

---

## Infrastructure Options

**Type**: `InfrastructureOptions`  
**Path**: `options.Infrastructure.*`  
**Purpose**: Database schema creation and multi-instance coordination

### Properties

| Property                | Type       | Default            | Validation | Reloadable | Status     |
| ----------------------- | ---------- | ------------------ | ---------- | ---------- | ---------- |
| `CreateInfrastructure`  | `bool`     | `false`            | -          | ❌ No      | ✅ Working |
| `EnableManagerElection` | `bool`     | `true`             | -          | ❌ No      | ✅ Working |
| `Lock.AdvisoryLockKey`  | `int`      | _(auto-generated)_ | -          | ❌ No      | ✅ Working |
| `Lock.LockTimeout`      | `TimeSpan` | `00:05:00` (5 min) | > 0        | ❌ No      | ✅ Working |

### Detailed Property Reference

#### `CreateInfrastructure` ✅

- **Type**: `bool`
- **Default**: `false`
- **Validation**: None
- **When to Use**:
  - `true`: Auto-create schema/table/indexes (development, simple deployments)
  - `false`: Use migrations or manual schema management (production)
- **Impact**: Controls automatic database schema creation on startup
- **Example**: `true` for development, `false` for production

#### `EnableManagerElection` ✅

- **Type**: `bool`
- **Default**: `true`
- **Validation**: None
- **When to Use**:
  - `true`: Enable coordination for multi-instance deployments
  - `false`: Single-instance or development environments
- **Impact**: Controls whether instances elect a manager for background tasks
- **Example**: `false` for single-instance development

#### `Lock.AdvisoryLockKey` ✅

- **Type**: `int`
- **Default**: Auto-generated from schema/table names
- **Validation**: None (auto-generated)
- **When to Use**: Usually auto-generated, manual override rarely needed
- **Impact**: Unique identifier for advisory locks in PostgreSQL
- **Example**: Auto-generated value based on `schema.table` hash

#### `Lock.LockTimeout` ✅

- **Type**: `TimeSpan`
- **Default**: `00:05:00` (5 minutes)
- **Validation**: Must be positive
- **When to Use**: Control how long to wait for infrastructure locks
- **Impact**: Maximum wait time for schema creation coordination
- **Example**: `TimeSpan.FromMinutes(10)` for complex schema operations

### Configuration Examples

#### Single-Instance Development

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Development: auto-create infrastructure, no election needed
    options.Infrastructure.CreateInfrastructure = true;
    options.Infrastructure.EnableManagerElection = false;
});
```

#### Multi-Instance Production

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Production: use migrations, enable manager election
    options.Infrastructure.CreateInfrastructure = false; // Use migrations
    options.Infrastructure.EnableManagerElection = true;  // Multi-instance coordination
});
```

#### Production with Manual Schema

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Production with external schema management
    options.Infrastructure.CreateInfrastructure = false;
    options.Infrastructure.EnableManagerElection = true;

    // Longer timeout for complex deployments
    options.Infrastructure.Lock.LockTimeout = TimeSpan.FromMinutes(10);
});
```

### How Infrastructure Works

1. **Schema Creation**: When `CreateInfrastructure = true`, creates schema, table, and indexes
2. **Manager Election**: Uses PostgreSQL advisory locks for coordination
3. **Lock Keys**: Auto-generated from schema/table names for uniqueness
4. **Background Tasks**: Only elected manager performs cleanup and maintenance

### Best Practices

- **Development**: `CreateInfrastructure = true`, `EnableManagerElection = false`
- **Production**: `CreateInfrastructure = false` (use migrations), `EnableManagerElection = true`
- **CI/CD**: Disable infrastructure creation, use separate migration steps
- **Multi-tenant**: Ensure unique schema/table combinations for lock key generation

---

## Security Options

**Type**: `SecurityOptions`  
**Path**: `options.Security.*`  
**Purpose**: Connection string masking, token handling, and audit logging ⚠️

### Properties

| Property                               | Type       | Default                        | Validation | Reloadable | Status                 |
| -------------------------------------- | ---------- | ------------------------------ | ---------- | ---------- | ---------------------- |
| `ConnectionString.MaskInLogs`          | `bool`     | `true`                         | -          | ❌ No      | ✅ Working             |
| `ConnectionString.SensitiveParameters` | `string[]` | `["Password", "Token", "Key"]` | -          | ❌ No      | ✅ Working             |
| `Tokens.EncryptInMemory`               | `bool`     | `false`                        | -          | ❌ No      | ⚠️ **NOT IMPLEMENTED** |
| `Tokens.TokenRefreshBuffer`            | `TimeSpan` | `00:05:00` (5 min)             | > 0        | ❌ No      | ⚠️ **NOT IMPLEMENTED** |
| `Audit.EnableAuditLogging`             | `bool`     | `false`                        | -          | ❌ No      | ⚠️ **NOT IMPLEMENTED** |
| `Audit.LogCacheAccessPatterns`         | `bool`     | `false`                        | -          | ❌ No      | ⚠️ **NOT IMPLEMENTED** |

### Detailed Property Reference

#### `ConnectionString.MaskInLogs` ✅

- **Type**: `bool`
- **Default**: `true`
- **Validation**: None
- **When to Use**:
  - `true`: Mask sensitive parameters in logs (recommended)
  - `false`: Show full connection strings (debugging only)
- **Impact**: Prevents credential exposure in application logs
- **Example**: `true` for production security

#### `ConnectionString.SensitiveParameters` ✅

- **Type**: `string[]`
- **Default**: `["Password", "Token", "Key"]`
- **Validation**: None
- **When to Use**: Customize which connection string parameters to mask
- **Impact**: Controls which parameters are replaced with `***` in logs
- **Example**: `["Password", "Token", "Key", "Secret"]` for additional parameters

#### `Tokens.EncryptInMemory` ⚠️ **NOT YET IMPLEMENTED**

- **Type**: `bool`
- **Default**: `false`
- **Validation**: None
- **When to Use**: Planned for encrypting tokens in memory
- **Impact**: Would encrypt authentication tokens in memory
- **Status**: **NOT IMPLEMENTED** - This feature is planned but not yet available

#### `Tokens.TokenRefreshBuffer` ⚠️ **NOT YET IMPLEMENTED**

- **Type**: `TimeSpan`
- **Default**: `00:05:00` (5 minutes)
- **Validation**: Must be positive
- **When to Use**: Planned buffer time before token expiration for refresh
- **Impact**: Would trigger token refresh before expiration
- **Status**: **NOT IMPLEMENTED** - This feature is planned but not yet available

#### `Audit.EnableAuditLogging` ⚠️ **NOT YET IMPLEMENTED**

- **Type**: `bool`
- **Default**: `false`
- **Validation**: None
- **When to Use**: Planned for enabling cache operation audit logging
- **Impact**: Would log all cache operations for compliance
- **Status**: **NOT IMPLEMENTED** - This feature is planned but not yet available

#### `Audit.LogCacheAccessPatterns` ⚠️ **NOT YET IMPLEMENTED**

- **Type**: `bool`
- **Default**: `false`
- **Validation**: None
- **When to Use**: Planned for logging cache access patterns
- **Impact**: Would track cache hit/miss patterns for analytics
- **Status**: **NOT IMPLEMENTED** - This feature is planned but not yet available

### Configuration Examples

#### Basic Security (Working Features)

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Connection string masking (working)
    options.Security.ConnectionString.MaskInLogs = true;
    options.Security.ConnectionString.SensitiveParameters = new[]
    {
        "Password", "Token", "Key", "Secret"
    };
});
```

#### Planned Features (Not Yet Implemented)

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // ⚠️ These features are NOT YET IMPLEMENTED
    // They will have no effect until implemented

    // Planned: Token encryption in memory
    // options.Security.Tokens.EncryptInMemory = true;
    // options.Security.Tokens.TokenRefreshBuffer = TimeSpan.FromMinutes(10);

    // Planned: Audit logging
    // options.Security.Audit.EnableAuditLogging = true;
    // options.Security.Audit.LogCacheAccessPatterns = true;
});
```

### Implementation Status Notes

**⚠️ Not Yet Implemented Features:**

- **Token Encryption**: `Tokens.EncryptInMemory` and `Tokens.TokenRefreshBuffer` are planned but not implemented
- **Audit Logging**: `Audit.EnableAuditLogging` and `Audit.LogCacheAccessPatterns` are planned but not implemented

These configuration options exist for future compatibility but currently have no effect. They are marked as planned features in the roadmap.

---

## Monitoring Options

**Type**: `MonitoringOptions`  
**Path**: `options.Monitoring.*`  
**Purpose**: Metrics collection and health check integration ⚠️

### ⚠️ IMPLEMENTATION STATUS: NOT YET IMPLEMENTED

**All monitoring options are planned features that are NOT YET IMPLEMENTED.** These configuration properties exist for future compatibility but currently have no effect. Metrics collection and health check integration are planned for future releases.

### Properties

| Property                            | Type       | Default                                            | Validation | Reloadable | Status                 |
| ----------------------------------- | ---------- | -------------------------------------------------- | ---------- | ---------- | ---------------------- |
| `Metrics.EnableMetrics`             | `bool`     | `true`                                             | -          | ❌ No      | ⚠️ **NOT IMPLEMENTED** |
| `Metrics.MetricsCollectionInterval` | `TimeSpan` | `00:01:00` (1 min)                                 | > 0        | ❌ No      | ⚠️ **NOT IMPLEMENTED** |
| `Metrics.EnabledMetrics`            | `string[]` | `["CacheHits", "CacheMisses", "OperationLatency"]` | -          | ❌ No      | ⚠️ **NOT IMPLEMENTED** |
| `HealthChecks.EnableHealthChecks`   | `bool`     | `true`                                             | -          | ❌ No      | ⚠️ **NOT IMPLEMENTED** |
| `HealthChecks.HealthCheckInterval`  | `TimeSpan` | `00:00:30` (30 sec)                                | > 0        | ❌ No      | ⚠️ **NOT IMPLEMENTED** |
| `HealthChecks.HealthCheckTimeout`   | `TimeSpan` | `00:00:10` (10 sec)                                | > 0        | ❌ No      | ⚠️ **NOT IMPLEMENTED** |

### Planned Features (Not Yet Available)

#### `Metrics.EnableMetrics` ⚠️ **NOT IMPLEMENTED**

- **Type**: `bool`
- **Default**: `true`
- **Planned**: Toggle collection of internal performance metrics
- **Future Impact**: Enable/disable metrics collection for monitoring systems

#### `Metrics.MetricsCollectionInterval` ⚠️ **NOT IMPLEMENTED**

- **Type**: `TimeSpan`
- **Default**: `00:01:00` (1 minute)
- **Planned**: How frequently metrics are collected and aggregated
- **Future Impact**: Control metrics collection frequency

#### `Metrics.EnabledMetrics` ⚠️ **NOT IMPLEMENTED**

- **Type**: `string[]`
- **Default**: `["CacheHits", "CacheMisses", "OperationLatency"]`
- **Planned**: Which metrics to collect (CacheHits, CacheMisses, OperationLatency, etc.)
- **Future Impact**: Selective metrics collection for performance monitoring

#### `HealthChecks.EnableHealthChecks` ⚠️ **NOT IMPLEMENTED**

- **Type**: `bool`
- **Default**: `true`
- **Planned**: Enable ASP.NET Core health check integration
- **Future Impact**: Participate in application health monitoring

#### `HealthChecks.HealthCheckInterval` ⚠️ **NOT IMPLEMENTED**

- **Type**: `TimeSpan`
- **Default**: `00:00:30` (30 seconds)
- **Planned**: How often health checks are performed
- **Future Impact**: Control health check frequency

#### `HealthChecks.HealthCheckTimeout` ⚠️ **NOT IMPLEMENTED**

- **Type**: `TimeSpan`
- **Default**: `00:00:10` (10 seconds)
- **Planned**: Timeout for health check operations
- **Future Impact**: Control health check responsiveness

### Future Configuration (When Implemented)

```csharp
// ⚠️ This configuration will work when monitoring is implemented
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Planned: Metrics collection
    options.Monitoring.Metrics.EnableMetrics = true;
    options.Monitoring.Metrics.MetricsCollectionInterval = TimeSpan.FromMinutes(1);
    options.Monitoring.Metrics.EnabledMetrics = new[]
    {
        "CacheHits", "CacheMisses", "OperationLatency",
        "ConnectionPoolSize", "CleanupOperations"
    };

    // Planned: Health checks
    options.Monitoring.HealthChecks.EnableHealthChecks = true;
    options.Monitoring.HealthChecks.HealthCheckInterval = TimeSpan.FromSeconds(30);
    options.Monitoring.HealthChecks.HealthCheckTimeout = TimeSpan.FromSeconds(5);
});
```

### Roadmap Status

Metrics collection and health check integration are planned features. See the [Roadmap](../README.md#roadmap--planned-features) for implementation timeline and updates.

---

## Configuration Binding

GlacialCache supports binding from `appsettings.json` and other .NET configuration providers.

### Basic appsettings.json Binding

```json
{
  "ConnectionStrings": {
    "GlacialCache": "Host=localhost;Database=glacialcache;Username=postgres;Password=postgres"
  },
  "GlacialCache": {
    "Connection": {
      "ConnectionString": "${ConnectionStrings:GlacialCache}",
      "Pool": {
        "MinSize": 5,
        "MaxSize": 50
      }
    },
    "Cache": {
      "SchemaName": "cache",
      "TableName": "entries",
      "DefaultSlidingExpiration": "00:30:00",
      "DefaultAbsoluteExpirationRelativeToNow": "02:00:00"
    }
  }
}
```

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    builder.Configuration.GetSection("GlacialCache").Bind(options);
});
```

### Complete Configuration Example

```json
{
  "GlacialCache": {
    "Connection": {
      "ConnectionString": "Host=prod-db.example.com;Database=cache;Username=app;Password=secret",
      "Pool": {
        "MinSize": 10,
        "MaxSize": 100,
        "IdleLifetimeSeconds": 300,
        "PruningIntervalSeconds": 30
      },
      "Timeouts": {
        "OperationTimeout": "00:00:10",
        "ConnectionTimeout": "00:00:10",
        "CommandTimeout": "00:00:10"
      }
    },
    "Cache": {
      "SchemaName": "cache",
      "TableName": "entries",
      "DefaultSlidingExpiration": "00:20:00",
      "DefaultAbsoluteExpirationRelativeToNow": "01:00:00",
      "MinimumExpirationInterval": "00:00:00.001",
      "MaximumExpirationInterval": "365.00:00:00",
      "EnableEdgeCaseLogging": true,
      "Serializer": "MemoryPack"
    },
    "Maintenance": {
      "EnableAutomaticCleanup": true,
      "CleanupInterval": "00:30:00",
      "MaxCleanupBatchSize": 1000
    },
    "Resilience": {
      "EnableResiliencePatterns": true,
      "Retry": {
        "MaxAttempts": 3,
        "BaseDelay": "00:00:01",
        "BackoffStrategy": "ExponentialWithJitter"
      },
      "CircuitBreaker": {
        "Enable": true,
        "FailureThreshold": 5,
        "DurationOfBreak": "00:01:00"
      },
      "Timeouts": {
        "OperationTimeout": "00:00:30"
      },
      "Logging": {
        "EnableResilienceLogging": true,
        "ConnectionFailureLogLevel": "Warning"
      }
    },
    "Infrastructure": {
      "CreateInfrastructure": false,
      "EnableManagerElection": true
    },
    "Security": {
      "ConnectionString": {
        "MaskInLogs": true,
        "SensitiveParameters": ["Password", "Token", "Key"]
      }
    },
    "Monitoring": {
      "Metrics": {
        "EnableMetrics": true,
        "MetricsCollectionInterval": "00:01:00",
        "EnabledMetrics": ["CacheHits", "CacheMisses", "OperationLatency"]
      },
      "HealthChecks": {
        "EnableHealthChecks": true,
        "HealthCheckInterval": "00:00:30",
        "HealthCheckTimeout": "00:00:10"
      }
    }
  }
}
```

### Environment-Specific Configuration

```json
// appsettings.Development.json
{
  "GlacialCache": {
    "Connection": {
      "Pool": {
        "MinSize": 1,
        "MaxSize": 10
      }
    },
    "Infrastructure": {
      "CreateInfrastructure": true,
      "EnableManagerElection": false
    }
  }
}
```

```json
// appsettings.Production.json
{
  "GlacialCache": {
    "Connection": {
      "Pool": {
        "MinSize": 10,
        "MaxSize": 100
      }
    },
    "Infrastructure": {
      "CreateInfrastructure": false,
      "EnableManagerElection": true
    }
  }
}
```

### Validation

Configuration validation occurs at startup. Invalid configurations throw `ArgumentException` with detailed error messages:

- Required connection string validation
- PostgreSQL identifier format validation
- Range and constraint validation
- Cross-property dependency validation

Misconfigurations are caught early, preventing runtime issues.

---

## Validation Rules Reference

GlacialCache validates configuration at startup using DataAnnotations and custom validation logic.

### Connection Validation

| Property                      | Rule                   | Error Message                                         |
| ----------------------------- | ---------------------- | ----------------------------------------------------- |
| `ConnectionString`            | Required, min 10 chars | "Connection string is required"                       |
| `Pool.MaxSize`                | 1-1000                 | "Max connection pool size must be between 1 and 1000" |
| `Pool.MinSize`                | 1-100, ≤ MaxSize       | "Min connection pool size must be between 1 and 100"  |
| `Pool.IdleLifetimeSeconds`    | > 0                    | "Connection idle lifetime must be positive"           |
| `Pool.PruningIntervalSeconds` | > 0                    | "Connection pruning interval must be positive"        |
| `Timeouts.*`                  | > 0                    | "Timeout must be positive"                            |

### Cache Validation

| Property                    | Rule                             | Error Message                                       |
| --------------------------- | -------------------------------- | --------------------------------------------------- |
| `TableName`                 | Required, PostgreSQL identifier  | "Table name must be a valid PostgreSQL identifier"  |
| `SchemaName`                | Required, PostgreSQL identifier  | "Schema name must be a valid PostgreSQL identifier" |
| `MinimumExpirationInterval` | > 0, < MaximumExpirationInterval | -                                                   |
| `MaximumExpirationInterval` | > MinimumExpirationInterval      | -                                                   |

### Maintenance Validation

| Property              | Rule    | Error Message                                        |
| --------------------- | ------- | ---------------------------------------------------- |
| `CleanupInterval`     | > 0     | "Cleanup interval must be positive"                  |
| `MaxCleanupBatchSize` | 1-10000 | "Max cleanup batch size must be between 1 and 10000" |

### Resilience Validation

| Property                          | Rule  | Error Message                                                 |
| --------------------------------- | ----- | ------------------------------------------------------------- |
| `Retry.MaxAttempts`               | 0-10  | "Max retry attempts must be between 0 and 10"                 |
| `Retry.BaseDelay`                 | > 0   | "Retry base delay must be positive"                           |
| `CircuitBreaker.FailureThreshold` | 1-100 | "Circuit breaker failure threshold must be between 1 and 100" |
| `CircuitBreaker.DurationOfBreak`  | > 0   | "Circuit breaker duration of break must be positive"          |
| `Timeouts.OperationTimeout`       | > 0   | "Operation timeout must be positive"                          |

### PostgreSQL Identifier Rules

PostgreSQL identifiers must:

- Start with letter (a-z, A-Z) or underscore (\_)
- Contain only letters, digits (0-9), and underscores
- Be 1-63 bytes in length
- Not be reserved SQL keywords

**Valid examples**: `cache_table`, `CacheEntries`, `entries_v2`  
**Invalid examples**: `123table`, `cache-table`, `table name`

### Cross-Property Validation

| Validation              | Description                                                |
| ----------------------- | ---------------------------------------------------------- |
| Pool Min/Max            | `MinSize ≤ MaxSize`                                        |
| Expiration Ranges       | `MinimumExpirationInterval < MaximumExpirationInterval`    |
| Schema/Table Uniqueness | Advisory lock keys generated from schema+table combination |

### Runtime Validation

Some validations occur at runtime:

- Connection string format validation
- PostgreSQL identifier byte length limits
- Database connectivity checks (when infrastructure creation enabled)

---

## Configuration Scenarios

Complete configuration examples for different deployment scenarios.

### Development Setup

**Use Case**: Local development with minimal resource usage and maximum debugging capability.

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    // Local PostgreSQL instance
    options.Connection.ConnectionString = "Host=localhost;Database=glacialcache;Username=postgres;Password=postgres";

    // Small connection pool for development
    options.Connection.Pool.MinSize = 1;
    options.Connection.Pool.MaxSize = 10;

    // Longer timeouts for debugging
    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromMinutes(1);
    options.Connection.Timeouts.CommandTimeout = TimeSpan.FromMinutes(1);

    // Auto-create schema and table
    options.Infrastructure.CreateInfrastructure = true;
    options.Infrastructure.EnableManagerElection = false; // Single instance

    // Aggressive cleanup for testing
    options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(1);

    // Minimal resilience for fast failure detection
    options.Resilience.Retry.MaxAttempts = 1;
    options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromMinutes(1);

    // Enable edge case logging for debugging
    options.Cache.EnableEdgeCaseLogging = true;
});
```

### Single-Instance Production

**Use Case**: Simple production deployment with one application instance.

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = builder.Configuration.GetConnectionString("GlacialCache")
        ?? throw new InvalidOperationException("GlacialCache connection string is required");

    // Medium connection pool
    options.Connection.Pool.MinSize = 5;
    options.Connection.Pool.MaxSize = 50;

    // Reasonable timeouts
    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(30);
    options.Connection.Timeouts.CommandTimeout = TimeSpan.FromSeconds(15);

    // Use migrations for schema management
    options.Infrastructure.CreateInfrastructure = false;
    options.Infrastructure.EnableManagerElection = false; // Single instance

    // Standard maintenance
    options.Maintenance.EnableAutomaticCleanup = true;
    options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(30);

    // Production resilience
    options.Resilience.EnableResiliencePatterns = true;
    options.Resilience.Retry.MaxAttempts = 3;
    options.Resilience.CircuitBreaker.Enable = true;

    // Security: mask connection strings in logs
    options.Security.ConnectionString.MaskInLogs = true;
});
```

### Multi-Instance Production

**Use Case**: High-availability deployment with multiple application instances.

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = builder.Configuration.GetConnectionString("GlacialCache")
        ?? throw new InvalidOperationException("GlacialCache connection string is required");

    // Larger connection pool for high availability
    options.Connection.Pool.MinSize = 10;
    options.Connection.Pool.MaxSize = 100;

    // Tighter timeouts for performance
    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(15);
    options.Connection.Timeouts.CommandTimeout = TimeSpan.FromSeconds(10);

    // Multi-instance coordination
    options.Infrastructure.CreateInfrastructure = false; // Use migrations
    options.Infrastructure.EnableManagerElection = true; // Coordinate cleanup

    // Frequent cleanup for high-traffic
    options.Maintenance.EnableAutomaticCleanup = true;
    options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(15);
    options.Maintenance.MaxCleanupBatchSize = 2000;

    // Aggressive resilience
    options.Resilience.Retry.MaxAttempts = 5;
    options.Resilience.CircuitBreaker.FailureThreshold = 10;
    options.Resilience.CircuitBreaker.DurationOfBreak = TimeSpan.FromMinutes(2);

    // Security hardening
    options.Security.ConnectionString.MaskInLogs = true;
});
```

### High-Performance Setup

**Use Case**: Maximum performance with minimal latency, suitable for high-throughput applications.

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = builder.Configuration.GetConnectionString("GlacialCache")
        ?? throw new InvalidOperationException("GlacialCache connection string is required");

    // Large connection pool
    options.Connection.Pool.MinSize = 20;
    options.Connection.Pool.MaxSize = 200;
    options.Connection.Pool.IdleLifetimeSeconds = 120; // 2 minutes

    // Fast timeouts
    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(5);
    options.Connection.Timeouts.CommandTimeout = TimeSpan.FromSeconds(3);

    // Custom schema for performance isolation
    options.Cache.SchemaName = "cache";
    options.Cache.TableName = "entries";
    options.Cache.Serializer = SerializerType.MemoryPack; // Fastest

    // Minimal maintenance impact
    options.Maintenance.CleanupInterval = TimeSpan.FromHours(1);
    options.Maintenance.MaxCleanupBatchSize = 5000;

    // Minimal resilience for speed
    options.Resilience.Retry.MaxAttempts = 2;
    options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(3);

    // Multi-instance with careful coordination
    options.Infrastructure.EnableManagerElection = true;
});
```

### High-Security Setup

**Use Case**: Maximum security with audit logging and connection protection.

````csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = builder.Configuration.GetConnectionString("GlacialCache")
        ?? throw new InvalidOperationException("GlacialCache connection string is required");

    // Standard pool sizing
    options.Connection.Pool.MinSize = 5;
    options.Connection.Pool.MaxSize = 50;

    // Security-focused timeouts
    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(30);

    // Security hardening (working features)
    options.Security.ConnectionString.MaskInLogs = true;
    options.Security.ConnectionString.SensitiveParameters = new[]
    {
        "Password", "Token", "Key", "Secret", "Certificate"
    };

    // ⚠️ Planned features (not yet implemented):
    // options.Security.Tokens.EncryptInMemory = true;
    // options.Security.Audit.EnableAuditLogging = true;
    // options.Security.Audit.LogCacheAccessPatterns = true;

    // Minimal logging for security
    options.Cache.EnableEdgeCaseLogging = false;
    options.Resilience.Logging.EnableResilienceLogging = false;

    // Standard maintenance and resilience
    options.Maintenance.EnableAutomaticCleanup = true;
    options.Resilience.EnableResiliencePatterns = true;
});

---

## Best Practices

Recommended configuration patterns for different scenarios.

### Connection Pool Sizing

| Scenario | Min Size | Max Size | Rationale |
|----------|----------|----------|-----------|
| Development | 1 | 10 | Minimal resources, fast startup |
| Small Production | 5 | 50 | Balanced for moderate load |
| Large Production | 10 | 100 | Handle traffic spikes |
| High Performance | 20 | 200 | Maximum concurrency |

**Rules**:
- `MinSize`: Keep connections ready for steady load
- `MaxSize`: Prevent connection pool exhaustion
- `IdleLifetime`: Balance memory vs connection churn
- Monitor PostgreSQL `max_connections` setting

### Expiration Strategy

```csharp
// Recommended: Use defaults for most cases
options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(20);
options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);

// Guardrails prevent issues
options.Cache.MinimumExpirationInterval = TimeSpan.FromSeconds(1);
options.Cache.MaximumExpirationInterval = TimeSpan.FromDays(30);

// Enable logging in development
options.Cache.EnableEdgeCaseLogging = !builder.Environment.IsProduction();
````

**Guidelines**:

- **Sliding expiration**: For user sessions, temporary data
- **Absolute expiration**: For time-sensitive data
- **Guardrails**: Prevent accidental very short/long expirations
- **Logging**: Monitor edge cases in development

### Maintenance Configuration

```csharp
// Production recommendation
options.Maintenance.EnableAutomaticCleanup = true;
options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(30);
options.Maintenance.MaxCleanupBatchSize = 1000;
```

**Scaling by Load**:

- **Low traffic**: `CleanupInterval = 2 hours`, `MaxCleanupBatchSize = 2000`
- **High traffic**: `CleanupInterval = 15 minutes`, `MaxCleanupBatchSize = 500`
- **Very high traffic**: `CleanupInterval = 5 minutes`, `MaxCleanupBatchSize = 200`

### Resilience Tuning

```csharp
// Balanced production settings
options.Resilience.EnableResiliencePatterns = true;
options.Resilience.Retry.MaxAttempts = 3;
options.Resilience.Retry.BaseDelay = TimeSpan.FromSeconds(1);
options.Resilience.Retry.BackoffStrategy = BackoffStrategy.ExponentialWithJitter;
options.Resilience.CircuitBreaker.Enable = true;
options.Resilience.CircuitBreaker.FailureThreshold = 5;
options.Resilience.CircuitBreaker.DurationOfBreak = TimeSpan.FromMinutes(1);
```

**Network Conditions**:

- **Reliable network**: Reduce retry attempts, shorter timeouts
- **Unreliable network**: Increase retry attempts, longer base delay
- **High latency**: Increase operation timeouts

### Security Recommendations

```csharp
// Always enable in production
options.Security.ConnectionString.MaskInLogs = true;

// Comprehensive parameter masking
options.Security.ConnectionString.SensitiveParameters = new[]
{
    "Password", "Token", "Key", "Secret", "Certificate", "ConnectionString"
};
```

**Security Checklist**:

- ✅ Mask connection strings in logs
- ✅ Use secure connection strings (SSL/TLS)
- ✅ Rotate credentials regularly
- ✅ Monitor for unusual access patterns
- ⚠️ Audit logging (planned feature)
- ⚠️ Token encryption (planned feature)

### Environment-Specific Patterns

#### Development

```csharp
public void ConfigureDevelopment(IServiceCollection services, IConfiguration configuration)
{
    services.AddGlacialCachePostgreSQL(options =>
    {
        options.Connection.ConnectionString = "Host=localhost;...";
        options.Connection.Pool.MinSize = 1;
        options.Connection.Pool.MaxSize = 10;
        options.Infrastructure.CreateInfrastructure = true;
        options.Cache.EnableEdgeCaseLogging = true;
        options.Resilience.Retry.MaxAttempts = 1; // Fast failure
    });
}
```

#### Production

```csharp
public void ConfigureProduction(IServiceCollection services, IConfiguration configuration)
{
    services.AddGlacialCachePostgreSQL(options =>
    {
        options.Connection.ConnectionString = configuration.GetConnectionString("GlacialCache");
        options.Connection.Pool.MinSize = 10;
        options.Connection.Pool.MaxSize = 100;
        options.Infrastructure.CreateInfrastructure = false; // Use migrations
        options.Security.ConnectionString.MaskInLogs = true;
        options.Cache.EnableEdgeCaseLogging = false;
    });
}
```

### Performance Tuning Checklist

- [ ] Connection pool sized appropriately for load
- [ ] Timeouts tuned for network latency
- [ ] Serializer chosen for use case (MemoryPack for speed, JSON for compatibility)
- [ ] Maintenance intervals balanced with cleanup needs
- [ ] Resilience settings match infrastructure reliability
- [ ] Schema/table names optimized for your PostgreSQL setup

---

## Implementation Status & Planned Features

### ✅ Fully Implemented Features

All core caching functionality is implemented and production-ready:

- **Connection Management**: Pooling, timeouts, PostgreSQL connectivity
- **Cache Operations**: Get, Set, Remove with expiration support
- **Serialization**: MemoryPack (fast), JSON (compatible), Custom (extensible)
- **Maintenance**: Automatic cleanup of expired entries
- **Resilience**: Retry policies, circuit breaker, fault tolerance
- **Infrastructure**: Schema creation, multi-instance coordination
- **Security**: Connection string masking (working features)
- **Configuration**: Comprehensive options with validation
- **Runtime Reloading**: Hot configuration updates for many properties

### ⚠️ Not Yet Implemented (Configuration Exists)

These configuration options exist but currently have **no effect**:

#### Monitoring Options (All properties under `Monitoring`)

```csharp
// These settings are planned but NOT YET IMPLEMENTED
options.Monitoring.Metrics.EnableMetrics = true;           // No effect
options.Monitoring.Metrics.MetricsCollectionInterval = ...; // No effect
options.Monitoring.HealthChecks.EnableHealthChecks = true;  // No effect
```

**Status**: Planned for future release. Metrics collection and ASP.NET Core health check integration are roadmap items.

#### Security Audit Features

```csharp
// These settings are planned but NOT YET IMPLEMENTED
options.Security.Audit.EnableAuditLogging = true;          // No effect
options.Security.Audit.LogCacheAccessPatterns = true;      // No effect
```

**Status**: Planned for future release. Audit logging for compliance and access pattern tracking.

#### Token Encryption Features

```csharp
// These settings are planned but NOT YET IMPLEMENTED
options.Security.Tokens.EncryptInMemory = true;            // No effect
options.Security.Tokens.TokenRefreshBuffer = TimeSpan.FromMinutes(5); // No effect
```

**Status**: Planned for future release. In-memory token encryption and refresh buffer management.

### 📋 Planned Features (Not Yet Available)

#### Azure Managed Identity Support

**Status**: Planned for future release

Automatic Azure Managed Identity integration leveraging the existing reloadable configuration system:

```csharp
// Planned API (not yet available)
builder.Services.AddGlacialCachePostgreSQLWithAzureManagedIdentity(options =>
{
    options.Azure.ClientId = "...";  // Optional
    options.Azure.TenantId = "...";  // Optional
    // Automatic token refresh using existing reloadable config system
});
```

**Key Features** (when implemented):

- Automatic token refresh using `Azure.Identity`
- Integration with existing `ObservableProperty<T>` infrastructure
- Support for system-assigned and user-assigned managed identities
- Zero-downtime credential rotation

#### Additional Serialization Providers

**Status**: Planned for future release

```csharp
// Planned serializers (not yet available)
options.Cache.Serializer = SerializerType.MessagePack;
options.Cache.Serializer = SerializerType.Protobuf;
```

#### Distributed Locking Primitives

**Status**: Planned for future release

```csharp
// Planned distributed locking (not yet available)
await cache.AcquireLockAsync("resource-key", TimeSpan.FromMinutes(5));
```

### How to Handle Not-Implemented Features

1. **Check Status**: Review this documentation for current implementation status
2. **Safe to Configure**: You can safely set these options - they won't cause errors
3. **No Effect**: Configuration will be stored but ignored until features are implemented
4. **Future-Proof**: Your configuration will work automatically when features are added
5. **Monitor Roadmap**: Check [GitHub Issues](https://github.com/leonibr/glacial-cache/issues) for implementation updates

### Contributing to Planned Features

These features are tracked as GitHub issues. You can:

- 👍 Upvote existing feature requests
- 💬 Comment with your use case
- 🛠️ Contribute implementation (PRs welcome)
- 📊 Check implementation progress

---

## Troubleshooting

Common configuration issues and their solutions.

### Configuration Validation Errors

#### "Connection string is required"

```csharp
// Problem
builder.Services.AddGlacialCachePostgreSQL(options => { });

// Solution
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = "Host=...;Database=...";
});
```

#### "Table name must be a valid PostgreSQL identifier"

```csharp
// Problem
options.Cache.TableName = "cache-table";  // Hyphens not allowed

// Solution
options.Cache.TableName = "cache_table";  // Use underscores
```

#### "Min connection pool size cannot be greater than max connection pool size"

```csharp
// Problem
options.Connection.Pool.MinSize = 50;
options.Connection.Pool.MaxSize = 10;  // Min > Max

// Solution
options.Connection.Pool.MaxSize = 100; // Make Max > Min
```

### Runtime Issues

#### Connection Pool Exhaustion

**Symptoms**: Timeout errors, slow performance

```csharp
// Increase pool size
options.Connection.Pool.MaxSize = 100;  // From default 50
options.Connection.Pool.MinSize = 10;   // Keep some connections ready
```

#### Slow Queries During Maintenance

**Symptoms**: Periodic performance degradation

```csharp
// Adjust cleanup frequency
options.Maintenance.CleanupInterval = TimeSpan.FromHours(1);     // Less frequent
options.Maintenance.MaxCleanupBatchSize = 500;                  // Smaller batches
```

#### Circuit Breaker Opening Frequently

**Symptoms**: Operations failing with circuit breaker errors

```csharp
// Adjust circuit breaker sensitivity
options.Resilience.CircuitBreaker.FailureThreshold = 10;         // More tolerant
options.Resilience.CircuitBreaker.DurationOfBreak = TimeSpan.FromMinutes(2); // Longer recovery
```

### Not-Implemented Feature Confusion

#### "Why doesn't my monitoring configuration work?"

**Answer**: Monitoring features are planned but not yet implemented. See [Implementation Status](#implementation-status--planned-features).

#### "Audit logging settings have no effect"

**Answer**: Security audit features are planned but not yet implemented. Your configuration is future-proof and will work when implemented.

### Performance Tuning

#### High Latency

```csharp
// Reduce timeouts for faster failure detection
options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(5);
options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(3);

// Increase connection pool
options.Connection.Pool.MaxSize = 100;
```

#### High Memory Usage

```csharp
// Reduce connection pool
options.Connection.Pool.MaxSize = 50;
options.Connection.Pool.IdleLifetimeSeconds = 120; // Shorter idle time

// Reduce maintenance batch size
options.Maintenance.MaxCleanupBatchSize = 500;
```

#### Database Connection Errors

```csharp
// Add resilience
options.Resilience.EnableResiliencePatterns = true;
options.Resilience.Retry.MaxAttempts = 3;

// Adjust timeouts
options.Connection.Timeouts.ConnectionTimeout = TimeSpan.FromSeconds(30);
options.Connection.Timeouts.CommandTimeout = TimeSpan.FromSeconds(30);
```

### Debugging Tips

1. **Enable Edge Case Logging**: `options.Cache.EnableEdgeCaseLogging = true`
2. **Enable Resilience Logging**: `options.Resilience.Logging.EnableResilienceLogging = true`
3. **Check Logs**: Look for GlacialCache-specific log messages
4. **Validate Configuration**: Call `ConfigurationValidator.ValidateOptions(options)` manually
5. **Monitor Connections**: Check PostgreSQL `pg_stat_activity` for connection usage

---

## Runtime Configuration Reloading

GlacialCache supports runtime configuration changes without requiring application restarts. The cache automatically reloads when configuration values change through any .NET configuration provider.

### How It Works

The reloadable configuration system uses:

- `IOptionsMonitor<GlacialCachePostgreSQLOptions>` for external changes
- `ObservableProperty<T>` pattern for internal change propagation
- Automatic resource recreation when critical properties change

### Runtime Reload Callback Contract

Runtime reload callbacks run synchronously on the configuration change-notification path. Callback handlers must stay fast, must be thread-safe, and should not block on database or network work. If a runtime subscriber or `ObservableProperty<T>` handler throws, the exception is propagated to the caller that initiated the reload and later subscribers on that callback path are not invoked.

Direct `ObservableProperty<T>` callbacks remain a compatibility path. For sensitive properties such as `Connection.ConnectionString`, the `PropertyChangedEventArgs<T>` payload can still contain the raw old and new values so internal components can rebuild runtime state. Generic observable logs redact sensitive values, and callback handlers must not log raw `PropertyChangedEventArgs<T>` values directly.

### Supported Reloadable Properties

#### Connection String

When changed, the connection pool is automatically recreated:

- Existing connections complete gracefully
- New connections use updated string
- Enables database failover and credential rotation

#### Cache Table and Schema Names

When changed, SQL queries are automatically regenerated:

- All operations use new table/schema immediately
- Useful for tenant switching or maintenance windows

#### Connection Pool Settings

Pool size and pruning behavior update dynamically:

- `Pool.MinSize` / `Pool.MaxSize`
- `Pool.IdleLifetimeSeconds`
- `Pool.PruningIntervalSeconds`

#### Maintenance Settings

Cleanup behavior adjusts at runtime:

- `Maintenance.CleanupInterval`
- `Maintenance.MaxCleanupBatchSize`
- `Maintenance.EnableAutomaticCleanup`

### Real-World Use Cases

#### 1. Database Failover

Seamlessly switch to a backup database:

```csharp
// Initial configuration
{
  "GlacialCache": {
    "Connection": {
      "ConnectionString": "Host=primary-db.example.com;Database=cache;..."
    }
  }
}

// Update during failover (via Azure App Configuration, K8s ConfigMap, etc.)
// GlacialCache automatically reconnects to backup
{
  "GlacialCache": {
    "Connection": {
      "ConnectionString": "Host=backup-db.example.com;Database=cache;..."
    }
  }
}
```

#### 2. Security Credential Rotation

Update passwords without downtime:

```csharp
// Credential rotation happens automatically
// Configuration updated by your secret management system
// GlacialCache detects change and reconnects with new credentials
```

#### 3. Azure App Configuration Integration

```csharp
// Program.cs
// Install: Microsoft.Extensions.Configuration.AzureAppConfiguration
// Add Azure App Configuration provider (method name may vary by package version)
builder.Configuration.AddAzureAppConfiguration(/* connection string or options */);

// Configure refresh for GlacialCache section
// Refer to Azure App Configuration documentation for exact API
// Example pattern:
// - Register "GlacialCache" section for refresh
// - Set appropriate cache expiration
// - Enable refresh middleware if required
```

**Note**: The exact API depends on your Azure App Configuration package version. Refer to the [Azure App Configuration documentation](https://learn.microsoft.com/azure/azure-app-configuration/) for the current API.

#### 4. Kubernetes ConfigMap Hot Reload

```yaml
# ConfigMap with cache configuration
apiVersion: v1
kind: ConfigMap
metadata:
  name: glacialcache-config
data:
  appsettings.json: |
    {
      "GlacialCache": {
        "Connection": {
          "ConnectionString": "Host=postgres-service;Database=cache;..."
        }
      }
    }
```

When ConfigMap changes, GlacialCache automatically reloads.

### Configuration Change Monitoring

All configuration changes are logged at `Information` level:

```
info: GlacialCache.PostgreSQL[0]
      Connection string changed from 'Host=old-db;Username=user;Password=***'
      to 'Host=new-db;Username=user;Password=***'

info: GlacialCache.PostgreSQL[0]
      Configuration property 'Cache.TableName' changed from 'old_table' to 'new_table',
      rebuilding SQL queries
```

**Security Note**: Connection strings are automatically masked in logs to prevent exposure of passwords and tokens.

### Configuration Providers

Reloadable configuration works with any .NET configuration provider:

- **appsettings.json** - File-based with reload on change
- **Environment Variables** - Container orchestration
- **Azure App Configuration** - Centralized cloud configuration
- **Key Vault** - Secure credential management
- **Kubernetes ConfigMaps/Secrets** - Container configuration
- **User Secrets** - Development-time configuration

### Limitations

**Not Reloadable** (require restart):

- `Infrastructure.CreateInfrastructure` - Schema creation is one-time
- `Infrastructure.EnableManagerElection` - Election mode is set at startup
- `Cache.Serializer` - Serializer is chosen once at initialization

---

## Azure Managed Identity

**Status**: Planned Feature

Azure Managed Identity support is planned for a future release. The implementation will leverage GlacialCache's existing reloadable configuration system for automatic token refresh.

See the [Roadmap](../README.md#roadmap--planned-features) for more details on planned features.
