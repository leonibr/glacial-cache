using Microsoft.Extensions.Caching.Distributed;

namespace GlacialCache.Abstractions;

/// <summary>
/// Provider-neutral distributed cache contract with efficient batch operations.
/// Database providers own the persistence semantics behind this interface.
/// </summary>
public interface IGlacialCache : IDistributedCache
{
    Task<Dictionary<string, byte[]?>> GetMultipleAsync(
        IEnumerable<string> keys,
        CancellationToken token = default);

    Task<Dictionary<string, byte[]?>> SetAndGetMultipleAsync(
        Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> entries,
        CancellationToken token = default);

    Task SetMultipleAsync(
        Dictionary<string, (ReadOnlyMemory<byte> value, DistributedCacheEntryOptions options)> entries,
        CancellationToken token = default);

    Task SetMultipleDirectAsync(
        Dictionary<string, (ReadOnlyMemory<byte> value, DistributedCacheEntryOptions options)> entries,
        CancellationToken token = default);

    Task SetMultipleAsync(
        Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> entries,
        CancellationToken token = default);

    Task<int> RemoveMultipleAsync(IEnumerable<string> keys, CancellationToken token = default);

    Task<int> RefreshMultipleAsync(IEnumerable<string> keys, CancellationToken token = default);

    CacheEntry<byte[]>? GetEntry(string key);
    Task<CacheEntry<byte[]>?> GetEntryAsync(string key, CancellationToken token = default);
    void SetEntry(CacheEntry<byte[]> entry);
    Task SetEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default);
    void RefreshEntry(CacheEntry<byte[]> entry);
    Task RefreshEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default);
    void RemoveEntry(CacheEntry<byte[]> entry);
    Task RemoveEntryAsync(CacheEntry<byte[]> entry, CancellationToken token = default);
    Task<Dictionary<string, CacheEntry<byte[]>?>> GetMultipleEntriesAsync(IEnumerable<string> keys, CancellationToken token = default);
    Task SetMultipleEntriesAsync(IEnumerable<CacheEntry<byte[]>> entries, CancellationToken token = default);
    Task<CacheEntry<T>?> GetEntryAsync<T>(string key, CancellationToken token = default);
    Task SetEntryAsync<T>(CacheEntry<T> entry, CancellationToken token = default);
    Task SetEntryAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default);
    Task<Dictionary<string, CacheEntry<T>?>> GetMultipleEntriesAsync<T>(IEnumerable<string> keys, CancellationToken token = default);
    Task SetMultipleEntriesAsync<T>(IEnumerable<CacheEntry<T>> entries, CancellationToken token = default);
    Task SetMultipleEntriesAsync<T>(Dictionary<string, (T value, DistributedCacheEntryOptions? options)> entries, CancellationToken token = default);
}
