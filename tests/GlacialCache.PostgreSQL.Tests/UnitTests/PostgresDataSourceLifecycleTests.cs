using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class PostgresDataSourceLifecycleTests
{
    [Fact]
    public void Constructor_BuildsDataSourceExactlyOnce()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(applicationName: "startup"));
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        factory.BuildCount.ShouldBe(1);
        factory.Sources.ShouldHaveSingleItem();

        dataSource.Dispose();

        factory.Sources[0].DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void Reload_WhenConnectionStringChanges_BuildsOneReplacement()
    {
        var options = CreateOptions(applicationName: "before");
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("after");

        factory.BuildCount.ShouldBe(2);
        factory.BuiltSettings[^1].ApplicationName.ShouldBe("after");
        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("after");
    }

    [Fact]
    public void Reload_WhenMinPoolSizeChanges_BuildsOneReplacement()
    {
        var options = CreateOptions(minPoolSize: 5, maxPoolSize: 50);
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        options.Connection.Pool.MinSizeObservable.Value = 7;

        factory.BuildCount.ShouldBe(2);
        factory.BuiltSettings[^1].MinPoolSize.ShouldBe(7);
        dataSource.GetPoolMetrics().MinPoolSize.ShouldBe(7);
    }

    [Fact]
    public void Reload_WhenMaxPoolSizeChanges_BuildsOneReplacement()
    {
        var options = CreateOptions(minPoolSize: 5, maxPoolSize: 50);
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        options.Connection.Pool.MaxSizeObservable.Value = 60;

        factory.BuildCount.ShouldBe(2);
        factory.BuiltSettings[^1].MaxPoolSize.ShouldBe(60);
        dataSource.GetPoolMetrics().MaxPoolSize.ShouldBe(60);
    }

    [Fact]
    public void Reload_WhenIdleLifetimeChanges_BuildsOneReplacement()
    {
        var options = CreateOptions(idleLifetime: 300);
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        options.Connection.Pool.IdleLifetimeSecondsObservable.Value = 301;

        factory.BuildCount.ShouldBe(2);
        factory.BuiltSettings[^1].IdleLifetime.ShouldBe(301);
        dataSource.GetPoolMetrics().IdleLifetime.ShouldBe(301);
    }

    [Fact]
    public void Reload_WhenPruningIntervalChanges_BuildsOneReplacement()
    {
        var options = CreateOptions(pruningInterval: 10);
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        options.Connection.Pool.PruningIntervalSecondsObservable.Value = 11;

        factory.BuildCount.ShouldBe(2);
        factory.BuiltSettings[^1].PruningInterval.ShouldBe(11);
        dataSource.GetPoolMetrics().PruningInterval.ShouldBe(11);
    }

    [Fact]
    public void OptionsMonitorReload_WhenConnectionAndPoolSettingsChange_BuildsOneReplacement()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(applicationName: "before", minPoolSize: 5));
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        monitor.Reload(CreateOptions(applicationName: "after", minPoolSize: 7));

        factory.BuildCount.ShouldBe(2);
        factory.BuiltSettings[^1].ApplicationName.ShouldBe("after");
        factory.BuiltSettings[^1].MinPoolSize.ShouldBe(7);
    }

    [Fact]
    public void OptionsMonitorReload_WhenNonDataSourceSettingChanges_DoesNotBuildReplacement()
    {
        var monitor = new TestOptionsMonitor(CreateOptions());
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        var reloadedOptions = CreateOptions();
        reloadedOptions.Cache.TableName = "other_table";
        reloadedOptions.Cache.SetLogger(NullLogger.Instance);
        monitor.Reload(reloadedOptions);

        factory.BuildCount.ShouldBe(1);
    }

    [Fact]
    public void Reload_WhenReplacementSucceeds_DisposesReplacedSourceOnce()
    {
        var options = CreateOptions(applicationName: "before");
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("after");

        factory.Sources[0].DisposeCount.ShouldBe(1);
        factory.Sources[1].DisposeCount.ShouldBe(0);
    }

    [Fact]
    public void Reload_WhenMultipleAcceptedReloads_DisposesEachReplacedSourceOnce()
    {
        var options = CreateOptions(applicationName: "first");
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("second");
        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("third");

        factory.Sources[0].DisposeCount.ShouldBe(1);
        factory.Sources[1].DisposeCount.ShouldBe(1);
        factory.Sources[2].DisposeCount.ShouldBe(0);

        dataSource.Dispose();

        factory.Sources[2].DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetConnectionAsync_WhenReloadOccursDuringOpen_DoesNotDisposeCapturedSourceUntilOpenCompletes()
    {
        var options = CreateOptions(applicationName: "before");
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory();

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        var openingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOpenToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        factory.Sources[0].OpenConnectionAsyncCallback = async token =>
        {
            openingStarted.SetResult();
            await allowOpenToComplete.Task.WaitAsync(token);

            if (factory.Sources[0].IsDisposed)
            {
                throw new ObjectDisposedException(nameof(RecordingDataSourceHandle));
            }

            return new Npgsql.NpgsqlConnection();
        };

        var openTask = dataSource.GetConnectionAsync().AsTask();

        try
        {
            await openingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            options.Connection.ConnectionStringObservable.Value = CreateConnectionString("after");

            factory.Sources[0].DisposeCount.ShouldBe(0);

            allowOpenToComplete.SetResult();
            var connection = await openTask.WaitAsync(TimeSpan.FromSeconds(5));
            connection.Dispose();

            factory.Sources[0].DisposeCount.ShouldBe(1);
            factory.Sources[1].DisposeCount.ShouldBe(0);
        }
        finally
        {
            allowOpenToComplete.TrySetResult();

            try
            {
                await openTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    [Fact]
    public void Reload_WhenReplacementBuildThrows_KeepsPreviousActiveSource()
    {
        var options = CreateOptions(applicationName: "before");
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory
        {
            ThrowWhen = settings => settings.ApplicationName == "bad"
        };

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        var act = () => options.Connection.ConnectionStringObservable.Value = CreateConnectionString("bad");

        act.ShouldNotThrow();
        factory.BuildCount.ShouldBe(2);
        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("before");
        factory.Sources[0].DisposeCount.ShouldBe(0);
    }

    [Fact]
    public void Reload_WhenReplacementBuildThrows_DoesNotAdvanceCurrentSettings()
    {
        var options = CreateOptions(applicationName: "before");
        var monitor = new TestOptionsMonitor(options);
        var factory = new RecordingDataSourceFactory
        {
            ThrowWhen = settings => settings.ApplicationName == "bad"
        };

        using var dataSource = new PostgreSQLDataSource(
            NullLogger<PostgreSQLDataSource>.Instance,
            monitor,
            factory.Create);

        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("bad");
        options.Connection.ConnectionStringObservable.Value = CreateConnectionString("after");

        factory.BuildCount.ShouldBe(3);
        factory.BuiltSettings[^1].ApplicationName.ShouldBe("after");
        dataSource.GetPoolMetrics().ApplicationName.ShouldBe("after");
        factory.Sources[0].DisposeCount.ShouldBe(1);
    }

    private static GlacialCachePostgreSQLOptions CreateOptions(
        string applicationName = "before",
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
        public List<PostgreSQLDataSourceSettings> BuiltSettings { get; } = [];
        public List<RecordingDataSourceHandle> Sources { get; } = [];
        public Func<PostgreSQLDataSourceSettings, bool> ThrowWhen { get; init; } = _ => false;
        public int BuildCount { get; private set; }

        public IPostgreSQLDataSourceHandle Create(PostgreSQLDataSourceSettings settings)
        {
            BuildCount++;
            BuiltSettings.Add(settings);

            if (ThrowWhen(settings))
            {
                throw new InvalidOperationException("Factory failure for test.");
            }

            var source = new RecordingDataSourceHandle(settings.ConnectionString);
            Sources.Add(source);
            return source;
        }
    }

    private sealed class RecordingDataSourceHandle : IPostgreSQLDataSourceHandle
    {
        private int _disposeCount;

        public RecordingDataSourceHandle(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool IsDisposed => DisposeCount > 0;
        public Func<CancellationToken, ValueTask<Npgsql.NpgsqlConnection>>? OpenConnectionAsyncCallback { get; set; }

        public ValueTask<Npgsql.NpgsqlConnection> OpenConnectionAsync(CancellationToken token = default)
        {
            if (OpenConnectionAsyncCallback != null)
            {
                return OpenConnectionAsyncCallback(token);
            }

            throw new NotSupportedException();
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
