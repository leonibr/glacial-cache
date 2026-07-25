using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class RuntimeConfigurationSnapshotTests
{
    [Fact]
    public void FromOptions_BuildsNormalizedIdentifiersSqlConnectionAndPoolSnapshot()
    {
        var options = CreateOptions(
            tableName: "Mixed_Table",
            schemaName: "Mixed_Schema",
            applicationName: "snapshot",
            minPoolSize: 7,
            maxPoolSize: 70,
            idleLifetime: 301,
            pruningInterval: 11,
            defaultAbsoluteExpirationRelativeToNow: TimeSpan.FromDays(3));

        var snapshot = RuntimeConfigurationSnapshot.FromOptions(options);

        snapshot.Cache.TableName.ShouldBe("mixed_table");
        snapshot.Cache.SchemaName.ShouldBe("mixed_schema");
        snapshot.Cache.FullTableName.ShouldBe("mixed_schema.mixed_table");
        snapshot.Cache.DefaultAbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromDays(3));
        snapshot.Cache.Sql.SetSql.ShouldContain("mixed_schema.mixed_table");
        snapshot.Cache.Sql.SetSql.ShouldContain("interval '3 days'");
        snapshot.Cache.Sql.GetSql.ShouldContain("mixed_schema.mixed_table");

        snapshot.Connection.ConnectionString.ShouldContain("Application Name=snapshot");
        snapshot.Connection.MinPoolSize.ShouldBe(7);
        snapshot.Connection.MaxPoolSize.ShouldBe(70);
        snapshot.Connection.IdleLifetimeSeconds.ShouldBe(301);
        snapshot.Connection.PruningIntervalSeconds.ShouldBe(11);
    }

    [Fact]
    public void FromOptions_MutatingSourceOptionsAfterBuild_DoesNotChangeSnapshot()
    {
        var options = CreateOptions(
            tableName: "before_table",
            schemaName: "before_schema",
            applicationName: "before",
            minPoolSize: 5);

        var snapshot = RuntimeConfigurationSnapshot.FromOptions(options);

        options.Cache.TableName = "after_table";
        options.Cache.SchemaName = "after_schema";
        options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromDays(9);
        options.Connection.ConnectionString = CreateConnectionString("after");
        options.Connection.Pool.MinSize = 9;

        snapshot.Cache.TableName.ShouldBe("before_table");
        snapshot.Cache.SchemaName.ShouldBe("before_schema");
        snapshot.Cache.FullTableName.ShouldBe("before_schema.before_table");
        snapshot.Cache.Sql.SetSql.ShouldContain("before_schema.before_table");
        snapshot.Cache.Sql.SetSql.ShouldNotContain("after_schema.after_table");
        snapshot.Connection.ConnectionString.ShouldContain("Application Name=before");
        snapshot.Connection.MinPoolSize.ShouldBe(5);
    }

    [Fact]
    public void Publisher_PublishesWholeSnapshotAtomicallyOnReload()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(
            tableName: "before_table",
            schemaName: "before_schema",
            applicationName: "before",
            minPoolSize: 5));

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);

        var before = publisher.Current;

        monitor.Reload(CreateOptions(
            tableName: "after_table",
            schemaName: "after_schema",
            applicationName: "after",
            minPoolSize: 9,
            defaultAbsoluteExpirationRelativeToNow: TimeSpan.FromDays(9)));

        var after = publisher.Current;

        after.ShouldNotBeSameAs(before);
        before.Cache.FullTableName.ShouldBe("before_schema.before_table");
        before.Cache.Sql.SetSql.ShouldContain("before_schema.before_table");
        before.Connection.ConnectionString.ShouldContain("Application Name=before");
        before.Connection.MinPoolSize.ShouldBe(5);

        after.Cache.FullTableName.ShouldBe("after_schema.after_table");
        after.Cache.Sql.SetSql.ShouldContain("after_schema.after_table");
        after.Cache.Sql.SetSql.ShouldContain("interval '9 days'");
        after.Cache.Sql.SetSql.ShouldNotContain("before_schema.before_table");
        after.Connection.ConnectionString.ShouldContain("Application Name=after");
        after.Connection.MinPoolSize.ShouldBe(9);
    }

    [Fact]
    public void Subscriber_CanReadPublishedSnapshotDuringReloadNotification()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(tableName: "before_table", schemaName: "before_schema"));

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            NullLogger<RuntimeConfigurationPublisher>.Instance);
        var subscriber = new SnapshotReadingSubscriber(publisher);
        using var subscription = publisher.Subscribe(subscriber);

        monitor.Reload(CreateOptions(tableName: "after_table", schemaName: "after_schema"));

        subscriber.ObservedSnapshot.ShouldNotBeNull();
        subscriber.ObservedSnapshot.Cache.FullTableName.ShouldBe("after_schema.after_table");
        subscriber.ObservedSnapshot.Cache.Sql.GetSql.ShouldContain("after_schema.after_table");
    }

    [Fact]
    public void DbNomenclatureAndRawCommands_ReadSnapshotBackedDerivedValuesAfterReload()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(
            tableName: "before_table",
            schemaName: "before_schema",
            defaultAbsoluteExpirationRelativeToNow: TimeSpan.FromDays(2)));

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

        monitor.Reload(CreateOptions(
            tableName: "after_table",
            schemaName: "after_schema",
            defaultAbsoluteExpirationRelativeToNow: TimeSpan.FromDays(6)));

        nomenclature.TableName.ShouldBe("after_table");
        nomenclature.SchemaName.ShouldBe("after_schema");
        nomenclature.FullTableName.ShouldBe("after_schema.after_table");
        commands.GetSql.ShouldContain("after_schema.after_table");
        commands.SetSql.ShouldContain("after_schema.after_table");
        commands.SetSql.ShouldContain("interval '6 days'");
        commands.SetSql.ShouldNotContain("interval '2 days'");
    }

    [Fact]
    public void PostgreSQLDataSourceSettings_FromConnectionSnapshot_UsesCapturedConnectionAndPoolValues()
    {
        var snapshot = RuntimeConfigurationSnapshot.FromOptions(CreateOptions(
            applicationName: "from_snapshot",
            minPoolSize: 4,
            maxPoolSize: 44,
            idleLifetime: 222,
            pruningInterval: 12));

        var settings = PostgreSQLDataSourceSettings.FromSnapshot(snapshot.Connection);

        settings.ConnectionString.ShouldContain("Application Name=from_snapshot");
        settings.MinPoolSize.ShouldBe(4);
        settings.MaxPoolSize.ShouldBe(44);
        settings.IdleLifetime.ShouldBe(222);
        settings.PruningInterval.ShouldBe(12);
        settings.ApplicationName.ShouldBe("from_snapshot");
        settings.PoolingEnabled.ShouldBeTrue();
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

    private sealed class SnapshotReadingSubscriber : IRuntimeConfigurationSubscriber
    {
        private readonly IRuntimeConfigurationSnapshotProvider _snapshotProvider;

        public SnapshotReadingSubscriber(IRuntimeConfigurationSnapshotProvider snapshotProvider)
        {
            _snapshotProvider = snapshotProvider;
        }

        public RuntimeConfigurationSnapshot? ObservedSnapshot { get; private set; }

        public void OnRuntimeConfigurationChanged(GlacialCachePostgreSQLOptions options)
        {
            ObservedSnapshot = _snapshotProvider.Current;
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
}
