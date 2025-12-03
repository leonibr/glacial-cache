# GlacialCache Examples

This directory contains comprehensive examples demonstrating various GlacialCache PostgreSQL features and integration patterns. Each example is now separated into its own project for better organization and clarity.

## 📁 Project Structure

```
examples/
├── GlacialCache.Example.Basic/           # Basic PostgreSQL operations
├── GlacialCache.Example.CacheEntry/      # Advanced CacheEntry features
├── GlacialCache.Example.MemoryPack/      # High-performance serialization
├── GlacialCache.Example.WebApi/          # RESTful API integration
├── docker-compose.yml                  # Multi-service orchestration
└── README.md                          # This documentation
```

## 🚀 Quick Start

### Prerequisites

- .NET 10.0
- PostgreSQL database
- Docker and Docker Compose (for containerized examples)

### Choose Your Example

| Example                                            | Purpose                        | Framework         | Key Features                            |
| -------------------------------------------------- | ------------------------------ | ----------------- | --------------------------------------- |
| **[Basic](./GlacialCache.Example.Basic/)**           | Standard PostgreSQL operations | .NET 10.0 Console | Cache operations, batch processing      |
| **[CacheEntry](./GlacialCache.Example.CacheEntry/)** | Advanced cache features        | .NET 10.0 Console | Rich metadata, TimeProvider integration |
| **[MemoryPack](./GlacialCache.Example.MemoryPack/)** | High-performance serialization | .NET 10.0 Console | 10x faster serialization, type safety   |
| **[Web API](./GlacialCache.Example.WebApi/)**        | RESTful API integration        | .NET 10.0 Web API | HTTP endpoints, Swagger documentation   |

## 🏃‍♂️ Running Examples

### Option 1: Individual Examples

Each example can be run independently:

```bash
# Basic Example
cd GlacialCache.Example.Basic
dotnet run --project GlacialCache.Example.Basic.csproj

# CacheEntry Example
cd GlacialCache.Example.CacheEntry
dotnet run --project GlacialCache.Example.CacheEntry.csproj

# MemoryPack Example
cd GlacialCache.Example.MemoryPack
dotnet run --project GlacialCache.Example.MemoryPack.csproj

# Web API Example
cd GlacialCache.Example.WebApi
dotnet run --project GlacialCache.Example.WebApi.csproj
# API available at http://localhost:5000/swagger
```

### Option 2: Docker Compose (Recommended)

Use Docker Compose to run examples with PostgreSQL:

```bash
# Run Basic Example
docker-compose --profile basic up postgres GlacialCache-basic

# Run CacheEntry Example
docker-compose --profile cacheentry up postgres GlacialCache-cacheentry

# Run MemoryPack Example
docker-compose --profile memorypack up postgres GlacialCache-memorypack

# Run Web API Example
docker-compose --profile webapi up postgres GlacialCache-webapi
# API available at http://localhost:8080/swagger

# Run All Examples
docker-compose up
```

### Option 3: Development Scripts

Use the provided PowerShell/Bash scripts:

```bash
# PowerShell (Windows)
.\run-examples.ps1 -Version basic -Docker

# Bash (Linux/macOS)
./run-examples.sh -v basic -d
```

## 🐳 Docker Profiles

The `docker-compose.yml` uses profiles to run specific examples:

```bash
# Available profiles
docker-compose --profile basic up           # Basic PostgreSQL operations
docker-compose --profile cacheentry up      # CacheEntry features
docker-compose --profile memorypack up      # MemoryPack serialization
docker-compose --profile webapi up          # RESTful API

# Run multiple profiles
docker-compose --profile basic --profile cacheentry up
```

## 🔧 Configuration

Each example includes its own configuration files:

### Environment Variables

```bash
# Database connection
export POSTGRES_HOST=localhost
export POSTGRES_DB=GlacialCache
export POSTGRES_USER=postgres
export POSTGRES_PASSWORD=password
```

### Database Setup

The examples use a shared PostgreSQL database. Initialize it with:

```sql
-- Run init.sql to create database structure
psql -U postgres -d GlacialCache -f examples/GlacialCache.Example/init.sql
```

## 📊 Example Comparison

