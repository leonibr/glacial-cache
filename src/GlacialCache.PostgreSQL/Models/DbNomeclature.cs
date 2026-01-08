using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Models;
using Abstractions;
using Configuration;
using Extensions;

internal sealed class DbNomenclature : IDbNomenclature, IDisposable
{
    private readonly ILogger<DbNomenclature> _logger;
    private readonly IOptionsMonitor<GlacialCachePostgreSQLOptions> _optionsMonitor;
    private readonly IDisposable? _optionsChangeToken;
    private GlacialCachePostgreSQLOptions _options;

    /// <summary>
    /// The table name (lowercase, validated PostgreSQL identifier).
    /// </summary>
    public string TableName { get; private set; } = string.Empty;

    /// <summary>
    /// The fully qualified table name (schema.table).
    /// </summary>
    public string FullTableName { get; private set; } = string.Empty;

    /// <summary>
    /// The schema name (lowercase, validated PostgreSQL identifier).
    /// </summary>
    public string SchemaName { get; private set; } = string.Empty;

    internal DbNomenclature(IOptionsMonitor<GlacialCachePostgreSQLOptions> options, ILogger<DbNomenclature> logger)
    {
        _optionsMonitor = options;
        _options = options.CurrentValue;
        _logger = logger;

        // Initialize from current values
        InitializeFromOptions(_options);

        // Register for observable property changes to keep internal state synchronized
        _options.Cache.TableNameObservable.PropertyChanged += OnTableNameChanged;
        _options.Cache.SchemaNameObservable.PropertyChanged += OnSchemaNameChanged;

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
            // Use extension method to sync observable properties
            _options.Cache.SyncFromExternalChanges(newOptions.Cache, _logger);

            // Update our internal reference
            _options = newOptions;

            _logger.LogDebug("External configuration changes synchronized to observable properties");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync external configuration changes");
        }
    }

    private void OnTableNameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is PropertyChangedEventArgs<string> typedArgs)
        {
            _logger.LogDebug("Cache table name changed from {OldValue} to {NewValue}", typedArgs.OldValue, typedArgs.NewValue);
            // Update internal properties to stay synchronized
            UpdateFromObservableProperties();
        }
    }

    private void OnSchemaNameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is PropertyChangedEventArgs<string> typedArgs)
        {
            _logger.LogDebug("Cache schema name changed from {OldValue} to {NewValue}", typedArgs.OldValue, typedArgs.NewValue);
            // Update internal properties to stay synchronized
            UpdateFromObservableProperties();
        }
    }

    private void UpdateFromObservableProperties()
    {
        // CacheOptions already validates and lowercases the values
        TableName = _options.Cache.TableNameObservable.Value;
        SchemaName = _options.Cache.SchemaNameObservable.Value;
        FullTableName = $"{SchemaName}.{TableName}";
    }

    private void InitializeFromOptions(GlacialCachePostgreSQLOptions options)
    {
        // Initial setup without notifications
        UpdateProperties(options);
    }

    private void UpdateProperties(GlacialCachePostgreSQLOptions options)
    {
        // CacheOptions already validates and lowercases the values
        TableName = options.Cache.TableName;
        SchemaName = options.Cache.SchemaName;
        FullTableName = $"{SchemaName}.{TableName}";
    }

    public void Dispose()
    {
        // Unregister observable property change handlers to prevent memory leaks
        _options.Cache.TableNameObservable.PropertyChanged -= OnTableNameChanged;
        _options.Cache.SchemaNameObservable.PropertyChanged -= OnSchemaNameChanged;

        // Dispose the options change token to prevent memory leaks
        _optionsChangeToken?.Dispose();
    }
}
