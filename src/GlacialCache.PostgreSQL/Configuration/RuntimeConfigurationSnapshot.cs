using Npgsql;

namespace GlacialCache.PostgreSQL.Configuration;

internal interface IRuntimeConfigurationSnapshotProvider
{
    RuntimeConfigurationSnapshot Current { get; }
}

internal sealed record RuntimeConfigurationSnapshot(
    CacheRuntimeSnapshot Cache,
    ConnectionRuntimeSnapshot Connection)
{
    public static RuntimeConfigurationSnapshot FromOptions(GlacialCachePostgreSQLOptions options)
    {
        return FromOptions(options, preferObservableCacheValues: false);
    }

    public static RuntimeConfigurationSnapshot FromObservableOptions(GlacialCachePostgreSQLOptions options)
    {
        return FromOptions(options, preferObservableCacheValues: true);
    }

    private static RuntimeConfigurationSnapshot FromOptions(
        GlacialCachePostgreSQLOptions options,
        bool preferObservableCacheValues)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tableName = preferObservableCacheValues
            ? options.Cache.TableNameObservable.Value
            : options.Cache.TableName;
        var schemaName = preferObservableCacheValues
            ? options.Cache.SchemaNameObservable.Value
            : options.Cache.SchemaName;
        var fullTableName = $"{schemaName}.{tableName}";
        var defaultAbsoluteExpirationRelativeToNow = options.Cache.DefaultAbsoluteExpirationRelativeToNow;

        var cache = new CacheRuntimeSnapshot(
            schemaName,
            tableName,
            fullTableName,
            options.Cache.DefaultSlidingExpiration,
            defaultAbsoluteExpirationRelativeToNow,
            options.Cache.MinimumExpirationInterval,
            options.Cache.MaximumExpirationInterval,
            options.Cache.EnableEdgeCaseLogging,
            options.Cache.Serializer,
            options.Cache.CustomSerializerType,
            RuntimeSqlBuilder.Build(fullTableName, defaultAbsoluteExpirationRelativeToNow));

        return new RuntimeConfigurationSnapshot(
            cache,
            ConnectionRuntimeSnapshot.FromOptions(options.Connection, preferObservableValues: preferObservableCacheValues));
    }
}

internal sealed record CacheRuntimeSnapshot(
    string SchemaName,
    string TableName,
    string FullTableName,
    TimeSpan? DefaultSlidingExpiration,
    TimeSpan? DefaultAbsoluteExpirationRelativeToNow,
    TimeSpan MinimumExpirationInterval,
    TimeSpan MaximumExpirationInterval,
    bool EnableEdgeCaseLogging,
    SerializerType Serializer,
    Type? CustomSerializerType,
    DbSqlSnapshot Sql);

internal sealed record ConnectionRuntimeSnapshot(
    string ConnectionString,
    int MinPoolSize,
    int MaxPoolSize,
    int IdleLifetimeSeconds,
    int PruningIntervalSeconds)
{
    public static ConnectionRuntimeSnapshot FromOptions(ConnectionOptions options, bool preferObservableValues = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = preferObservableValues && !string.IsNullOrWhiteSpace(options.ConnectionStringObservable.Value)
            ? options.ConnectionStringObservable.Value
            : options.ConnectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true
        };

        var minPoolSize = GetPoolOption(options.Pool.MinSizeObservable, options.Pool.MinSize, preferObservableValues);
        var maxPoolSize = GetPoolOption(options.Pool.MaxSizeObservable, options.Pool.MaxSize, preferObservableValues);
        var idleLifetime = GetPoolOption(options.Pool.IdleLifetimeSecondsObservable, options.Pool.IdleLifetimeSeconds, preferObservableValues);
        var pruningInterval = GetPoolOption(options.Pool.PruningIntervalSecondsObservable, options.Pool.PruningIntervalSeconds, preferObservableValues);

        builder.MinPoolSize = builder.MinPoolSize != 0 ? builder.MinPoolSize : minPoolSize;
        builder.MaxPoolSize = builder.MaxPoolSize != 100 ? builder.MaxPoolSize : maxPoolSize;
        builder.ConnectionIdleLifetime = idleLifetime;
        builder.ConnectionPruningInterval = pruningInterval;
        builder.ApplicationName = string.IsNullOrEmpty(builder.ApplicationName) ? "GlacialCache" : builder.ApplicationName;

        return new ConnectionRuntimeSnapshot(
            builder.ConnectionString,
            builder.MinPoolSize,
            builder.MaxPoolSize,
            builder.ConnectionIdleLifetime,
            builder.ConnectionPruningInterval);
    }

    private static int GetPoolOption(ObservableProperty<int> observable, int configuredValue, bool preferObservableValues)
    {
        if (!preferObservableValues)
        {
            return configuredValue;
        }

        var observableValue = observable.Value;
        return observableValue != default ? observableValue : configuredValue;
    }
}

