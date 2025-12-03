## Core concepts

GlacialCache.PostgreSQL is an `IDistributedCache` implementation backed by a single PostgreSQL table, plus a background maintenance loop that keeps that table healthy and small.

This page explains the data model, expiration behavior, and cleanup strategy, and compares GlacialCache to in-memory and Redis-based caches.

### Data model

By default GlacialCache creates a table similar to:

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

CREATE INDEX idx_glacial_cache_entries_absolute_expiration
ON public.glacial_cache_entries (absolute_expiration)
WHERE absolute_expiration IS NOT NULL;

CREATE INDEX idx_glacial_cache_entries_next_expiration
ON public.glacial_cache_entries (next_expiration);
```

- **`key`**: cache key (up to 900 characters). This is the same key you pass to `IDistributedCache`.
- **`value`**: raw bytes. Serialization happens in the application, not the database.
- **`absolute_expiration`**: when the entry becomes invalid, regardless of access.
- **`sliding_interval`**: how much to extend the expiration window whenever the entry is accessed.
- **`next_expiration`**: the next point in time when the entry should be considered expired (used by cleanup).
- **`value_type` / `value_size`**: optional metadata for diagnostics and analysis.

The schema and table names are configurable via `Cache.SchemaName` and `Cache.TableName`. The defaults are:

- **Schema**: `public`
- **Table**: `glacial_cache`

You can customize these for organizational or multi-tenant scenarios:

```csharp
options.Cache.SchemaName = "cache";
options.Cache.TableName = "entries";
```

All examples in these docs use the default names unless explicitly noted otherwise.

### Expiration behavior

GlacialCache follows the semantics of `DistributedCacheEntryOptions`:

- **Absolute expiration**

  - Configured via `AbsoluteExpiration` or `AbsoluteExpirationRelativeToNow`.
  - Once the absolute time is reached, the entry is expired even if it was recently used.
  - GlacialCache persists this in the `absolute_expiration` column.

- **Sliding expiration**

  - Configured via `SlidingExpiration`.
  - Each successful `Get` / `GetAsync` updates the entry’s `next_expiration` by adding the sliding interval to “now”.
  - If an entry is not read for one full sliding window, it is considered expired and a candidate for cleanup.

- **Combined expiration**
  - You can combine absolute and sliding expiration.
  - GlacialCache computes `next_expiration` as the minimum of “absolute expiration” and “now + sliding interval”.
  - This protects you from entries that are read frequently but should still age out eventually.

#### What happens on read?

On each cache hit:

1. GlacialCache checks whether the current UTC time is greater than `next_expiration`.
2. If so, the entry is treated as expired and behaves like a miss.
3. If the entry is still valid and has a sliding interval, the background maintenance logic and/or read path will update `next_expiration` to “now + sliding interval”.

#### What happens on write?

On `Set`/`SetAsync`:

1. GlacialCache computes the effective absolute expiration (if any).
2. It computes `next_expiration` based on absolute/sliding settings.
3. It inserts or updates the row with these values.

### Cleanup strategy

Expired entries are removed by a **background maintenance loop** rather than on every request. This keeps the common read/write paths lean while still reclaiming space.

Key ideas:

- **Manager election**

  - To avoid multiple instances running cleanup at the same time, GlacialCache uses a lightweight manager-election mechanism.
  - In many single-instance or development scenarios you can disable this via `Infrastructure.EnableManagerElection = false`.

- **Batch cleanup**

  - Cleanup runs on an interval configured via `Maintenance.CleanupInterval`.
  - It deletes expired rows in batches of `Maintenance.MaxCleanupBatchSize`.
  - This prevents long-running delete statements and reduces lock contention.

- **Failure handling**
  - Cleanup uses the same resilience policies (retries, timeouts) as regular operations.
  - Failures are logged but do not crash the app; entries will simply be cleaned up on a later run.

You can tune these settings in `GlacialCachePostgreSQLOptions.Maintenance` based on your workload and database size.

### How this compares to other caches

#### vs `IMemoryCache`

`IMemoryCache`:

- Lives inside a single process.
- Very fast (no network hop, no disk I/O).
- Entries are lost when the process restarts or scales out.
- Not shared between instances; multi-node scenarios need extra work.

GlacialCache.PostgreSQL:

- Stored durably in PostgreSQL.
- Shared across all instances that point to the same database.
- Survives restarts, deployments, and process crashes.
- Slightly higher latency than in-memory because of network + database round-trips.

Use GlacialCache when:

- You need **cross-instance** caching.
- You care about **durability** across restarts.
- You already operate PostgreSQL and want to avoid adding another infra component.

#### vs Redis-based caches

Redis:

- Purpose-built in-memory cache / data store.
- Extremely low latency, supports rich data structures and pub/sub.
- Requires running and operating Redis (standalone, cluster, or managed).
- Data may be volatile depending on configuration and persistence strategy.

GlacialCache.PostgreSQL:

- Reuses your existing PostgreSQL infrastructure.
- Strong consistency and durability guarantees provided by PostgreSQL.
- Simpler operational model when your team is already invested in Postgres.
- Throughput and latency are bounded by what your database can handle.

Use GlacialCache when:

- You prefer **fewer moving parts** and already rely on Postgres.
- Cache sizes and throughput are compatible with your database capacity.
- You want SQL visibility into cache entries for debugging and analysis.

Use Redis when:

- You need **very high throughput / ultra-low latency** cache traffic.
- Your cache workload would overload the primary database.
- You want advanced cache patterns (pub/sub, streams, Lua scripts, etc.).
