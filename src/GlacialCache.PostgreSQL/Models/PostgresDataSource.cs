using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using System.ComponentModel;

namespace GlacialCache.PostgreSQL.Models;

using Configuration;

internal interface IPostgreSQLDataSource : IDisposable
{
    // NpgsqlDataSource DataSource { get; }
    ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken token = default);
    ConnectionPoolMetrics GetPoolMetrics();
}

internal interface IPostgreSQLDataSourceHandle : IDisposable
{
    string ConnectionString { get; }
    ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken token = default);
}

/// <summary>
/// Connection pool metrics for monitoring and diagnostics.
/// </summary>
public record ConnectionPoolMetrics
{
    public int MinPoolSize { get; init; }
    public int MaxPoolSize { get; init; }
    public int IdleLifetime { get; init; }
    public int PruningInterval { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public bool PoolingEnabled { get; init; }
}

internal sealed record PostgreSQLDataSourceSettings(
    string ConnectionString,
    int MinPoolSize,
    int MaxPoolSize,
    int IdleLifetime,
    int PruningInterval,
    string ApplicationName,
    bool PoolingEnabled)
{
    public static PostgreSQLDataSourceSettings FromOptions(GlacialCachePostgreSQLOptions options)
    {
        return FromSnapshot(RuntimeConfigurationSnapshot.FromOptions(options).Connection);
    }

    public static PostgreSQLDataSourceSettings FromSnapshot(ConnectionRuntimeSnapshot snapshot)
    {
        var builder = new NpgsqlConnectionStringBuilder(snapshot.ConnectionString)
        {
            Pooling = true
        };

        builder.MinPoolSize = snapshot.MinPoolSize;
        builder.MaxPoolSize = snapshot.MaxPoolSize;
        builder.ConnectionIdleLifetime = snapshot.IdleLifetimeSeconds;
        builder.ConnectionPruningInterval = snapshot.PruningIntervalSeconds;
        builder.ApplicationName = string.IsNullOrEmpty(builder.ApplicationName) ? "GlacialCache" : builder.ApplicationName;

        return new PostgreSQLDataSourceSettings(
            builder.ConnectionString,
            builder.MinPoolSize,
            builder.MaxPoolSize,
            builder.ConnectionIdleLifetime,
            builder.ConnectionPruningInterval,
            builder.ApplicationName ?? string.Empty,
            builder.Pooling);
    }
}

internal sealed class NpgsqlDataSourceHandle : IPostgreSQLDataSourceHandle
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlDataSourceHandle(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public string ConnectionString => _dataSource.ConnectionString;

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken token = default) =>
        _dataSource.OpenConnectionAsync(token);

    public void Dispose() => _dataSource.Dispose();
}

internal sealed class PostgreSQLDataSource : IPostgreSQLDataSource, IRuntimeConfigurationSubscriber
{
    private readonly ILogger<PostgreSQLDataSource> _logger;
    private readonly GlacialCachePostgreSQLOptions _observableOptions;
    private readonly IDisposable _runtimeConfigurationSubscription;
    private readonly IRuntimeConfigurationPublisher? _ownedRuntimeConfigurationPublisher;
    private readonly IRuntimeConfigurationSnapshotProvider _snapshotProvider;
    private readonly Func<PostgreSQLDataSourceSettings, IPostgreSQLDataSourceHandle> _dataSourceFactory;
    private readonly object _syncRoot = new();
    private GlacialCachePostgreSQLOptions _options;
    private PostgreSQLDataSourceSettings _settings;
    private ActiveDataSource? _dataSource;
    private bool _disposed;

    public PostgreSQLDataSource(
        ILogger<PostgreSQLDataSource> logger,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options)
        : this(
            logger,
            options,
            new RuntimeConfigurationPublisher(
                options,
                new ObservableOptionsSynchronizer(),
                NullLogger<RuntimeConfigurationPublisher>.Instance),
            CreateDataSource,
            ownsRuntimeConfigurationPublisher: true)
    {
    }

