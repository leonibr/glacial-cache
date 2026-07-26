using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GlacialCache.Abstractions;

namespace GlacialCache.SqlServer;

/// <summary>SQL Server 2019+ implementation of the provider-neutral GlacialCache contract.</summary>
public sealed partial class GlacialCacheSqlServer : global::GlacialCache.Abstractions.IGlacialCache
{
    private const int MaxKeyLength = 900;
    private const int BatchSize = 500; // two parameters per key, comfortably below SQL Server's 2100 limit
    private readonly GlacialCacheSqlServerOptions _options;
    private readonly ILogger<GlacialCacheSqlServer> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string _table;
    private readonly CacheEntryFactory _entryFactory;

    public GlacialCacheSqlServer(
        IOptions<GlacialCacheSqlServerOptions> options,
        ILogger<GlacialCacheSqlServer> logger,
        TimeProvider timeProvider,
        CacheEntryFactory entryFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _entryFactory = entryFactory ?? throw new ArgumentNullException(nameof(entryFactory));

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(options));

        var schema = SqlServerIdentifier.Quote(_options.SchemaName, nameof(_options.SchemaName));
        var table = SqlServerIdentifier.Quote(_options.TableName, nameof(_options.TableName));
        _table = $"{schema}.{table}";

        if (_options.CreateInfrastructure)
            EnsureSchema();
    }

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        ValidateKey(key);
        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.CommandText = $"""
            DECLARE @now datetimeoffset(7) = @utcNow;
            UPDATE {_table}
            SET LastAccessed = CASE WHEN SlidingExpirationTicks IS NULL THEN LastAccessed ELSE @now END
            OUTPUT inserted.CacheValue
            WHERE KeyHash = @keyHash
              AND CacheKey = @key COLLATE Latin1_General_100_BIN2
              AND (AbsoluteExpiration IS NULL OR AbsoluteExpiration > @now)
              AND (SlidingExpirationTicks IS NULL OR DATEDIFF_BIG(MILLISECOND, LastAccessed, @now) < SlidingExpirationTicks / 10000);
            """;
        AddKey(command, key);
        command.Parameters.Add(new SqlParameter("@utcNow", SqlDbType.DateTimeOffset) { Value = _timeProvider.GetUtcNow() });
        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is DBNull or null ? null : (byte[])value;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        SetAsync(key, value, options).GetAwaiter().GetResult();

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        await UpsertAsync(connection, transaction, key, value, options, token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
    }

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        ValidateKey(key);
        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using var command = CreateRefreshCommand(connection, null, new[] { key });
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        ValidateKey(key);
        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.CommandText = $"DELETE FROM {_table} WHERE KeyHash = @keyHash AND CacheKey = @key COLLATE Latin1_General_100_BIN2;";
        AddKey(command, key);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, byte[]?>> GetMultipleAsync(IEnumerable<string> keys, CancellationToken token = default)
    {
        var materialized = MaterializeKeys(keys);
        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        if (materialized.Count == 0) return result;

        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        foreach (var chunk in materialized.Chunk(BatchSize))
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            var values = AddRequestedKeys(command, chunk);
            command.Parameters.Add(new SqlParameter("@utcNow", SqlDbType.DateTimeOffset) { Value = _timeProvider.GetUtcNow() });
            command.CommandText = $"""
                DECLARE @now datetimeoffset(7) = @utcNow;
                UPDATE cache
                SET LastAccessed = CASE WHEN cache.SlidingExpirationTicks IS NULL THEN cache.LastAccessed ELSE @now END
                OUTPUT inserted.CacheKey, inserted.CacheValue
                FROM {_table} AS cache
                INNER JOIN (VALUES {values}) AS requested(KeyHash, CacheKey)
                  ON cache.KeyHash = requested.KeyHash
                 AND cache.CacheKey = requested.CacheKey COLLATE Latin1_General_100_BIN2
                WHERE (cache.AbsoluteExpiration IS NULL OR cache.AbsoluteExpiration > @now)
                  AND (cache.SlidingExpirationTicks IS NULL OR DATEDIFF_BIG(MILLISECOND, cache.LastAccessed, @now) < cache.SlidingExpirationTicks / 10000);
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                result.Add(reader.GetString(0), (byte[])reader.GetValue(1));
        }
        return result;
    }

    public Task SetMultipleAsync(Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> entries, CancellationToken token = default) =>
        SetMultipleCoreAsync(entries.Select(pair => (pair.Key, (ReadOnlyMemory<byte>)pair.Value.value, pair.Value.options)), copyValues: false, token);

    public Task SetMultipleAsync(Dictionary<string, (ReadOnlyMemory<byte> value, DistributedCacheEntryOptions options)> entries, CancellationToken token = default) =>
        SetMultipleCoreAsync(entries.Select(pair => (pair.Key, pair.Value.value, pair.Value.options)), copyValues: true, token);

    public Task SetMultipleDirectAsync(Dictionary<string, (ReadOnlyMemory<byte> value, DistributedCacheEntryOptions options)> entries, CancellationToken token = default) =>
        SetMultipleCoreAsync(entries.Select(pair => (pair.Key, pair.Value.value, pair.Value.options)), copyValues: false, token);

    public async Task<Dictionary<string, byte[]?>> SetAndGetMultipleAsync(
        Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> entries,
        CancellationToken token = default)
    {
        await SetMultipleAsync(entries, token).ConfigureAwait(false);
        return await GetMultipleAsync(entries.Keys, token).ConfigureAwait(false);
    }

    public async Task<int> RemoveMultipleAsync(IEnumerable<string> keys, CancellationToken token = default)
    {
        var materialized = MaterializeKeys(keys);
        if (materialized.Count == 0) return 0;
        var removed = 0;
        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        foreach (var chunk in materialized.Chunk(BatchSize))
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            var values = AddRequestedKeys(command, chunk);
            command.CommandText = $"""
                DELETE cache
                FROM {_table} AS cache
                INNER JOIN (VALUES {values}) AS requested(KeyHash, CacheKey)
                  ON cache.KeyHash = requested.KeyHash
                 AND cache.CacheKey = requested.CacheKey COLLATE Latin1_General_100_BIN2;
                """;
            removed += await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        return removed;
    }

    public async Task<int> RefreshMultipleAsync(IEnumerable<string> keys, CancellationToken token = default)
    {
        var materialized = MaterializeKeys(keys);
        if (materialized.Count == 0) return 0;
        var refreshed = 0;
        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        foreach (var chunk in materialized.Chunk(BatchSize))
        {
            await using var command = CreateRefreshCommand(connection, null, chunk);
            refreshed += await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        return refreshed;
    }

    private async Task SetMultipleCoreAsync(
        IEnumerable<(string Key, ReadOnlyMemory<byte> Value, DistributedCacheEntryOptions Options)> source,
        bool copyValues,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        var entries = source.Select(entry =>
        {
            ValidateKey(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Options);
            return (entry.Key, Value: copyValues ? entry.Value.ToArray() : entry.Value, entry.Options);
        }).OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        if (entries.Length == 0) return;

        await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        foreach (var entry in entries)
            await UpsertAsync(connection, transaction, entry.Key, entry.Value, entry.Options, token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
    }

    private async Task UpsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        ReadOnlyMemory<byte> value,
        DistributedCacheEntryOptions entryOptions,
        CancellationToken token,
        string? baseType = null)
    {
        var now = _timeProvider.GetUtcNow();
        var absolute = GetAbsoluteExpiration(now, entryOptions);
        var sliding = entryOptions.SlidingExpiration ?? _options.DefaultSlidingExpiration;
        if (sliding <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(entryOptions), "Sliding expiration must be positive.");
        if (absolute <= now) throw new ArgumentOutOfRangeException(nameof(entryOptions), "Absolute expiration must be in the future.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.CommandText = $"""
            UPDATE {_table} WITH (UPDLOCK, HOLDLOCK)
            SET CacheKey = @key, CacheValue = @value, AbsoluteExpiration = @absoluteExpiration,
                BaseType = @baseType,
                SlidingExpirationTicks = @slidingExpirationTicks, LastAccessed = @utcNow
            WHERE KeyHash = @keyHash AND CacheKey = @key COLLATE Latin1_General_100_BIN2;
            IF @@ROWCOUNT = 0
            BEGIN
                IF EXISTS (SELECT 1 FROM {_table} WITH (UPDLOCK, HOLDLOCK) WHERE KeyHash = @keyHash)
                    THROW 50001, 'A SHA-256 cache-key collision was detected.', 1;
                INSERT INTO {_table} (KeyHash, CacheKey, CacheValue, AbsoluteExpiration, SlidingExpirationTicks, LastAccessed, BaseType)
                VALUES (@keyHash, @key, @value, @absoluteExpiration, @slidingExpirationTicks, @utcNow, @baseType);
            END;
            """;
        AddKey(command, key);
        command.Parameters.Add(new SqlParameter("@value", SqlDbType.VarBinary, -1) { Value = value.ToArray() });
        command.Parameters.Add(new SqlParameter("@absoluteExpiration", SqlDbType.DateTimeOffset) { Value = absolute is null ? DBNull.Value : absolute.Value });
        command.Parameters.Add(new SqlParameter("@slidingExpirationTicks", SqlDbType.BigInt) { Value = sliding is null ? DBNull.Value : sliding.Value.Ticks });
        command.Parameters.Add(new SqlParameter("@utcNow", SqlDbType.DateTimeOffset) { Value = now });
        command.Parameters.Add(new SqlParameter("@baseType", SqlDbType.NVarChar, -1) { Value = baseType is null ? DBNull.Value : baseType });
        try
        {
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number == 50001)
        {
            throw new InvalidOperationException("The SQL Server provider detected an unrecoverable cache-key hash collision.", exception);
        }
    }

    private DateTimeOffset? GetAbsoluteExpiration(DateTimeOffset now, DistributedCacheEntryOptions entryOptions)
    {
        var absolute = entryOptions.AbsoluteExpiration;
        var relative = entryOptions.AbsoluteExpirationRelativeToNow ?? _options.DefaultAbsoluteExpirationRelativeToNow;
        if (relative <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(entryOptions), "Relative expiration must be positive.");
        if (relative is not null)
        {
            var relativeAbsolute = now.Add(relative.Value);
            absolute = absolute is null || relativeAbsolute < absolute ? relativeAbsolute : absolute;
        }
        return absolute;
    }

    private SqlCommand CreateRefreshCommand(SqlConnection connection, SqlTransaction? transaction, IReadOnlyList<string> keys)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        var values = AddRequestedKeys(command, keys);
        command.Parameters.Add(new SqlParameter("@utcNow", SqlDbType.DateTimeOffset) { Value = _timeProvider.GetUtcNow() });
        command.CommandText = $"""
            DECLARE @now datetimeoffset(7) = @utcNow;
            UPDATE cache
            SET LastAccessed = CASE WHEN cache.SlidingExpirationTicks IS NULL THEN cache.LastAccessed ELSE @now END
            FROM {_table} AS cache
            INNER JOIN (VALUES {values}) AS requested(KeyHash, CacheKey)
              ON cache.KeyHash = requested.KeyHash
             AND cache.CacheKey = requested.CacheKey COLLATE Latin1_General_100_BIN2
            WHERE (cache.AbsoluteExpiration IS NULL OR cache.AbsoluteExpiration > @now)
              AND (cache.SlidingExpirationTicks IS NULL OR DATEDIFF_BIG(MILLISECOND, cache.LastAccessed, @now) < cache.SlidingExpirationTicks / 10000);
            """;
        return command;
    }

    private void EnsureSchema()
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@lockResource", SqlDbType.NVarChar, 255) { Value = $"GlacialCache.Schema.{_options.SchemaName}.{_options.TableName}" });
        command.CommandText = $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = @lockResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50002, 'Could not acquire the GlacialCache schema lock.', 1;

            IF SCHEMA_ID(N'{_options.SchemaName}') IS NULL EXEC(N'CREATE SCHEMA {SqlServerIdentifier.Quote(_options.SchemaName, nameof(_options.SchemaName))}');
            IF OBJECT_ID(N'{_options.SchemaName}.{_options.TableName}', N'U') IS NULL
            BEGIN
                CREATE TABLE {_table} (
                    KeyHash binary(32) NOT NULL,
                    CacheKey nvarchar(900) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    CacheValue varbinary(max) NOT NULL,
                    AbsoluteExpiration datetimeoffset(7) NULL,
                    SlidingExpirationTicks bigint NULL,
                    LastAccessed datetimeoffset(7) NOT NULL,
                    BaseType nvarchar(max) NULL,
                    CONSTRAINT {GetGeneratedIdentifier("PK_", _options.TableName)} PRIMARY KEY CLUSTERED (KeyHash),
                    CONSTRAINT {GetGeneratedIdentifier("CK_", $"{_options.TableName}_Sliding")} CHECK (SlidingExpirationTicks IS NULL OR SlidingExpirationTicks > 0)
                );
                CREATE INDEX {GetGeneratedIdentifier("IX_", $"{_options.TableName}_Expiration")}
                    ON {_table} (AbsoluteExpiration, LastAccessed) INCLUDE (SlidingExpirationTicks);
            END;
            ELSE IF COL_LENGTH(N'{_options.SchemaName}.{_options.TableName}', 'BaseType') IS NULL
                ALTER TABLE {_table} ADD BaseType nvarchar(max) NULL;
            ELSE IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'{_options.SchemaName}.{_options.TableName}')
                  AND name = N'BaseType'
                  AND (system_type_id <> TYPE_ID(N'nvarchar') OR max_length <> -1)
            )
                ALTER TABLE {_table} ALTER COLUMN BaseType nvarchar(max) NULL;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        _logger.LogInformation("GlacialCache SQL Server table {Schema}.{Table} is ready", _options.SchemaName, _options.TableName);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken token)
    {
        var connection = new SqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void AddKey(SqlCommand command, string key)
    {
        command.Parameters.Add(new SqlParameter("@keyHash", SqlDbType.Binary, 32) { Value = HashKey(key) });
        command.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, MaxKeyLength) { Value = key });
    }

    private static string AddRequestedKeys(SqlCommand command, IReadOnlyList<string> keys)
    {
        var values = new StringBuilder();
        for (var index = 0; index < keys.Count; index++)
        {
            if (index > 0) values.Append(',');
            values.Append($"(@hash{index}, @key{index})");
            command.Parameters.Add(new SqlParameter($"@hash{index}", SqlDbType.Binary, 32) { Value = HashKey(keys[index]) });
            command.Parameters.Add(new SqlParameter($"@key{index}", SqlDbType.NVarChar, MaxKeyLength) { Value = keys[index] });
        }
        return values.ToString();
    }

    private static byte[] HashKey(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private static string GetGeneratedIdentifier(string prefix, string value)
    {
        const int maxIdentifierLength = 128;
        var candidate = prefix + value;
        if (candidate.Length > maxIdentifierLength)
        {
            var suffix = $"_{Convert.ToHexString(HashKey(candidate)).Substring(0, 16)}";
            candidate = candidate.Substring(0, maxIdentifierLength - suffix.Length) + suffix;
        }

        return SqlServerIdentifier.Quote(candidate, nameof(value));
    }

    private static List<string> MaterializeKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = keys.Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in result) ValidateKey(key);
        return result;
    }

    private static void ValidateKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be empty or whitespace.", nameof(key));
        if (key.Length > MaxKeyLength) throw new ArgumentException($"Key length cannot exceed {MaxKeyLength} characters.", nameof(key));
        if (key.Any(char.IsControl)) throw new ArgumentException("Key cannot contain control characters.", nameof(key));
    }
}
