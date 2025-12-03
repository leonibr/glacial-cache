# GlacialCache Basic Example

This example demonstrates the basic usage of GlacialCache with standard PostgreSQL connections (non-Azure).

## 🚀 Features Demonstrated

- Standard PostgreSQL connection strings
- Basic cache operations (Set/Get/Remove)
- Batch operations for multiple entries
- Scoped operations for connection reuse
- CacheEntry operations with rich metadata
- Automatic infrastructure creation

## 🏃‍♂️ Quick Start

### Prerequisites

- .NET 9.0 or later
- PostgreSQL database
- Connection string configured

### Running the Example

```bash
# Using dotnet CLI
dotnet run --project GlacialCache.Example.Basic.csproj

# Using Docker
docker build -t GlacialCache-basic .
docker run --network host GlacialCache-basic
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

  GlacialCache-basic:
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
🔧 Using connection string: Host=localhost;Database=glacialcache;Username=postgres;Password=password
⏳ Waiting for PostgreSQL to be ready...
✅ PostgreSQL is ready!

🚀 GlacialCache PostgreSQL Example (Non-Azure)
=============================================

📝 Standard Operations:
✅ Set key1 [45 ms]
✅ Get key1 [12 ms]
✅ Retrieved: value1

📦 Batch Operations:
✅ Set multiple entries in batch [78 ms]
✅ Get multiple entries in batch [23 ms]
✅ Retrieved 3 entries in batch:
   batch-key1: batch-value1
   batch-key2: batch-value2
   batch-key3: batch-value3

🔗 Scoped Operations (Connection Reuse):
✅ Set scoped key [15 ms]
✅ Get scoped key [8 ms]
✅ Retrieved from scoped operation: scoped-value1

🎯 CacheEntry Operations:
✅ CacheEntry operations completed successfully!

🎉 All operations completed successfully!
```

## 🔧 Configuration Options

| Setting                           | Description                         | Default   |
| --------------------------------- | ----------------------------------- | --------- |
| `SchemaName`                      | PostgreSQL schema for cache tables  | `cache`   |
| `TableName`                       | PostgreSQL table for cache entries  | `entries` |
| `DefaultSlidingExpirationMinutes` | Default sliding expiration          | `30`      |
| `DefaultAbsoluteExpirationHours`  | Default absolute expiration         | `1`       |
| `EnableManagerElection`           | Enable distributed manager election | `false`   |
| `CreateInfrastructure`            | Auto-create database infrastructure | `true`    |

## 🛠️ Development

### Building

```bash
dotnet build GlacialCache.Example.Basic.csproj
```

### Testing

```bash
dotnet run --project GlacialCache.Example.Basic.csproj
```

### Debugging

```bash
# With detailed logging
dotnet run --project GlacialCache.Example.Basic.csproj --verbosity detailed
```

## 📚 Related Examples

- [Azure Managed Identity Example](../GlacialCache.Example.Azure/) - Azure authentication
- [CacheEntry Example](../GlacialCache.Example.CacheEntry/) - Advanced cache entry features
- [MemoryPack Example](../GlacialCache.Example.MemoryPack/) - High-performance serialization
- [Token Expiration Example](../GlacialCache.Example.TokenExpiration/) - Azure token management

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

---

**🎉 Happy Caching!** This basic example shows how easy it is to get started with GlacialCache.