    internal PostgreSQLDataSource(
        ILogger<PostgreSQLDataSource> logger,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        Func<PostgreSQLDataSourceSettings, IPostgreSQLDataSourceHandle> dataSourceFactory)
        : this(
            logger,
            options,
            new RuntimeConfigurationPublisher(
                options,
                new ObservableOptionsSynchronizer(),
                NullLogger<RuntimeConfigurationPublisher>.Instance),
            dataSourceFactory,
            ownsRuntimeConfigurationPublisher: true)
    {
    }

    internal PostgreSQLDataSource(
        ILogger<PostgreSQLDataSource> logger,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        IRuntimeConfigurationPublisher runtimeConfigurationPublisher)
        : this(logger, options, runtimeConfigurationPublisher, CreateDataSource, ownsRuntimeConfigurationPublisher: false)
    {
    }

    internal PostgreSQLDataSource(
        ILogger<PostgreSQLDataSource> logger,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        IRuntimeConfigurationPublisher runtimeConfigurationPublisher,
        Func<PostgreSQLDataSourceSettings, IPostgreSQLDataSourceHandle> dataSourceFactory)
        : this(logger, options, runtimeConfigurationPublisher, dataSourceFactory, ownsRuntimeConfigurationPublisher: false)
    {
    }

    private PostgreSQLDataSource(
        ILogger<PostgreSQLDataSource> logger,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        IRuntimeConfigurationPublisher runtimeConfigurationPublisher,
        Func<PostgreSQLDataSourceSettings, IPostgreSQLDataSourceHandle> dataSourceFactory,
        bool ownsRuntimeConfigurationPublisher)
    {
        _logger = logger;
        _options = options.CurrentValue;
        _observableOptions = _options;
        _snapshotProvider = runtimeConfigurationPublisher;
        _dataSourceFactory = dataSourceFactory;
        _ownedRuntimeConfigurationPublisher = ownsRuntimeConfigurationPublisher ? runtimeConfigurationPublisher : null;

        _options.Connection.SetLogger(_logger);
        _settings = PostgreSQLDataSourceSettings.FromSnapshot(_snapshotProvider.Current.Connection);
        _dataSource = new ActiveDataSource(_dataSourceFactory(_settings));
        LogDataSourceConfigured(_settings);

        // Register for observable property changes to keep data source synchronized
        _options.Connection.ConnectionStringObservable.PropertyChanged += OnConnectionStringChanged;
        _options.Connection.Pool.MinSizeObservable.PropertyChanged += OnPoolPropertyChanged;
        _options.Connection.Pool.MaxSizeObservable.PropertyChanged += OnPoolPropertyChanged;
        _options.Connection.Pool.IdleLifetimeSecondsObservable.PropertyChanged += OnPoolPropertyChanged;
        _options.Connection.Pool.PruningIntervalSecondsObservable.PropertyChanged += OnPoolPropertyChanged;

        _runtimeConfigurationSubscription = runtimeConfigurationPublisher.Subscribe(this);
    }

