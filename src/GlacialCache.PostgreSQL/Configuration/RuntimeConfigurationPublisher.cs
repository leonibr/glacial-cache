using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GlacialCache.PostgreSQL.Extensions;

namespace GlacialCache.PostgreSQL.Configuration;

internal interface IRuntimeConfigurationPublisher : IDisposable
{
    IDisposable Subscribe(IRuntimeConfigurationSubscriber subscriber);
}

internal interface IRuntimeConfigurationSubscriber
{
    void OnRuntimeConfigurationChanged(GlacialCachePostgreSQLOptions options);
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
        _optionsChangeToken = optionsMonitor.OnChange(OnExternalConfigurationChanged);
    }

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

        foreach (var subscriber in subscribers)
        {
            subscriber.OnRuntimeConfigurationChanged(newOptions);
        }
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
