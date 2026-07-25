using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class RuntimeConfigurationDomainEventTests
{
    [Fact]
    public void Publisher_WhenRuntimeValuesChange_NotifiesWithTypedPreviousCurrentSnapshotsAndChangeSet()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(
            tableName: "before_table",
            schemaName: "before_schema",
            applicationName: "before",
            minPoolSize: 5));
        var subscriber = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription = publisher.Subscribe(subscriber);

        monitor.Reload(CreateOptions(
            tableName: "after_table",
            schemaName: "after_schema",
            applicationName: "after",
            minPoolSize: 7));

        subscriber.Notifications.ShouldBe(1);
        subscriber.LastChange.ShouldNotBeNull();
        subscriber.LastChange.Previous.Cache.FullTableName.ShouldBe("before_schema.before_table");
        subscriber.LastChange.Current.Cache.FullTableName.ShouldBe("after_schema.after_table");
        subscriber.LastChange.Previous.Connection.ConnectionString.ShouldContain("Application Name=before");
        subscriber.LastChange.Current.Connection.ConnectionString.ShouldContain("Application Name=after");
        subscriber.LastChange.Changes.CacheSnapshotChanged.ShouldBeTrue();
        subscriber.LastChange.Changes.CacheNomenclatureChanged.ShouldBeTrue();
        subscriber.LastChange.Changes.ConnectionStringChanged.ShouldBeTrue();
        subscriber.LastChange.Changes.ConnectionPoolChanged.ShouldBeTrue();
    }

    [Fact]
    public void Publisher_WhenNoRuntimeSnapshotValuesChange_DoesNotNotifySubscribers()
    {
        var monitor = new TestOptionsMonitor(CreateOptions());
        var subscriber = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription = publisher.Subscribe(subscriber);

        monitor.Reload(CreateOptions());

        subscriber.Notifications.ShouldBe(0);
    }

    [Fact]
    public void Publisher_SubscriptionDispose_PreventsFurtherTypedNotifications()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(tableName: "first_table"));
        var subscriber = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        var subscription = publisher.Subscribe(subscriber);

        subscription.Dispose();
        subscription.Dispose();
        monitor.Reload(CreateOptions(tableName: "second_table"));

        subscriber.Notifications.ShouldBe(0);
    }

    [Fact]
    public void Publisher_WhenSubscriberThrows_CommitsSnapshotAndStopsLaterSubscribers()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(tableName: "before_table"));
        var throwingSubscriber = new RecordingSubscriber
        {
            ThrowOnNotification = true
        };
        var laterSubscriber = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription1 = publisher.Subscribe(throwingSubscriber);
        using var subscription2 = publisher.Subscribe(laterSubscriber);

        var act = () => monitor.Reload(CreateOptions(tableName: "after_table"));

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldBe("Subscriber failure for test.");
        publisher.Current.Cache.TableName.ShouldBe("after_table");
        throwingSubscriber.Notifications.ShouldBe(1);
        laterSubscriber.Notifications.ShouldBe(0);
    }

    [Fact]
    public void DbNomenclature_RuntimeReload_UsesTypedSnapshotNotification()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(tableName: "before_table", schemaName: "before_schema"));
        using var publisher = new ManualRuntimeConfigurationPublisher(RuntimeConfigurationSnapshot.FromOptions(monitor.CurrentValue));
        using var nomenclature = new DbNomenclature(
            monitor,
            NullLogger<DbNomenclature>.Instance,
            publisher);

        publisher.Publish(CreateOptions(tableName: "after_table", schemaName: "after_schema"));

        nomenclature.TableName.ShouldBe("after_table");
        nomenclature.SchemaName.ShouldBe("after_schema");
        nomenclature.FullTableName.ShouldBe("after_schema.after_table");
    }

    [Fact]
    public void DbRawCommands_RuntimeReload_UsesTypedSnapshotNotification()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(
            tableName: "before_table",
            schemaName: "before_schema",
            defaultAbsoluteExpirationRelativeToNow: TimeSpan.FromDays(2)));
        using var publisher = new ManualRuntimeConfigurationPublisher(RuntimeConfigurationSnapshot.FromOptions(monitor.CurrentValue));
        using var nomenclature = new DbNomenclature(
            monitor,
            NullLogger<DbNomenclature>.Instance,
            publisher);
        using var commands = new DbRawCommands(
            nomenclature,
            monitor,
            NullLogger<DbRawCommands>.Instance,
            publisher);

        publisher.Publish(CreateOptions(
            tableName: "after_table",
            schemaName: "after_schema",
            defaultAbsoluteExpirationRelativeToNow: TimeSpan.FromDays(6)));

        commands.GetSql.ShouldContain("after_schema.after_table");
        commands.SetSql.ShouldContain("interval '6 days'");
        commands.GetSql.ShouldNotContain("before_schema.before_table");
    }

    [Fact]
    public void PostgreSQLDataSource_RuntimeReload_UsesTypedSnapshotNotification()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(applicationName: "before", minPoolSize: 5));
        using var publisher = new ManualRuntimeConfigurationPublisher(RuntimeConfigurationSnapshot.FromOptions(monitor.CurrentValue));
        var factory = new RecordingDataSourceFactory();
        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            publisher,
            factory.Create);

        publisher.Publish(CreateOptions(applicationName: "after", minPoolSize: 7));

        factory.BuildCount.ShouldBe(2);
        factory.BuiltSettings[^1].ApplicationName.ShouldBe("after");
        factory.BuiltSettings[^1].MinPoolSize.ShouldBe(7);
        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("after");
        dataSource.GetPoolMetrics().MinPoolSize.ShouldBe(7);
    }

    [Fact]
    public void RuntimeConsumers_Dispose_UnsubscribesFromTypedSnapshotNotifications()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(tableName: "before_table", schemaName: "before_schema"));
        using var publisher = new ManualRuntimeConfigurationPublisher(RuntimeConfigurationSnapshot.FromOptions(monitor.CurrentValue));
        var nomenclature = new DbNomenclature(
            monitor,
            NullLogger<DbNomenclature>.Instance,
            publisher);
        var commands = new DbRawCommands(
            nomenclature,
            monitor,
            NullLogger<DbRawCommands>.Instance,
            publisher);
        var factory = new RecordingDataSourceFactory();
        var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            publisher,
            factory.Create);

        nomenclature.Dispose();
        commands.Dispose();
        dataSource.Dispose();
        publisher.Publish(CreateOptions(tableName: "after_table", schemaName: "after_schema", applicationName: "after"));

        nomenclature.TableName.ShouldBe("before_table");
        commands.GetSql.ShouldContain("before_schema.before_table");
        factory.BuildCount.ShouldBe(1);
    }

    [Fact]
    public void DirectObservableAssignment_RemainsCompatibilityPath()
    {
        var options = CreateOptions(tableName: "before_table", schemaName: "before_schema");
        var monitor = new TestOptionsMonitor(options);
        using var publisher = new ManualRuntimeConfigurationPublisher(RuntimeConfigurationSnapshot.FromOptions(options));
        using var nomenclature = new DbNomenclature(
            monitor,
            NullLogger<DbNomenclature>.Instance,
            publisher);

        options.Cache.TableNameObservable.Value = "direct_table";

        nomenclature.TableName.ShouldBe("direct_table");
        nomenclature.FullTableName.ShouldBe("before_schema.direct_table");
    }

    private static GlacialCachePostgreSQLOptions CreateOptions(
        string tableName = "glacial_cache",
        string schemaName = "public",
        string applicationName = "runtime",
        int minPoolSize = 5,
        int maxPoolSize = 50,
        int idleLifetime = 300,
        int pruningInterval = 10,
        TimeSpan? defaultAbsoluteExpirationRelativeToNow = null)
    {
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache =
            {
                TableName = tableName,
                SchemaName = schemaName,
                DefaultAbsoluteExpirationRelativeToNow = defaultAbsoluteExpirationRelativeToNow
            },
            Connection =
            {
                ConnectionString = CreateConnectionString(applicationName),
                Pool =
                {
                    MinSize = minPoolSize,
                    MaxSize = maxPoolSize,
                    IdleLifetimeSeconds = idleLifetime,
                    PruningIntervalSeconds = pruningInterval
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
        public RuntimeConfigurationChangedEventArgs? LastChange { get; private set; }
        public bool ThrowOnNotification { get; init; }

        public void OnRuntimeConfigurationChanged(RuntimeConfigurationChangedEventArgs change)
        {
            Notifications++;
            LastChange = change;

            if (ThrowOnNotification)
            {
                throw new InvalidOperationException("Subscriber failure for test.");
            }
        }
    }

    private sealed class ManualRuntimeConfigurationPublisher : IRuntimeConfigurationPublisher
    {
        private readonly List<IRuntimeConfigurationSubscriber> _subscribers = [];

        public ManualRuntimeConfigurationPublisher(RuntimeConfigurationSnapshot current)
        {
            Current = current;
        }

        public RuntimeConfigurationSnapshot Current { get; private set; }

        public IDisposable Subscribe(IRuntimeConfigurationSubscriber subscriber)
        {
            _subscribers.Add(subscriber);
            return new Subscription(() => _subscribers.Remove(subscriber));
        }

        public void Publish(GlacialCachePostgreSQLOptions options)
        {
            var previous = Current;
            Current = RuntimeConfigurationSnapshot.FromOptions(options);
            var change = RuntimeConfigurationChangedEventArgs.Create(previous, Current, options);

            foreach (var subscriber in _subscribers.ToArray())
            {
                subscriber.OnRuntimeConfigurationChanged(change);
            }
        }

        public void Dispose() => _subscribers.Clear();

        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;
            private bool _disposed;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _dispose();
            }
        }
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<GlacialCachePostgreSQLOptions>
    {
        private readonly List<Action<GlacialCachePostgreSQLOptions, string?>> _listeners = [];

        public TestOptionsMonitor(GlacialCachePostgreSQLOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public GlacialCachePostgreSQLOptions CurrentValue { get; private set; }

        public GlacialCachePostgreSQLOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<GlacialCachePostgreSQLOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new Subscription(() => _listeners.Remove(listener));
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
            private bool _disposed;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
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