| Feature               | Basic | CacheEntry | MemoryPack | Web API |
| --------------------- | ----- | ---------- | ---------- | ------- |
| PostgreSQL Standard   | ✅    | ✅         | ✅         | ✅      |
| Batch Operations      | ✅    | ✅         | ✅         | ✅      |
| RESTful API           | ❌    | ❌         | ❌         | ✅      |
| High Performance      | ❌    | ❌         | ✅         | ❌      |
| Rich Metadata         | ❌    | ✅         | ❌         | ❌      |
| Swagger Documentation | ❌    | ❌         | ❌         | ✅      |

## 🧪 Testing

### Automated Testing

```bash
# Run all tests
dotnet test ../tests/GlacialCache.PostgreSQL.Tests/

# Run integration tests
dotnet test ../tests/GlacialCache.PostgreSQL.Tests/ --filter "Integration"

# Run performance benchmarks
dotnet run --project ../src/GlacialCache.Benchmarks/
```

### Manual Testing

Each example includes comprehensive demonstrations:

1. **Basic Example**: Core cache operations
2. **CacheEntry Example**: Advanced metadata features
3. **MemoryPack Example**: Performance comparisons
4. **Web API Example**: HTTP endpoint testing

## 🚀 Production Deployment

### Docker Deployment

```bash
# Build all examples
docker-compose build

# Deploy specific service
docker-compose --profile webapi up -d

# Scale services
docker-compose --profile webapi up -d --scale GlacialCache-webapi=3
```

### Kubernetes

Each example can be deployed to Kubernetes with appropriate manifests:

```bash
# Deploy PostgreSQL
kubectl apply -f k8s/postgres.yaml

# Deploy Web API
kubectl apply -f k8s/webapi.yaml
```

## 🔍 Troubleshooting

### Common Issues

1. **PostgreSQL Connection Failed**

   ```bash
   # Check if PostgreSQL is running
   docker-compose ps postgres

   # View logs
   docker-compose logs postgres
   ```

2. **Example Won't Start**

   ```bash
   # Check dependencies
   docker-compose --profile basic config

   # View example logs
   docker-compose --profile basic logs GlacialCache-basic
   ```

3. **Port Conflicts**

   ```bash
   # Check port usage
   netstat -an | grep 5432
   netstat -an | grep 8080

   # Change ports in docker-compose.yml
   ```

### Debug Mode

```bash
# Run with detailed logging
export ASPNETCORE_ENVIRONMENT=Development
docker-compose --profile basic up

# Enable debug logging
export Logging__LogLevel__GlacialCache__PostgreSQL=Debug
```

## 📚 Documentation

- [GlacialCache Main Documentation](../../README.md)
- [API Reference](../../src/GlacialCache.PostgreSQL/README.md)
- [Performance Benchmarks](../../src/GlacialCache.Benchmarks/README.md)

## 🤝 Contributing

1. **Choose an Example**: Pick the example most relevant to your contribution
2. **Follow Patterns**: Maintain consistency with existing examples
3. **Add Documentation**: Update README files with new features
4. **Test Thoroughly**: Ensure all examples work correctly
5. **Submit PR**: Create a pull request with clear description

### Adding New Examples

1. Create new directory: `examples/GlacialCache.Example.NewFeature/`
2. Add project file: `GlacialCache.Example.NewFeature.csproj`
3. Create Program.cs with demonstration code
4. Add Dockerfile and docker-compose profile
5. Update this README with new example information

## 📈 Performance Tips

### MemoryPack Example

- Use for high-throughput scenarios
- Consider for large object serialization
- Monitor memory usage with the provided metrics

### CacheEntry Example

- Leverage TimeProvider for precise timing
- Use batch operations for efficiency
- Implement proper expiration strategies

## 🎯 Best Practices

1. **Environment Separation**: Use different databases for development/production
2. **Connection Pooling**: Configure appropriate pool sizes for your workload
3. **Monitoring**: Implement health checks and metrics collection
4. **Security**: Use secure connection strings and proper authentication
5. **Performance**: Choose the right serialization method for your use case
6. **Scalability**: Design for horizontal scaling when needed

## 📞 Support

- **Issues**: [GitHub Issues](../../issues)
- **Discussions**: [GitHub Discussions](../../discussions)
- **Documentation**: [GlacialCache Docs](../../docs/)

---

**🎉 Welcome to GlacialCache Examples!** Choose the example that best fits your needs and start building high-performance caching solutions.
