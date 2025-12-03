using Microsoft.Extensions.Caching.Distributed;
using GlacialCache.PostgreSQL.Models;

namespace GlacialCache.PostgreSQL.Abstractions;

/// <summary>
/// Enhanced interface for GlacialCache PostgreSQL that extends IDistributedCache with batch operations.
/// This provides a clean API without requiring casting from IDistributedCache.
/// </summary>
public interface IGlacialCache : IDistributedCache
{

    /// <summary>
    /// Retrieves multiple cache entries by their keys in a single database operation.
    /// This is significantly more efficient than multiple individual GetAsync calls.
    /// </summary>
    /// <param name="keys">The keys of the cache entries to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A dictionary containing the keys and their corresponding values. Missing keys will not be included in the result.</returns>
    Task<Dictionary<string, byte[]?>> GetMultipleAsync(
        IEnumerable<string> keys,
        CancellationToken token = default);

    /// <summary>
    /// Sets multiple cache entries in a single database operation using PostgreSQL's batch functionality.
    /// Overload that accepts ReadOnlyMemory<byte> values.
    /// </summary>
    /// <param name="entries">A dictionary of key-value pairs with their expiration options.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetMultipleAsync(
        Dictionary<string, (ReadOnlyMemory<byte> value, DistributedCacheEntryOptions options)> entries,
        CancellationToken token = default);

    /// <summary>
    /// Sets multiple cache entries in a single database operation using PostgreSQL's batch functionality.
    /// This is significantly more efficient than multiple individual SetAsync calls.
    /// </summary>
    /// <param name="entries">A dictionary of key-value pairs with their expiration options.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetMultipleAsync(
        Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> entries,
        CancellationToken token = default);

    /// <summary>
    /// Removes multiple cache entries by their keys in a single database operation.
    /// This is significantly more efficient than multiple individual RemoveAsync calls.
    /// </summary>
    /// <param name="keys">The keys of the cache entries to remove.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The number of entries that were actually removed.</returns>
    Task<int> RemoveMultipleAsync(
        IEnumerable<string> keys,
        CancellationToken token = default);

    /// <summary>
    /// Refreshes multiple cache entries. Note: Sliding expiration is now handled atomically by the database.
    /// This is significantly more efficient than multiple individual RefreshAsync calls.
    /// </summary>
    /// <param name="keys">The keys of the cache entries to refresh.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The number of entries that were actually refreshed.</returns>
    Task<int> RefreshMultipleAsync(
        IEnumerable<string> keys,
        CancellationToken token = default);



    // ===== CacheEntry Operations =====

    /// <summary>
    /// Retrieves a cache entry by its key.
    /// </summary>
    /// <param name="key">The key of the cache entry to retrieve.</param>
    /// <returns>The cache entry if found and not expired; otherwise, null.</returns>
    CacheEntry<byte[]>? GetEntry(string key);

    /// <summary>
    /// Retrieves a cache entry by its key asynchronously.
    /// </summary>
    /// <param name="key">The key of the cache entry to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The cache entry if found and not expired; otherwise, null.</returns>
    Task<CacheEntry<byte[]>?> GetEntryAsync(string key, CancellationToken token = default);

    /// <summary>
    /// Sets a cache entry.
    /// </summary>
    /// <param name="entry">The cache entry to set.</param>
    void SetEntry(CacheEntry<byte[]> entry);

    /// <summary>
    /// Sets a cache entry asynchronously.
    /// </summary>
    /// <param name="entry">The cache entry to set.</param>
    /// <param name="token">Cancellation token.</param>
    Task SetEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default);

    /// <summary>
    /// Refreshes a cache entry by updating its last accessed time.
    /// </summary>
    /// <param name="entry">The cache entry to refresh.</param>
    void RefreshEntry(CacheEntry<byte[]> entry);

    /// <summary>
    /// Refreshes a cache entry by updating its last accessed time asynchronously.
    /// </summary>
    /// <param name="entry">The cache entry to refresh.</param>
    /// <param name="token">Cancellation token.</param>
    Task RefreshEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default);

    /// <summary>
    /// Removes a cache entry.
    /// </summary>
    /// <param name="entry">The cache entry to remove.</param>
    void RemoveEntry(CacheEntry<byte[]> entry);

    /// <summary>
    /// Removes a cache entry asynchronously.
    /// </summary>
    /// <param name="entry">The cache entry to remove.</param>
    /// <param name="token">Cancellation token.</param>
    Task RemoveEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default);

    /// <summary>
    /// Retrieves multiple cache entries by their keys in a single database operation.
    /// This is significantly more efficient than multiple individual GetEntryAsync calls.
    /// </summary>
    /// <param name="keys">The keys of the cache entries to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A dictionary containing the keys and their corresponding cache entries. Missing keys will not be included in the result.</returns>
    Task<Dictionary<string, CacheEntry<byte[]>?>> GetMultipleEntriesAsync(IEnumerable<string> keys, CancellationToken token = default);

    /// <summary>
    /// Sets multiple cache entries in a single database operation using PostgreSQL's batch functionality.
    /// This is significantly more efficient than multiple individual SetEntryAsync calls.
    /// </summary>
    /// <param name="entries">A collection of cache entries to set.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetMultipleEntriesAsync(IEnumerable<CacheEntry<byte[]>> entries, CancellationToken token = default);


    // ===== Typed Operations with MemoryPack =====

    /// <summary>
    /// Retrieves a typed cache entry by its key using MemoryPack deserialization.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The key of the cache entry to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The typed cache entry if found and not expired; otherwise, null.</returns>
    Task<CacheEntry<T>?> GetEntryAsync<T>(string key, CancellationToken token = default);

    /// <summary>
    /// Sets a typed cache entry using MemoryPack serialization.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="entry">The typed cache entry to set.</param>
    /// <param name="token">Cancellation token.</param>
    Task SetEntryAsync<T>(CacheEntry<T> entry, CancellationToken token = default);

    /// <summary>
    /// Sets a typed value with optional expiration policies using MemoryPack serialization.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The key for the cache entry.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="options">Optional expiration options.</param>
    /// <param name="token">Cancellation token.</param>
    Task SetEntryAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default);

    /// <summary>
    /// Retrieves multiple typed cache entries by their keys in a single database operation.
    /// </summary>
    /// <typeparam name="T">The type of the cached values.</typeparam>
    /// <param name="keys">The keys of the cache entries to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A dictionary containing the keys and their corresponding typed cache entries. Missing keys will not be included in the result.</returns>
    Task<Dictionary<string, CacheEntry<T>?>> GetMultipleEntriesAsync<T>(IEnumerable<string> keys, CancellationToken token = default);

    /// <summary>
    /// Sets multiple typed cache entries in a single database operation using PostgreSQL's batch functionality.
    /// </summary>
    /// <typeparam name="T">The type of the values to cache.</typeparam>
    /// <param name="entries">A collection of typed cache entries to set.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetMultipleEntriesAsync<T>(IEnumerable<CacheEntry<T>> entries, CancellationToken token = default);

    /// <summary>
    /// Sets multiple typed values with optional expiration policies in a single database operation.
    /// </summary>
    /// <typeparam name="T">The type of the values to cache.</typeparam>
    /// <param name="entries">A dictionary of key-value pairs with their expiration options.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetMultipleEntriesAsync<T>(Dictionary<string, (T value, DistributedCacheEntryOptions? options)> entries, CancellationToken token = default);

}