    private static IPostgreSQLDataSourceHandle CreateDataSource(PostgreSQLDataSourceSettings settings)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        return new NpgsqlDataSourceHandle(dataSourceBuilder.Build());
    }

    public void OnRuntimeConfigurationChanged(RuntimeConfigurationChangedEventArgs change)
    {
        if (change.Options != null)
        {
            _options = change.Options;
        }

        if (!change.Changes.ConnectionStringChanged && !change.Changes.ConnectionPoolChanged)
        {
            return;
        }

        TryUpdateDataSource(change.Current.Connection);
        _logger.LogDebug("Runtime configuration changes synchronized to PostgreSQL data source");
    }

    private string MaskConnectionString(string connectionString, Configuration.Security.ConnectionStringOptions securityOptions)
    {
        if (!securityOptions.MaskInLogs)
        {
            return connectionString;
        }

        try
        {
            // Parse connection string manually to handle custom parameters
            var parts = connectionString.Split(';');
            var maskedParts = new List<string>();

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var keyValue = part.Split('=', 2);
                if (keyValue.Length == 2)
                {
                    var key = keyValue[0].Trim();
                    var value = keyValue[1].Trim();

                    // Check if this parameter should be masked (case-insensitive)
                    var shouldMask = securityOptions.SensitiveParameters.Any(
                        sensitive => string.Equals(sensitive, key, StringComparison.OrdinalIgnoreCase));

                    if (shouldMask)
                    {
                        maskedParts.Add($"{key}=***");
                    }
                    else
                    {
                        maskedParts.Add(part);
                    }
                }
                else
                {
                    // If not a key=value pair, keep as is
                    maskedParts.Add(part);
                }
            }

            return string.Join(';', maskedParts);
        }
        catch (Exception ex)
        {
            // If parsing fails, return masked placeholder to avoid exposing credentials
            _logger.LogWarning(ex, "Failed to parse connection string for masking");
            return "[Connection string parsing failed - masked for security]";
        }
    }

    private void OnConnectionStringChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (RuntimeConfigurationReloadScope.IsActive)
        {
            return;
        }

        if (e is PropertyChangedEventArgs<string> typedArgs)
        {
            var maskedOldValue = MaskConnectionString(typedArgs.OldValue, _options.Security.ConnectionString);
            var maskedNewValue = MaskConnectionString(typedArgs.NewValue, _options.Security.ConnectionString);

            _logger.LogDebug("Connection string changed from {OldValue} to {NewValue}", maskedOldValue, maskedNewValue);
            TryUpdateDataSourceFromObservableOptions();
        }
    }

    private void OnPoolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (RuntimeConfigurationReloadScope.IsActive)
        {
            return;
        }

        _logger.LogDebug("Connection pool property changed: {PropertyName}", e.PropertyName);
        TryUpdateDataSourceFromObservableOptions();
    }

    private bool TryUpdateDataSourceFromObservableOptions()
    {
        try
        {
            return TryUpdateDataSource(RuntimeConfigurationSnapshot.FromObservableOptions(_observableOptions).Connection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create PostgreSQL data source settings from configuration");
            return false;
        }
    }

    private bool TryUpdateDataSource(ConnectionRuntimeSnapshot snapshot)
    {
        PostgreSQLDataSourceSettings candidateSettings;
        try
        {
            candidateSettings = PostgreSQLDataSourceSettings.FromSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create PostgreSQL data source settings from configuration");
            return false;
        }

        return TryUpdateDataSource(candidateSettings);
    }

    private bool TryUpdateDataSource(PostgreSQLDataSourceSettings candidateSettings)
    {
        PostgreSQLDataSourceSettings previousSettings;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return false;
            }

            previousSettings = _settings;
            if (candidateSettings == previousSettings)
            {
                return false;
            }
        }

        IPostgreSQLDataSourceHandle replacementDataSource;
        try
        {
            replacementDataSource = _dataSourceFactory(candidateSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build replacement PostgreSQL data source");
            return false;
        }

        ActiveDataSource replacementState = new(replacementDataSource);
        IPostgreSQLDataSourceHandle? dataSourceToDispose;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                replacementDataSource.Dispose();
                return false;
            }

            if (_settings != previousSettings)
            {
                replacementDataSource.Dispose();
                return false;
            }

            var replacedDataSource = _dataSource;
            _dataSource = replacementState;
            _settings = candidateSettings;
            dataSourceToDispose = RetireDataSource(replacedDataSource);
        }

        dataSourceToDispose?.Dispose();
        LogDataSourceConfigured(candidateSettings);

        return true;
    }

    private void LogDataSourceConfigured(PostgreSQLDataSourceSettings settings)
    {
        _logger.LogInformation(
            "PostgreSQL connection pool configured: MinSize={MinPoolSize}, MaxSize={MaxPoolSize}, IdleLifetime={IdleLifetime}s, PruningInterval={PruningInterval}s",
            settings.MinPoolSize,
            settings.MaxPoolSize,
            settings.IdleLifetime,
            settings.PruningInterval);
    }

    public async ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken token = default)
    {
        using var lease = TryAcquireDataSourceLease();

        if (lease == null)
        {
            throw new InvalidOperationException("DataSource has not been initialized.");
        }

        return await lease.DataSource.OpenConnectionAsync(token).ConfigureAwait(false);
    }

    public ConnectionPoolMetrics GetPoolMetrics()
    {
        using var lease = TryAcquireDataSourceLease();

        if (lease == null)
        {
            return new ConnectionPoolMetrics
            {
                MinPoolSize = 0,
                MaxPoolSize = 0,
                IdleLifetime = 0,
                PruningInterval = 0,
                ApplicationName = string.Empty,
                PoolingEnabled = false
            };
        }

        var connectionString = lease.DataSource.ConnectionString;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        return new ConnectionPoolMetrics
        {
            MinPoolSize = builder.MinPoolSize,
            MaxPoolSize = builder.MaxPoolSize,
            IdleLifetime = builder.ConnectionIdleLifetime,
            PruningInterval = builder.ConnectionPruningInterval,
            ApplicationName = builder.ApplicationName ?? string.Empty,
            PoolingEnabled = builder.Pooling
        };
    }

    public void Dispose()
    {
        IPostgreSQLDataSourceHandle? dataSourceToDispose;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var dataSource = _dataSource;
            _dataSource = null;
            dataSourceToDispose = RetireDataSource(dataSource);
        }

        // Unregister observable property change handlers to prevent memory leaks
        _observableOptions.Connection.ConnectionStringObservable.PropertyChanged -= OnConnectionStringChanged;
        _observableOptions.Connection.Pool.MinSizeObservable.PropertyChanged -= OnPoolPropertyChanged;
        _observableOptions.Connection.Pool.MaxSizeObservable.PropertyChanged -= OnPoolPropertyChanged;
        _observableOptions.Connection.Pool.IdleLifetimeSecondsObservable.PropertyChanged -= OnPoolPropertyChanged;
        _observableOptions.Connection.Pool.PruningIntervalSecondsObservable.PropertyChanged -= OnPoolPropertyChanged;

        _runtimeConfigurationSubscription.Dispose();
        _ownedRuntimeConfigurationPublisher?.Dispose();

        dataSourceToDispose?.Dispose();
    }

    private DataSourceLease? TryAcquireDataSourceLease()
    {
        lock (_syncRoot)
        {
            if (_dataSource == null)
            {
                return null;
            }

            _dataSource.LeaseCount++;
            return new DataSourceLease(this, _dataSource);
        }
    }

    private static IPostgreSQLDataSourceHandle? RetireDataSource(ActiveDataSource? dataSource)
    {
        if (dataSource == null)
        {
            return null;
        }

        dataSource.Retired = true;
        return dataSource.LeaseCount == 0 ? dataSource.Handle : null;
    }

    private void ReleaseDataSourceLease(ActiveDataSource dataSource)
    {
        IPostgreSQLDataSourceHandle? dataSourceToDispose = null;
        lock (_syncRoot)
        {
            dataSource.LeaseCount--;
            if (dataSource.LeaseCount == 0 && dataSource.Retired)
            {
                dataSourceToDispose = dataSource.Handle;
            }
        }

        dataSourceToDispose?.Dispose();
    }

    private sealed class ActiveDataSource
    {
        public ActiveDataSource(IPostgreSQLDataSourceHandle handle)
        {
            Handle = handle;
        }

        public IPostgreSQLDataSourceHandle Handle { get; }
        public int LeaseCount { get; set; }
        public bool Retired { get; set; }
    }

    private sealed class DataSourceLease : IDisposable
    {
        private readonly PostgreSQLDataSource _owner;
        private readonly ActiveDataSource _dataSource;
        private bool _disposed;

        public DataSourceLease(PostgreSQLDataSource owner, ActiveDataSource dataSource)
        {
            _owner = owner;
            _dataSource = dataSource;
        }

        public IPostgreSQLDataSourceHandle DataSource => _dataSource.Handle;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.ReleaseDataSourceLease(_dataSource);
        }
    }
}
