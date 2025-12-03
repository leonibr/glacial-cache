## Configuration

`GlacialCachePostgreSQLOptions` groups all configuration for the PostgreSQL-backed cache. You typically configure it via the `AddGlacialCachePostgreSQL` extension:

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

This page walks through each section and shows example configurations.

### Overview of `GlacialCachePostgreSQLOptions`

- **`Connection`**: connection string, pooling, and timeout behavior.
- **`Cache`**: schema/table names, default expiration, and serializer choice.
- **`Maintenance`**: background cleanup behavior.
- **`Resilience`**: retries, circuit breakers, and timeout policies.
- **`Infrastructure`**: schema creation and manager-election behavior.
- **`Security`**: connection string and token-related safety switches.
- **`Monitoring`**: metrics and health-check cadence.

---

## Connection options

Type: `ConnectionOptions`

```csharp
options.Connection.ConnectionString = "...";
options.Connection.Pool.MaxSize = 50;
options.Connection.Pool.MinSize = 5;
options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(30);
```

### Important properties

- **`ConnectionString`** (required)

  - Full Npgsql connection string.
  - Must be at least 10 characters and is validated at startup.

- **`Pool.MaxSize` / `Pool.MinSize`**

  - Max/min connections in the pool (defaults: 50 / 5).
  - `MinSize` must not exceed `MaxSize` (enforced by validation).

- **`Pool.IdleLifetimeSeconds`**

  - Idle connections beyond this lifetime may be pruned (default: 300 seconds).

- **`Pool.PruningIntervalSeconds`**

  - How often idle connections are pruned (default: 10 seconds).

- **`Timeouts.OperationTimeout` / `ConnectionTimeout` / `CommandTimeout`**
  - High-level timeouts for key parts of the pipeline (default: 30 seconds each).

### Example: production-friendly connection configuration

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = builder.Configuration.GetConnectionString("GlacialCache")
        ?? throw new InvalidOperationException("GlacialCache connection string is required");

    options.Connection.Pool.MinSize = 10;
    options.Connection.Pool.MaxSize = 100;
    options.Connection.Pool.IdleLifetimeSeconds = 300;
    options.Connection.Pool.PruningIntervalSeconds = 15;

    options.Connection.Timeouts.OperationTimeout = TimeSpan.FromSeconds(10);
    options.Connection.Timeouts.CommandTimeout = TimeSpan.FromSeconds(10);
    options.Connection.Timeouts.ConnectionTimeout = TimeSpan.FromSeconds(10);
});
```

---

## Cache options

Type: `CacheOptions`

```csharp
options.Cache.SchemaName = "cache";
options.Cache.TableName = "entries";
options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(20);
options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
options.Cache.Serializer = SerializerType.MemoryPack;
```

### Important properties

- **`SchemaName` / `TableName`**

  - Default schema: `public`, default table: `glacial_cache`.
  - Both must be valid PostgreSQL identifiers (letters/underscore followed by letters/digits/underscores).

- **`DefaultSlidingExpiration` / `DefaultAbsoluteExpirationRelativeToNow`**

  - Applied when `DistributedCacheEntryOptions` don’t specify their own values.
  - You can still override per entry when calling `Set` / `SetAsync`.

- **`MinimumExpirationInterval` / `MaximumExpirationInterval`**

  - Guardrails for very small or large expiration values.
  - Intervals are clamped to this range; optionally logged as edge cases.

- **`EnableEdgeCaseLogging`**

  - When true (default), logs warnings if expiration values are clamped.

- **`Serializer`**
  - `MemoryPack` (default): high-performance binary serialization.
  - `JsonBytes`: UTF-8 JSON bytes for interoperability and easier debugging.
  - `Custom`: use your own implementation via `CustomSerializerType`.

### Example: using JSON serialization

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Custom schema/table names (optional - defaults are "public"/"glacial_cache")
    options.Cache.SchemaName = "cache";
    options.Cache.TableName = "entries";

    // Use JSON serialization instead of default MemoryPack
    options.Cache.Serializer = SerializerType.JsonBytes;
});
```

