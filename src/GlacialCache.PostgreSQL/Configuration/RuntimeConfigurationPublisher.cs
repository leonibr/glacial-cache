using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Services;
using System.ComponentModel.DataAnnotations;

namespace GlacialCache.PostgreSQL.Configuration;

internal interface IRuntimeConfigurationPublisher : IRuntimeConfigurationSnapshotProvider, IDisposable
{
    IDisposable Subscribe(IRuntimeConfigurationSubscriber subscriber);
}

internal interface IRuntimeConfigurationSubscriber
{
    void OnRuntimeConfigurationChanged(RuntimeConfigurationChangedEventArgs change)
    {
        if (change.Options != null)
        {
            OnRuntimeConfigurationChanged(change.Options);
        }
    }

    void OnRuntimeConfigurationChanged(GlacialCachePostgreSQLOptions options)
    {
    }
}

internal sealed record RuntimeConfigurationChangedEventArgs(
    RuntimeConfigurationSnapshot Previous,
    RuntimeConfigurationSnapshot Current,
    RuntimeConfigurationChangeSet Changes)
{
    internal GlacialCachePostgreSQLOptions? Options { get; init; }

    public static RuntimeConfigurationChangedEventArgs Create(
        RuntimeConfigurationSnapshot previous,
        RuntimeConfigurationSnapshot current,
        GlacialCachePostgreSQLOptions? options = null)
    {
        return new RuntimeConfigurationChangedEventArgs(
            previous,
            current,
            RuntimeConfigurationChangeSet.FromSnapshots(previous, current))
        {
            Options = options
        };
    }
}

internal sealed record RuntimeConfigurationChangeSet(
    bool CacheSnapshotChanged,
    bool CacheNomenclatureChanged,
    bool ConnectionStringChanged,
    bool ConnectionPoolChanged)
{
    public bool HasChanges =>
        CacheSnapshotChanged ||
        CacheNomenclatureChanged ||
        ConnectionStringChanged ||
        ConnectionPoolChanged;

    public static RuntimeConfigurationChangeSet FromSnapshots(
        RuntimeConfigurationSnapshot previous,
        RuntimeConfigurationSnapshot current)
    {
        var cacheSnapshotChanged = previous.Cache != current.Cache;
        var cacheNomenclatureChanged =
            !StringComparer.Ordinal.Equals(previous.Cache.SchemaName, current.Cache.SchemaName) ||
            !StringComparer.Ordinal.Equals(previous.Cache.TableName, current.Cache.TableName);
        var connectionStringChanged =
            !StringComparer.Ordinal.Equals(previous.Connection.ConnectionString, current.Connection.ConnectionString);
        var connectionPoolChanged =
            previous.Connection.MinPoolSize != current.Connection.MinPoolSize ||
            previous.Connection.MaxPoolSize != current.Connection.MaxPoolSize ||
            previous.Connection.IdleLifetimeSeconds != current.Connection.IdleLifetimeSeconds ||
            previous.Connection.PruningIntervalSeconds != current.Connection.PruningIntervalSeconds;

        return new RuntimeConfigurationChangeSet(
            cacheSnapshotChanged,
            cacheNomenclatureChanged,
            connectionStringChanged,
            connectionPoolChanged);
    }
}

internal interface IObservableOptionsSynchronizer
{
    void Sync(GlacialCachePostgreSQLOptions currentOptions, GlacialCachePostgreSQLOptions newOptions, ILogger logger);
}

internal sealed class RuntimeConfigurationPublisher : IRuntimeConfigurationPublisher
{
    private readonly object _syncRoot = new();
    private readonly GlacialCachePostgreSQLOptions _currentOptions;
    private readonly IObservableOptionsSynchronizer _synchronizer;
    private readonly ILogger<RuntimeConfigurationPublisher> _logger;
    private readonly IDisposable? _optionsChangeToken;
    private readonly List<IRuntimeConfigurationSubscriber> _subscribers = [];
    private RuntimeConfigurationSnapshot _currentSnapshot;
    private bool _disposed;

    internal RuntimeConfigurationPublisher(IOptionsMonitor<GlacialCachePostgreSQLOptions> optionsMonitor)
        : this(
            optionsMonitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance)
    {
    }

