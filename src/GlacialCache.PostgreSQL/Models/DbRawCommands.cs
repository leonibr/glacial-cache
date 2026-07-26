using System.ComponentModel;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Models;

internal sealed class DbRawCommands : IDbRawCommands, IRuntimeConfigurationSubscriber, IDisposable
{
    private readonly GlacialCachePostgreSQLOptions _observableOptions;
    private readonly IDisposable _runtimeConfigurationSubscription;
    private readonly IRuntimeConfigurationPublisher? _ownedRuntimeConfigurationPublisher;
    private readonly IRuntimeConfigurationSnapshotProvider _snapshotProvider;
    private readonly ILogger<DbRawCommands>? _logger;
    private RuntimeConfigurationSnapshot _snapshot;

    internal DbRawCommands(IDbNomenclature dbNomenclature, IOptionsMonitor<GlacialCachePostgreSQLOptions> options, ILogger<DbRawCommands>? logger = null)
        : this(
            dbNomenclature,
            options,
            logger,
            new RuntimeConfigurationPublisher(
                options,
                new ObservableOptionsSynchronizer(),
                NullLogger<RuntimeConfigurationPublisher>.Instance),
            ownsRuntimeConfigurationPublisher: true)
    {
    }

    internal DbRawCommands(
        IDbNomenclature dbNomenclature,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        ILogger<DbRawCommands>? logger,
        IRuntimeConfigurationPublisher runtimeConfigurationPublisher)
        : this(dbNomenclature, options, logger, runtimeConfigurationPublisher, ownsRuntimeConfigurationPublisher: false)
    {
    }

    private DbRawCommands(
        IDbNomenclature dbNomenclature,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        ILogger<DbRawCommands>? logger,
        IRuntimeConfigurationPublisher runtimeConfigurationPublisher,
        bool ownsRuntimeConfigurationPublisher)
    {
        ArgumentNullException.ThrowIfNull(dbNomenclature);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtimeConfigurationPublisher);

        _observableOptions = options.CurrentValue;
        _logger = logger;
        _snapshotProvider = runtimeConfigurationPublisher;
        _ownedRuntimeConfigurationPublisher = ownsRuntimeConfigurationPublisher ? runtimeConfigurationPublisher : null;
        _snapshot = _snapshotProvider.Current;

        _observableOptions.Cache.TableNameObservable.PropertyChanged += OnConfigurationPropertyChanged;
        _observableOptions.Cache.SchemaNameObservable.PropertyChanged += OnConfigurationPropertyChanged;

        _runtimeConfigurationSubscription = runtimeConfigurationPublisher.Subscribe(this);
    }

    public void OnRuntimeConfigurationChanged(RuntimeConfigurationChangedEventArgs change)
    {
        if (!change.Changes.CacheSnapshotChanged)
        {
            return;
        }

        Volatile.Write(ref _snapshot, change.Current);
        _logger?.LogDebug("Runtime configuration changes synchronized to raw SQL commands");
    }

    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (RuntimeConfigurationReloadScope.IsActive)
        {
            return;
        }

        if (e is PropertyChangedEventArgs<string> typedArgs)
        {
            _logger?.LogDebug(
                "Configuration property {PropertyName} changed from {OldValue} to {NewValue}, rebuilding SQL",
                e.PropertyName, typedArgs.OldValue, typedArgs.NewValue);

            Volatile.Write(ref _snapshot, RuntimeConfigurationSnapshot.FromObservableOptions(_observableOptions));
        }
    }

    private DbSqlSnapshot Sql => Volatile.Read(ref _snapshot).Cache.Sql;

    /// <inheritdoc cref="IDbRawCommands.GetSql" />
    public string GetSql => Sql.GetSql;

    /// <inheritdoc cref="IDbRawCommands.GetSqlCore" />
    public string GetSqlCore => Sql.GetSqlCore;

    /// <inheritdoc cref="IDbRawCommands.SetSql" />
    public string SetSql => Sql.SetSql;

    /// <inheritdoc cref="IDbRawCommands.DeleteSql" />
    public string DeleteSql => Sql.DeleteSql;

    /// <inheritdoc cref="IDbRawCommands.DeleteMultipleSql" />
    public string DeleteMultipleSql => Sql.DeleteMultipleSql;

    /// <inheritdoc cref="IDbRawCommands.RefreshSql" />
    public string RefreshSql => Sql.RefreshSql;

    /// <inheritdoc cref="IDbRawCommands.CleanupExpiredSql" />
    public string CleanupExpiredSql => Sql.CleanupExpiredSql;

    /// <inheritdoc cref="IDbRawCommands.GetMultipleSql" />
    public string GetMultipleSql => Sql.GetMultipleSql;

    /// </inheritdoc cref="IDbRawCommands.SetMultipleSql" />
    public string SetMultipleSql => Sql.SetMultipleSql;

    /// <inheritdoc cref="IDbRawCommands.SetMultipleBulkSql" />
    public string SetMultipleBulkSql => Sql.SetMultipleBulkSql;

    /// <inheritdoc cref="IDbRawCommands.RemoveMultipleSql" />
    public string RemoveMultipleSql => Sql.RemoveMultipleSql;

    /// <inheritdoc cref="IDbRawCommands.RefreshMultipleSql" />
    public string RefreshMultipleSql => Sql.RefreshMultipleSql;

    public void Dispose()
    {
        _observableOptions.Cache.TableNameObservable.PropertyChanged -= OnConfigurationPropertyChanged;
        _observableOptions.Cache.SchemaNameObservable.PropertyChanged -= OnConfigurationPropertyChanged;

        _runtimeConfigurationSubscription.Dispose();
        _ownedRuntimeConfigurationPublisher?.Dispose();
    }
}