### Example: custom serializer

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    options.Cache.Serializer = SerializerType.Custom;
    options.Cache.CustomSerializerType = typeof(MyCustomSerializer);
});
```

Your custom serializer must implement `ICacheEntrySerializer`.

---

## Maintenance options

Namespace: `GlacialCache.PostgreSQL.Configuration.Maintenance`  
Type: `MaintenanceOptions`

```csharp
options.Maintenance.EnableAutomaticCleanup = true;
options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(30);
options.Maintenance.MaxCleanupBatchSize = 1000;
```

### Important properties

- **`EnableAutomaticCleanup`**

  - If `true`, GlacialCache periodically deletes expired entries.

- **`CleanupInterval`**

  - How often the cleanup job runs.
  - Must be positive; validated at startup.

- **`MaxCleanupBatchSize`**
  - Maximum number of rows to delete per cleanup iteration.
  - Default: 1000; valid range: 1–10000.

### Example: more aggressive cleanup

```csharp
options.Maintenance.EnableAutomaticCleanup = true;
options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(5);
options.Maintenance.MaxCleanupBatchSize = 500;
```

---

## Resilience options

Namespace: `GlacialCache.PostgreSQL.Configuration.Resilience`  
Type: `ResilienceOptions`

```csharp
options.Resilience.EnableResiliencePatterns = true;
options.Resilience.Retry.MaxAttempts = 3;
options.Resilience.Retry.BaseDelay = TimeSpan.FromSeconds(1);
options.Resilience.Retry.BackoffStrategy = BackoffStrategy.ExponentialWithJitter;
options.Resilience.CircuitBreaker.Enable = true;
options.Resilience.CircuitBreaker.FailureThreshold = 5;
options.Resilience.CircuitBreaker.DurationOfBreak = TimeSpan.FromMinutes(1);
options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(30);
```

`ResilienceOptions` wraps Polly-based policies for retries, circuit-breaking, and timeouts. The properties are exposed via nested options classes:

### Retry options (`RetryOptions`)

- **`MaxAttempts`** (default: 3)
  - Maximum number of retry attempts for transient failures (range: 0-10).
- **`BaseDelay`** (default: 1 second)
  - Base delay between retry attempts.
- **`BackoffStrategy`**
  - Strategy for calculating retry delays. Options:
    - `ExponentialWithJitter` (default): exponential backoff with randomization to avoid thundering herd.
    - Other strategies as defined in the `BackoffStrategy` enum.

### Circuit breaker options (`CircuitBreakerOptions`)

- **`Enable`** (default: true)
  - Whether to enable circuit breaker pattern.
- **`FailureThreshold`** (default: 5)
  - Number of consecutive failures before opening the circuit (range: 1-100).
- **`DurationOfBreak`** (default: 1 minute)
  - How long the circuit stays open before attempting to close.

### Timeout options (`TimeoutOptions`)

- **`OperationTimeout`** (default: 30 seconds)
  - Overall timeout for cache operations at the resilience layer.
  - Separate from connection-level timeouts in `Connection.Timeouts`.

### Logging options (`LoggingOptions`)

- **`EnableResilienceLogging`** (default: true)
  - Whether to log resilience events (retries, circuit breaker state changes).
- **`ConnectionFailureLogLevel`** (default: Warning)
  - Log level for connection failure events.

### Example: production resilience configuration

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Enable resilience with aggressive retry behavior
    options.Resilience.EnableResiliencePatterns = true;
    options.Resilience.Retry.MaxAttempts = 5;
    options.Resilience.Retry.BaseDelay = TimeSpan.FromMilliseconds(500);
    options.Resilience.Retry.BackoffStrategy = BackoffStrategy.ExponentialWithJitter;

    // Circuit breaker to protect database
    options.Resilience.CircuitBreaker.Enable = true;
    options.Resilience.CircuitBreaker.FailureThreshold = 10;
    options.Resilience.CircuitBreaker.DurationOfBreak = TimeSpan.FromMinutes(2);

    // Tighter operation timeout
    options.Resilience.Timeouts.OperationTimeout = TimeSpan.FromSeconds(5);

    // Log resilience events for observability
    options.Resilience.Logging.EnableResilienceLogging = true;
    options.Resilience.Logging.ConnectionFailureLogLevel = LogLevel.Error;
});
```

Tune these based on your SLAs and the characteristics of your PostgreSQL deployment.

---

## Infrastructure options

Namespace: `GlacialCache.PostgreSQL.Configuration.Infrastructure`  
Type: `InfrastructureOptions`

```csharp
options.Infrastructure.CreateInfrastructure = true;
options.Infrastructure.EnableManagerElection = true;
options.Infrastructure.Lock.ManagerLockKey = 42;
```

### Important properties

