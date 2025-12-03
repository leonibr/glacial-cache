## Troubleshooting

This guide lists common issues when using GlacialCache.PostgreSQL and how to fix them.

---

## Connection and connectivity issues

### Cannot connect to PostgreSQL

**Symptoms**

- Exceptions such as:
  - `Npgsql.NpgsqlException: Failed to connect to ...`
  - `System.TimeoutException: The operation has timed out.`

**Checks**

1. Verify the connection string:
   - Host, port, database, username, password.
   - SSL settings if required by your provider.
2. Confirm the database is reachable from the app host (firewall/VNet rules).
3. Ensure the user has permission to connect and create tables (on first run).

**Resolutions**

- Fix the connection string in configuration (environment variables or `appsettings.json`).
- If using containers, use the service name (e.g. `postgres`) instead of `localhost`.
- For managed Postgres (Azure, RDS), ensure IP / network rules allow your app to connect.

### Connection pool exhaustion

**Symptoms**

- Timeouts acquiring connections under load.
- Logs indicating many concurrent waits or pool exhaustion.

**Resolutions**

- Increase pool size:

  ```csharp
  options.Connection.Pool.MinSize = 10;
  options.Connection.Pool.MaxSize = 100;
  ```

- Reduce command/operation timeouts if they are excessively long.
- Review long-running operations or slow queries on the cache table using your PostgreSQL monitoring tools.

---

## Schema and table problems

### Cache table not found

**Symptoms**

- Errors like:
  - `relation "public.glacial_cache" does not exist`
  - `relation "cache.entries" does not exist` (if using custom schema/table)

**Checks**

1. Confirm `Cache.SchemaName` and `Cache.TableName` in your configuration match the actual schema/table in PostgreSQL.
   - Default values: `SchemaName = "public"`, `TableName = "glacial_cache"`
2. Check whether `Infrastructure.CreateInfrastructure` is enabled on at least one instance or that migrations have been applied.

**Resolutions**

- For development/single instance (using defaults):

  ```csharp
  builder.Services.AddGlacialCachePostgreSQL(options =>
  {
      options.Connection.ConnectionString = connectionString;
      // Defaults: SchemaName = "public", TableName = "glacial_cache"
      options.Infrastructure.CreateInfrastructure = true;
      options.Infrastructure.EnableManagerElection = false;
  });
  ```

- For custom schema/table names:

  ```csharp
  builder.Services.AddGlacialCachePostgreSQL(options =>
  {
      options.Connection.ConnectionString = connectionString;
      options.Cache.SchemaName = "cache";  // custom schema
      options.Cache.TableName = "entries"; // custom table
      options.Infrastructure.CreateInfrastructure = true;
      options.Infrastructure.EnableManagerElection = false;
  });
  ```

- For production / multi-instance:
  - Run the schema creation SQL manually or via migrations (see `Database Schema` section in the package README).
  - Set `CreateInfrastructure = false` once the schema is in place to avoid race conditions.

### Permission errors on schema or table

**Symptoms**

- `permission denied for schema ...`
- `permission denied for table ...`

**Resolutions**

- Grant the application user the necessary privileges.

  For default schema/table (`public.glacial_cache`):

  ```sql
  -- Usually the public schema is accessible by default, but if not:
  GRANT USAGE ON SCHEMA public TO app_user;
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.glacial_cache TO app_user;
  ```

  For custom schema/table (e.g., `cache.entries`):

  ```sql
  -- Create schema if it doesn't exist
  CREATE SCHEMA IF NOT EXISTS cache;

  -- Grant schema access
  GRANT USAGE ON SCHEMA cache TO app_user;
  GRANT CREATE ON SCHEMA cache TO app_user; -- if app needs to create tables

  -- Grant table permissions
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE cache.entries TO app_user;
  ```

- Ensure the connection string uses the correct database/user.

---

## Expiration and cleanup issues

### Entries never seem to expire

**Checks**