internal sealed record DbSqlSnapshot(
    string GetSql,
    string GetSqlCore,
    string SetSql,
    string DeleteSql,
    string DeleteMultipleSql,
    string RefreshSql,
    string CleanupExpiredSql,
    string GetMultipleSql,
    string SetMultipleSql,
    string RemoveMultipleSql,
    string RefreshMultipleSql);

internal static class RuntimeSqlBuilder
{
    public static DbSqlSnapshot Build(string fullTableName, TimeSpan? defaultAbsoluteExpirationRelativeToNow)
    {
        return new DbSqlSnapshot(
            BuildGetSql(fullTableName),
            BuildGetSqlCore(fullTableName),
            BuildSetSql(fullTableName, defaultAbsoluteExpirationRelativeToNow),
            BuildDeleteSql(fullTableName),
            BuildDeleteMultipleSql(fullTableName),
            BuildRefreshSql(fullTableName),
            BuildCleanupExpiredSql(fullTableName),
            BuildGetMultipleSql(fullTableName),
            BuildSetMultipleSql(fullTableName, defaultAbsoluteExpirationRelativeToNow),
            BuildRemoveMultipleSql(fullTableName),
            BuildRefreshMultipleSql(fullTableName));
    }

    private static string GetNextExpirationCaseStatement(
        string absoluteExp = "absolute_expiration",
        string slidingInt = "sliding_interval",
        string defaultInterval = "interval '1 day'",
        string nowParam = "@now")
    => $@"CASE
            WHEN {absoluteExp} IS NOT NULL AND {slidingInt} IS NULL THEN {absoluteExp}
            WHEN {absoluteExp} IS NOT NULL AND {slidingInt} IS NOT NULL THEN LEAST({nowParam} + {slidingInt}, {absoluteExp})
            WHEN {absoluteExp} IS NULL AND {slidingInt} IS NOT NULL THEN {nowParam} + {slidingInt}
            ELSE {nowParam} + {defaultInterval}
        END";

    private static string GetNextExpirationForInsert(
        TimeSpan? defaultAbsoluteExpirationRelativeToNow,
        string relativeParam = "@relativeInterval",
        string slidingParam = "@slidingInterval",
        string nowParam = "@now")
    {
        var defaultInterval = defaultAbsoluteExpirationRelativeToNow ?? TimeSpan.FromDays(1);
        var defaultIntervalSql = $"interval '{Math.Max(1, (int)defaultInterval.TotalDays)} days'";

        return $@"CASE
            WHEN {relativeParam} IS NOT NULL AND {slidingParam} IS NOT NULL THEN LEAST({nowParam} + {relativeParam}, {nowParam} + {slidingParam})
            WHEN {slidingParam} IS NOT NULL THEN {nowParam} + {slidingParam}
            WHEN {relativeParam} IS NOT NULL THEN {nowParam} + {relativeParam}
            ELSE {nowParam} + {defaultIntervalSql}
        END";
    }

