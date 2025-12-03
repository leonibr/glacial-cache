# GlacialCache CacheEntry Example

This example demonstrates the advanced CacheEntry features in GlacialCache, including rich metadata, TimeProvider integration, and type-safe operations.

## 🚀 Features Demonstrated

- CacheEntry objects with rich metadata
- TimeProvider integration for precise time management
- Type-safe cache operations
- Batch operations with CacheEntry collections
- Expiration management with absolute and sliding expiration
- CacheEntry serialization and deserialization

## 🏃‍♂️ Quick Start

### Prerequisites

- .NET 8.0 or later
- PostgreSQL database
- Connection string configured

### Running the Example

```bash
# Using dotnet CLI
dotnet run --project GlacialCache.Example.CacheEntry.csproj

# Using Docker
docker build -t GlacialCache-cacheentry .
docker run --network host GlacialCache-cacheentry
```

### Configuration

The example uses the following configuration (from `appsettings.json`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=glacialcache;Username=postgres;Password=password"
  },
  "GlacialCache": {
    "SchemaName": "cache",
    "TableName": "entries",
    "DefaultSlidingExpirationMinutes": 30,
    "DefaultAbsoluteExpirationHours": 1
  }
}
```

## 🐳 Docker Compose

To run with PostgreSQL using Docker Compose:

```yaml
version: '3.8'
services:
  postgres:
    image: postgres:15
    environment:
      POSTGRES_DB: GlacialCache
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: password
    ports:
      - '5432:5432'

  GlacialCache-cacheentry:
    build: .
    depends_on:
      - postgres
    environment:
      - POSTGRES_HOST=postgres
      - POSTGRES_DB=GlacialCache
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=password
```

## 📊 Example Output

```
🚀 GlacialCache PostgreSQL CacheEntry Example
============================================

📝 Example 1: GetAsync with CacheEntry
✅ Retrieved CacheEntry:
   Key: user:123
   Value: John Doe
   AbsoluteExpiration: 10/15/2024 2:30:00 PM
   SlidingExpiration: 00:10:00

📝 Example 2: SetAsync with CacheEntry and TimeProvider
✅ Set CacheEntry for key: user:456
✅ Retrieved newly set CacheEntry:
   Value: Jane Smith

📝 Example 3: Using IDistributedCache interface
✅ Retrieved from IDistributedCache:
   Value: John Doe

📝 Example 4: Batch operations with CacheEntry
✅ Set 3 cache entries in batch
✅ Retrieved 3 cache entries:
   batch:1: Batch Entry 1
   batch:2: Batch Entry 2
   batch:3: Batch Entry 3

🎉 CacheEntry example completed successfully!
```

## 🔧 CacheEntry Features

### Rich Metadata

CacheEntry objects provide rich metadata about cached items:

```csharp
var cacheEntry = await cache.GetEntryAsync("my-key");

if (cacheEntry != null)
{
    Console.WriteLine($"Key: {cacheEntry.Key}");
    Console.WriteLine($"Value: {Encoding.UTF8.GetString(cacheEntry.Value.ToArray())}");
    Console.WriteLine($"Absolute Expiration: {cacheEntry.AbsoluteExpiration}");
    Console.WriteLine($"Sliding Expiration: {cacheEntry.SlidingExpiration}");
    Console.WriteLine($"Created At: {cacheEntry.CreatedAt}");
    Console.WriteLine($"Last Accessed: {cacheEntry.LastAccessed}");
}
```

### TimeProvider Integration

CacheEntry supports TimeProvider for precise time management:

```csharp
var timeProvider = TimeProvider.System;

var entry = new CacheEntry<byte[]>(
    key: "user:123",
    value: Encoding.UTF8.GetBytes("John Doe"),
    absoluteExpiration: timeProvider.GetUtcNow().AddHours(2),
    slidingExpiration: TimeSpan.FromMinutes(30),
);

await cache.SetAsync(entry);
```

### Batch Operations

Perform efficient batch operations with collections of CacheEntry:

```csharp
var entries = new List<CacheEntry<byte[]>>
{
    new CacheEntry<byte[]>("key1", value1),
    new CacheEntry<byte[]>("key2", value2),
    new CacheEntry<byte[]>("key3", value3)
};

await cache.SetMultipleEntriesAsync(entries);

var keys = new[] { "key1", "key2", "key3" };
var retrieved = await cache.GetMultipleEntriesAsync(keys);
```

## 🛠️ Development

### Building

```bash
dotnet build GlacialCache.Example.CacheEntry.csproj
```

### Testing

```bash
dotnet run --project GlacialCache.Example.CacheEntry.csproj
```

### Debugging

```bash
# With detailed logging
dotnet run --project GlacialCache.Example.CacheEntry.csproj --verbosity detailed
```

## 📚 Related Examples

- [Basic Example](../GlacialCache.Example.Basic/) - Standard cache operations
- [Azure Example](../GlacialCache.Example.Azure/) - Azure Managed Identity
- [MemoryPack Example](../GlacialCache.Example.MemoryPack/) - High-performance serialization
- [Token Expiration Example](../GlacialCache.Example.TokenExpiration/) - Azure token management

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

---

**🎉 Advanced Caching Made Simple!** This CacheEntry example shows how to leverage rich metadata and precise time management in your caching strategy.
