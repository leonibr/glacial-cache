using System.ComponentModel.DataAnnotations;
using GlacialCache.PostgreSQL.Configuration.Security;
using Microsoft.Extensions.Logging;

namespace GlacialCache.PostgreSQL.Configuration;

/// <summary>
/// Serializer types available for complex object serialization.
/// Strings always use UTF8 optimization regardless of this setting.
/// </summary>
public enum SerializerType
{
    /// <summary>
    /// MemoryPack serialization - fast binary serialization.
    /// </summary>
    MemoryPack,

    /// <summary>
    /// JSON serialization with UTF8 bytes - broader compatibility (default).
    /// </summary>
    JsonBytes,

    /// <summary>
    /// Consumer-provided custom serializer implementation.
    /// </summary>
    Custom
}

/// <summary>
/// Cache-specific configuration options.
/// </summary>
public class CacheOptions
{
    private ILogger? _logger;
    private string _tableName = "glacial_cache";
    private string _schemaName = "public";

    /// <summary>
    /// Sets the logger for observable properties and initializes them immediately.
    /// </summary>
    internal void SetLogger(ILogger? logger)
    {
        _logger = logger;

        // Initialize observable properties immediately with current values
        TableNameObservable = new ObservableProperty<string>("Cache.TableName", logger) { Value = TableName };
        SchemaNameObservable = new ObservableProperty<string>("Cache.SchemaName", logger) { Value = SchemaName };
    }

    /// <summary>
    /// The name of the table to store cache entries. Default is "glacial_cache".
    /// The value is validated and lowercased to ensure it's a safe PostgreSQL identifier.
    /// </summary>
    [Required(ErrorMessage = "Table name is required")]
    public string TableName
    {
        get => _tableName;
        set
        {
            // Validate and lowercase - this will throw if invalid
            _tableName = PostgreSQLIdentifierSanitizer.ValidateAndNormalize(value);
        }
    }

    /// <summary>
    /// The schema name where the cache table resides. Default is "public".
    /// The value is validated and lowercased to ensure it's a safe PostgreSQL identifier.
    /// </summary>
    [Required(ErrorMessage = "Schema name is required")]
    public string SchemaName
    {
        get => _schemaName;
        set
        {
            // Validate and lowercase - this will throw if invalid
            _schemaName = PostgreSQLIdentifierSanitizer.ValidateAndNormalize(value);
        }
    }

    /// <summary>
    /// The default sliding expiration time for cache entries. Default is null (no sliding expiration).
    /// </summary>
    public TimeSpan? DefaultSlidingExpiration { get; set; }

    /// <summary>
    /// The default absolute expiration time for cache entries. Default is null (no absolute expiration).
    /// </summary>
    public TimeSpan? DefaultAbsoluteExpirationRelativeToNow { get; set; }

    /// <summary>
    /// The minimum allowed expiration interval. Intervals shorter than this will be clamped to this value.
    /// Default is 1 millisecond.
    /// </summary>
    public TimeSpan MinimumExpirationInterval { get; set; } = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// The maximum allowed expiration interval. Intervals longer than this will be clamped to this value.
    /// Default is 1 year.
    /// </summary>
    public TimeSpan MaximumExpirationInterval { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Whether to log warnings for edge cases (very short/long intervals, past times).
    /// Default is true.
    /// </summary>
    public bool EnableEdgeCaseLogging { get; set; } = true;

    /// <summary>
    /// Serializer to use for complex objects. Strings always use UTF8 optimization.
    /// Default is JsonBytes for broader compatibility.
    /// </summary>
    public SerializerType Serializer { get; set; } = SerializerType.JsonBytes;

    /// <summary>
    /// Custom serializer implementation. Required when Serializer is set to Custom.
    /// Must implement ICacheEntrySerializer interface.
    /// </summary>
    public Type? CustomSerializerType { get; set; }

    // Observable Properties (Phase 2: Parallel Implementation)

    /// <summary>
    /// Observable version of TableName property for change notifications.
    /// </summary>
    public ObservableProperty<string> TableNameObservable { get; private set; } = new() { Value = "glacial_cache" };

    /// <summary>
    /// Observable version of SchemaName property for change notifications.
    /// </summary>
    public ObservableProperty<string> SchemaNameObservable { get; private set; } = new() { Value = "public" };
}