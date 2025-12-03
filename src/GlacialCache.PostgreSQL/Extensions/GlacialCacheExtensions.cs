using Microsoft.Extensions.Caching.Distributed;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Services;

namespace GlacialCache.PostgreSQL.Extensions;

/// <summary>
/// Extension methods for GlacialCache that provide additional functionality for working with CacheEntry objects.
/// </summary>
public static class GlacialCacheExtensions
{
    /// <summary>
    /// Retrieves a cache entry by its key asynchronously.
    /// </summary>
    /// <param name="cache">The cache instance.</param>
    /// <param name="key">The key of the cache entry to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The cache entry if found and not expired; otherwise, null.</returns>
    public static async Task<CacheEntry<byte[]>?> GetAsync(this IGlacialCache cache, string key, CancellationToken token = default)
    {
        return await cache.GetEntryAsync(key, token);
    }

    /// <summary>
    /// Sets a cache entry asynchronously.
    /// </summary>
    /// <param name="cache">The cache instance.</param>
    /// <param name="entry">The cache entry to set.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task SetAsync(this IGlacialCache cache, CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        await cache.SetEntryAsync(entry, token);
    }

    /// <summary>
    /// Retrieves a cache entry by its key asynchronously.
    /// </summary>
    /// <param name="cache">The cache instance.</param>
    /// <param name="key">The key of the cache entry to retrieve.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The cache entry if found and not expired; otherwise, null.</returns>
    public static async Task<CacheEntry<byte[]>?> GetAsync(this IDistributedCache cache, string key, CancellationToken token = default)
    {
        if (cache is IGlacialCache GlacialCache)
        {
            return await GlacialCache.GetEntryAsync(key, token);
        }

        // Fallback for IDistributedCache implementations that don't support CacheEntry
        var bytes = await cache.GetAsync(key, token);
        if (bytes == null) return null;

        // Create a basic CacheEntry with the retrieved data
        // For fallback, we'll create a simple entry without factory
        return new CacheEntry<byte[]>
        {
            Key = key,
            Value = bytes,
            SerializedData = bytes,
            BaseType = "System.Byte[]",
            SizeInBytes = bytes.Length
        };
    }

    /// <summary>
    /// Sets a cache entry asynchronously.
    /// </summary>
    /// <param name="cache">The cache instance.</param>
    /// <param name="entry">The cache entry to set.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task SetAsync(this IDistributedCache cache, CacheEntry<byte[]> entry, CancellationToken token = default)
    {
        if (cache is IGlacialCache GlacialCache)
        {
            await GlacialCache.SetEntryAsync(entry, token);
            return;
        }

        // Fallback for IDistributedCache implementations that don't support CacheEntry
        var options = new DistributedCacheEntryOptions();
        if (entry.AbsoluteExpiration.HasValue)
        {
            options.AbsoluteExpiration = entry.AbsoluteExpiration;
        }
        if (entry.SlidingExpiration.HasValue)
        {
            options.SlidingExpiration = entry.SlidingExpiration;
        }

        // Convert ReadOnlyMemory<byte> to byte[] for IDistributedCache
        var valueBytes = entry.SerializedData.ToArray();
        await cache.SetAsync(entry.Key, valueBytes, options, token);
    }
}