1. Confirm you are setting expiration on entries:

   ```csharp
   var opts = new DistributedCacheEntryOptions
   {
       AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
       SlidingExpiration = TimeSpan.FromMinutes(5)
   };
   ```

2. Check `Cache.DefaultSlidingExpiration` / `DefaultAbsoluteExpirationRelativeToNow` for defaults.
3. Ensure `Maintenance.EnableAutomaticCleanup` is `true`.
4. Inspect logs for `CleanupBackgroundService` to see whether it is running.

**Resolutions**

- Enable cleanup and shorten the interval:

  ```csharp
  options.Maintenance.EnableAutomaticCleanup = true;
  options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(5);
  ```

- Verify that the manager-election configuration allows at least one instance to run cleanup (see below).

### Cleanup not running in a multi-instance deployment

**Symptoms**

- No cleanup logs.
- Expired rows accumulate in the table.

**Checks**

1. `Infrastructure.EnableManagerElection` is `true` (default).
2. All instances point to the same database and schema.
3. No persistent errors in logs from `ElectionBackgroundService`.

**Resolutions**

- Ensure your instances share the same `Infrastructure.Lock` configuration so they compete for the same advisory lock.
- If you only have one instance, consider disabling election:

  ```csharp
  options.Infrastructure.EnableManagerElection = false;
  ```

  The cleanup service will then run without needing a manager.

---

## Performance and locking

### High database CPU or slow queries on the cache table

**Checks**

1. Use `EXPLAIN ANALYZE` on key queries (lookups, cleanup).
2. Check for missing indexes if you customized the schema.
3. Ensure statistics are up to date (`ANALYZE`).

**Resolutions**

- Use the default schema or replicate its indexes for custom schemas.
- Reduce `Maintenance.MaxCleanupBatchSize` if large deletes are causing contention:

  ```csharp
  options.Maintenance.MaxCleanupBatchSize = 500;
  ```

- Increase `CleanupInterval` if cleanup is running too frequently and causing load.

### Locks or deadlocks on the cache table

**Symptoms**

- `deadlock detected` errors.
- Long-running transactions holding locks on the cache table.

**Resolutions**

- Avoid wrapping cache operations in large user transactions when possible.
- Keep individual cache operations outside of long-running database transactions.
- If necessary, coordinate heavy batch operations to off-peak times or use smaller batches.

---

## Azure Managed Identity problems

### Authentication failures using Managed Identity

**Symptoms**

- Errors such as:
  - `Azure.Identity.CredentialUnavailableException: ManagedIdentityCredential authentication unavailable`
  - `Npgsql.NpgsqlException: 28000: password authentication failed for user "..."`
  - `Azure.RequestFailedException: Service request failed. Status: 400 (Bad Request)`
  - Token refresh failures logged but connections still attempted

**Checks**

1. **Connection string validation**

   - `BaseConnectionString` must NOT include a password parameter.
   - Should look like: `Host=myserver.postgres.database.azure.com;Database=mydb;Username=myuser@myserver`

2. **Managed identity setup**

   - System-assigned or user-assigned managed identity is enabled on the compute resource (App Service, VM, AKS, Container Apps, etc.).
   - For user-assigned, ensure `ClientId` is specified in `AzureOptions`.

3. **PostgreSQL server permissions**

   - The managed identity has been granted connect privileges:

     ```sql
     -- For system-assigned identity
     GRANT CONNECT ON DATABASE mydb TO "my-app-name";

     -- For user-assigned identity
     GRANT CONNECT ON DATABASE mydb TO "client-id-of-managed-identity";
     ```

4. **Network access**
   - Compute resource can reach Azure Instance Metadata Service (IMDS) at `http://169.254.169.254`.
   - PostgreSQL server firewall allows connections from the compute resource's IP/VNet.

**Resolutions**