    private static string GetNextExpirationForInsertPositional(
        TimeSpan? defaultAbsoluteExpirationRelativeToNow,
        string relativeParam = "$3",
        string slidingParam = "$4",
        string nowParam = "$6")
    {
        var defaultInterval = defaultAbsoluteExpirationRelativeToNow ?? TimeSpan.FromDays(1);
        var defaultIntervalSql = $"interval '{Math.Max(1, (int)defaultInterval.TotalDays)} days'";

        return $@"CASE
            WHEN {relativeParam} IS NOT NULL AND {slidingParam} IS NOT NULL THEN LEAST({nowParam} + {relativeParam}, {nowParam} + {slidingParam})
            WHEN {slidingParam} IS NOT NULL THEN {nowParam} + {slidingParam}
            WHEN {relativeParam} IS NOT NULL THEN {nowParam} + {relativeParam}
            ELSE {nowParam} + {defaultIntervalSql}
        END";
    }

    private static string BuildGetSql(string fullTableName) => $@"
                UPDATE {fullTableName}
                SET next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@Now")}
                WHERE key = @Key AND next_expiration > @Now
                RETURNING
                    value,
                    absolute_expiration,
                    sliding_interval,
                    value_type,
                    value_size,
                    next_expiration";

    private static string BuildGetSqlCore(string fullTableName) => $@"
                UPDATE {fullTableName}
                SET next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@Now")}
                WHERE key = @Key AND next_expiration > @Now
                RETURNING value";

    private static string BuildSetSql(string fullTableName, TimeSpan? defaultAbsoluteExpirationRelativeToNow) => $@"
            INSERT INTO {fullTableName} (key, value, absolute_expiration, sliding_interval, value_type, next_expiration)
            VALUES (
            @Key, @Value, @Now + @RelativeInterval::interval, @SlidingInterval, @ValueType,
            {GetNextExpirationForInsert(defaultAbsoluteExpirationRelativeToNow, "@RelativeInterval", "@SlidingInterval", "@Now")})
            ON CONFLICT (key)
            DO UPDATE SET
                value = EXCLUDED.value,
                absolute_expiration = EXCLUDED.absolute_expiration,
                sliding_interval = EXCLUDED.sliding_interval,
                next_expiration = EXCLUDED.next_expiration";

    private static string BuildDeleteSql(string fullTableName) => $"DELETE FROM {fullTableName} WHERE key = @Key";

    private static string BuildDeleteMultipleSql(string fullTableName) => $"DELETE FROM {fullTableName} WHERE key = ANY(@keys)";

    private static string BuildRefreshSql(string fullTableName) => $@"
            UPDATE {fullTableName} 
            SET next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@Now")}
            WHERE key = @Key AND sliding_interval IS NOT NULL
            AND next_expiration > @Now";

    private static string BuildCleanupExpiredSql(string fullTableName) => $@"
            DELETE FROM {fullTableName}
            WHERE next_expiration <= @now";

    private static string BuildGetMultipleSql(string fullTableName) => $@"
            UPDATE {fullTableName}
            SET 
                next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@now")}                 
            WHERE key = ANY(@keys) AND next_expiration > @now
            RETURNING 
                key, value, absolute_expiration, sliding_interval, 
                value_type, value_size, next_expiration;";

    private static string BuildSetMultipleSql(string fullTableName, TimeSpan? defaultAbsoluteExpirationRelativeToNow) => $@"
            INSERT INTO {fullTableName} (key, value, absolute_expiration, sliding_interval, value_type, next_expiration)
            VALUES ($1, $2, $6 + $3::interval, $4, $5, {GetNextExpirationForInsertPositional(defaultAbsoluteExpirationRelativeToNow, "$3", "$4", "$6")})
            ON CONFLICT (key)
            DO UPDATE SET
                value = EXCLUDED.value,
                absolute_expiration = EXCLUDED.absolute_expiration,
                sliding_interval = EXCLUDED.sliding_interval,
                next_expiration = EXCLUDED.next_expiration";

    private static string BuildRemoveMultipleSql(string fullTableName) => $"DELETE FROM {fullTableName} WHERE key = ANY(@keys)";

    private static string BuildRefreshMultipleSql(string fullTableName) => $@"
            UPDATE {fullTableName} 
            SET next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@now")}
            WHERE key = ANY(@keys) 
                AND sliding_interval IS NOT NULL
                AND  next_expiration > @now";
}
