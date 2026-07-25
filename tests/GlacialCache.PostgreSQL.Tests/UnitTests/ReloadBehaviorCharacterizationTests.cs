using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class ReloadBehaviorCharacterizationTests
{
    [Fact]
    public void ConnectionStringObservable_WhenChanged_UpdatesMetricsApplicationName()
    {
        var options = CreateOptions(applicationName: "reload_before");
        var monitor = new TestOptionsMonitor(options);

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor);

        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("reload_after");

        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("reload_after");
    }

    [Fact]
    public void PoolObservables_WhenChanged_UpdatesMetrics()
    {
        var options = CreateOptions(minPoolSize: 5, maxPoolSize: 50, idleLifetime: 300, pruningInterval: 10);
        var monitor = new TestOptionsMonitor(options);

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor);

        options.Connection.Pool.MinSizeObservable.Value = 7;
        options.Connection.Pool.MaxSizeObservable.Value = 60;
        options.Connection.Pool.IdleLifetimeSecondsObservable.Value = 301;
        options.Connection.Pool.PruningIntervalSecondsObservable.Value = 11;

        var metrics = dataSource.GetPoolMetrics();
        metrics.MinPoolSize.ShouldBe(7);
        metrics.MaxPoolSize.ShouldBe(60);
        metrics.IdleLifetime.ShouldBe(301);
        metrics.PruningInterval.ShouldBe(11);
    }

    [Fact]
    public void ConnectionStringObservable_WhenChangedRepeatedly_LastValueWins()
    {
        var options = CreateOptions(applicationName: "reload_before");
        var monitor = new TestOptionsMonitor(options);

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor);

        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("reload_middle");
        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("reload_after");

        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("reload_after");
    }

    [Fact]
    public void TableAndSchemaObservables_WhenChangedSequentially_LastValuesWin()
    {
        var options = CreateOptions();
        var monitor = new TestOptionsMonitor(options);

        using var nomenclature = new DbNomenclature(
            monitor,
            NullLogger<DbNomenclature>.Instance);
        using var commands = new DbRawCommands(
            nomenclature,
            monitor,
            NullLogger<DbRawCommands>.Instance);

        options.Cache.SchemaNameObservable.Value = "reload_schema";
        options.Cache.TableNameObservable.Value = "reload_table";

        nomenclature.SchemaName.ShouldBe("reload_schema");
        nomenclature.TableName.ShouldBe("reload_table");
        nomenclature.FullTableName.ShouldBe("reload_schema.reload_table");
        commands.GetSql.ShouldContain("reload_schema.reload_table");
        commands.GetSql.ShouldNotContain("public.glacial_cache");
    }

    [Fact]
    public void ConnectionStringObservable_WhenMalformedConnectionStringAssigned_KeepsPreviousMetrics()
    {
        var options = CreateOptions(applicationName: "reload_before");
        var monitor = new TestOptionsMonitor(options);

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor);

        var act = () => options.Connection.ConnectionStringObservable.Value = "InvalidKeywordForNpgsql=true";

        act.ShouldNotThrow();
        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("reload_before");
    }

    [Fact]
    public void ConnectionStringObservable_WhenReplacementBuildFails_KeepsPreviousMetrics()
    {
        var options = CreateOptions(applicationName: "reload_before");
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory
        {
            ThrowWhen = settings => settings.ApplicationName == "reload_bad"
        };

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        var act = () => options.Connection.ConnectionStringObservable.Value = CreateConnectionString("reload_bad");

        act.ShouldNotThrow();
        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("reload_before");
    }

    private static GlacialCachePostgreSQLOptions CreateOptions(
        string applicationName = "reload_before",
        int minPoolSize = 5,
        int maxPoolSize = 50,
        int idleLifetime = 300,
        int pruningInterval = 10)
    {
        var options = new GlacialCachePostgreSQLOptions
        {
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

        options.Connection.SetLogger(NullLogger.Instance);
        options.Cache.SetLogger(NullLogger.Instance);
        return options;
    }

    private static string CreateConnectionString(string applicationName) =>
        $"Host=localhost;Database=glacial_cache_tests;Username=test;Password=test;Application Name={applicationName}";

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
        public Func<PostgreSQLDataSourceSettings, bool> ThrowWhen { get; init; } = _ => false;

        public IPostgreSQLDataSourceHandle Create(PostgreSQLDataSourceSettings settings)
        {
            if (ThrowWhen(settings))
            {
                throw new InvalidOperationException("Factory failure for test.");
            }

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
