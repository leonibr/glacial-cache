using System.Collections.Concurrent;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GlacialCache.PostgreSQL.Tests.Integration;

public sealed class BatchWriteReadWorkflowIntegrationTests : IAsyncLifetime
{
    private const string PostgreSqlImage =
        "postgres@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193";

    private PostgreSqlContainer? _postgres;
    private ServiceProvider? _serviceProvider;
    private RecordingLoggerProvider? _logs;
    private IGlacialCache? _cache;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage(PostgreSqlImage)
            .WithUsername("test")
            .WithPassword("test")
            .WithDatabase("glacialcache_test")
            .WithCleanUp(true)
            .Build();
        await _postgres.StartAsync();

        _logs = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(_logs));
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = _postgres.GetConnectionString();
            options.Cache.DefaultAbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            options.Cache.DefaultSlidingExpiration = null;
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
        });

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<IGlacialCache>();
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
        _logs?.Dispose();
    }

    [Fact]
    public async Task SetAndGetMultipleAsync_PreservesProductionWorkflowContract()
    {
        await VerifySuccessExpirationAndSlidingRefreshAsync();
        await VerifyWriteFailureRollsBackAndLogsErrorAsync();
        await VerifyReadFailurePreservesWritesAndLogsSuccessAsync();
        await VerifyReadCancellationPreservesWritesAndLogsSuccessAsync();
        await VerifyLargeBatchFallbackAsync();
    }

    private async Task VerifySuccessExpirationAndSlidingRefreshAsync()
    {
        _logs!.Clear();
        var entries = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>
        {
            ["workflow:fixed"] = (
                [1, 2, 3],
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                }),
            ["workflow:sliding"] = (
                [4, 5, 6],
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                    SlidingExpiration = TimeSpan.FromMinutes(2)
                })
        };

        var before = DateTimeOffset.UtcNow;
        var result = await _cache!.SetAndGetMultipleAsync(entries);
        var after = DateTimeOffset.UtcNow;

        result.Count.ShouldBe(2);
        result["workflow:fixed"].ShouldBeEquivalentTo(entries["workflow:fixed"].value);
        result["workflow:sliding"].ShouldBeEquivalentTo(entries["workflow:sliding"].value);
        _logs.Messages.ShouldContain(message =>
            message.Level == LogLevel.Debug &&
            message.Text.Contains("Successfully set 2 cache entries in batch", StringComparison.Ordinal));

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT key, absolute_expiration, sliding_interval, next_expiration
            FROM public.glacial_cache
            WHERE key = ANY($1)
            ORDER BY key
            """,
            connection);
        command.Parameters.AddWithValue(entries.Keys.ToArray());
        await using var reader = await command.ExecuteReaderAsync();

        await reader.ReadAsync();
        reader.GetString(0).ShouldBe("workflow:fixed");
        var fixedAbsolute = reader.GetFieldValue<DateTimeOffset>(1);
        reader.IsDBNull(2).ShouldBeTrue();
        reader.GetFieldValue<DateTimeOffset>(3).ShouldBe(fixedAbsolute);
        fixedAbsolute.ShouldBeInRange(before.AddMinutes(5), after.AddMinutes(5));

        await reader.ReadAsync();
        reader.GetString(0).ShouldBe("workflow:sliding");
        var slidingAbsolute = reader.GetFieldValue<DateTimeOffset>(1);
        reader.GetFieldValue<TimeSpan>(2).ShouldBe(TimeSpan.FromMinutes(2));
        var slidingNext = reader.GetFieldValue<DateTimeOffset>(3);
        slidingAbsolute.ShouldBeInRange(before.AddMinutes(10), after.AddMinutes(10));
        slidingNext.ShouldBeInRange(before.AddMinutes(2), after.AddMinutes(2));
    }

    private async Task VerifyWriteFailureRollsBackAndLogsErrorAsync()
    {
        await ExecuteSqlAsync(
            """
            CREATE OR REPLACE FUNCTION fail_workflow_write() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.key = 'workflow:write-failure' THEN
                    RAISE EXCEPTION 'injected write failure';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER fail_workflow_write_trigger
            BEFORE INSERT ON public.glacial_cache
            FOR EACH ROW EXECUTE FUNCTION fail_workflow_write();
            """);
        _logs!.Clear();

        var entries = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>
        {
            ["workflow:write-good"] = ([7], new DistributedCacheEntryOptions()),
            ["workflow:write-failure"] = ([8], new DistributedCacheEntryOptions())
        };

        await Should.ThrowAsync<PostgresException>(() => _cache!.SetAndGetMultipleAsync(entries));
        (await CountRowsAsync(entries.Keys)).ShouldBe(0);
        _logs.Messages.ShouldContain(message =>
            message.Level == LogLevel.Error &&
            message.Text.Contains("Error setting 2 cache entries in batch", StringComparison.Ordinal));

        await ExecuteSqlAsync(
            "DROP TRIGGER fail_workflow_write_trigger ON public.glacial_cache; DROP FUNCTION fail_workflow_write();");
    }

    private async Task VerifyReadFailurePreservesWritesAndLogsSuccessAsync()
    {
        await ExecuteSqlAsync(
            """
            CREATE OR REPLACE FUNCTION fail_workflow_read() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.key = 'workflow:read-failure' AND NEW.value = OLD.value THEN
                    RAISE EXCEPTION 'injected read failure';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER fail_workflow_read_trigger
            BEFORE UPDATE ON public.glacial_cache
            FOR EACH ROW EXECUTE FUNCTION fail_workflow_read();
            """);
        _logs!.Clear();
        var entries = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>
        {
            ["workflow:read-failure"] = ([9, 10], new DistributedCacheEntryOptions())
        };

        await Should.ThrowAsync<PostgresException>(() => _cache!.SetAndGetMultipleAsync(entries));
        _logs.Messages.ShouldContain(message =>
            message.Level == LogLevel.Debug &&
            message.Text.Contains("Successfully set 1 cache entries in batch", StringComparison.Ordinal));
        _logs.Messages.ShouldNotContain(message =>
            message.Text.Contains("Error setting", StringComparison.Ordinal));

        await ExecuteSqlAsync(
            "DROP TRIGGER fail_workflow_read_trigger ON public.glacial_cache; DROP FUNCTION fail_workflow_read();");
        (await ReadValueAsync("workflow:read-failure")).ShouldBeEquivalentTo(entries["workflow:read-failure"].value);
    }

    private async Task VerifyReadCancellationPreservesWritesAndLogsSuccessAsync()
    {
        await ExecuteSqlAsync(
            """
            CREATE OR REPLACE FUNCTION delay_workflow_read() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.key = 'workflow:cancel' AND NEW.value = OLD.value THEN
                    PERFORM pg_sleep(30);
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER delay_workflow_read_trigger
            BEFORE UPDATE ON public.glacial_cache
            FOR EACH ROW EXECUTE FUNCTION delay_workflow_read();
            """);
        _logs!.Clear();
        var entries = new Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)>
        {
            ["workflow:cancel"] = ([11, 12], new DistributedCacheEntryOptions())
        };
        using var cancellation = new CancellationTokenSource();

        var workflow = _cache!.SetAndGetMultipleAsync(entries, cancellation.Token);
        await WaitForReadDelayAsync();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => workflow);

        _logs.Messages.ShouldContain(message =>
            message.Level == LogLevel.Debug &&
            message.Text.Contains("Successfully set 1 cache entries in batch", StringComparison.Ordinal));
        await ExecuteSqlAsync(
            "DROP TRIGGER delay_workflow_read_trigger ON public.glacial_cache; DROP FUNCTION delay_workflow_read();");
        (await ReadValueAsync("workflow:cancel")).ShouldBeEquivalentTo(entries["workflow:cancel"].value);
    }

    private async Task VerifyLargeBatchFallbackAsync()
    {
        var entries = Enumerable.Range(0, 1001).ToDictionary(
            index => $"workflow:large:{index}",
            index => (BitConverter.GetBytes(index), new DistributedCacheEntryOptions()));

        var result = await _cache!.SetAndGetMultipleAsync(entries);

        result.Count.ShouldBe(entries.Count);
        result["workflow:large:0"].ShouldBeEquivalentTo(BitConverter.GetBytes(0));
        result["workflow:large:1000"].ShouldBeEquivalentTo(BitConverter.GetBytes(1000));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_postgres!.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountRowsAsync(IEnumerable<string> keys)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::int FROM public.glacial_cache WHERE key = ANY($1)",
            connection);
        command.Parameters.AddWithValue(keys.ToArray());
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task<byte[]> ReadValueAsync(string key)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT value FROM public.glacial_cache WHERE key = $1",
            connection);
        command.Parameters.AddWithValue(key);
        return (byte[])(await command.ExecuteScalarAsync())!;
    }

    private async Task WaitForReadDelayAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE datname = current_database()
                      AND wait_event = 'PgSleep'
                )
                """,
                connection);
            if (await command.ExecuteScalarAsync(timeout.Token) is true)
            {
                return;
            }
            await Task.Delay(20, timeout.Token);
        }
        throw new TimeoutException("The combined workflow did not reach the delayed read.");
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogMessage> _messages = new();

        public IReadOnlyCollection<LogMessage> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(_messages);

        public void Clear()
        {
            while (_messages.TryDequeue(out _))
            {
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(ConcurrentQueue<LogMessage> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(new LogMessage(logLevel, formatter(state, exception)));
    }

    private sealed record LogMessage(LogLevel Level, string Text);
}
