using GlacialCache.PostgreSQL.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public sealed class Section7SensitiveFragileHardeningTests
{
    [Fact]
    public void ObservableProperty_WhenConnectionStringChanges_DoesNotLogRawConnectionStringSecrets()
    {
        var logger = new RecordingLogger<Section7SensitiveFragileHardeningTests>();
        var observable = new ObservableProperty<string>("Connection.ConnectionString", logger);
        var oldConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=old-password;Token=old-token";
        var newConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=new-password;Token=new-token";

        observable.Value = oldConnectionString;
        logger.Messages.Clear();

        observable.Value = newConnectionString;

        logger.Messages.ShouldContain(message => message.Contains("Connection.ConnectionString", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("old-password", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("new-password", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("old-token", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("new-token", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains(oldConnectionString, StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains(newConnectionString, StringComparison.Ordinal));
    }

    [Fact]
    public void ObservableProperty_WhenNonSensitivePropertyChanges_LogsUsefulPropertyNameAndValues()
    {
        var logger = new RecordingLogger<Section7SensitiveFragileHardeningTests>();
        var observable = new ObservableProperty<int>("Connection.Pool.MaxSize", logger)
        {
            Value = 50
        };
        logger.Messages.Clear();

        observable.Value = 75;

        logger.Messages.ShouldContain(message => message.Contains("Connection.Pool.MaxSize", StringComparison.Ordinal));
        logger.Messages.ShouldContain(message => message.Contains("50", StringComparison.Ordinal));
        logger.Messages.ShouldContain(message => message.Contains("75", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeReloadDiagnostics_WhenConnectionStringRejected_DoNotIncludeRawConnectionStringSecrets()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(applicationName: "before"));
        var logger = new RecordingLogger<RuntimeConfigurationPublisher>();

        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ObservableOptionsSynchronizer(),
            logger);

        var rejectedConnectionString = "Host=localhost;Username=testuser;Password=rejected-password;Secret=rejected-secret;Application Name=after";

        monitor.Reload(CreateOptions(connectionString: rejectedConnectionString));

        logger.Messages.ShouldContain(message => message.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        logger.Messages.ShouldContain(message => message.Contains("Connection.ConnectionString", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("rejected-password", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains("rejected-secret", StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message => message.Contains(rejectedConnectionString, StringComparison.Ordinal));
        publisher.Current.Connection.ConnectionString.ShouldContain("Application Name=before");
    }

    [Fact]
    public void RuntimeConfigurationPublisher_WhenSynchronizerFails_SurfacesFailureAndKeepsPreviousSnapshot()
    {
        var monitor = new TestOptionsMonitor(CreateOptions(applicationName: "before"));
        var subscriber = new RecordingSubscriber();
        using var publisher = new RuntimeConfigurationPublisher(
            monitor,
            new ThrowingSynchronizer(),
            new RecordingLogger<RuntimeConfigurationPublisher>());
        using var subscription = publisher.Subscribe(subscriber);

        var act = () => monitor.Reload(CreateOptions(applicationName: "after"));

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldBe("Synchronizer failure for Section 7 test.");
        subscriber.Notifications.ShouldBe(0);
        publisher.Current.Connection.ConnectionString.ShouldContain("Application Name=before");
    }

    [Fact]
    public void ObservableProperty_CallbacksRunSynchronouslyAndExceptionsPropagate()
    {
        var observable = new ObservableProperty<string>("Connection.ConnectionString");
        var callbackRanBeforeSetterReturned = false;

        observable.PropertyChanged += (_, _) =>
        {
            callbackRanBeforeSetterReturned = true;
            throw new InvalidOperationException("Callback failure for Section 7 test.");
        };

        var act = () => observable.Value = "Host=localhost;Database=testdb;Username=testuser;Password=callback-secret";

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldBe("Callback failure for Section 7 test.");
        callbackRanBeforeSetterReturned.ShouldBeTrue();
    }

    [Fact]
    public void RuntimeReloadCallbackContract_IsDocumented()
    {
        var configurationDoc = File.ReadAllText(FindRepositoryFile("docs", "configuration.md"));

        configurationDoc.ShouldContain("Runtime reload callback contract", Case.Insensitive);
        configurationDoc.ShouldContain("synchronously", Case.Insensitive);
        configurationDoc.ShouldContain("PropertyChangedEventArgs", Case.Insensitive);
        configurationDoc.ShouldContain("sensitive", Case.Insensitive);
    }

    private static GlacialCachePostgreSQLOptions CreateOptions(
        string applicationName = "runtime",
        string? connectionString = null)
    {
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection =
            {
                ConnectionString = connectionString ?? CreateConnectionString(applicationName),
                Pool =
                {
                    MinSize = 5,
                    MaxSize = 50,
                    IdleLifetimeSeconds = 300,
                    PruningIntervalSeconds = 10
                }
            }
        };

        options.Cache.SetLogger(null);
        options.Connection.SetLogger(null);
        return options;
    }

    private static string CreateConnectionString(string applicationName) =>
        $"Host=localhost;Database=glacial_cache_tests;Username=testuser;Password=test-password;Application Name={applicationName}";

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(pathParts)}");
    }

    private sealed class ThrowingSynchronizer : IObservableOptionsSynchronizer
    {
        public void Sync(GlacialCachePostgreSQLOptions currentOptions, GlacialCachePostgreSQLOptions newOptions, ILogger logger)
        {
            throw new InvalidOperationException("Synchronizer failure for Section 7 test.");
        }
    }

    private sealed class RecordingSubscriber : IRuntimeConfigurationSubscriber
    {
        public int Notifications { get; private set; }

        public void OnRuntimeConfigurationChanged(RuntimeConfigurationChangedEventArgs change)
        {
            Notifications++;
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

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
