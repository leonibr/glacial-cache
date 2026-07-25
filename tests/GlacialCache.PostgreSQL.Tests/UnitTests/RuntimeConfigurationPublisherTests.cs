using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class RuntimeConfigurationPublisherTests
{
    [Fact]
    public void Constructor_RegistersSingleOptionsMonitorChangeSubscription()
    {
        var monitor = new TestOptionsMonitor(CreateOptions());

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new RecordingSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);

        monitor.SubscriptionCount.ShouldBe(1);
    }

    [Fact]
    public void OptionsMonitorReload_PublishesOneRuntimeUpdateToEachSubscriber()
    {
        var monitor = new TestOptionsMonitor(CreateOptions());
        var subscriber1 = new RecordingSubscriber();
        var subscriber2 = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new RecordingSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription1 = publisher.Subscribe(subscriber1);
        using var subscription2 = publisher.Subscribe(subscriber2);

        var reloadedOptions = CreateOptions(tableName: "runtime_reload");
        monitor.Reload(reloadedOptions);

        subscriber1.Notifications.ShouldBe(1);
        subscriber2.Notifications.ShouldBe(1);
        subscriber1.LastOptions.ShouldBeSameAs(reloadedOptions);
        subscriber2.LastOptions.ShouldBeSameAs(reloadedOptions);
    }

    [Fact]
    public void Dispose_DisposesOptionsMonitorChangeRegistration()
    {
        var monitor = new TestOptionsMonitor(CreateOptions());
        var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new RecordingSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);

        publisher.Dispose();

        monitor.ActiveSubscriptionCount.ShouldBe(0);
        monitor.DisposedSubscriptionCount.ShouldBe(1);
    }

    [Fact]
    public void SynchronizerFailure_IsObservableAndDoesNotNotifySubscribers()
    {
        var monitor = new TestOptionsMonitor(CreateOptions());
        var subscriber = new RecordingSubscriber();
        var synchronizer = new RecordingSynchronizer
        {
            ThrowOnSync = true
        };

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            synchronizer,
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription = publisher.Subscribe(subscriber);

        var act = () => monitor.Reload(CreateOptions(tableName: "reload_failed"));

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldBe("Synchronizer failure for test.");
        subscriber.Notifications.ShouldBe(0);
    }

    [Fact]
    public void SubscriberFailure_IsObservableAndStopsLaterSubscribers()
    {
        var monitor = new TestOptionsMonitor(CreateOptions());
        var throwingSubscriber = new RecordingSubscriber
        {
            ThrowOnNotification = true
        };
        var laterSubscriber = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new RecordingSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription1 = publisher.Subscribe(throwingSubscriber);
        using var subscription2 = publisher.Subscribe(laterSubscriber);

        var act = () => monitor.Reload(CreateOptions(tableName: "reload_failed"));

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldBe("Subscriber failure for test.");
        throwingSubscriber.Notifications.ShouldBe(1);
        laterSubscriber.Notifications.ShouldBe(0);
    }

    [Fact]
    public void PostgreSQLDataSource_OptionsMonitorReload_UsesCentralizedPublisherPath()
    {
        var currentOptions = CreateOptions(applicationName: "before", minPoolSize: 5);
        var monitor = new TestOptionsMonitor(currentOptions);
        var factory = new RecordingDataSourceFactory();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            publisher,
            factory.Create);

        monitor.Reload(CreateOptions(applicationName: "after", minPoolSize: 7));

        monitor.SubscriptionCount.ShouldBe(1);
        factory.BuildCount.ShouldBe(2);
        factory.BuiltSettings[^1].ApplicationName.ShouldBe("after");
        factory.BuiltSettings[^1].MinPoolSize.ShouldBe(7);
        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("after");
    }

    [Fact]
    public void DbNomenclatureAndRawCommands_OptionsMonitorReload_UseCentralizedPublisherPath()
    {
        var currentOptions = CreateOptions(tableName: "before_table", schemaName: "before_schema");
        var monitor = new TestOptionsMonitor(currentOptions);

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var nomenclature = new DbNomenclature(
            monitor,
            NullLogger<DbNomenclature>.Instance,
            publisher);
        using var commands = new DbRawCommands(
            nomenclature,
            monitor,
            NullLogger<DbRawCommands>.Instance,
            publisher);

        monitor.Reload(CreateOptions(tableName: "after_table", schemaName: "after_schema"));

        monitor.SubscriptionCount.ShouldBe(1);
        nomenclature.TableName.ShouldBe("after_table");
        nomenclature.SchemaName.ShouldBe("after_schema");
        nomenclature.FullTableName.ShouldBe("after_schema.after_table");
        commands.GetSql.ShouldContain("after_schema.after_table");
        commands.GetSql.ShouldNotContain("before_schema.before_table");
    }

    private static GlacialCachePostgreSQLOptions CreateOptions(
        string tableName = "glacial_cache",
        string schemaName = "public",
        string applicationName = "runtime",
        int minPoolSize = 5)
    {
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache =
            {
                TableName = tableName,
                SchemaName = schemaName
            },
            Connection =
            {
                ConnectionString = CreateConnectionString(applicationName),
                Pool =
                {
                    MinSize = minPoolSize,
                    MaxSize = 50,
                    IdleLifetimeSeconds = 300,
                    PruningIntervalSeconds = 10
                }
            }
        };

        options.Cache.SetLogger(NullLogger.Instance);
        options.Connection.SetLogger(NullLogger.Instance);
        return options;
    }

    private static string CreateConnectionString(string applicationName) =>
        $"Host=localhost;Database=glacial_cache_tests;Username=test;Password=test;Application Name={applicationName}";

    private sealed class RecordingSubscriber : IRuntimeConfigurationSubscriber
    {
        public int Notifications { get; private set; }
        public GlacialCachePostgreSQLOptions? LastOptions { get; private set; }
        public bool ThrowOnNotification { get; init; }

        public void OnRuntimeConfigurationChanged(GlacialCachePostgreSQLOptions options)
        {
            Notifications++;
            LastOptions = options;

            if (ThrowOnNotification)
            {
                throw new InvalidOperationException("Subscriber failure for test.");
            }
        }
    }

    private sealed class RecordingSynchronizer : IObservableOptionsSynchronizer
    {
        public int SyncCount { get; private set; }
        public bool ThrowOnSync { get; init; }

        public void Sync(GlacialCachePostgreSQLOptions currentOptions, GlacialCachePostgreSQLOptions newOptions, ILogger logger)
        {
            SyncCount++;

            if (ThrowOnSync)
            {
                throw new InvalidOperationException("Synchronizer failure for test.");
            }
        }
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<GlacialCachePostgreSQLOptions>
    {
        private readonly List<Action<GlacialCachePostgreSQLOptions, string?>> _listeners = [];
        private readonly List<Subscription> _subscriptions = [];

        public TestOptionsMonitor(GlacialCachePostgreSQLOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public int SubscriptionCount => _subscriptions.Count;
        public int ActiveSubscriptionCount => _subscriptions.Count(subscription => !subscription.IsDisposed);
        public int DisposedSubscriptionCount => _subscriptions.Count(subscription => subscription.IsDisposed);
        public GlacialCachePostgreSQLOptions CurrentValue { get; private set; }

        public GlacialCachePostgreSQLOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<GlacialCachePostgreSQLOptions, string?> listener)
        {
            _listeners.Add(listener);
            var subscription = new Subscription(() => _listeners.Remove(listener));
            _subscriptions.Add(subscription);
            return subscription;
        }

        public void Reload(GlacialCachePostgreSQLOptions newValue, string? name = null)
        {
            CurrentValue = newValue;

            foreach (var listener in _listeners.ToArray())
            {
                listener(newValue, name);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                if (IsDisposed)
                {
                    return;
                }

                IsDisposed = true;
                _dispose();
            }
        }
    }

    private sealed class RecordingDataSourceFactory
    {
        public List<PostgreSQLDataSourceSettings> BuiltSettings { get; } = [];
        public int BuildCount { get; private set; }

        public IPostgreSQLDataSourceHandle Create(PostgreSQLDataSourceSettings settings)
        {
            BuildCount++;
            BuiltSettings.Add(settings);
            return new RecordingDataSourceHandle(settings.ConnectionString);
        }
    }

    private sealed class RecordingDataSourceHandle : IPostgreSQLDataSourceHandle
    {
        public RecordingDataSourceHandle(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public ValueTask<Npgsql.NpgsqlConnection> OpenConnectionAsync(CancellationToken token = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
