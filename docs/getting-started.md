## Getting started with GlacialCache.PostgreSQL in ASP.NET Core

This guide shows how to plug GlacialCache.PostgreSQL into a minimal ASP.NET Core application and start caching data using `IDistributedCache`.

### Prerequisites

- **.NET**: .NET 8.0, 9.0, or 10.0
- **Database**: A reachable PostgreSQL instance (PostgreSQL 12 or later recommended)
- **Connection string**: User with permissions to create tables in the target database (for first run)

GlacialCache will create the cache table automatically when `CreateInfrastructure` is enabled.

### 1. Install the NuGet package

```bash
dotnet add package GlacialCache.PostgreSQL
```

### 2. Minimal `Program.cs`

Copy-paste this into a new ASP.NET Core project (e.g. `dotnet new webapi`), replacing the generated `Program.cs`.

```csharp
using System.Text;
using System.Text.Json;
using GlacialCache.PostgreSQL;
using GlacialCache.PostgreSQL.Extensions;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

// Logging (optional but recommended)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Basic configuration: connection string from configuration or fallback
var connectionString = builder.Configuration.GetConnectionString("GlacialCache") ??
                       "Host=localhost;Database=glacialcache;Username=postgres;Password=postgres";

// Option 1: Simple connection-string overload
// builder.Services.AddGlacialCachePostgreSQL(connectionString);

// Option 2: Full options overload (recommended for production)
builder.Services.AddGlacialCachePostgreSQL(options =>
{
    options.Connection.ConnectionString = connectionString;

    // Cache schema and table (using defaults: "public" schema, "glacial_cache" table)
    // For this example, we'll use custom names:
    options.Cache.SchemaName = "public";
    options.Cache.TableName = "glacial_cache";

    // Default expirations (can be overridden per entry)
    options.Cache.DefaultSlidingExpiration = TimeSpan.FromMinutes(20);
    options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);

    // Automatically create schema/table on startup
    options.Infrastructure.CreateInfrastructure = true;

    // For single-instance or dev environments you can disable manager election
    // (in clustered scenarios, leave this enabled so only one node runs maintenance)
    options.Infrastructure.EnableManagerElection = false;
});

// Add minimal services / endpoints
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Simple cache endpoints using IDistributedCache
app.MapGet("/cache/{key}", async (string key, IDistributedCache cache) =>
{
    var bytes = await cache.GetAsync(key);
    if (bytes is null)
    {
        return Results.NotFound();
    }

    var value = Encoding.UTF8.GetString(bytes);
    return Results.Ok(new { key, value });
});

app.MapPost("/cache/{key}", async (string key, HttpContext http, IDistributedCache cache) =>
{
    // Read request body as string
    using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync();

    var options = new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
        SlidingExpiration = TimeSpan.FromMinutes(10)
    };

    await cache.SetAsync(key, Encoding.UTF8.GetBytes(body), options);

    return Results.Ok(new { key, value = body });
});

// Example endpoint that uses the cache as an application concern
app.MapGet("/products/{id:int}", async (int id, IDistributedCache cache) =>
{
    var cacheKey = $"product:{id}";

    // Try cache
    var cachedBytes = await cache.GetAsync(cacheKey);
    if (cachedBytes is not null)
    {
        var cachedJson = Encoding.UTF8.GetString(cachedBytes);
        var cachedProduct = JsonSerializer.Deserialize<Product>(cachedJson);
        return Results.Ok(new { source = "cache", product = cachedProduct });
    }

    // Simulate a database fetch
    var product = new Product(id, $"Product {id}", 9.99m);

    // Store in cache
    var json = JsonSerializer.Serialize(product);
    await cache.SetAsync(
        cacheKey,
        Encoding.UTF8.GetBytes(json),
        new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(5)
        });

    return Results.Ok(new { source = "database", product });
});

app.Run();

public record Product(int Id, string Name, decimal Price);
```

### 3. Configuration (`appsettings.json`)

Add a connection string to your `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "GlacialCache": "Host=localhost;Database=glacialcache;Username=postgres;Password=postgres"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "GlacialCache.PostgreSQL": "Information"
    }
  }
}
```

### 4. Running the application

```bash
dotnet run
```

Then exercise the cache:

- **Set a value**

  ```bash
  curl -X POST "https://localhost:5001/cache/greeting" -d "Hello from GlacialCache"
  ```

- **Get a value**

  ```bash
  curl "https://localhost:5001/cache/greeting"
  ```

### 5. Next steps

- See `docs/concepts.md` for expiration behavior and data model details.
- See `docs/configuration.md` for all available configuration options.
- See `docs/troubleshooting.md` if you hit connectivity or performance issues.