    internal RuntimeConfigurationPublisher(
        IOptionsMonitor<GlacialCachePostgreSQLOptions> optionsMonitor,
        IObservableOptionsSynchronizer synchronizer,
        ILogger<RuntimeConfigurationPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(synchronizer);
        ArgumentNullException.ThrowIfNull(logger);

        _currentOptions = optionsMonitor.CurrentValue;
        _synchronizer = synchronizer;
        _logger = logger;
        _currentSnapshot = RuntimeConfigurationSnapshot.FromOptions(_currentOptions);
        _optionsChangeToken = optionsMonitor.OnChange(OnExternalConfigurationChanged);
    }

    public RuntimeConfigurationSnapshot Current => Volatile.Read(ref _currentSnapshot);

    public IDisposable Subscribe(IRuntimeConfigurationSubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscribers.Add(subscriber);
        }

        return new Subscription(this, subscriber);
    }

    private void OnExternalConfigurationChanged(GlacialCachePostgreSQLOptions newOptions)
    {
        var validationResults = ConfigurationValidator.ValidateOptionsNonThrowing(newOptions).ToArray();
        if (validationResults.Length > 0)
        {
            LogRejectedReload(validationResults);
            return;
        }

        var previousSnapshot = Current;
        var snapshot = RuntimeConfigurationSnapshot.FromOptions(newOptions);
        var change = RuntimeConfigurationChangedEventArgs.Create(previousSnapshot, snapshot, newOptions);
        if (!change.Changes.HasChanges)
        {
            return;
        }

        IRuntimeConfigurationSubscriber[] subscribers;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            subscribers = _subscribers.ToArray();
        }

        using (RuntimeConfigurationReloadScope.Enter())
        {
            _synchronizer.Sync(_currentOptions, newOptions, _logger);
        }

        Interlocked.Exchange(ref _currentSnapshot, snapshot);

        foreach (var subscriber in subscribers)
        {
            subscriber.OnRuntimeConfigurationChanged(change);
        }
    }

    private void LogRejectedReload(IReadOnlyCollection<ValidationResult> validationResults)
    {
        var errors = string.Join("; ", validationResults.Select(FormatValidationResult));
        _logger.LogWarning("Runtime configuration reload rejected: {ValidationErrors}", errors);
    }

    private static string FormatValidationResult(ValidationResult validationResult)
    {
        var members = validationResult.MemberNames.Any()
            ? string.Join(", ", validationResult.MemberNames)
            : "Configuration";

        return $"{members}: {validationResult.ErrorMessage}";
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscribers.Clear();
        }

        _optionsChangeToken?.Dispose();
    }

    private void Unsubscribe(IRuntimeConfigurationSubscriber subscriber)
    {
        lock (_syncRoot)
        {
            _subscribers.Remove(subscriber);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly RuntimeConfigurationPublisher _publisher;
        private IRuntimeConfigurationSubscriber? _subscriber;

        public Subscription(RuntimeConfigurationPublisher publisher, IRuntimeConfigurationSubscriber subscriber)
        {
            _publisher = publisher;
            _subscriber = subscriber;
        }

        public void Dispose()
        {
            var subscriber = Interlocked.Exchange(ref _subscriber, null);
            if (subscriber != null)
            {
                _publisher.Unsubscribe(subscriber);
            }
        }
    }
}

internal sealed class ObservableOptionsSynchronizer : IObservableOptionsSynchronizer
{
    public void Sync(GlacialCachePostgreSQLOptions currentOptions, GlacialCachePostgreSQLOptions newOptions, ILogger logger)
    {
        newOptions.Cache.SetLogger(logger);
        newOptions.Connection.SetLogger(logger);

        currentOptions.Cache.SyncFromExternalChangesOrThrow(newOptions.Cache, logger);
        currentOptions.Connection.SyncFromExternalChangesOrThrow(newOptions.Connection, logger);
        currentOptions.Connection.Pool.SyncFromExternalChangesOrThrow(newOptions.Connection.Pool, logger);
    }
}

internal static class RuntimeConfigurationReloadScope
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool IsActive => Depth.Value > 0;

    public static IDisposable Enter()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Depth.Value--;
        }
    }
}
