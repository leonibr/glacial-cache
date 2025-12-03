# GlacialCache Web API Example

This example demonstrates how to integrate GlacialCache PostgreSQL into an ASP.NET Core Web API application, providing RESTful endpoints for cache operations.

## 🚀 Features Demonstrated

- RESTful API endpoints for cache operations
- Swagger/OpenAPI documentation
- Health check endpoints
- Batch operations via REST API
- Error handling and validation
- Configuration management
- Docker containerization

## 🏃‍♂️ Quick Start

### Prerequisites

- .NET 8.0 or later
- PostgreSQL database
- ASP.NET Core development environment

### Running the Example

```bash
# Using dotnet CLI
dotnet run --project GlacialCache.Example.WebApi.csproj

# Using Docker
docker build -t GlacialCache-webapi .
docker run -p 8080:80 GlacialCache-webapi

# Using Docker Compose
docker-compose up
```

The API will be available at:

- **API**: http://localhost:5000 or https://localhost:5001
- **Swagger UI**: http://localhost:5000/swagger

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

Create a `docker-compose.yml` file in the project directory:

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
    volumes:
      - postgres_data:/var/lib/postgresql/data

  GlacialCache-webapi:
    build: .
    ports:
      - '8080:80'
    depends_on:
      - postgres
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=glacialcache;Username=postgres;Password=password

volumes:
  postgres_data:
```

## 📋 API Endpoints

### Cache Operations

#### Get Cached Value

```http
GET /cache/string/{key}
```

**Response:**

```json
{
  "key": "my-key",
  "value": "my-value"
}
```

#### Set Cached Value

```http
POST /cache/string/{key}
Content-Type: application/json

"my-value"
```

**Response:**

```json
{
  "key": "my-key",
  "value": "my-value",
  "message": "Cached successfully"
}
```

#### Remove Cached Value

```http
DELETE /cache/{key}
```

**Response:**

```json
{
  "key": "my-key",
  "message": "Removed from cache"
}
```

### Batch Operations

#### Set Multiple Values

```http
POST /cache/batch
Content-Type: application/json

{
  "key1": "value1",
  "key2": "value2",
  "key3": "value3"
}
```

**Response:**

```json
{
  "count": 3,
  "message": "Batch cached successfully"
}
```

#### Get Multiple Values

```http
GET /cache/batch?keys=key1,key2,key3
```

**Response:**

```json
{
  "key1": "value1",
  "key2": "value2",
  "key3": "value3"
}
```

### Health Checks

#### Cache Health Check

```http
GET /health/cache
```

**Response:**

```json
{
  "status": "healthy",
  "message": "Cache is working correctly",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

#### Cache Statistics

```http
GET /cache/stats
```

**Response:**

```json
{
  "cacheType": "GlacialCache PostgreSQL",
  "status": "operational",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

## 🧪 Testing the API

### Using curl

```bash
# Set a value
curl -X POST "http://localhost:5000/cache/string/my-key" \
     -H "Content-Type: application/json" \
     -d '"Hello, World!"'

# Get the value
curl "http://localhost:5000/cache/string/my-key"

# Set multiple values
curl -X POST "http://localhost:5000/cache/batch" \
     -H "Content-Type: application/json" \
     -d '{"key1":"value1","key2":"value2"}'

# Get multiple values
curl "http://localhost:5000/cache/batch?keys=key1,key2"

# Check health
curl "http://localhost:5000/health/cache"
```

### Using PowerShell

```powershell
# Set a value
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/cache/string/test-key" `
    -Body '"PowerShell Test"' -ContentType "application/json"

# Get the value
Invoke-RestMethod -Uri "http://localhost:5000/cache/string/test-key"
```

### Using Swagger UI

1. Open http://localhost:5000/swagger in your browser
2. Explore the available endpoints
3. Test operations directly from the UI
4. View request/response examples

## 🏗️ Architecture

### Project Structure

```
GlacialCache.Example.WebApi/
├── Program.cs                    # Main application and API endpoints
├── GlacialCache.Example.WebApi.csproj
├── appsettings.json             # Configuration
├── appsettings.Development.json # Development configuration
├── Dockerfile                   # Docker build configuration
└── README.md                    # This documentation
```

### Middleware Pipeline

1. **Routing**: Maps HTTP requests to endpoints
2. **Authentication**: (Can be added for production use)
3. **Authorization**: (Can be added for production use)
4. **GlacialCache**: Provides caching functionality
5. **Swagger**: API documentation and testing

## 🔧 Configuration

### Database Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=glacialcache;Username=postgres;Password=password"
  }
}
```

### Cache Configuration

```json
{
  "GlacialCache": {
    "SchemaName": "cache",
    "TableName": "entries",
    "DefaultSlidingExpirationMinutes": 30,
    "DefaultAbsoluteExpirationHours": 1,
    "EnableManagerElection": false,
    "CreateInfrastructure": true,
    "CreateIfNotExists": true
  }
}
```

### Logging Configuration

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "GlacialCache.PostgreSQL": "Information"
    }
  }
}
```

## 🛠️ Development

### Building

```bash
dotnet build GlacialCache.Example.WebApi.csproj
```

### Running

```bash
# Development mode
dotnet run --project GlacialCache.Example.WebApi.csproj

# Production mode
dotnet run --project GlacialCache.Example.WebApi.csproj --environment Production
```

### Debugging

```bash
# With detailed logging
dotnet run --project GlacialCache.Example.WebApi.csproj --verbosity detailed

# With specific log levels
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project GlacialCache.Example.WebApi.csproj
```

## 🚀 Production Deployment

### Docker Deployment

```bash
# Build the image
docker build -t GlacialCache-webapi .

# Run the container
docker run -d \
  --name GlacialCache-webapi \
  -p 80:80 \
  -e ConnectionStrings__DefaultConnection="Host=your-db-host;Database=your-db;Username=your-user;Password=your-password" \
  GlacialCache-webapi
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: GlacialCache-webapi
spec:
  replicas: 3
  selector:
    matchLabels:
      app: GlacialCache-webapi
  template:
    metadata:
      labels:
        app: GlacialCache-webapi
    spec:
      containers:
        - name: GlacialCache-webapi
          image: GlacialCache-webapi:latest
          ports:
            - containerPort: 80
          env:
            - name: ConnectionStrings__DefaultConnection
              value: 'Host=postgres-service;Database=glacialcache;Username=postgres;Password=password'
```

## 📊 Monitoring

### Health Checks

The API includes health check endpoints that verify:

- Database connectivity
- Cache operations functionality
- Response times

### Logging

All cache operations are logged with appropriate levels:

- **Information**: Successful operations
- **Warning**: Non-critical issues
- **Error**: Failed operations

### Metrics

Consider adding:

- Response time metrics
- Cache hit/miss ratios
- Error rates
- Database connection pool stats

## 🔒 Security Considerations

### For Production Use

1. **HTTPS Only**: Always use HTTPS in production
2. **Authentication**: Add authentication middleware
3. **Authorization**: Implement role-based access control
4. **Rate Limiting**: Add rate limiting to prevent abuse
5. **Input Validation**: Validate all inputs thoroughly
6. **CORS**: Configure CORS policies appropriately

### Example Security Configuration

```csharp
// Add authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { ... });

// Add authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins("https://yourdomain.com")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

## 📚 Related Examples

- [Basic Example](../GlacialCache.Example.Basic/) - Console application with basic operations
- [Azure Example](../GlacialCache.Example.Azure/) - Azure Managed Identity authentication
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

**🌐 RESTful Caching Made Easy!** This Web API example shows how to expose GlacialCache functionality through a clean, documented REST interface.
