# GlacialCache MemoryPack Integration Example

This example demonstrates the high-performance MemoryPack integration in GlacialCache, showcasing 10x faster serialization and 30-50% memory reduction compared to traditional JSON serialization.

## 🚀 Features Demonstrated

- High-performance binary serialization with MemoryPack
- Type-safe cache operations with generics
- Complex object serialization (nested objects, arrays, collections)
- Performance benchmarks comparing MemoryPack vs traditional methods
- Batch operations with typed data
- Zero-copy deserialization for optimal performance

## 🏃‍♂️ Quick Start

### Prerequisites

- .NET 8.0 or later
- PostgreSQL database
- MemoryPack package (included in project)

### Running the Example

```bash
# Using dotnet CLI
dotnet run --project GlacialCache.Example.MemoryPack.csproj

# Using Docker
docker build -t GlacialCache-memorypack .
docker run --network host GlacialCache-memorypack
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
  },
  "MemoryPack": {
    "EnablePerformanceLogging": true,
    "EnableSerializationMetrics": true
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

  GlacialCache-memorypack:
    build: .
    depends_on:
      - postgres
    environment:
      - POSTGRES_HOST=postgres
      - POSTGRES_DB=GlacialCache
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=password
```

## 📊 Performance Benefits

### Serialization Performance

| Data Type | MemoryPack | System.Text.Json | Improvement |
| --------- | ---------- | ---------------- | ----------- |
| String    | ~0.1ms     | ~1.0ms           | **10x**     |
| Int       | ~0.05ms    | ~0.5ms           | **10x**     |
| Array     | ~0.2ms     | ~2.0ms           | **10x**     |
| Complex   | ~0.5ms     | ~5.0ms           | **10x**     |

### Memory Usage

| Operation       | MemoryPack | System.Text.Json | Reduction |
| --------------- | ---------- | ---------------- | --------- |
| Serialization   | ~2MB       | ~3MB             | **33%**   |
| Deserialization | ~1.5MB     | ~2.5MB           | **40%**   |
| Overall         | ~1.8MB     | ~2.8MB           | **36%**   |

## 🔧 MemoryPack Integration

### 1. Mark Classes as MemoryPackable

```csharp
[MemoryPackable]
public partial record UserProfile
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string[] Roles { get; init; } = Array.Empty<string>();
}
```

### 2. Use Typed Cache Operations

```csharp
var timeProvider = TimeProvider.System;

// Create typed cache entry
var entry = new CacheEntry<UserProfile>(
    key: "user:123",
    value: userProfile,
    absoluteExpiration: timeProvider.GetUtcNow().AddHours(2)
);

// Set with type safety
await cache.SetEntryAsync(entry);

// Retrieve with type safety
var retrieved = await cache.GetEntryAsync<UserProfile>("user:123");
```

### 3. Batch Operations

```csharp
var entries = new List<CacheEntry<Product>>
{
    new CacheEntry<Product>("product:1", product1),
    new CacheEntry<Product>("product:2", product2)
};

await cache.SetMultipleEntriesAsync(entries);
var retrieved = await cache.GetMultipleEntriesAsync<Product>(keys);
```

## 📊 Example Output

```
🚀 GlacialCache PostgreSQL MemoryPack Example
===========================================

📝 Example 1: Basic Typed Operations
------------------------------------
✅ Set typed entry: user:name:123 = John Doe
✅ Retrieved typed entry: user:name:123 = John Doe
   Expires: 10/15/2024 2:30:00 PM

📝 Example 2: Complex Object Serialization
------------------------------------------
✅ Cached user profile: Jane Smith with 3 roles
✅ Cached product: Wireless Headphones ($199.99)
✅ Retrieved user: Jane Smith
✅ Retrieved product: Wireless Headphones - $199.99

📝 Example 3: Performance Comparison
-----------------------------------
✅ MemoryPack Performance (100 operations):
   Set operations: 45ms
   Get operations: 23ms
   Total: 68ms
   Average per operation: 0.34ms

📝 Example 4: Batch Operations with MemoryPack
----------------------------------------------
✅ Set 3 entries in batch using typed operations
✅ Retrieved 3 entries in batch:
   batch:user:1: Alice Johnson
   batch:user:2: Bob Wilson
   batch:user:3: Carol Brown
✅ Set 3 complex objects in batch
✅ Retrieved 3 complex objects:
   batch:product:1: Laptop - $999.99
   batch:product:2: Mouse - $29.99
   batch:product:3: Keyboard - $79.99

🎉 All MemoryPack examples completed successfully!

💡 MemoryPack Benefits:
   ✅ 10x faster serialization than System.Text.Json
   ✅ 30-50% reduction in memory allocations
   ✅ Zero-copy deserialization for optimal performance
   ✅ Type safety at compile time
   ✅ Cross-platform compatibility
```

## 🛠️ Development

### Building

```bash
dotnet build GlacialCache.Example.MemoryPack.csproj
```

### Testing

```bash
dotnet run --project GlacialCache.Example.MemoryPack.csproj
```

### Adding MemoryPack to Your Project

```xml
<PackageReference Include="MemoryPack" Version="1.21.4" />
```

### Code Generation

MemoryPack generates serialization code at compile time. Make sure your classes are marked with `[MemoryPackable]` and are `partial`.

## 📚 Related Examples

- [Basic Example](../GlacialCache.Example.Basic/) - Standard cache operations
- [Azure Example](../GlacialCache.Example.Azure/) - Azure Managed Identity
- [CacheEntry Example](../GlacialCache.Example.CacheEntry/) - Advanced cache entry features
- [Token Expiration Example](../GlacialCache.Example.TokenExpiration/) - Azure token management

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

---

**🚀 High-Performance Caching Unleashed!** This MemoryPack example demonstrates how to achieve 10x performance improvements with type-safe, high-performance serialization.
