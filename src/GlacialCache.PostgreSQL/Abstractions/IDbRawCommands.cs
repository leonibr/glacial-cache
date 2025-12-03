

using Microsoft.Extensions.Caching.Distributed;

internal interface IDbRawCommands
{
    /// <summary>
    /// Gets the SQL for getting a key.
    /// <code>
    /// UPDATE {fullTableName}
    /// SET next_expiration = {GetNextExpirationCaseStatement()}
    /// WHERE key = @key AND next_expiration > now()
    /// RETURNING 
    ///     key, value, absolute_expiration, sliding_interval, 
    ///     value_type, value_size, next_expiration
    /// </code>
    /// </summary>
    string GetSql { get; }
    /// <summary>
    /// Gets the SQL for getting a key.
    /// <code>
    /// UPDATE {fullTableName}
    /// SET next_expiration = {GetNextExpirationCaseStatement()}
    /// WHERE key = @key AND next_expiration > now()
    /// RETURNING value
    /// </code>
    /// </summary>
    string GetSqlCore { get; }
    /// <summary>
    /// Gets the SQL for setting a key.
    /// <code>
    /// INSERT INTO {fullTableName} (key, value, absolute_expiration, sliding_interval, value_type, next_expiration)
    /// VALUES (@key, @value, @absoluteExpiration, @slidingInterval, @value_type, {GetNextExpirationCaseStatement("@absoluteExpiration", "@slidingInterval")})
    /// ON CONFLICT (key) 
    /// DO UPDATE SET   
    ///     value = EXCLUDED.value,
    ///     absolute_expiration = EXCLUDED.absolute_expiration,
    ///     sliding_interval = EXCLUDED.sliding_interval,
    ///     next_expiration = EXCLUDED.next_expiration
    /// </code>
    /// </summary>
    string SetSql { get; }
    /// <summary>
    /// Gets the SQL for deleting a key.
    /// <code>
    /// DELETE FROM {fullTableName} WHERE key = @key
    /// </code>
    /// </summary>
    string DeleteSql { get; }
    /// <summary>
    /// Gets the SQL for deleting multiple keys.
    /// <code>
    /// DELETE FROM {fullTableName} WHERE key = ANY(@keys)
    /// </code>
    /// </summary>
    string DeleteMultipleSql { get; }
    /// <summary>
    /// Gets the SQL for refreshing a key.
    /// <code>
    /// UPDATE {fullTableName} 
    /// SET next_expiration = {GetNextExpirationCaseStatement()}
    /// WHERE key = @key AND sliding_interval IS NOT NULL AND next_expiration > now()
    /// </code>
    /// </summary>
    string RefreshSql { get; }

    /// <summary>
    /// Gets the SQL for cleaning up expired keys.
    /// <code>
    /// DELETE FROM {fullTableName}
    /// WHERE next_expiration <= now()
    /// </code>
    /// </summary>
    string CleanupExpiredSql { get; }
    /// <summary>
    /// Gets the SQL for getting multiple keys.
    /// <code>
    /// UPDATE {fullTableName}
    /// SET next_expiration = {GetNextExpirationCaseStatement()}
    /// WHERE key = ANY(@keys) AND next_expiration > now()
    /// RETURNING 
    ///     key, value, absolute_expiration, sliding_interval, 
    ///     value_type, value_size, next_expiration
    /// </code>
    /// </summary>
    string GetMultipleSql { get; }
    /// <summary>
    /// Gets the SQL for setting multiple keys.
    /// <code>
    /// INSERT INTO {fullTableName} (key, value, absolute_expiration, sliding_interval, value_type, next_expiration)
    /// VALUES ($1, $2, NOW() + $3::interval, $4, $5, {GetNextExpirationForInsertPositional("$3", "$4")})
    /// ON CONFLICT (key)
    /// DO UPDATE SET
    ///     value = EXCLUDED.value,
    ///     absolute_expiration = EXCLUDED.absolute_expiration,
    ///     sliding_interval = EXCLUDED.sliding_interval,
    ///     next_expiration = EXCLUDED.next_expiration
    /// </code>
    /// </summary>
    string SetMultipleSql { get; }
    /// <summary>
    /// Gets the SQL for removing multiple keys.
    /// <code>
    /// DELETE FROM {fullTableName} WHERE key = ANY(@keys)
    /// </code>
    /// </summary>
    string RemoveMultipleSql { get; }
    /// <summary>
    /// Gets the SQL for refreshing multiple keys.
    /// <code>
    /// UPDATE {fullTableName} 
    /// SET next_expiration = {GetNextExpirationCaseStatement()}
    /// WHERE key = ANY(@keys) AND sliding_interval IS NOT NULL AND next_expiration > now()
    /// </code>
    /// </summary>
    string RefreshMultipleSql { get; }
}

