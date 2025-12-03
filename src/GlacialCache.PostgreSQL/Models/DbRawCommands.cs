using System.Diagnostics.CodeAnalysis;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace GlacialCache.PostgreSQL.Models;
using Extensions;

internal sealed class DbRawCommands : IDbRawCommands, IDisposable
{
    private readonly IDbNomenclature _dbNomenclature;
    private readonly IOptionsMonitor<GlacialCachePostgreSQLOptions> _optionsMonitor;
    private readonly IDisposable? _optionsChangeToken;
    private GlacialCachePostgreSQLOptions _options;
    private readonly ILogger<DbRawCommands>? _logger;
    private readonly object _lockObject = new();
    private readonly CacheOptions _cacheOptions;
    private string? _getSql;
    private string? _getSqlCore;
    private string? _setSql;
    private string? _deleteSql;
    private string? _deleteMultipleSql;
    private string? _refreshSql;
    private string? _cleanupExpiredSql;
    private string? _getMultipleSql;
    private string? _setMultipleSql;
    private string? _removeMultipleSql;
    private string? _refreshMultipleSql;

    internal DbRawCommands(IDbNomenclature dbNomenclature, IOptionsMonitor<GlacialCachePostgreSQLOptions> options, ILogger<DbRawCommands>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dbNomenclature = dbNomenclature ?? throw new ArgumentNullException(nameof(dbNomenclature));
        _optionsMonitor = options;
        _options = options.CurrentValue;
        _logger = logger;
        _cacheOptions = options.CurrentValue.Cache;

        // Subscribe directly to ObservableProperty changes for configuration updates
        _options.Cache.TableNameObservable.PropertyChanged += OnConfigurationPropertyChanged;
        _options.Cache.SchemaNameObservable.PropertyChanged += OnConfigurationPropertyChanged;

        // Register for external configuration changes (IOptionsMonitor pattern)
        _optionsChangeToken = _optionsMonitor.OnChange(OnExternalConfigurationChanged);

        // Initial SQL build
        Reload(dbNomenclature);
    }

    /// <summary>
    /// Handles external configuration changes from IOptionsMonitor and syncs to observable properties.
    /// </summary>
    private void OnExternalConfigurationChanged(GlacialCachePostgreSQLOptions newOptions)
    {
        try
        {
            // Use extension method to sync observable properties
            _options.Cache.SyncFromExternalChanges(newOptions.Cache, _logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DbRawCommands>.Instance);

            // Update our internal reference
            _options = newOptions;

            _logger?.LogDebug("External configuration changes synchronized to observable properties");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to sync external configuration changes");
        }
    }

