using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class HostedServiceLifecycleTests
{
    [Fact]
    public async Task Cleanup_RemainsRunningAndFollowsManagerTransitions()
    {
        var options = new GlacialCachePostgreSQLOptions();
        options.Infrastructure.EnableManagerElection = true;
        options.Maintenance.CleanupInterval = TimeSpan.FromMinutes(1);
        var timeProvider = new ManualTimeProvider();

        var optionsMonitor = new Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
        optionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var cleanupAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dataSource = new Mock<IPostgreSQLDataSource>();
        dataSource
            .Setup(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => cleanupAttempted.TrySetResult())
            .Returns(new ValueTask<NpgsqlConnection>((NpgsqlConnection)null!));

        var commands = new Mock<IDbRawCommands>();
        commands.SetupGet(x => x.CleanupExpiredSql).Returns("DELETE FROM cache");

        var electionState = new ElectionState(
            Mock.Of<ILogger<ElectionState>>(),
            TimeProvider.System,
            "test-instance");

        await using var service = new CleanupBackgroundService(
            optionsMonitor.Object,
            Mock.Of<ILogger<CleanupBackgroundService>>(),
            dataSource.Object,
            commands.Object,
            electionState,
            timeProvider);

        await service.StartAsync(CancellationToken.None);
        timeProvider.Tick();
        await Task.Yield();
        dataSource.Verify(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);

        await electionState.BecomeManagerAsync();
        timeProvider.Tick();
        await cleanupAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await electionState.LoseManagerAsync();
        var attemptsAfterDemotion = dataSource.Invocations.Count;
        timeProvider.Tick();
        await Task.Yield();

        dataSource.Invocations.Count.ShouldBe(attemptsAfterDemotion);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void DisabledManagerElection_DoesNotCreateElectionBackgroundService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGlacialCachePostgreSQL(options =>
            options.Infrastructure.EnableManagerElection = false);

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>()
            .ShouldNotContain(service => service is ElectionBackgroundService);
    }

    [Fact]
    public void EnabledManagerElection_CreatesElectionBackgroundService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = "Host=localhost;Database=test;Username=test;Password=test";
            options.Infrastructure.EnableManagerElection = true;
        });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>()
            .ShouldContain(service => service is ElectionBackgroundService);
    }

    [Fact]
    public void CleanupCommand_UsesConfiguredBatchSize()
    {
        using var connection = new NpgsqlConnection();

        using var command = CleanupBackgroundService.CreateCleanupCommand(
            "DELETE FROM cache",
            connection,
            DateTimeOffset.Parse("2026-07-26T12:00:00Z"),
            37);

        command.Parameters["maxBatchSize"].Value.ShouldBe(37);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private ManualTimer? _timer;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _timer = new ManualTimer(callback, state);
            return _timer;
        }

        public void Tick() => _timer!.Tick();

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Tick() => callback(state);

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
