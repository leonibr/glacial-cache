using System.Data;
using GlacialCache.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;

namespace GlacialCache.SqlServer;

public sealed partial class GlacialCacheSqlServer
{
    public CacheEntry<byte[]>? GetEntry(string key) => GetEntryAsync(key).GetAwaiter().GetResult();

    public async Task<CacheEntry<byte[]>?> GetEntryAsync(string key, CancellationToken token = default)
    {
        ValidateKey(key);
        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.CommandText = $"""
            DECLARE @now datetimeoffset(7) = @utcNow;
            UPDATE {_table}
            SET LastAccessed = CASE WHEN SlidingExpirationTicks IS NULL THEN LastAccessed ELSE @now END
            OUTPUT inserted.CacheValue, inserted.AbsoluteExpiration, inserted.SlidingExpirationTicks, inserted.BaseType
            WHERE KeyHash = @keyHash AND CacheKey = @key COLLATE Latin1_General_100_BIN2
              AND (AbsoluteExpiration IS NULL OR AbsoluteExpiration > @now)
              AND (SlidingExpirationTicks IS NULL OR DATEDIFF_BIG(MILLISECOND, LastAccessed, @now) < SlidingExpirationTicks / 10000);
            """;
        AddKey(command, key);
        command.Parameters.Add(new SqlParameter("@utcNow", SqlDbType.DateTimeOffset) { Value = _timeProvider.GetUtcNow() });
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var bytes = (byte[])reader.GetValue(0);
        var absolute = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset?>(1);
        TimeSpan? sliding = reader.IsDBNull(2) ? null : TimeSpan.FromTicks(reader.GetInt64(2));
        var baseType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
        return _entryFactory.FromSerializedData<byte[]>(key, bytes, absolute, sliding, baseType);
    }

    public void SetEntry(CacheEntry<byte[]> entry) => SetEntryAsync(entry).GetAwaiter().GetResult();

    public async Task SetEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateKey(entry.Key);
        var bytes = entry.SerializedData.IsEmpty ? entry.Value : entry.SerializedData.ToArray();
        var options = ToOptions(entry.AbsoluteExpiration, entry.SlidingExpiration);
        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        await UpsertAsync(connection, transaction, entry.Key, bytes, options, token, entry.BaseType).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
    }

    public void RefreshEntry(CacheEntry<byte[]> entry) => RefreshEntryAsync(entry).GetAwaiter().GetResult();
    public Task RefreshEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return RefreshAsync(entry.Key, token);
    }

    public void RemoveEntry(CacheEntry<byte[]> entry) => RemoveEntryAsync(entry).GetAwaiter().GetResult();
    public Task RemoveEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return RemoveAsync(entry.Key, token);
    }

    public async Task<Dictionary<string, CacheEntry<byte[]>?>> GetMultipleEntriesAsync(IEnumerable<string> keys, CancellationToken token = default)
    {
        var materialized = MaterializeKeys(keys);
        var result = new Dictionary<string, CacheEntry<byte[]>?>(StringComparer.Ordinal);
        foreach (var key in materialized)
        {
            var entry = await GetEntryAsync(key, token).ConfigureAwait(false);
            if (entry is not null) result[key] = entry;
        }
        return result;
    }

    public async Task SetMultipleEntriesAsync(IEnumerable<CacheEntry<byte[]>> entries, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries) await SetEntryAsync(entry, token).ConfigureAwait(false);
    }

    public async Task<CacheEntry<T>?> GetEntryAsync<T>(string key, CancellationToken token = default)
    {
        var entry = await GetEntryAsync(key, token).ConfigureAwait(false);
        if (entry is null) return null;
        return _entryFactory.TryFromSerializedData<T>(entry, out var result, out _) ? result : null;
    }

    public Task SetEntryAsync<T>(CacheEntry<T> entry, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var prepared = _entryFactory.PrepareForStorage(entry);
        return SetEntryAsync(_entryFactory.FromSerializedData<byte[]>(prepared.Key, prepared.SerializedData.ToArray(),
            prepared.AbsoluteExpiration, prepared.SlidingExpiration, prepared.BaseType), token);
    }

    public Task SetEntryAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        var (absolute, sliding) = Resolve(options);
        return SetEntryAsync(_entryFactory.Create(key, value, absolute, sliding), token);
    }

    public async Task<Dictionary<string, CacheEntry<T>?>> GetMultipleEntriesAsync<T>(IEnumerable<string> keys, CancellationToken token = default)
    {
        var materialized = MaterializeKeys(keys);
        var result = new Dictionary<string, CacheEntry<T>?>(StringComparer.Ordinal);
        foreach (var key in materialized)
        {
            var entry = await GetEntryAsync<T>(key, token).ConfigureAwait(false);
            if (entry is not null) result[key] = entry;
        }
        return result;
    }

    public async Task SetMultipleEntriesAsync<T>(IEnumerable<CacheEntry<T>> entries, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries) await SetEntryAsync(entry, token).ConfigureAwait(false);
    }

    public async Task SetMultipleEntriesAsync<T>(Dictionary<string, (T value, DistributedCacheEntryOptions? options)> entries, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var pair in entries) await SetEntryAsync(pair.Key, pair.Value.value, pair.Value.options, token).ConfigureAwait(false);
    }

    private static DistributedCacheEntryOptions ToOptions(DateTimeOffset? absolute, TimeSpan? sliding)
    {
        var options = new DistributedCacheEntryOptions();
        if (absolute is not null) options.AbsoluteExpiration = absolute;
        if (sliding is not null) options.SlidingExpiration = sliding;
        return options;
    }

    private (DateTimeOffset? Absolute, TimeSpan? Sliding) Resolve(DistributedCacheEntryOptions? options)
    {
        if (options is null) return (null, null);
        var absolute = options.AbsoluteExpiration;
        if (options.AbsoluteExpirationRelativeToNow is { } relative) absolute = _timeProvider.GetUtcNow().Add(relative);
        return (absolute, options.SlidingExpiration);
    }
}
