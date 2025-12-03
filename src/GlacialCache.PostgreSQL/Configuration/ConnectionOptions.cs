using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace GlacialCache.PostgreSQL.Configuration;

/// <summary>
/// Connection and database configuration options.
/// </summary>
public class ConnectionOptions
{
    private ILogger? _logger;

    /// <summary>
    /// Sets the logger for observable properties and initializes them immediately.
    /// </summary>
    internal void SetLogger(ILogger? logger)
    {
        _logger = logger;

        // Initialize observable properties immediately with current values
        ConnectionStringObservable = new ObservableProperty<string>("Connection.ConnectionString", logger) { Value = ConnectionString };
        Pool.SetLogger(logger);
    }
    /// <summary>
    /// The connection string to the PostgreSQL database.
    /// </summary>
    [Required(ErrorMessage = "Connection string is required")]
    [MinLength(10, ErrorMessage = "Connection string must be at least 10 characters")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Connection pool configuration.
    /// </summary>
    public ConnectionPoolOptions Pool { get; set; } = new();

    /// <summary>
    /// Timeout configuration for database operations.
    /// </summary>
    public TimeoutOptions Timeouts { get; set; } = new();

    // Observable Properties (Phase 2: Parallel Implementation)

    /// <summary>
    /// Observable version of ConnectionString property for change notifications.
    /// </summary>
    public ObservableProperty<string> ConnectionStringObservable { get; private set; } = new() { Value = string.Empty };
}

/// <summary>
/// Connection pool configuration options.
/// </summary>
public class ConnectionPoolOptions
{
    private ILogger? _logger;

    /// <summary>
    /// Sets the logger for observable properties and initializes them immediately.
    /// </summary>
    internal void SetLogger(ILogger? logger)
    {
        _logger = logger;

        // Initialize observable properties immediately with current values
        MinSizeObservable = new ObservableProperty<int>("Connection.Pool.MinSize", logger) { Value = MinSize };
        MaxSizeObservable = new ObservableProperty<int>("Connection.Pool.MaxSize", logger) { Value = MaxSize };
        IdleLifetimeSecondsObservable = new ObservableProperty<int>("Connection.Pool.IdleLifetimeSeconds", logger) { Value = IdleLifetimeSeconds };
        PruningIntervalSecondsObservable = new ObservableProperty<int>("Connection.Pool.PruningIntervalSeconds", logger) { Value = PruningIntervalSeconds };
    }
    /// <summary>
    /// The maximum number of connections in the connection pool. Default is 50.
    /// </summary>
    [Range(1, 1000, ErrorMessage = "Max connection pool size must be between 1 and 1000")]
    public int MaxSize { get; set; } = 50;

    /// <summary>
    /// The minimum number of connections in the connection pool. Default is 5.
    /// </summary>
    [Range(1, 100, ErrorMessage = "Min connection pool size must be between 1 and 100")]
    public int MinSize { get; set; } = 5;

    /// <summary>
    /// The lifetime of idle connections in seconds. Default is 300 seconds (5 minutes).
    /// </summary>
    public int IdleLifetimeSeconds { get; set; } = 300;

    /// <summary>
    /// The interval for pruning idle connections in seconds. Default is 10 seconds.
    /// </summary>
    public int PruningIntervalSeconds { get; set; } = 10;

    // Observable Properties (Phase 2: Parallel Implementation)

    /// <summary>
    /// Observable version of MinSize property for change notifications.
    /// </summary>
    public ObservableProperty<int> MinSizeObservable { get; private set; } = new() { Value = 5 };

    /// <summary>
    /// Observable version of MaxSize property for change notifications.
    /// </summary>
    public ObservableProperty<int> MaxSizeObservable { get; private set; } = new() { Value = 50 };

    /// <summary>
    /// Observable version of IdleLifetimeSeconds property for change notifications.
    /// </summary>
    public ObservableProperty<int> IdleLifetimeSecondsObservable { get; private set; } = new() { Value = 300 };

    /// <summary>
    /// Observable version of PruningIntervalSeconds property for change notifications.
    /// </summary>
    public ObservableProperty<int> PruningIntervalSecondsObservable { get; private set; } = new() { Value = 10 };
}

/// <summary>
/// Timeout configuration options.
/// </summary>
public class TimeoutOptions
{
    /// <summary>
    /// The timeout for individual database operations. Default is 30 seconds.
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The timeout for connection acquisition. Default is 30 seconds.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The timeout for command execution. Default is 30 seconds.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}