- **`CreateInfrastructure`**

  - When `true`, this instance will attempt to create schema, table, and indexes on startup.
  - In multi-instance environments, you usually enable this on just one instance or during migrations.

- **`EnableManagerElection`**

  - Controls whether instances coordinate and elect a manager for background tasks.
  - In single-instance or development environments, you can safely set this to `false`.

- **`Lock` (LockOptions)**
  - Advisory-lock configuration used for manager election and coordination.
  - Typical settings include lock keys and timeouts.

### Example: single-instance (development) setup

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Dev: create infrastructure automatically, no election needed
    options.Infrastructure.CreateInfrastructure = true;
    options.Infrastructure.EnableManagerElection = false;
});
```

### Example: multi-instance production setup

```csharp
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Prod: use migrations for schema, enable manager election
    options.Infrastructure.CreateInfrastructure = false; // use migrations instead
    options.Infrastructure.EnableManagerElection = true; // allow one manager for maintenance
});
```

---

## Security options

Namespace: `GlacialCache.PostgreSQL.Configuration.Security`  
Type: `SecurityOptions`

```csharp
options.Security.ConnectionString.RedactInLogs = true;
options.Security.Tokens.EnableAzureManagedIdentity = true;
options.Security.Audit.EnableAuditLogging = true;
```

`SecurityOptions` groups settings around:

- **Connection string handling** (`ConnectionStringOptions`)
- **Token and authentication behavior** (`TokenOptions`, including Azure Managed Identity integration)
- **Audit logging** (`AuditOptions`)

These options ensure secrets are handled correctly and that sensitive operations can be traced when needed.

For Azure-specific configuration, see also the Azure examples and `docs/getting-started.md` / package README sections on Managed Identity.

---

## Monitoring options

Type: `MonitoringOptions`

```csharp
options.Monitoring.Metrics.EnableMetrics = true;
options.Monitoring.Metrics.MetricsCollectionInterval = TimeSpan.FromMinutes(1);
options.Monitoring.HealthChecks.EnableHealthChecks = true;
options.Monitoring.HealthChecks.HealthCheckInterval = TimeSpan.FromSeconds(30);
```

### Metrics (`MetricsOptions`)

- **`EnableMetrics`**

  - Toggle collection of internal metrics.

- **`MetricsCollectionInterval`**

  - How frequently metrics are aggregated.

- **`EnabledMetrics`**
  - Names of metrics to collect (e.g., `CacheHits`, `CacheMisses`, `OperationLatency`).

### Health checks (`HealthCheckOptions`)

- **`EnableHealthChecks`**

  - If enabled, the library can participate in application-level health checks.

- **`HealthCheckInterval`**

  - How often health probes run.

- **`HealthCheckTimeout`**
  - How long a probe can take before being considered failed.

---

## Binding from `appsettings.json`

Because options follow the standard .NET options pattern, you can bind them from configuration:

```json
{
  "GlacialCache": {
    "Connection": {
      "ConnectionString": "Host=localhost;Database=glacialcache;Username=postgres;Password=postgres"
    },
    "Cache": {
      "SchemaName": "cache",
      "TableName": "entries",
      "DefaultSlidingExpiration": "00:20:00",
      "DefaultAbsoluteExpirationRelativeToNow": "01:00:00"
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

Validation rules (like required connection string and valid cache identifiers) run when the options are validated; misconfigurations are surfaced early so you can fix them before going to production.

---

## Azure Managed Identity configuration

For Azure-hosted environments you can configure Azure Managed Identity helpers:

```csharp
builder.Services.AddGlacialCachePostgreSQLWithAzureManagedIdentity(
    azureOptions =>
    {
        azureOptions.BaseConnectionString =
            "Host=your-server.postgres.database.azure.com;Database=yourdb;Username=your-user@your-server";
        azureOptions.ResourceId = "https://ossrdbms-aad.database.windows.net";
        azureOptions.ClientId = "your-managed-identity-client-id"; // optional, for user-assigned MI
        azureOptions.TokenRefreshBuffer = TimeSpan.FromHours(1);
        azureOptions.MaxRetryAttempts = 3;
    },
    cacheOptions =>
    {
        // Using defaults for schema/table
        cacheOptions.Cache.SchemaName = "public";
        cacheOptions.Cache.TableName = "glacial_cache";
        cacheOptions.Maintenance.CleanupInterval = TimeSpan.FromMinutes(5);
    });
```

This helper keeps your connection string password-free and refreshes tokens automatically using the configured managed identity.