1. **Fix connection string**

   - Remove any `Password=...` from the base connection string.
   - Use the helper method:
     ```csharp
     builder.Services.AddGlacialCachePostgreSQLWithAzureManagedIdentity(
         baseConnectionString: "Host=myserver.postgres.database.azure.com;Database=mydb;Username=myuser",
         resourceId: "https://ossrdbms-aad.database.windows.net"
     );
     ```

2. **Verify managed identity configuration**

   - In Azure Portal, navigate to your App Service / VM / AKS and confirm "Identity" blade shows "System assigned: On" or user-assigned identity is listed.
   - Check Application Insights or logs for identity token acquisition attempts.

3. **Grant database permissions**

   - Use Azure CLI or portal to grant the managed identity access to the PostgreSQL server:
     ```bash
     az postgres flexible-server ad-admin create --resource-group myRG --server-name myserver --object-id <identity-object-id> --display-name my-app-identity
     ```

4. **Verify IMDS connectivity**

   - From your compute resource (using SSH, Kudu console, or container exec):
     ```bash
     curl -H "Metadata:true" "http://169.254.169.254/metadata/instance?api-version=2021-02-01"
     ```
   - Should return instance metadata JSON (not a timeout or error).

5. **Check token refresh in logs**
   - Enable logging at `Information` level for GlacialCache:
     ```json
     {
       "Logging": {
         "LogLevel": {
           "GlacialCache.PostgreSQL": "Information"
         }
       }
     }
     ```
   - Look for log entries like:
     - `Azure token refreshed successfully`
     - `Azure token refresh failed, retrying...`
   - If tokens expire and aren't refreshed, verify `TokenRefreshBuffer` and `MaxRetryAttempts`:
     ```csharp
     azureOptions.TokenRefreshBuffer = TimeSpan.FromHours(1);
     azureOptions.MaxRetryAttempts = 5;
     azureOptions.RetryDelay = TimeSpan.FromSeconds(2);
     ```

### Token refresh not happening

**Symptoms**

- Connections work initially but fail after ~24 hours (typical Azure token lifetime).
- No token refresh logs appearing.

**Resolutions**

- Ensure `TokenRefreshBuffer` is set appropriately (default: 1 hour before expiration):
  ```csharp
  azureOptions.TokenRefreshBuffer = TimeSpan.FromHours(2); // refresh 2 hours early
  ```
- Check that the application is a long-running hosted service (not a short-lived console app).
- Review background service logs to confirm token refresh timer is active.

### User-assigned identity not working

**Symptoms**

- System-assigned identity works, but user-assigned fails.

**Resolutions**

- Explicitly provide the `ClientId` of the user-assigned identity:
  ```csharp
  builder.Services.AddGlacialCachePostgreSQLWithAzureManagedIdentity(
      azureOptions =>
      {
          azureOptions.BaseConnectionString = "...";
          azureOptions.ResourceId = "https://ossrdbms-aad.database.windows.net";
          azureOptions.ClientId = "12345678-1234-1234-1234-123456789abc"; // user-assigned MI client ID
      },
      cacheOptions => { /* ... */ }
  );
  ```

---

## Logging and diagnostics

### Enabling detailed logs

Add logging configuration to `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "GlacialCache.PostgreSQL": "Debug"
    }
  }
}
```

This enables more verbose logs from GlacialCache without flooding output from the rest of the framework.

### Verifying cache health

You can implement a simple health check endpoint:

```csharp
app.MapGet("/health/cache", async (IDistributedCache cache) =>
{
    var key = $"health-{Guid.NewGuid()}";
    var value = DateTime.UtcNow.ToString("O");

    await cache.SetStringAsync(key, value, new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
    });

    var roundTrip = await cache.GetStringAsync(key);
    await cache.RemoveAsync(key);

    return roundTrip == value
        ? Results.Ok(new { status = "healthy" })
        : Results.Problem("Cache round-trip failed", statusCode: 500);
});
```

If this endpoint succeeds, your configuration and basic operations are working correctly.