    /// <summary>
    /// Handles property changes from ObservableProperty instances and rebuilds SQL accordingly.
    /// </summary>
    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            if (e is PropertyChangedEventArgs<string> typedArgs)
            {
                _logger?.LogDebug(
                    "Configuration property {PropertyName} changed from {OldValue} to {NewValue}, rebuilding SQL",
                    e.PropertyName, typedArgs.OldValue, typedArgs.NewValue);

                // Rebuild SQL with updated configuration
                Reload(_dbNomenclature);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling configuration property change for {PropertyName}", e.PropertyName);
            throw;
        }
    }

    // Legacy method for backward compatibility (column-based)
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

    // New method for SET operations (parameter-based, relative intervals)
    private string GetNextExpirationForInsert(
        string relativeParam = "@relativeInterval",
        string slidingParam = "@slidingInterval",
        string nowParam = "@now")
    {
        var defaultInterval = _cacheOptions?.DefaultAbsoluteExpirationRelativeToNow ?? TimeSpan.FromDays(1);
        var defaultIntervalSql = $"interval '{Math.Max(1, (int)defaultInterval.TotalDays)} days'";

        return $@"CASE
            WHEN {relativeParam} IS NOT NULL AND {slidingParam} IS NOT NULL THEN LEAST({nowParam} + {relativeParam}, {nowParam} + {slidingParam})
            WHEN {slidingParam} IS NOT NULL THEN {nowParam} + {slidingParam}
            WHEN {relativeParam} IS NOT NULL THEN {nowParam} + {relativeParam}
            ELSE {nowParam} + {defaultIntervalSql}
        END";
    }

    // Method for SET operations with positional parameters
    private string GetNextExpirationForInsertPositional(
        string relativeParam = "$3",
        string slidingParam = "$4",
        string nowParam = "$6")
    {
        var defaultInterval = _cacheOptions?.DefaultAbsoluteExpirationRelativeToNow ?? TimeSpan.FromDays(1);
        var defaultIntervalSql = $"interval '{Math.Max(1, (int)defaultInterval.TotalDays)} days'";

        return $@"CASE
            WHEN {relativeParam} IS NOT NULL AND {slidingParam} IS NOT NULL THEN LEAST({nowParam} + {relativeParam}, {nowParam} + {slidingParam})
            WHEN {slidingParam} IS NOT NULL THEN {nowParam} + {slidingParam}
            WHEN {relativeParam} IS NOT NULL THEN {nowParam} + {relativeParam}
            ELSE {nowParam} + {defaultIntervalSql}
        END";
    }

    // New method for GET operations during transition (column-based with relative intervals)
    private string GetNextExpirationForSelect(
        string absoluteCol = "absolute_expiration",
        string slidingCol = "sliding_interval")
    {
        var defaultInterval = _cacheOptions?.DefaultAbsoluteExpirationRelativeToNow ?? TimeSpan.FromDays(1);
        var defaultIntervalSql = $"interval '{(int)defaultInterval.TotalDays} days'";

        return $@"CASE
            WHEN {absoluteCol} IS NOT NULL AND {slidingCol} IS NULL THEN {absoluteCol}
            WHEN {absoluteCol} IS NOT NULL AND {slidingCol} IS NOT NULL THEN LEAST(now() + {slidingCol}, {absoluteCol})
            WHEN {absoluteCol} IS NULL AND {slidingCol} IS NOT NULL THEN now() + {slidingCol}
            ELSE now() + {defaultIntervalSql}
        END";
    }



    private void Reload(IDbNomenclature nomenclature)
    {
        var _fullName = nomenclature.FullTableName;
        lock (_lockObject)
        {
            _getSql = _buildGetSql(_fullName);
            _getSqlCore = _buildGetSqlCore(_fullName);
            _setSql = _buildSetSql(_fullName);
            _deleteSql = _buildDeleteSql(_fullName);
            _deleteMultipleSql = _buildDeleteMultipleSql(_fullName);
            _refreshSql = _buildRefreshSql(_fullName);
            _cleanupExpiredSql = _buildCleanupExpiredSql(_fullName);
            _getMultipleSql = _buildGetMultipleSql(_fullName);
            _setMultipleSql = _buildSetMultipleSql(_fullName);
            _removeMultipleSql = _buildRemoveMultipleSql(_fullName);
            _refreshMultipleSql = _buildRefreshMultipleSql(_fullName);
        }
    }

    // SQL building methods
    private string _buildGetSql(string fullTableName) => $@"
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
    private string _buildGetSqlCore(string fullTableName) => $@"
                UPDATE {fullTableName}
                SET next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@Now")}
                WHERE key = @Key AND next_expiration > @Now
                RETURNING value";


    private string _buildSetSql(string fullTableName) => $@"
            INSERT INTO {fullTableName} (key, value, absolute_expiration, sliding_interval, value_type, next_expiration)
            VALUES (
            @Key, @Value, @Now + @RelativeInterval::interval, @SlidingInterval, @ValueType,
            {GetNextExpirationForInsert("@RelativeInterval", "@SlidingInterval", "@Now")})
            ON CONFLICT (key)
            DO UPDATE SET
                value = EXCLUDED.value,
                absolute_expiration = EXCLUDED.absolute_expiration,
                sliding_interval = EXCLUDED.sliding_interval,
                next_expiration = EXCLUDED.next_expiration";


    private string _buildDeleteSql(string fullTableName) => $"DELETE FROM {fullTableName} WHERE key = @Key";
    private string _buildDeleteMultipleSql(string fullTableName) => $"DELETE FROM {fullTableName} WHERE key = ANY(@keys)";

    private string _buildRefreshSql(string fullTableName) => $@"
            UPDATE {fullTableName} 
            SET next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@Now")}
            WHERE key = @Key AND sliding_interval IS NOT NULL
            AND next_expiration > @Now";


    private string _buildCleanupExpiredSql(string fullTableName) => $@"
            DELETE FROM {fullTableName}
            WHERE next_expiration <= @now";


    private string _buildGetMultipleSql(string fullTableName) => $@"
            UPDATE {fullTableName}
            SET 
                next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@now")}                 
            WHERE key = ANY(@keys) AND next_expiration > @now
            RETURNING 
                key, value, absolute_expiration, sliding_interval, 
                value_type, value_size, next_expiration;";


    // absolute_expiration is not null, sliding_interval is null -> absolute_expiration
    // absolute_expiration is not null, sliding_interval is not null -> LEAST(now() + sliding_interval, absolute_expiration)
    // absolute_expiration is null, sliding_interval is not null -> now() + sliding_interval
    private string _buildSetMultipleSql(string fullTableName) => $@"
            INSERT INTO {fullTableName} (key, value, absolute_expiration, sliding_interval, value_type, next_expiration)
            VALUES ($1, $2, $6 + $3::interval, $4, $5, {GetNextExpirationForInsertPositional("$3", "$4", "$6")})
            ON CONFLICT (key)
            DO UPDATE SET
                value = EXCLUDED.value,
                absolute_expiration = EXCLUDED.absolute_expiration,
                sliding_interval = EXCLUDED.sliding_interval,
                next_expiration = EXCLUDED.next_expiration";


    private string _buildRemoveMultipleSql(string fullTableName) => $"DELETE FROM {fullTableName} WHERE key = ANY(@keys)";

    private string _buildRefreshMultipleSql(string fullTableName) => $@"
            UPDATE {fullTableName} 
            SET next_expiration = {GetNextExpirationCaseStatement("absolute_expiration", "sliding_interval", "interval '1 day'", "@now")}
            WHERE key = ANY(@keys) 
                AND sliding_interval IS NOT NULL
                AND  next_expiration > @now";



    /// <inheritdoc cref="IDbRawCommands.GetSql" />
    public string GetSql => _getSql ?? throw new InvalidOperationException("GetSql is not initialized");

    /// <inheritdoc cref="IDbRawCommands.GetSqlCore" />
    public string GetSqlCore => _getSqlCore ?? throw new InvalidOperationException("GetSqlCore is not initialized");


    /// <inheritdoc cref="IDbRawCommands.SetSql" />
    public string SetSql => _setSql ?? throw new InvalidOperationException("SetSql is not initialized");

    /// <inheritdoc cref="IDbRawCommands.DeleteSql" />
    public string DeleteSql => _deleteSql ?? throw new InvalidOperationException("DeleteSql is not initialized");


    /// <inheritdoc cref="IDbRawCommands.DeleteMultipleSql" />
    public string DeleteMultipleSql => _deleteMultipleSql ?? throw new InvalidOperationException("DeleteMultipleSql is not initialized");

    /// <inheritdoc cref="IDbRawCommands.RefreshSql" />
    public string RefreshSql => _refreshSql ?? throw new InvalidOperationException("RefreshSql is not initialized");

    /// <inheritdoc cref="IDbRawCommands.CleanupExpiredSql" />
    public string CleanupExpiredSql => _cleanupExpiredSql ?? throw new InvalidOperationException("CleanupExpiredSql is not initialized");

    /// <inheritdoc cref="IDbRawCommands.GetMultipleSql" />
    public string GetMultipleSql => _getMultipleSql ?? throw new InvalidOperationException("GetMultipleSql is not initialized");


    /// </inheritdoc cref="IDbRawCommands.SetMultipleSql" />
    public string SetMultipleSql => _setMultipleSql ?? throw new InvalidOperationException("SetMultipleSql is not initialized");

    /// <inheritdoc cref="IDbRawCommands.RemoveMultipleSql" />
    public string RemoveMultipleSql => _removeMultipleSql ?? throw new InvalidOperationException("RemoveMultipleSql is not initialized");

    /// <inheritdoc cref="IDbRawCommands.RefreshMultipleSql" />
    public string RefreshMultipleSql => _refreshMultipleSql ?? throw new InvalidOperationException("RefreshMultipleSql is not initialized");

    public void Dispose()
    {
        // Unsubscribe from ObservableProperty events to prevent memory leaks
        _options.Cache.TableNameObservable.PropertyChanged -= OnConfigurationPropertyChanged;
        _options.Cache.SchemaNameObservable.PropertyChanged -= OnConfigurationPropertyChanged;

        // Dispose the options change token to prevent memory leaks
        _optionsChangeToken?.Dispose();
    }
}