using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel;

namespace GlacialCache.PostgreSQL.Models;
using Configuration;
using Abstractions;
using Extensions;

internal sealed class DbNomenclature : IDbNomenclature, IDisposable
{
    private readonly ILogger<DbNomenclature> _logger;
    private readonly IOptionsMonitor<GlacialCachePostgreSQLOptions> _optionsMonitor;
    private readonly IDisposable? _optionsChangeToken;
    private GlacialCachePostgreSQLOptions _options;

    public string TableName { get; private set; } = string.Empty;
    public string FullTableName { get; private set; } = string.Empty;
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
        // Update from observable properties (the new values)
        TableName = _options.Cache.TableNameObservable.Value.ToLowerInvariant();
        SchemaName = _options.Cache.SchemaNameObservable.Value.ToLowerInvariant();
        FullTableName = $"{SchemaName}.{TableName}";
    }

    private void InitializeFromOptions(GlacialCachePostgreSQLOptions options)
    {
        // Initial setup without notifications
        UpdateProperties(options);
    }

    private void UpdateProperties(GlacialCachePostgreSQLOptions options)
    {
        TableName = options.Cache.TableName.ToLowerInvariant();
        SchemaName = options.Cache.SchemaName.ToLowerInvariant();
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