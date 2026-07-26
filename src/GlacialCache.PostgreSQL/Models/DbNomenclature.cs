using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Models;
using Abstractions;
using Configuration;

internal sealed class DbNomenclature : IDbNomenclature, IRuntimeConfigurationSubscriber, IDisposable
{
    private readonly ILogger<DbNomenclature> _logger;
    private readonly GlacialCachePostgreSQLOptions _observableOptions;
    private readonly IDisposable _runtimeConfigurationSubscription;
    private readonly IRuntimeConfigurationPublisher? _ownedRuntimeConfigurationPublisher;
    private readonly IRuntimeConfigurationSnapshotProvider _snapshotProvider;
    private GlacialCachePostgreSQLOptions _options;
    private RuntimeConfigurationSnapshot _snapshot;

    /// <summary>
    /// The table name (lowercase, validated PostgreSQL identifier).
    /// </summary>
    public string TableName => Volatile.Read(ref _snapshot).Cache.TableName;

    /// <summary>
    /// The fully qualified table name (schema.table).
    /// </summary>
    public string FullTableName => Volatile.Read(ref _snapshot).Cache.FullTableName;

    /// <summary>
    /// The schema name (lowercase, validated PostgreSQL identifier).
    /// </summary>
    public string SchemaName => Volatile.Read(ref _snapshot).Cache.SchemaName;

    internal DbNomenclature(IOptionsMonitor<GlacialCachePostgreSQLOptions> options, ILogger<DbNomenclature> logger)
        : this(
            options,
            logger,
            new RuntimeConfigurationPublisher(
                options,
                new ObservableOptionsSynchronizer(),
                NullLogger<RuntimeConfigurationPublisher>.Instance),
            ownsRuntimeConfigurationPublisher: true)
    {
    }

    internal DbNomenclature(
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        ILogger<DbNomenclature> logger,
        IRuntimeConfigurationPublisher runtimeConfigurationPublisher)
        : this(options, logger, runtimeConfigurationPublisher, ownsRuntimeConfigurationPublisher: false)
    {
    }

    private DbNomenclature(
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        ILogger<DbNomenclature> logger,
        IRuntimeConfigurationPublisher runtimeConfigurationPublisher,
        bool ownsRuntimeConfigurationPublisher)
    {
        _options = options.CurrentValue;
        _observableOptions = _options;
        _logger = logger;
        _snapshotProvider = runtimeConfigurationPublisher;
        _ownedRuntimeConfigurationPublisher = ownsRuntimeConfigurationPublisher ? runtimeConfigurationPublisher : null;

        _snapshot = _snapshotProvider.Current;

        // Register for observable property changes to keep internal state synchronized
        _options.Cache.TableNameObservable.PropertyChanged += OnTableNameChanged;
        _options.Cache.SchemaNameObservable.PropertyChanged += OnSchemaNameChanged;

        _runtimeConfigurationSubscription = runtimeConfigurationPublisher.Subscribe(this);
    }

    public void OnRuntimeConfigurationChanged(RuntimeConfigurationChangedEventArgs change)
    {
        if (change.Options != null)
        {
            _options = change.Options;
        }

        if (!change.Changes.CacheNomenclatureChanged)
        {
            return;
        }

        Volatile.Write(ref _snapshot, change.Current);
        _logger.LogDebug("Runtime configuration changes synchronized to nomenclature");
    }

    private void OnTableNameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (RuntimeConfigurationReloadScope.IsActive)
        {
            return;
        }

        if (e is PropertyChangedEventArgs<string> typedArgs)
        {
            _logger.LogDebug("Cache table name changed from {OldValue} to {NewValue}", typedArgs.OldValue, typedArgs.NewValue);
            // Update internal properties to stay synchronized
            UpdateFromObservableProperties();
        }
    }

    private void OnSchemaNameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (RuntimeConfigurationReloadScope.IsActive)
        {
            return;
        }

        if (e is PropertyChangedEventArgs<string> typedArgs)
        {
            _logger.LogDebug("Cache schema name changed from {OldValue} to {NewValue}", typedArgs.OldValue, typedArgs.NewValue);
            // Update internal properties to stay synchronized
            UpdateFromObservableProperties();
        }
    }

    private void UpdateFromObservableProperties()
    {
        Volatile.Write(ref _snapshot, RuntimeConfigurationSnapshot.FromObservableOptions(_observableOptions));
    }

    public void Dispose()
    {
        // Unregister observable property change handlers to prevent memory leaks
        _observableOptions.Cache.TableNameObservable.PropertyChanged -= OnTableNameChanged;
        _observableOptions.Cache.SchemaNameObservable.PropertyChanged -= OnSchemaNameChanged;

        _runtimeConfigurationSubscription.Dispose();
        _ownedRuntimeConfigurationPublisher?.Dispose();
    }
}
