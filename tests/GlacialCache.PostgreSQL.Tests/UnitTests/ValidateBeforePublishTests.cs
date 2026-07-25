using System.ComponentModel;
using System.Reflection;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class ValidateBeforePublishTests
{
    [Fact]
    public void InvalidConnectionStringReload_IsRejectedBeforeSnapshotPublishAndSubscriberNotification()
    {
        var currentOptions = CreateOptions(applicationName: "before");
        var monitor = new TestOptionsMonitor(currentOptions);
        var subscriber = new RecordingSubscriber();
        var logger = new RecordingLogger<RuntimeConfigurationPublisher>();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            logger);
        using var subscription = publisher.Subscribe(subscriber);

        var invalidOptions = CreateOptions(
            connectionString: "Host=localhost;Username=test;Password=supersecret;Application Name=after");

        var act = () => monitor.Reload(invalidOptions);

        act.ShouldNotThrow();
        subscriber.Notifications.ShouldBe(0);
        publisher.Current.Connection.ConnectionString.ShouldContain("Application Name=before");
        currentOptions.Connection.ConnectionStringObservable.Value.ShouldContain("Application Name=before");
        logger.Messages.ShouldContain(message => message.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        logger.Messages.ShouldContain(message => message.Contains("Connection.ConnectionString", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("supersecret", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedConnectionStringReload_WithRequiredParts_IsRejectedBeforeSnapshotCreation()
    {
        var currentOptions = CreateOptions(applicationName: "before");
        var monitor = new TestOptionsMonitor(currentOptions);
        var subscriber = new RecordingSubscriber();
        var logger = new RecordingLogger<RuntimeConfigurationPublisher>();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            logger);
        using var subscription = publisher.Subscribe(subscriber);
        var previousSnapshot = publisher.Current;

        var invalidOptions = CreateOptions(
            connectionString: "Host=localhost;Database=db;Username=user;Port=abc;Password=secret;Application Name=after");

        var act = () => monitor.Reload(invalidOptions);

        act.ShouldNotThrow();
        subscriber.Notifications.ShouldBe(0);
        publisher.Current.ShouldBeSameAs(previousSnapshot);
        publisher.Current.Connection.ConnectionString.ShouldContain("Application Name=before");
        currentOptions.Connection.ConnectionStringObservable.Value.ShouldContain("Application Name=before");
        logger.Messages.ShouldContain(message => message.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        logger.Messages.ShouldContain(message => message.Contains("Connection.ConnectionString", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidPoolReload_IsRejectedBeforeDataSourceSwap()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(applicationName: "before", minPoolSize: 5, maxPoolSize: 50));
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

        var invalidOptions = CreateOptions(applicationName: "after", minPoolSize: 70, maxPoolSize: 50);

        var act = () => monitor.Reload(invalidOptions);

        act.ShouldNotThrow();
        factory.BuildCount.ShouldBe(1);
        publisher.Current.Connection.MinPoolSize.ShouldBe(5);
        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("before");
        dataSource.GetPoolMetrics().MinPoolSize.ShouldBe(5);
    }

    [Fact]
    public void InvalidTableReload_IsRejectedBeforeObservableSync()
    {
        var currentOptions = CreateOptions(tableName: "before_table");
        var monitor = new TestOptionsMonitor(currentOptions);
        var subscriber = new RecordingSubscriber();
        var tableNotifications = 0;
        currentOptions.Cache.TableNameObservable.PropertyChanged += (_, _) => tableNotifications++;

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription = publisher.Subscribe(subscriber);

        var invalidOptions = CreateOptions(tableName: "after_table");
        BypassConfiguredIdentifierSetter(invalidOptions.Cache, "_tableName", "bad-table");
        invalidOptions.Cache.SetLogger(NullLogger.Instance);

        var act = () => monitor.Reload(invalidOptions);

        act.ShouldNotThrow();
        subscriber.Notifications.ShouldBe(0);
        tableNotifications.ShouldBe(0);
        currentOptions.Cache.TableNameObservable.Value.ShouldBe("before_table");
        publisher.Current.Cache.TableName.ShouldBe("before_table");
    }

    [Fact]
    public void InvalidSchemaReload_IsRejectedBeforeSnapshotPublish()
    {
        var currentOptions = CreateOptions(schemaName: "before_schema");
        var monitor = new TestOptionsMonitor(currentOptions);
        var subscriber = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription = publisher.Subscribe(subscriber);

        var invalidOptions = CreateOptions(schemaName: "after_schema");
        BypassConfiguredIdentifierSetter(invalidOptions.Cache, "_schemaName", "bad-schema");
        invalidOptions.Cache.SetLogger(NullLogger.Instance);

        var act = () => monitor.Reload(invalidOptions);

        act.ShouldNotThrow();
        subscriber.Notifications.ShouldBe(0);
        currentOptions.Cache.SchemaNameObservable.Value.ShouldBe("before_schema");
        publisher.Current.Cache.SchemaName.ShouldBe("before_schema");
    }

    [Fact]
    public void InvalidMixedReload_DoesNotPartiallyApplyValidFields()
    {
        var currentOptions = CreateOptions(tableName: "before_table", minPoolSize: 5, maxPoolSize: 50);
        var monitor = new TestOptionsMonitor(currentOptions);
        var subscriber = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription = publisher.Subscribe(subscriber);

        var invalidOptions = CreateOptions(tableName: "after_table", minPoolSize: 70, maxPoolSize: 50);

        var act = () => monitor.Reload(invalidOptions);

        act.ShouldNotThrow();
        subscriber.Notifications.ShouldBe(0);
        currentOptions.Cache.TableNameObservable.Value.ShouldBe("before_table");
        currentOptions.Connection.Pool.MinSizeObservable.Value.ShouldBe(5);
        publisher.Current.Cache.TableName.ShouldBe("before_table");
        publisher.Current.Connection.MinPoolSize.ShouldBe(5);
    }

    [Fact]
    public void ValidReloadAfterInvalidReload_PublishesSnapshotAndNotifiesOnce()
    {
        var currentOptions = CreateOptions(tableName: "before_table", applicationName: "before", minPoolSize: 5);
        var monitor = new TestOptionsMonitor(currentOptions);
        var subscriber = new RecordingSubscriber();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        using var subscription = publisher.Subscribe(subscriber);

        monitor.Reload(CreateOptions(tableName: "invalid_table", applicationName: "invalid", minPoolSize: 70, maxPoolSize: 50));
        monitor.Reload(CreateOptions(tableName: "after_table", applicationName: "after", minPoolSize: 7));

        subscriber.Notifications.ShouldBe(1);
        subscriber.LastOptions.ShouldNotBeNull();
        subscriber.LastOptions.Cache.TableName.ShouldBe("after_table");
        publisher.Current.Cache.TableName.ShouldBe("after_table");
        publisher.Current.Connection.ConnectionString.ShouldContain("Application Name=after");
        publisher.Current.Connection.MinPoolSize.ShouldBe(7);
    }

    private static GlacialCachePostgreSQLOptions CreateOptions(
        string tableName = "glacial_cache",
        string schemaName = "public",
        string applicationName = "runtime",
        string? connectionString = null,
        int minPoolSize = 5,
        int maxPoolSize = 50,
        int idleLifetime = 300,
        int pruningInterval = 10)
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
                ConnectionString = connectionString ?? CreateConnectionString(applicationName),
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

    private static void BypassConfiguredIdentifierSetter(CacheOptions cacheOptions, string fieldName, string value)
    {
        var field = typeof(CacheOptions).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.ShouldNotBeNull();
        field.SetValue(cacheOptions, value);
    }

    private sealed class RecordingSubscriber : IRuntimeConfigurationSubscriber
    {
        public int Notifications { get; private set; }
        public GlacialCachePostgreSQLOptions? LastOptions { get; private set; }

        public void OnRuntimeConfigurationChanged(GlacialCachePostgreSQLOptions options)
        {
            Notifications++;
            LastOptions = options;
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

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose() => _dispose();
        }
    }

    private sealed class RecordingDataSourceFactory
    {
        public int BuildCount { get; private set; }

        public IPostgreSQLDataSourceHandle Create(PostgreSQLDataSourceSettings settings)
        {
            BuildCount++;
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
