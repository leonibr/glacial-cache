using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.ComponentModel;

namespace GlacialCache.PostgreSQL.Models;

using Configuration;
using Extensions;

internal interface IPostgreSQLDataSource : IDisposable
{
    // NpgsqlDataSource DataSource { get; }
    ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken token = default);
    ConnectionPoolMetrics GetPoolMetrics();
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

internal sealed class PostgreSQLDataSource : IPostgreSQLDataSource
{
    private readonly ILogger<PostgreSQLDataSource> _logger;
    private readonly IOptionsMonitor<GlacialCachePostgreSQLOptions> _optionsMonitor;
    private readonly IDisposable? _optionsChangeToken;
    private GlacialCachePostgreSQLOptions _options;
    private string _connectionString;
    private NpgsqlDataSource? _dataSource;
    private bool _disposed;

    public PostgreSQLDataSource(
        ILogger<PostgreSQLDataSource> logger,
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options)
    {
        _logger = logger;
        _optionsMonitor = options;
        _options = options.CurrentValue;

        _connectionString = options.CurrentValue.Connection.ConnectionString;
        _dataSource = new NpgsqlDataSourceBuilder(_connectionString).Build();

        InitializeFromOptions(options.CurrentValue);

        // Register for observable property changes to keep data source synchronized
        _options.Connection.ConnectionStringObservable.PropertyChanged += OnConnectionStringChanged;
        _options.Connection.Pool.MinSizeObservable.PropertyChanged += OnPoolPropertyChanged;
        _options.Connection.Pool.MaxSizeObservable.PropertyChanged += OnPoolPropertyChanged;
        _options.Connection.Pool.IdleLifetimeSecondsObservable.PropertyChanged += OnPoolPropertyChanged;
        _options.Connection.Pool.PruningIntervalSecondsObservable.PropertyChanged += OnPoolPropertyChanged;

        // Register for external configuration changes (IOptionsMonitor pattern)
        _optionsChangeToken = _optionsMonitor.OnChange(OnExternalConfigurationChanged);
    }

    /// <summary>
    /// Handles external configuration changes from IOptionsMonitor and syncs to observable properties.
    /// </summary>
    private void OnExternalConfigurationChanged(GlacialCachePostgreSQLOptions newOptions)
    {
        try
        {
            // Use extension method to sync observable properties for Connection and Pool
            _options.Connection.SyncFromExternalChanges(newOptions.Connection, _logger);
            _options.Connection.Pool.SyncFromExternalChanges(newOptions.Connection.Pool, _logger);

            // Update our internal reference
            _options = newOptions;

            _logger.LogDebug("External configuration changes synchronized to observable properties");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync external configuration changes");
        }
    }

    private void OnConnectionStringChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is PropertyChangedEventArgs<string> typedArgs)
        {
            _logger.LogDebug("Connection string changed from {OldValue} to {NewValue}", typedArgs.OldValue, typedArgs.NewValue);
            ReloadFromConnectionString(typedArgs.NewValue);
        }
    }

    private void OnPoolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _logger.LogDebug("Connection pool property changed: {PropertyName}", e.PropertyName);
        ReloadFromOptions(_options);
    }

    private void InitializeFromOptions(GlacialCachePostgreSQLOptions options)
    {
        UpdateDataSource(options);
    }

    private void ReloadFromConnectionString(string newConnectionString)
    {
        if (!_connectionString.Equals(newConnectionString))
        {
            UpdateDataSource(_options);
        }
    }

    private void ReloadFromOptions(GlacialCachePostgreSQLOptions options)
    {
        var hasChanged = DetectChanges(options);

        if (hasChanged)
        {
            UpdateDataSource(options);
        }
    }

    private bool DetectChanges(GlacialCachePostgreSQLOptions options)
    {
        return !_connectionString.Equals(options.Connection.ConnectionString);
    }

    private void UpdateDataSource(GlacialCachePostgreSQLOptions options)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.Connection.ConnectionString)
        {
            ConnectionStringBuilder =
            {
                Pooling = true
            }
        };

        // Use provided options or fall back to defaults
        var minPoolSize = options?.Connection.Pool.MinSize ?? 5;
        var maxPoolSize = options?.Connection.Pool.MaxSize ?? 50;
        var idleLifetime = options?.Connection.Pool.IdleLifetimeSeconds ?? 300;
        var pruningInterval = options?.Connection.Pool.PruningIntervalSeconds ?? 10;

        // Apply pool size settings (only if not already set in connection string)
        dataSourceBuilder.ConnectionStringBuilder.MinPoolSize =
            dataSourceBuilder.ConnectionStringBuilder.MinPoolSize != 0 ? dataSourceBuilder.ConnectionStringBuilder.MinPoolSize : minPoolSize;
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize =
            dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize != 100 ? dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize : maxPoolSize;

        // Apply connection lifetime settings
        dataSourceBuilder.ConnectionStringBuilder.ConnectionIdleLifetime = idleLifetime;
        dataSourceBuilder.ConnectionStringBuilder.ConnectionPruningInterval = pruningInterval;

        // Set application name for monitoring
        dataSourceBuilder.ConnectionStringBuilder.ApplicationName =
            string.IsNullOrEmpty(dataSourceBuilder.ConnectionStringBuilder.ApplicationName) ? "GlacialCache" : dataSourceBuilder.ConnectionStringBuilder.ApplicationName;

        _dataSource = dataSourceBuilder.Build();
        _connectionString = options!.Connection.ConnectionString;

        _logger.LogInformation(
            "PostgreSQL connection pool configured: MinSize={MinPoolSize}, MaxSize={MaxPoolSize}, IdleLifetime={IdleLifetime}s, PruningInterval={PruningInterval}s",
            dataSourceBuilder.ConnectionStringBuilder.MinPoolSize,
            dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize,
            idleLifetime,
            pruningInterval);
    }

    public async ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken token = default)
    {
        if (_dataSource == null)
        {
            throw new InvalidOperationException("DataSource has not been initialized.");
        }
        return await _dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
    }

    public ConnectionPoolMetrics GetPoolMetrics()
    {
        if (_dataSource is not NpgsqlDataSource dataSource)
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

        var connectionString = dataSource.ConnectionString;
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
        if (!_disposed)
        {
            _disposed = true;

            // Unregister observable property change handlers to prevent memory leaks
            _options.Connection.ConnectionStringObservable.PropertyChanged -= OnConnectionStringChanged;
            _options.Connection.Pool.MinSizeObservable.PropertyChanged -= OnPoolPropertyChanged;
            _options.Connection.Pool.MaxSizeObservable.PropertyChanged -= OnPoolPropertyChanged;
            _options.Connection.Pool.IdleLifetimeSecondsObservable.PropertyChanged -= OnPoolPropertyChanged;
            _options.Connection.Pool.PruningIntervalSecondsObservable.PropertyChanged -= OnPoolPropertyChanged;

            // Dispose the options change token to prevent memory leaks
            _optionsChangeToken?.Dispose();

            _dataSource?.Dispose();
        }
    }
}