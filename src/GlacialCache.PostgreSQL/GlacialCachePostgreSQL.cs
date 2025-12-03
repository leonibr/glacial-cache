using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using MemoryPack;

namespace GlacialCache.PostgreSQL;
using Models;
using Models.CommandParameters;
using Services;
using Abstractions;
using Configuration;
using Logging;
using Extensions;


/// <summary>
/// Enhanced PostgreSQL implementation of IDistributedCache with connection optimization.
/// </summary>
public class GlacialCachePostgreSQL : IGlacialCache, IDisposable
{
    private readonly IOptionsMonitor<GlacialCachePostgreSQLOptions> _optionsMonitor;
    private readonly IDisposable? _optionsChangeToken;
    private GlacialCachePostgreSQLOptions _options;
    private readonly ILogger<GlacialCachePostgreSQL> _logger;
    private readonly ITimeConverterService _timeConverter;
    private readonly IPostgreSQLDataSource _dataSource;
    private readonly IDbRawCommands _dbRawCommands;
    private bool _disposed = false;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPolicyFactory? _policyFactory;
    private readonly IAsyncPolicy? _resiliencePolicy;
    private readonly TimeProvider _timeProvider;
    private readonly GlacialCacheEntryFactory _entryFactory;




    internal GlacialCachePostgreSQL(
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        ILogger<GlacialCachePostgreSQL> logger,
        ITimeConverterService timeConverter,
        IPostgreSQLDataSource dataSource,
        IDbRawCommands dbRawCommands,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        GlacialCacheEntryFactory entryFactory)
    {

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(timeConverter);
        ArgumentNullException.ThrowIfNull(dbRawCommands);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(entryFactory);
        if (string.IsNullOrEmpty(options.CurrentValue.Connection.ConnectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(options));

        _optionsMonitor = options;
        _options = options.CurrentValue ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _dataSource = dataSource;
        _timeConverter = timeConverter;
        _dbRawCommands = dbRawCommands;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _entryFactory = entryFactory;

        // Initialize resilience patterns if enabled
        if (_options.Resilience.EnableResiliencePatterns)
        {
            _policyFactory = serviceProvider.GetService<IPolicyFactory>();
            if (_policyFactory != null)
            {
                _resiliencePolicy = _policyFactory.CreateResiliencePolicy(_options);
            }
        }

        // Register for external configuration changes (IOptionsMonitor pattern)
        _optionsChangeToken = _optionsMonitor.OnChange(OnExternalConfigurationChanged);

        // Initialize the cache table synchronously
        EnsureInitialized();
    }

    /// <summary>
    /// Handles external configuration changes from IOptionsMonitor and syncs to observable properties.
    /// </summary>
    private void OnExternalConfigurationChanged(GlacialCachePostgreSQLOptions newOptions)
    {
        try
        {
            // Use extension method to sync observable properties for Cache and Connection
            _options.Cache.SyncFromExternalChanges(newOptions.Cache, _logger);
            _options.Connection.SyncFromExternalChanges(newOptions.Connection, _logger);
            _options.Connection.Pool.SyncFromExternalChanges(newOptions.Connection.Pool, _logger);

            // Update our internal reference
            _options = newOptions;

            _logger.LogDebug("External configuration changes synchronized to observable properties");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync external configuration changes");
        }
    }

    /// <summary>
    /// Initializes the cache table if it doesn't exist.
    /// </summary>
    private void EnsureInitialized()
    {
        try
        {
            // Execute schema management instead of migrations
            var schemaManager = _serviceProvider.GetRequiredService<ISchemaManager>();
            schemaManager.EnsureSchemaAsync().GetAwaiter().GetResult();

            _logger.LogCacheInitialized();
        }
        catch (Exception ex)
        {
            _logger.LogInitializationError(ex);
            throw;
        }
    }

    public byte[]? Get(string key)
    {
        return GetAsync(key).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Retrieves a cache entry by its key.
    /// </summary>
    /// <param name="key">The key of the cache entry to retrieve.</param>
    /// <returns>The cache entry if found and not expired; otherwise, null.</returns>
    public CacheEntry<byte[]>? GetEntry(string key)
    {
        return GetEntryAsync(key).GetAwaiter().GetResult();
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));
        }


