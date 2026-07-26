#if NET9_0_OR_GREATER
using System.Buffers;
using System.Data;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Logging;
using GlacialCache.PostgreSQL.Models.CommandParameters;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
using NpgsqlTypes;

namespace GlacialCache.PostgreSQL;

/// <summary>
/// Low-allocation distributed cache operations available on .NET 9 and later.
/// </summary>
public partial class GlacialCachePostgreSQL
{
    /// <inheritdoc />
    [Obsolete("Use TryGetAsync for better performance. Synchronous calls may cause thread pool starvation and deadlocks.")]
    public bool TryGet(string key, IBufferWriter<byte> destination)
        => TryGetAsync(key, destination).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask<bool> TryGetAsync(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken token = default)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(destination);

        return await ExecuteWithResilienceAsync(
            TryGetAsyncCore(key, destination, token),
            operationName: "TryGetAsync",
            key: key);
    }

    private async Task<bool> TryGetAsyncCore(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken token)
    {
        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);
            await using var command = new NpgsqlCommand(_dbRawCommands.GetSqlCore, connection);
            command.AddParameters(new GetEntryParameters
            {
                Key = key,
                Now = _timeProvider.GetUtcNow()
            });

            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleRow,
                token).ConfigureAwait(false);

            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return false;

            await using var stream = reader.GetStream(0);
            while (true)
            {
                var buffer = destination.GetMemory(81920);
                var bytesRead = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (bytesRead == 0)
                    return true;

                destination.Advance(bytesRead);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCacheGetError(key, ex);
            throw;
        }
    }

    /// <inheritdoc />
    [Obsolete("Use SetAsync for better performance. Synchronous calls may cause thread pool starvation and deadlocks.")]
    public void Set(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options)
        => SetAsync(key, value, options).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async ValueTask SetAsync(
        string key,
        ReadOnlySequence<byte> value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(options);

        ReadOnlyMemory<byte> contiguousValue = value.IsSingleSegment
            ? value.First
            : value.ToArray();

        await ExecuteWithResilienceAsync(
            SetBufferAsyncCore(key, contiguousValue, options, token),
            operationName: "SetAsync",
            key: key);
    }

    private async Task SetBufferAsyncCore(
        string key,
        ReadOnlyMemory<byte> value,
        DistributedCacheEntryOptions options,
        CancellationToken token)
    {
        TimeSpan? relativeInterval = null;
        var now = _timeProvider.GetUtcNow();

        if (options.AbsoluteExpiration.HasValue)
        {
            relativeInterval = _timeConverter.ConvertToRelativeInterval(options.AbsoluteExpiration.Value, now);
        }
        else if (options.AbsoluteExpirationRelativeToNow.HasValue)
        {
            relativeInterval = options.AbsoluteExpirationRelativeToNow.Value;
            if (relativeInterval <= TimeSpan.Zero)
                relativeInterval = TimeSpan.FromMilliseconds(1);
        }
        else if (_options.Cache.DefaultAbsoluteExpirationRelativeToNow.HasValue)
        {
            relativeInterval = _options.Cache.DefaultAbsoluteExpirationRelativeToNow.Value;
        }

        var slidingInterval = options.SlidingExpiration ?? _options.Cache.DefaultSlidingExpiration;

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);
            await using var command = new NpgsqlCommand(_dbRawCommands.SetSql, connection);

            command.Parameters.AddWithValue("@Key", key);
            command.Parameters.Add(new NpgsqlParameter<ReadOnlyMemory<byte>>("@Value", NpgsqlDbType.Bytea)
            {
                TypedValue = value
            });
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue(
                "@RelativeInterval",
                NpgsqlDbType.Interval,
                relativeInterval ?? (object)DBNull.Value);
            command.Parameters.AddWithValue(
                "@SlidingInterval",
                NpgsqlDbType.Interval,
                slidingInterval ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ValueType", DBNull.Value);

            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCacheSetError(key, ex);
            throw;
        }
    }
}
#endif
