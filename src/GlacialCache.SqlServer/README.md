# GlacialCache.SqlServer

SQL Server 2019+ provider for the provider-neutral `GlacialCache.Abstractions.IGlacialCache` contract and .NET's `IDistributedCache`.

It implements the complete canonical surface: raw bytes, rich `CacheEntry<T>` metadata, typed MemoryPack operations, and typed/raw batches.

```csharp
using GlacialCache.SqlServer;
using GlacialCache.Abstractions;

services.AddGlacialCacheSqlServer(options =>
{
    options.ConnectionString = configuration.GetConnectionString("Cache")!;
    options.SchemaName = "dbo";
    options.TableName = "glacial_cache";
});
```

Resolve `IGlacialCache` for the full API. `IGlacialCache` and `IDistributedCache` resolve to the same singleton.

Infrastructure creation is synchronous and idempotent by default. It can be disabled with `CreateInfrastructure = false` when migrations provision the table separately. Keys are case-sensitive, may contain up to 900 Unicode characters, and are located through SHA-256 plus full-key equality. Batch key commands are chunked below SQL Server's 2100-parameter limit.