        return await ExecuteWithResilienceAsync(GetAsyncCore(key, token),
            operationName: "GetAsync",
            key: key);

    }

    /// <summary>
    /// Retrieves a cache entry by its key asynchronously.
    /// </summary>
    /// <param name="key">The key of the cache entry to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The cache entry if found and not expired; otherwise, null.</returns>
    public async Task<CacheEntry<byte[]>?> GetEntryAsync(string key, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));
        }


        return await ExecuteWithResilienceAsync(
            GetEntryAsyncCore(key, token),
            operationName: "GetEntryAsync",
            key: key);

    }

    private async Task<byte[]?> GetAsyncCore(string key, CancellationToken token = default)
    {
        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            await using var command = new NpgsqlCommand(_dbRawCommands.GetSqlCore, connection);

            var parameters = new GetEntryParameters
            {
                Key = key,
                Now = _timeProvider.GetUtcNow()
            };

            command.AddParameters(parameters);

            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as byte[];
        }
        catch (Exception ex)
        {
            _logger.LogCacheGetError(key, ex);
            throw;
        }
    }

    private async Task<CacheEntry<byte[]>?> GetEntryAsyncCore(string key, CancellationToken token = default)
    {
        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Phase 1 optimization: Use pre-compiled SQL with CTE for atomic sliding expiration updates
            await using var command = new NpgsqlCommand(_dbRawCommands.GetSql, connection);

            var parameters = new GetEntryParameters
            {
                Key = key,
                Now = _timeProvider.GetUtcNow()
            };

            command.AddParameters(parameters);

            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);

            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return null; // Key not found
            }

            var value = reader.GetFieldValue<byte[]>(0); // value column - guaranteed non-null since UPDATE...RETURNING only returns existing rows
            var absoluteExpiration = reader.IsDBNull(1)
                ? null
                : (DateTimeOffset?)DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
            var slidingExpiration = reader.IsDBNull(2)
                ? null
                : (TimeSpan?)reader.GetTimeSpan(2);
            string? baseType = null;
            if (reader.FieldCount > 3 && !reader.IsDBNull(3))
            {
                baseType = reader.GetString(3);  // value_type column (was index 4, now index 3)
            }
            var nextExpirationUtc = reader.FieldCount > 5 && !reader.IsDBNull(5)
                ? (DateTimeOffset?)DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)  // next_expiration column (was index 6, now index 5)
                : null;

            // Check if value is null (should not happen in normal cases, but handle gracefully)
            if (value == null)
            {
                return null;
            }


            // Note: Sliding expiration is now handled atomically by the CTE in the SQL query above
            // Entry is guaranteed to be non-expired if we reach this point

            return _entryFactory.FromSerializedData<byte[]>(key, value, absoluteExpiration, slidingExpiration, baseType);
        }
        catch (Exception ex)
        {
            _logger.LogCacheGetError(key, ex);
            throw;
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        SetAsync(key, value, options).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sets a cache entry.
    /// </summary>
    /// <param name="entry">The cache entry to set.</param>
    public void SetEntry(CacheEntry<byte[]> entry)
    {
        SetEntryAsync(entry).GetAwaiter().GetResult();
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value, nameof(value));

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));
        }


        await ExecuteWithResilienceAsync(SetAsyncCore(key, value, options, token),
            operationName: "SetAsync",
            key: key);
        return;
    }

    /// <summary>
    /// Sets a cache entry asynchronously.
    /// </summary>
    /// <param name="entry">The cache entry to set.</param>
    /// <param name="token">Cancellation token.</param>
    public async Task SetEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Key))
        {
            throw new ArgumentException("Entry key cannot be null, empty, or whitespace.", nameof(entry));
        }


        await ExecuteWithResilienceAsync(SetEntryAsyncCore(entry, token),
            operationName: "SetEntryAsync",
            key: entry.Key);

    }

    private async Task SetAsyncCore(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        // Convert absolute expiration to relative interval using TimeConverterService
        TimeSpan? relativeInterval = null;

        // Handle DistributedCacheEntryOptions logic in the calling code
        if (options.AbsoluteExpiration.HasValue)
        {
            relativeInterval = _timeConverter.ConvertToRelativeInterval(options.AbsoluteExpiration.Value);
        }
        else if (options.AbsoluteExpirationRelativeToNow.HasValue)
        {
            relativeInterval = options.AbsoluteExpirationRelativeToNow.Value;
            // Handle negative intervals if needed
            if (relativeInterval <= TimeSpan.Zero)
            {
                relativeInterval = TimeSpan.FromMilliseconds(1);
            }
        }
        else if (_options.Cache.DefaultAbsoluteExpirationRelativeToNow.HasValue)
        {
            relativeInterval = _options.Cache.DefaultAbsoluteExpirationRelativeToNow.Value;
        }

        var slidingInterval = options.SlidingExpiration ?? _options.Cache.DefaultSlidingExpiration;

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Phase 1 optimization: Use pre-compiled SQL with optimized parameter handling
            await using var command = new NpgsqlCommand(_dbRawCommands.SetSql, connection);

            var parameters = new SetEntryParameters
            {
                Key = key,
                Value = value,
                Now = _timeProvider.GetUtcNow(),
                RelativeInterval = relativeInterval,
                SlidingInterval = slidingInterval,
                ValueType = null
            };

            command.AddParameters(parameters);

            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCacheSetError(key, ex);
            throw;
        }
    }

    private async Task SetEntryAsyncCore<T>(CacheEntry<T> entry, CancellationToken token = default)
    {
        // If SerializedData is empty, we need to serialize the Value first
        var entryToStore = entry.SerializedData.IsEmpty
            ? _entryFactory.Create(entry.Key, entry.Value, entry.AbsoluteExpiration, entry.SlidingExpiration)
            : entry;

        // Create a byte[] entry from the serialized value
        var byteEntry = _entryFactory.FromSerializedData<byte[]>(
            entryToStore.Key,
            entryToStore.SerializedData.ToArray(),
            entryToStore.AbsoluteExpiration,
            entryToStore.SlidingExpiration,
            entryToStore.BaseType);

        await SetEntryAsyncCore(byteEntry, token);
    }

    private async Task SetEntryAsyncCore(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Phase 1 optimization: Use pre-compiled SQL with optimized parameter handling
            await using var command = new NpgsqlCommand(_dbRawCommands.SetSql, connection);

            TimeSpan? relativeInterval = null;
            if (entry.AbsoluteExpiration.HasValue)
            {
                relativeInterval = _timeConverter.ConvertToRelativeInterval(entry.AbsoluteExpiration.Value);
            }

            var parameters = new SetEntryParameters
            {
                Key = entry.Key,
                Value = entry.SerializedData.ToArray(),
                Now = _timeProvider.GetUtcNow(),
                RelativeInterval = relativeInterval,
                SlidingInterval = entry.SlidingExpiration,
                ValueType = !string.IsNullOrWhiteSpace(entry.BaseType) ? entry.BaseType : null
            };

            command.AddParameters(parameters);

            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCacheSetError(entry.Key, ex);
            throw;
        }
    }

    public void Refresh(string key)
    {
        RefreshAsync(key).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Refreshes a cache entry. Note: Sliding expiration is now handled atomically by the database.
    /// </summary>
    /// <param name="entry">The cache entry to refresh.</param>
    public void RefreshEntry(CacheEntry<byte[]> entry)
    {
        RefreshEntryAsync(entry).GetAwaiter().GetResult();
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));
        }

        await ExecuteWithResilienceAsync(RefreshAsyncCore(key, token),
            operationName: "RefreshAsync",
            key: key);
        return;

    }

    /// <summary>
    /// Refreshes a cache entry asynchronously. Note: Sliding expiration is now handled atomically by the database.
    /// </summary>
    /// <param name="entry">The cache entry to refresh.</param>
    /// <param name="token">Cancellation token.</param>
    public async Task RefreshEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Key))
        {
            throw new ArgumentException("Entry key cannot be null, empty, or whitespace.", nameof(entry));
        }

        if (_resiliencePolicy != null)
        {
            await ExecuteWithResilienceAsync(RefreshEntryAsyncCore(entry, token),
                operationName: "RefreshEntryAsync",
                key: entry.Key);
            return;
        }

        await RefreshEntryAsyncCore(entry, token);
    }

    private async Task RefreshAsyncCore(string key, CancellationToken token = default)
    {
        // Note: Refresh operation no longer needs to update last accessed time
        // since sliding expiration is now handled by the database using next_expiration field
        await Task.CompletedTask;
    }

    private async Task RefreshEntryAsyncCore(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Phase 1 optimization: Use pre-compiled SQL
            await using var command = new NpgsqlCommand(_dbRawCommands.RefreshSql, connection);

            var parameters = new RefreshEntryParameters
            {
                Key = entry.Key,
                Now = _timeProvider.GetUtcNow()
            };

            command.AddParameters(parameters);

            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCacheRefreshError(entry.Key, ex);
            throw;
        }
    }

    public void Remove(string key)
    {
        RemoveAsync(key).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Removes a cache entry.
    /// </summary>
    /// <param name="entry">The cache entry to remove.</param>
    public void RemoveEntry(CacheEntry<byte[]> entry)
    {
        RemoveEntryAsync(entry).GetAwaiter().GetResult();
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));
        }


        await ExecuteWithResilienceAsync(RemoveAsyncCore(key, token),
            operationName: "RemoveAsync",
            key: key);

    }

    /// <summary>
    /// Removes a cache entry asynchronously.
    /// </summary>
    /// <param name="entry">The cache entry to remove.</param>
    /// <param name="token">Cancellation token.</param>
    public async Task RemoveEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Key))
        {
            throw new ArgumentException("Entry key cannot be null, empty, or whitespace.", nameof(entry));
        }


        await ExecuteWithResilienceAsync(RemoveEntryAsyncCore(entry, token),
            operationName: "RemoveEntryAsync",
            key: entry.Key);

    }

    private async Task RemoveAsyncCore(string key, CancellationToken token = default)
    {
        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Phase 1 optimization: Use pre-compiled SQL
            await using var command = new NpgsqlCommand(_dbRawCommands.DeleteSql, connection);

            var parameters = new RemoveEntryParameters
            {
                Key = key
            };

            command.AddParameters(parameters);

            await command.ExecuteNonQueryAsync(token);
        }
        catch (Exception ex)
        {
            _logger.LogCacheRemoveError(key, ex);
            throw;
        }
    }

    private async Task RemoveEntryAsyncCore(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Phase 1 optimization: Use pre-compiled SQL
            await using var command = new NpgsqlCommand(_dbRawCommands.DeleteSql, connection);

            var parameters = new RemoveEntryParameters
            {
                Key = entry.Key
            };

            command.AddParameters(parameters);

            await command.ExecuteNonQueryAsync(token);
        }
        catch (Exception ex)
        {
            _logger.LogCacheRemoveError(entry.Key, ex);
            throw;
        }
    }

    // ===== Batch Operations (Multiple Keys) =====

    /// <summary>
    /// Retrieves multiple cache entries by their keys in a single database operation.
    /// This is significantly more efficient than multiple individual GetAsync calls.
    /// </summary>
    /// <param name="keys">The keys of the cache entries to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A dictionary containing the keys and their corresponding values. Missing keys will not be included in the result.</returns>
    public async Task<Dictionary<string, byte[]?>> GetMultipleAsync(IEnumerable<string> keys, CancellationToken token = default)
    {
        ThrowIfDisposed();

        var keyArray = keys?.ToArray() ?? throw new ArgumentNullException(nameof(keys));
        if (keyArray.Length == 0)
            return new Dictionary<string, byte[]?>();

        // Direct allocation for better simplicity
        var result = new Dictionary<string, byte[]?>(keyArray.Length);
        var keysRequiringUpdate = new List<string>(keyArray.Length);

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            await using var command = new NpgsqlCommand(_dbRawCommands.GetMultipleSql, connection);
            command.Parameters.AddWithValue("@keys", keyArray);
            command.Parameters.AddWithValue("@now", _timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(token);

            while (await reader.ReadAsync(token))
            {
                var key = reader.GetString(0);
                var value = reader.GetFieldValue<byte[]>(1);

                result[key] = value;
            }

            // Return a copy of the result
            var resultCopy = new Dictionary<string, byte[]?>(result);
            return resultCopy;
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Retrieves multiple cache entries by their keys in a single database operation.
    /// This is significantly more efficient than multiple individual GetEntryAsync calls.
    /// </summary>
    /// <param name="keys">The keys of the cache entries to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A dictionary containing the keys and their corresponding cache entries. Missing keys will not be included in the result.</returns>
    public async Task<Dictionary<string, CacheEntry<byte[]>?>> GetMultipleEntriesAsync(IEnumerable<string> keys, CancellationToken token = default)
    {
        ThrowIfDisposed();

        var keyArray = keys?.ToArray() ?? throw new ArgumentNullException(nameof(keys));
        if (keyArray.Length == 0)
            return new Dictionary<string, CacheEntry<byte[]>?>();

        var result = new Dictionary<string, CacheEntry<byte[]>?>(keyArray.Length);
        var keysRequiringUpdate = new List<string>(keyArray.Length);

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            await using var command = new NpgsqlCommand(_dbRawCommands.GetMultipleSql, connection);
            command.Parameters.AddWithValue("@keys", keyArray);
            command.Parameters.AddWithValue("@lastAccessed", _timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@now", _timeProvider.GetUtcNow());

            await using var reader = await command.ExecuteReaderAsync(token);

            var now = _timeProvider.GetUtcNow();

            while (await reader.ReadAsync(token))
            {
                var key = reader.GetString(0); // key column
                var value = reader.GetFieldValue<byte[]>(1); // value column
                var absoluteExpiration = reader.IsDBNull(2)
                    ? (DateTimeOffset?)null
                    : DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
                var slidingExpiration = reader.IsDBNull(3) ? (TimeSpan?)null : reader.GetTimeSpan(3); // sliding_expiration column
                var nextExpirationUtc = reader.FieldCount > 6 && !reader.IsDBNull(6)
                    ? (DateTimeOffset?)DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)  // next_expiration column (was index 7, now index 6)
                    : null;



                // Create CacheEntry and add to result
                string? baseType = null;
                if (reader.FieldCount > 4 && !reader.IsDBNull(4))
                {
                    baseType = reader.GetString(4);  // value_type column (was index 5, now index 4)
                }
                var cacheEntry = _entryFactory.FromSerializedData<byte[]>(key, value, absoluteExpiration, slidingExpiration, baseType);
                result[key] = cacheEntry;

                // Note: Sliding expiration updates are now handled atomically by the CTE in the SQL query above
            }

            return result;
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Sets multiple cache entries in a single database operation using PostgreSQL's batch functionality.
    /// This is significantly more efficient than multiple individual SetAsync calls.
    /// </summary>
    /// <param name="entries">A dictionary of key-value pairs with their expiration options.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetMultipleAsync(Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> entries, CancellationToken token = default)
    {
        ThrowIfDisposed();

        if (entries == null || entries.Count == 0)
            return;

        // Consider batching for very large dictionaries to avoid memory pressure
        const int maxBatchSize = 1000;
        if (entries.Count > maxBatchSize)
        {
            await ProcessLargeBatchAsync(entries, token);
            return;
        }

        var keysWithLengthError = entries.Where(kvp => kvp.Key.Length > 900).Select(kvp => kvp.Key).ToList();
        if (keysWithLengthError.Count > 0)
        {
            throw new ArgumentException($"Key length cannot be greater than 900 characters. Invalid keys: {string.Join(", ", keysWithLengthError)}", nameof(entries));
        }

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Use NpgsqlBatch for efficient multiple inserts
            await using var batch = new NpgsqlBatch(connection);

            foreach (var entry in entries)
            {
                // Phase 2: Convert absolute expiration to relative interval using TimeConverterService
                TimeSpan? relativeInterval = null;

                // Handle DistributedCacheEntryOptions logic
                if (entry.Value.options.AbsoluteExpiration.HasValue)
                {
                    relativeInterval = _timeConverter.ConvertToRelativeInterval(entry.Value.options.AbsoluteExpiration.Value);
                }
                else if (entry.Value.options.AbsoluteExpirationRelativeToNow.HasValue)
                {
                    relativeInterval = entry.Value.options.AbsoluteExpirationRelativeToNow.Value;
                    // Handle negative intervals if needed
                    if (relativeInterval <= TimeSpan.Zero)
                    {
                        relativeInterval = TimeSpan.FromMilliseconds(1);
                    }
                }
                else if (_options.Cache.DefaultAbsoluteExpirationRelativeToNow.HasValue)
                {
                    relativeInterval = _options.Cache.DefaultAbsoluteExpirationRelativeToNow.Value;
                }

                var slidingExpiration = entry.Value.options.SlidingExpiration ?? _options.Cache.DefaultSlidingExpiration;

                var batchCommand = new NpgsqlBatchCommand(_dbRawCommands.SetMultipleSql);

                batchCommand.Parameters.Add(new() { Value = entry.Key });
                batchCommand.Parameters.Add(new() { Value = entry.Value.value });

                // Phase 2: Use relative interval instead of absolute expiration
                if (relativeInterval.HasValue)
                    batchCommand.Parameters.Add(new() { Value = relativeInterval.Value });
                else
                    batchCommand.Parameters.Add(new() { Value = (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Interval });

                if (slidingExpiration.HasValue)
                    batchCommand.Parameters.Add(new() { Value = slidingExpiration.Value });
                else
                    batchCommand.Parameters.Add(new() { Value = (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Interval });

                // batchCommand.Parameters.Add(new() { Value = lastAccessed });
                batchCommand.Parameters.Add(new() { Value = DBNull.Value });
                batchCommand.Parameters.Add(new() { Value = _timeProvider.GetUtcNow() });

                batch.BatchCommands.Add(batchCommand);
            }

            await batch.ExecuteNonQueryAsync(token);

            _logger.LogBatchSetSuccess(entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogBatchSetError(entries.Count, ex);
            throw;
        }
    }

    /// <summary>
    /// Sets multiple cache entries in a single database operation using PostgreSQL's batch functionality.
    /// Overload that accepts ReadOnlyMemory<byte> values to reduce allocations.
    /// </summary>
    public async Task SetMultipleAsync(Dictionary<string, (ReadOnlyMemory<byte> value, DistributedCacheEntryOptions options)> entries, CancellationToken token = default)
    {
        ThrowIfDisposed();

        if (entries == null || entries.Count == 0)
            return;

        const int maxBatchSize = 1000;
        if (entries.Count > maxBatchSize)
        {
            // Convert in streaming fashion to byte[] to reuse existing processing logic for large batches
            var converted = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>(entries.Count);
            foreach (var kvp in entries)
            {
                converted[kvp.Key] = (kvp.Value.value.ToArray(), kvp.Value.options);
            }
            await SetMultipleAsync(converted, token);
            return;
        }

        var keysWithLengthError = entries.Where(kvp => kvp.Key.Length > 900).Select(kvp => kvp.Key).ToList();
        if (keysWithLengthError.Count > 0)
        {
            throw new ArgumentException($"Key length cannot be greater than 900 characters. Invalid keys: {string.Join(", ", keysWithLengthError)}", nameof(entries));
        }

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            await using var batch = new NpgsqlBatch(connection);

            foreach (var entry in entries)
            {
                // Phase 2: Convert absolute expiration to relative interval using TimeConverterService
                TimeSpan? relativeInterval = null;

                // Handle DistributedCacheEntryOptions logic
                if (entry.Value.options.AbsoluteExpiration.HasValue)
                {
                    relativeInterval = _timeConverter.ConvertToRelativeInterval(entry.Value.options.AbsoluteExpiration.Value);
                }
                else if (entry.Value.options.AbsoluteExpirationRelativeToNow.HasValue)
                {
                    relativeInterval = entry.Value.options.AbsoluteExpirationRelativeToNow.Value;
                    // Handle negative intervals if needed
                    if (relativeInterval <= TimeSpan.Zero)
                    {
                        relativeInterval = TimeSpan.FromMilliseconds(1);
                    }
                }
                else if (_options.Cache.DefaultAbsoluteExpirationRelativeToNow.HasValue)
                {
                    relativeInterval = _options.Cache.DefaultAbsoluteExpirationRelativeToNow.Value;
                }

                var slidingExpiration = entry.Value.options.SlidingExpiration ?? _options.Cache.DefaultSlidingExpiration;

                var batchCommand = new NpgsqlBatchCommand(_dbRawCommands.SetMultipleSql);

                batchCommand.Parameters.Add(new() { Value = entry.Key });
                // ROM<byte> -> byte[] for Npgsql parameter
                batchCommand.Parameters.Add(new() { Value = entry.Value.value.ToArray() });

                // Phase 2: Use relative interval instead of absolute expiration
                if (relativeInterval.HasValue)
                    batchCommand.Parameters.Add(new() { Value = relativeInterval.Value });
                else
                    batchCommand.Parameters.Add(new() { Value = (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Interval });

                if (slidingExpiration.HasValue)
                    batchCommand.Parameters.Add(new() { Value = slidingExpiration.Value });
                else
                    batchCommand.Parameters.Add(new() { Value = (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Interval });

                batchCommand.Parameters.Add(new() { Value = DBNull.Value });

                batch.BatchCommands.Add(batchCommand);
            }

            await batch.ExecuteNonQueryAsync(token);

            _logger.LogBatchSetSuccess(entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogBatchSetError(entries.Count, ex);
            throw;
        }
    }

    /// <summary>
    /// Sets multiple cache entries in a single database operation using PostgreSQL's batch functionality.
    /// This is significantly more efficient than multiple individual SetEntryAsync calls.
    /// </summary>
    /// <param name="entries">A collection of cache entries to set.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetMultipleEntriesAsync(IEnumerable<CacheEntry<byte[]>> entries, CancellationToken token = default)
    {
        ThrowIfDisposed();

        var entriesArray = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
        if (entriesArray.Length == 0)
            return;

        // Consider batching for very large collections to avoid memory pressure
        const int maxBatchSize = 1000;
        if (entriesArray.Length > maxBatchSize)
        {
            await ProcessLargeEntriesBatchAsync(entriesArray, token);
            return;
        }

        var keysWithLengthError = entriesArray.Where(entry => entry.Key.Length > 900).Select(entry => entry.Key).ToList();
        if (keysWithLengthError.Count > 0)
        {
            throw new ArgumentException($"Key length cannot be greater than 900 characters. Invalid keys: {string.Join(", ", keysWithLengthError)}", nameof(entries));
        }

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Use NpgsqlBatch for efficient multiple inserts
            await using var batch = new NpgsqlBatch(connection);

            foreach (var entry in entriesArray)
            {
                var batchCommand = new NpgsqlBatchCommand(_dbRawCommands.SetMultipleSql);

                batchCommand.Parameters.Add(new() { Value = entry.Key });
                batchCommand.Parameters.Add(new() { Value = entry.Value });

                // Phase 2: Convert absolute expiration to relative interval using TimeConverterService
                if (entry.AbsoluteExpiration.HasValue)
                {
                    var relativeInterval = _timeConverter.ConvertToRelativeInterval(entry.AbsoluteExpiration.Value);
                    if (relativeInterval.HasValue)
                        batchCommand.Parameters.Add(new() { Value = relativeInterval.Value });
                    else
                        batchCommand.Parameters.Add(new() { Value = (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Interval });
                }
                else
                {
                    batchCommand.Parameters.Add(new() { Value = (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Interval });
                }

                if (entry.SlidingExpiration.HasValue)
                    batchCommand.Parameters.Add(new() { Value = entry.SlidingExpiration.Value });
                else
                    batchCommand.Parameters.Add(new() { Value = (object)DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Interval });

                batchCommand.Parameters.Add(new() { Value = (object)DBNull.Value });
                batchCommand.Parameters.Add(new() { Value = _timeProvider.GetUtcNow() });

                batch.BatchCommands.Add(batchCommand);
            }

            await batch.ExecuteNonQueryAsync(token);

            _logger.LogBatchSetSuccess(entriesArray.Length);
        }
        catch (Exception ex)
        {
            _logger.LogBatchSetError(entriesArray.Length, ex);
            throw;
        }
    }

    /// <summary>
    /// Processes large batches of entries by splitting them into smaller chunks to avoid memory pressure.
    /// </summary>
    /// <param name="entries">The entries to process.</param>
    /// <param name="token">Cancellation token.</param>
    private async Task ProcessLargeBatchAsync(Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> entries, CancellationToken token)
    {
        const int chunkSize = 500; // Process in chunks of 500 entries
        var entriesArray = entries.ToArray();

        for (int i = 0; i < entriesArray.Length; i += chunkSize)
        {
            var chunk = entriesArray.Skip(i).Take(chunkSize).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            await SetMultipleAsync(chunk, token);
        }

        _logger.LogLargeBatchProcessing(entries.Count);
    }

    /// <summary>
    /// Processes large batches of entries by splitting them into smaller chunks to avoid memory pressure.
    /// </summary>
    /// <param name="entries">The entries to process.</param>
    /// <param name="token">Cancellation token.</param>
    private async Task ProcessLargeEntriesBatchAsync(CacheEntry<byte[]>[] entries, CancellationToken token)
    {
        const int chunkSize = 500; // Process in chunks of 500 entries
        var entriesArray = entries.ToArray();

        for (int i = 0; i < entriesArray.Length; i += chunkSize)
        {
            var chunk = entriesArray.Skip(i).Take(chunkSize).ToArray();
            await SetMultipleEntriesAsync(chunk, token);
        }

        _logger.LogLargeBatchProcessing(entries.Length);
    }

    /// <summary>
    /// Removes multiple cache entries by their keys in a single database operation.
    /// This is significantly more efficient than multiple individual RemoveAsync calls.
    /// </summary>
    /// <param name="keys">The keys of the cache entries to remove.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The number of entries that were actually removed.</returns>
    public async Task<int> RemoveMultipleAsync(IEnumerable<string> keys, CancellationToken token = default)
    {
        ThrowIfDisposed();

        var keyArray = keys?.ToArray() ?? throw new ArgumentNullException(nameof(keys));
        if (keyArray.Length == 0)
            return 0;

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Use pre-compiled SQL with ANY() for efficient batch removal
            await using var command = new NpgsqlCommand(_dbRawCommands.RemoveMultipleSql, connection);
            command.Parameters.AddWithValue("@keys", keyArray);

            var deletedCount = await command.ExecuteNonQueryAsync(token);

            _logger.LogBatchRemoveSuccess(keyArray.Length);
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogBatchRemoveError(keyArray.Length, ex);
            throw;
        }
    }

    /// <summary>
    /// Refreshes multiple cache entries. Note: Sliding expiration is now handled atomically by the database.
    /// This is significantly more efficient than multiple individual RefreshAsync calls.
    /// </summary>
    /// <param name="keys">The keys of the cache entries to refresh.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The number of entries that were actually refreshed.</returns>
    public async Task<int> RefreshMultipleAsync(IEnumerable<string> keys, CancellationToken token = default)
    {
        ThrowIfDisposed();

        var keyArray = keys?.ToArray() ?? throw new ArgumentNullException(nameof(keys));
        if (keyArray.Length == 0)
            return 0;

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            // Use pre-compiled SQL with ANY() for efficient batch refresh
            await using var command = new NpgsqlCommand(_dbRawCommands.RefreshMultipleSql, connection);
            command.Parameters.AddWithValue("@keys", keyArray);
            command.Parameters.AddWithValue("@lastAccessed", _timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@now", _timeProvider.GetUtcNow());

            var refreshedCount = await command.ExecuteNonQueryAsync(token);

            _logger.LogBatchRefreshSuccess(keyArray.Length);
            return refreshedCount;
        }
        catch (Exception ex)
        {
            _logger.LogBatchRefreshError(keyArray.Length, ex);
            throw;
        }
    }












    // Phase 2 optimization: Enhanced DateTime handling with optimized expiration calculation
    // GetAbsoluteExpiration method removed in Phase 5 - replaced by TimeConverterService



    /// <summary>
    /// Executes an async operation with resilience policies if enabled.
    /// Supports both value-returning and void operations using a single method.
    /// </summary>
    private async Task<T?> ExecuteWithResilienceAsync<T>(Task<T> operation, string operationName = "", string? key = null)
    {
        if (_resiliencePolicy != null)
        {
            try
            {
                return await _resiliencePolicy.ExecuteAsync(() => operation).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                _logger.LogResilienceConnectionFailure(operationName, key, ex);
                return default;
            }
        }
        else
        {
            return await operation.ConfigureAwait(false);
        }
    }

    private async Task ExecuteWithResilienceAsync(Task operation, string operationName = "", string? key = null)
    {
        if (_resiliencePolicy != null)
        {
            try
            {
                await _resiliencePolicy.ExecuteAsync(() => operation).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                _logger.LogResilienceConnectionFailure(operationName, key, ex);
            }
        }
        else
        {
            await operation.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines if an exception is a connection failure that should be handled gracefully.
    /// </summary>
    private static bool IsConnectionFailure(Exception ex)
    {
        return ex is PostgresException pgEx &&
               (IsTransientException(pgEx) || IsPermanentException(pgEx)) ||
               ex is NpgsqlException npgsqlEx &&
               (npgsqlEx.Message.Contains("reading from stream") ||
                npgsqlEx.Message.Contains("connection") ||
                npgsqlEx.InnerException is System.IO.IOException ||
                npgsqlEx.InnerException is System.Net.Sockets.SocketException) ||
               ex is TimeoutException ||
               ex is Polly.Timeout.TimeoutRejectedException ||
               ex is Polly.CircuitBreaker.BrokenCircuitException ||
               ex is System.Net.Sockets.SocketException ||
               ex is System.IO.IOException && ex.Message.Contains("transport connection") ||
               ex is InvalidOperationException && ex.Message.Contains("connection") ||
               ex is ObjectDisposedException && ex.Message.Contains("connection");
    }

    /// <summary>
    /// Determines if a PostgreSQL exception is transient and should be retried.
    /// </summary>
    private static bool IsTransientException(PostgresException ex)
    {
        return ex.SqlState switch
        {
            // Connection failures (likely temporary)
            "08001" => true, // Connection failed - server unavailable or network issue
            "08006" => true, // Connection failure - connection lost during operation
            "08000" => true, // Connection exception - general connection problem
            "08003" => true, // Connection does not exist - connection was closed
            "08004" => true, // SQL server rejected establishment of SQL connection - server overload
            "08007" => true, // Connection failure during transaction - network interruption

            // Resource exhaustion (likely temporary)
            "53300" => true, // Too many connections - connection pool exhausted
            "57014" => true, // Query canceled - server canceled due to resource constraints
            "57000" => true, // Statement timeout - query took too long, server busy

            // Server issues (likely temporary)
            "57P01" => true, // Admin shutdown - server shutting down for maintenance
            "57P02" => true, // Crash shutdown - server crashed, will restart
            "57P03" => true, // Cannot connect now - server temporarily unavailable
            "57P04" => true, // Database shutdown - database shutting down
            "57P05" => true, // Database restart - database restarting

            // Network issues (likely temporary)
            "XX000" => true, // Internal error - some internal errors are transient

            _ => false
        };
    }

    /// <summary>
    /// Determines if a PostgreSQL exception is permanent and should not be retried.
    /// </summary>
    private static bool IsPermanentException(PostgresException ex)
    {
        return ex.SqlState switch
        {
            // Authentication failures
            "28P01" => true, // Password authentication failed
            "28P02" => true, // Password authentication failed
            "28P03" => true, // Password authentication failed
            "28P04" => true, // Password authentication failed

            // Authorization failures
            "42501" => true, // Insufficient privilege
            "42502" => true, // Insufficient privilege
            "42503" => true, // Insufficient privilege
            "42504" => true, // Insufficient privilege
            "42505" => true, // Insufficient privilege
            "42506" => true, // Insufficient privilege

            // Configuration errors
            "3D000" => true, // Invalid catalog name
            "3F000" => true, // Invalid schema name
            "42P01" => true, // Undefined table
            "42P02" => true, // Undefined parameter
            "42P03" => true, // Undefined column
            "42P04" => true, // Undefined object

            // Data type errors
            "22P02" => true, // Invalid text representation
            "22P03" => true, // Invalid binary representation
            "22P04" => true, // Bad copy file format
            "22P05" => true, // Untranslatable character

            _ => false
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GlacialCachePostgreSQL));
        }
    }

    // ===== Typed Operations with MemoryPack =====

    private async Task<CacheEntry<T>?> GetTypedEntryAsync<T>(string key, CancellationToken token)
    {
        var entry = await GetEntryAsyncCore(key, token);
        if (entry == null) return null;

        try
        {
            // If a BaseType is recorded and it doesn't match the requested T, short-circuit to null
            if (!string.IsNullOrWhiteSpace(entry.BaseType) && !string.Equals(entry.BaseType, typeof(T).FullName, StringComparison.Ordinal))
            {
                return null;
            }

            // Try to deserialize as the requested type via configured serializer
            var value = _entryFactory.Deserialize<T>(entry.SerializedData.ToArray());
            if (value == null)
            {
                _logger.LogDeserializationError(key, typeof(T).Name, null);
                return null;
            }

            return _entryFactory.FromSerializedData<T>(
                key,
                entry.SerializedData.ToArray(),
                entry.AbsoluteExpiration,
                entry.SlidingExpiration,
                entry.BaseType);
        }
        catch (Exception ex)
        {
            // Type-safety: if BaseType is stored and does not match requested type, return null
            if (!string.IsNullOrWhiteSpace(entry.BaseType) && !string.Equals(entry.BaseType, typeof(T).FullName, StringComparison.Ordinal))
            {
                return null;
            }

            // Backward compatibility fallback: only if no BaseType stored and T is string
            if (typeof(T) == typeof(string) && string.IsNullOrWhiteSpace(entry.BaseType))
            {
                try
                {
                    // Strict UTF-8 decoding (fail on invalid bytes)
                    var enc = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                    var str = enc.GetString(entry.SerializedData.Span);
                    var compat = _entryFactory.Create<string>(
                        key,
                        str,
                        entry.AbsoluteExpiration,
                        entry.SlidingExpiration);
                    return (CacheEntry<T>)(object)compat;
                }
                catch
                {
                    // ignore and fall through
                }
            }

            _logger.LogDeserializationError(key, typeof(T).Name, ex);
            return null;
        }
    }

    private async Task<Dictionary<string, CacheEntry<T>?>> GetMultipleTypedEntriesAsync<T>(IEnumerable<string> keys, CancellationToken token)
    {
        var entries = await GetMultipleEntriesAsync(keys, token);
        var result = new Dictionary<string, CacheEntry<T>?>();

        foreach (var kvp in entries)
        {
            if (kvp.Value == null)
            {
                result[kvp.Key] = null;
                continue;
            }

            try
            {
                var value = _entryFactory.Deserialize<T>(kvp.Value.SerializedData.ToArray());
                if (value == null)
                {
                    _logger.LogDeserializationError(kvp.Key, typeof(T).Name, null);
                    result[kvp.Key] = null;
                    continue;
                }

                result[kvp.Key] = _entryFactory.FromSerializedData<T>(
                    kvp.Key,
                    kvp.Value.SerializedData.ToArray(),
                    kvp.Value.AbsoluteExpiration,
                    kvp.Value.SlidingExpiration,
                    kvp.Value.BaseType);
            }
            catch (Exception ex)
            {
                _logger.LogDeserializationError(kvp.Key, typeof(T).Name, ex);
                result[kvp.Key] = null;
            }
        }

        return result;
    }

    /// <summary>
    /// Retrieves a typed cache entry by its key using MemoryPack deserialization.
    /// </summary>
    public async Task<CacheEntry<T>?> GetEntryAsync<T>(string key, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));
        }
        if (key.Length > 900)
        {
            throw new ArgumentException("Key length cannot be greater than 900 characters.", nameof(key));
        }

        return await ExecuteWithResilienceAsync(
            GetTypedEntryAsync<T>(key, token),
            "GetEntryAsync<T>",
            key);
    }

    /// <summary>
    /// Sets a typed cache entry using MemoryPack serialization.
    /// </summary>
    public async Task SetEntryAsync<T>(CacheEntry<T> entry, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);

        await ExecuteWithResilienceAsync(SetEntryAsyncCore(entry, token),
            "SetEntryAsync<T>",
            entry.Key);
    }

    /// <summary>
    /// Sets a typed value with optional expiration policies using MemoryPack serialization.
    /// </summary>
    public async Task SetEntryAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        // Convert DistributedCacheEntryOptions to absolute expiration and sliding expiration
        DateTimeOffset? absoluteExpiration = null;
        TimeSpan? slidingExpiration = null;

        if (options != null)
        {
            if (options.AbsoluteExpiration.HasValue)
            {
                absoluteExpiration = options.AbsoluteExpiration.Value;
            }
            else if (options.AbsoluteExpirationRelativeToNow.HasValue)
            {
                absoluteExpiration = _timeProvider.GetUtcNow().Add(options.AbsoluteExpirationRelativeToNow.Value);
            }

            slidingExpiration = options.SlidingExpiration;
        }

        await ExecuteWithResilienceAsync(
            SetEntryAsyncCore(_entryFactory.Create<T>(key, value, absoluteExpiration, slidingExpiration), token),
            "SetEntryAsync<T>",
            key);
    }

    /// <summary>
    /// Retrieves multiple typed cache entries by their keys in a single database operation.
    /// </summary>
    public async Task<Dictionary<string, CacheEntry<T>?>> GetMultipleEntriesAsync<T>(IEnumerable<string> keys, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(keys);

        var result = await ExecuteWithResilienceAsync(
              GetMultipleTypedEntriesAsync<T>(keys, token),
              "GetMultipleEntriesAsync<T>");

        return result ?? new Dictionary<string, CacheEntry<T>?>();
    }

    /// <summary>
    /// Sets multiple typed cache entries in a single database operation using PostgreSQL's batch functionality.
    /// </summary>
    public async Task SetMultipleEntriesAsync<T>(IEnumerable<CacheEntry<T>> entries, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entries);

        await ExecuteWithResilienceAsync(
            SetMultipleEntriesAsync(entries.Select(e =>
            {
                // If SerializedData is empty, we need to serialize the Value first
                var entryToStore = e.SerializedData.IsEmpty
                    ? _entryFactory.Create(e.Key, e.Value, e.AbsoluteExpiration, e.SlidingExpiration)
                    : e;

                return _entryFactory.FromSerializedData<byte[]>(
                    entryToStore.Key, entryToStore.SerializedData.ToArray(),
                    entryToStore.AbsoluteExpiration, entryToStore.SlidingExpiration, entryToStore.BaseType);
            }).ToArray(), token),
            "SetMultipleEntriesAsync<T>");
    }

    /// <summary>
    /// Sets multiple typed values with optional expiration policies in a single database operation.
    /// </summary>
    public async Task SetMultipleEntriesAsync<T>(Dictionary<string, (T value, DistributedCacheEntryOptions? options)> entries, CancellationToken token = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entries);

        await ExecuteWithResilienceAsync(
            SetMultipleEntriesAsync(entries.Select(e => _entryFactory.Create<T>(e.Key, e.Value.value, e.Value.options?.AbsoluteExpiration, e.Value.options?.SlidingExpiration)).ToArray(), token),
            "SetMultipleEntriesAsync<T>");
    }





    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose the options change token to prevent memory leaks
        _optionsChangeToken?.Dispose();

        _dataSource.Dispose();
        GC.SuppressFinalize(this);
    }
}