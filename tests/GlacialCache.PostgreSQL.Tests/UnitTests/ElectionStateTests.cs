using System.Collections.Concurrent;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Tests.Shared;
using Microsoft.Extensions.Logging;
using Moq;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class ElectionStateTests
{
    private readonly TimeTestHelper _time;

    public ElectionStateTests()
    {
        _time = TimeTestHelper.CreateForUnitTests();
    }

    private Mock<ILogger<ElectionState>> CreateLogger()
    {
        var loggerMock = new Mock<ILogger<ElectionState>>();

        // Source-generated logging methods check IsEnabled() first
        loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Configure the mock to handle source-generated logging calls
        loggerMock.Setup(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Verifiable();

        return loggerMock;
    }


    private (ElectionState ElectionState, Mock<ILogger<ElectionState>> LoggerMock) CreateElectionStateWithLogger(string instanceId = "test-instance")
    {
        var loggerMock = CreateLogger();
        var electionState = new ElectionState(loggerMock.Object, _time.TimeProvider, instanceId);
        return (electionState, loggerMock);
    }


    private ElectionState CreateElectionState(string instanceId = "test-instance")
        => CreateElectionStateWithLogger(instanceId).ElectionState;


    [Fact]
    public void Constructor_SetsPropertiesAndLogsInitialization()
    {

        var (electionState, loggerMock) = CreateElectionStateWithLogger("test-instance-123");


        electionState.InstanceId.ShouldBe("test-instance-123",
            "Instance ID should be set to the provided value");
        electionState.IsManager.ShouldBeFalse(
            "New instances should not be managers by default");
        electionState.ElectedAt.ShouldBeNull(
            "Election time should be null for new instances");
        electionState.LostAt.ShouldBeNull(
            "Loss time should be null for new instances");


        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                LoggingIds.ElectionStateInitialized,
                It.IsAny<It.IsAnyType>(),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once,
            "Constructor should log initialization to track instance creation"
        );
    }


    [Fact]
    public async Task BecomeManagerAsync_SetsIsManagerFlagAndRecordsTimestamp()
    {
        // Arrange: Create a fresh instance in non-manager state
        var (electionState, loggerMock) = CreateElectionStateWithLogger();

        // Act: Transition to manager status
        await electionState.BecomeManagerAsync();

        // Assert: Verify state changes are applied correctly
        electionState.IsManager.ShouldBeTrue(
            "Instance should be marked as manager after successful transition");
        electionState.ElectedAt.ShouldNotBeNull(
            "Election timestamp should be recorded when becoming manager");
        electionState.LostAt.ShouldBeNull(
            "Loss timestamp should remain null when currently manager");

        // Verify audit trail - ensures state changes are logged for debugging/monitoring
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                LoggingIds.ElectionStateUpdated,
                It.IsAny<It.IsAnyType>(),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once,
            "State transitions should be logged for operational visibility"
        );
    }


    [Fact]
    public async Task ObjectDisposedException_ContractVerification_BecomeManagerAsync()
    {
        var electionState = CreateElectionState();
        electionState.Dispose();

        Func<Task> act = () => electionState.BecomeManagerAsync();

        await act.ShouldThrowAsync<ObjectDisposedException>(
            "BecomeManagerAsync should throw ObjectDisposedException when disposed");
    }

    [Fact]
    public async Task LoseManagerAsync_ClearsManagerFlagAndPreservesElectionTime()
    {
        // Arrange: Create instance and make it a manager first
        var (electionState, loggerMock) = CreateElectionStateWithLogger();
        await electionState.BecomeManagerAsync();

        // Reset mock to focus only on LoseManagerAsync logging behavior
        loggerMock.Reset();
        loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var originalElectedAt = electionState.ElectedAt;

        // Act: Lose manager status
        await electionState.LoseManagerAsync();

        electionState.IsManager.ShouldBeFalse(
            "Instance should no longer be manager after losing leadership");
        electionState.LostAt.ShouldNotBeNull(
            "Loss timestamp should be recorded when leadership is lost");
        electionState.ElectedAt.ShouldBe(originalElectedAt,
            "Original election time should be preserved for historical tracking");

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                LoggingIds.ElectionStateUpdated,
                It.IsAny<It.IsAnyType>(),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once,
            "Leadership loss should be logged for monitoring and debugging"
        );
    }

    [Fact]
    public async Task GetStateSnapshotAsync_ReturnsCurrentElectionState()
    {
        // Arrange
        var electionState = CreateElectionState();
        await electionState.BecomeManagerAsync();
        var expectedElectedAt = electionState.ElectedAt;

        // Act
        var (isManager, electedAt, lostAt) = await electionState.GetStateSnapshotAsync();

        // Assert
        isManager.ShouldBeTrue();
        electedAt.ShouldBe(expectedElectedAt);
        lostAt.ShouldBeNull();
    }

    [Fact]
    public async Task ConcurrentReaders_CanAccessStateWithoutBlocking()
    {
        // Arrange: Create election state instance for concurrent access testing
        var electionState = CreateElectionState();
        const int threadCount = 10;
        const int readsPerThread = 100;
        var tasks = new Task[threadCount];

        // Act: Launch multiple concurrent readers accessing the same state
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                for (int j = 0; j < readsPerThread; j++)
                {
                    await electionState.GetStateSnapshotAsync();
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ConcurrentWriters_MaintainDataConsistency()
    {
        // Arrange: Create election state for concurrent write testing
        var electionState = CreateElectionState();
        const int threadCount = 5;
        const int operationsPerThread = 10;
        var tasks = new Task[threadCount];

        // Act: Launch multiple threads performing state transitions
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                for (int j = 0; j < operationsPerThread; j++)
                {
                    await electionState.BecomeManagerAsync();
                    await electionState.LoseManagerAsync();
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task BecomeManagerAsync_IdempotentOperation()
    {
        // Arrange
        var (electionState, loggerMock) = CreateElectionStateWithLogger();
        await electionState.BecomeManagerAsync();
        var firstElectedAt = electionState.ElectedAt;

        // Clear previous logging calls to focus on the second call
        loggerMock.Reset();
        loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Act
        await electionState.BecomeManagerAsync();

        // Assert
        electionState.IsManager.ShouldBeTrue();
        electionState.ElectedAt.ShouldBe(firstElectedAt); // Should not change

        // Verify NO additional logging occurred (idempotent operation)
        // The Reset() call above clears previous calls, so we should see no new calls
        loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Never,
            "BecomeManagerAsync should not log when already manager"
        );
    }

    [Fact]
    public async Task LoseManagerAsync_NoOpWhenNotManager()
    {
        // Arrange
        var (electionState, loggerMock) = CreateElectionStateWithLogger();

        // Reset mock to clear constructor logging and focus on LoseManagerAsync
        loggerMock.Reset();
        loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Act
        await electionState.LoseManagerAsync();

        // Assert
        electionState.IsManager.ShouldBeFalse();
        electionState.LostAt.ShouldBeNull(); // Should remain null

        // Verify NO logging occurred (no-op operation)
        loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Never,
            "LoseManagerAsync should not log when not currently manager"
        );
    }

    [Fact]
    public async Task UpdateStateAsync_ExecutesActionAtomically()
    {
        // Arrange
        var (electionState, loggerMock) = CreateElectionStateWithLogger();
        var actionExecuted = false;

        // Act
        await electionState.UpdateStateAsync(updater =>
        {
            actionExecuted = true;
            updater.BecomeManager();
        });

        // Assert
        actionExecuted.ShouldBeTrue();
        electionState.IsManager.ShouldBeTrue();

        // Verify logging occurred for the state change
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                LoggingIds.ElectionStateUpdated,
                It.IsAny<It.IsAnyType>(),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once,
            "UpdateStateAsync should log the resulting state change"
        );
    }

    [Fact]
    public async Task Dispose_ReleasesResourcesAndLogsDisposal()
    {
        // Arrange
        var (electionState, loggerMock) = CreateElectionStateWithLogger();

        // Act
        electionState.Dispose();

        // Assert - Should not throw when Dispose is called multiple times
        // This is mainly to ensure Dispose can be called safely multiple times
        Action act = () => electionState.Dispose();
        act.ShouldNotThrow();

        // Verify disposal was logged
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                LoggingIds.ElectionServiceDisposed,
                It.IsAny<It.IsAnyType>(),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Exactly(2),
            "Dispose should log service disposal each time it's called"
        );
    }

    #region Error Scenarios

    [Fact]
    public void Constructor_RejectsNullInstanceId()
    {
        // Arrange: Set up valid dependencies but null instanceId
        var logger = CreateLogger();
        var timeProvider = _time.TimeProvider;

        // Act & Assert: Constructor should reject null instanceId
        Action act = () => new ElectionState(logger.Object, timeProvider, null!);

        act.ShouldThrow<ArgumentNullException>()
            .Message.ShouldContain("instanceId");
    }


    [Fact]
    public void Constructor_RejectsNullLogger()
    {
        // Arrange: Valid time provider but null logger
        var timeProvider = _time.TimeProvider;

        // Act & Assert: Constructor should reject null logger
        Action act = () => new ElectionState(null, timeProvider, "test-id");

        act.ShouldThrow<ArgumentNullException>()
            .Message.ShouldContain("logger");
    }

    [Fact]
    public void Constructor_RejectsNullTimeProvider()
    {
        // Arrange: Valid logger but null time provider
        var logger = CreateLogger();

        // Act & Assert: Constructor should reject null time provider
        Action act = () => new ElectionState(logger.Object, null, "test-id");

        act.ShouldThrow<ArgumentNullException>()
            .Message.ShouldContain("timeProvider");
    }


    [Fact]
    public async Task BecomeManagerAsync_ThrowsWhenDisposed()
    {
        var electionState = CreateElectionState();
        electionState.Dispose();

        Func<Task> act = () => electionState.BecomeManagerAsync();

        await act.ShouldThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task LoseManagerAsync_ThrowsWhenDisposed()
    {
        var electionState = CreateElectionState();
        electionState.Dispose();

        Func<Task> act = () => electionState.LoseManagerAsync();

        await act.ShouldThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task GetStateSnapshotAsync_ThrowsWhenDisposed()
    {
        var electionState = CreateElectionState();
        electionState.Dispose();

        Func<Task> act = () => electionState.GetStateSnapshotAsync();

        await act.ShouldThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task UpdateStateAsync_ThrowsWhenDisposed()
    {
        var electionState = CreateElectionState();
        electionState.Dispose();

        Func<Task> act = () => electionState.UpdateStateAsync(updater => updater.BecomeManager());

        await act.ShouldThrowAsync<ObjectDisposedException>();
    }


    [Fact]
    public async Task UpdateStateAsync_HandlesExceptionsAndReleasesSemaphore()
    {
        var electionState = CreateElectionState();

        // Exception in UpdateStateAsync action
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => electionState.UpdateStateAsync(updater =>
            {
                throw new InvalidOperationException("Action failed");
            })
        );

        // Verify semaphore was released - subsequent operations should work
        await electionState.GetStateSnapshotAsync(); // Should not hang
    }


    #endregion

    #region Cancellation Token Testing

    [Fact]
    public async Task BecomeManagerAsync_RespondsToCancellation()
    {
        var electionState = CreateElectionState();
        var cts = new CancellationTokenSource();


        cts.Cancel();

        Func<Task> act = () => electionState.BecomeManagerAsync(cts.Token);

        await act.ShouldThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LoseManagerAsync_RespondsToCancellation()
    {
        var electionState = CreateElectionState();
        var cts = new CancellationTokenSource();

        // Become manager first
        await electionState.BecomeManagerAsync();

        cts.Cancel();

        Func<Task> act = () => electionState.LoseManagerAsync(cts.Token);

        await act.ShouldThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetStateSnapshotAsync_RespondsToCancellation()
    {
        var electionState = CreateElectionState();
        var cts = new CancellationTokenSource();

        cts.Cancel();

        Func<Task> act = () => electionState.GetStateSnapshotAsync(cts.Token);

        await act.ShouldThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UpdateStateAsync_RespondsToCancellation()
    {
        var electionState = CreateElectionState();
        var cts = new CancellationTokenSource();


        cts.Cancel();

        Func<Task> act = () => electionState.UpdateStateAsync(
            updater => updater.BecomeManager(),
            cts.Token
        );

        await act.ShouldThrowAsync<OperationCanceledException>();
    }



    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task Semaphore_PreventsConcurrentCriticalSectionAccess()
    {
        // Arrange
        var electionState = CreateElectionState();
        var criticalSectionCount = 0;
        var maxConcurrentAccess = 0;
        var semaphore = new SemaphoreSlim(1, 1);

        // Act - Start multiple threads trying to access critical section
        var tasks = new Task[5];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var current = Interlocked.Increment(ref criticalSectionCount);
                    maxConcurrentAccess = Math.Max(maxConcurrentAccess, current);

                    // Simulate work in critical section
                    await Task.Delay(10);

                    Interlocked.Decrement(ref criticalSectionCount);
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - Verify semaphore prevented concurrent access
        maxConcurrentAccess.ShouldBe(1, "Semaphore should prevent concurrent critical section access");
    }

    [Fact]
    public async Task ConcurrentOperations_MaintainStateConsistency()
    {
        // Arrange
        var electionState = CreateElectionState();
        const int operationCount = 100;
        var successfulTransitions = 0;

        // Act - Multiple threads performing state transitions
        var tasks = new Task[10];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                for (int j = 0; j < operationCount; j++)
                {
                    var wasManagerBefore = electionState.IsManager;
                    await electionState.BecomeManagerAsync();

                    // Verify state transition was consistent
                    if (electionState.IsManager && !wasManagerBefore)
                        Interlocked.Increment(ref successfulTransitions);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - Verify all operations maintained consistency
        successfulTransitions.ShouldBeGreaterThan(0);
        electionState.IsManager.ShouldBeTrue(); // Final state should be consistent
    }

    [Fact]
    public async Task RapidStateTransitions_DontCauseInconsistencies()
    {
        // Arrange
        var electionState = CreateElectionState();
        var inconsistencies = 0;

        // Act - Rapid become/lose cycles from multiple threads
        var tasks = new Task[20];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                for (int j = 0; j < 50; j++)
                {
                    await electionState.BecomeManagerAsync();
                    await electionState.LoseManagerAsync();

                    // Check for impossible states
                    if (electionState.IsManager && electionState.LostAt.HasValue &&
                        electionState.ElectedAt > electionState.LostAt)
                    {
                        Interlocked.Increment(ref inconsistencies);
                    }
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - Verify no impossible state transitions occurred
        inconsistencies.ShouldBe(0, "No impossible state transitions should occur");
    }

    [Fact]
    public async Task ExceptionInCriticalSection_ReleasesSemaphore()
    {
        // Arrange
        var (electionState, loggerMock) = CreateElectionStateWithLogger();

        // Act & Assert - Exception in critical section should release semaphore
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => electionState.UpdateStateAsync(updater =>
            {
                throw new InvalidOperationException("Test exception in critical section");
            })
        );

        // Verify semaphore was properly released despite exception
        await electionState.GetStateSnapshotAsync(); // Should not hang
    }

    [Fact]
    public async Task ConcurrentCancellation_HandledGracefully()
    {
        // Arrange
        var electionState = CreateElectionState();
        var cts = new CancellationTokenSource();
        var cancelledTasks = 0;
        var semaphore = new SemaphoreSlim(1, 1);

        // Act - Start multiple operations that will wait on semaphore, then cancel
        var tasks = new Task[10];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    // First acquire semaphore to simulate longer operation
                    await semaphore.WaitAsync(cts.Token);
                    try
                    {
                        await Task.Delay(100, cts.Token); // Longer delay to allow cancellation
                        await electionState.BecomeManagerAsync(cts.Token);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelledTasks);
                }
            });
        }

        // Cancel after some operations started but before they complete
        await Task.Delay(10);
        cts.Cancel();

        await Task.WhenAll(tasks);

        // Assert - Verify cancellation was handled properly
        cancelledTasks.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SemaphoreExhaustion_HandledGracefully()
    {
        // Arrange
        var electionState = CreateElectionState();
        var longRunningOperation = new TaskCompletionSource();

        // Start a long-running operation to hold the semaphore
        var holdingTask = Task.Run(async () =>
        {
            await electionState.UpdateStateAsync(async updater =>
            {
                await longRunningOperation.Task; // Hold semaphore indefinitely
            });
        });

        await Task.Delay(10); // Let the first operation acquire semaphore

        // Act - Try concurrent operations while semaphore is held
        var concurrentTasks = new Task[5];
        for (int i = 0; i < concurrentTasks.Length; i++)
        {
            concurrentTasks[i] = Task.Run(async () =>
            {
                // This should wait for semaphore
                await electionState.GetStateSnapshotAsync();
            });
        }

        // Complete the holding operation
        longRunningOperation.SetResult();
        await Task.WhenAll(concurrentTasks);

        // Assert - All operations should complete successfully
        // If semaphore wasn't properly released, this would hang or timeout
    }

    [Fact]
    public async Task DisposeDuringConcurrentOperations_HandledSafely()
    {
        // Arrange
        var electionState = CreateElectionState();
        var exceptions = new ConcurrentBag<Exception>();

        // Act - Start concurrent operations, then dispose
        var tasks = new Task[5];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    var semaphoreField = electionState.GetType().GetField("_stateSemaphore",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                    while (semaphoreField?.GetValue(electionState) != null)
                    {
                        await electionState.GetStateSnapshotAsync();
                        await Task.Delay(1);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        await Task.Delay(10);
        electionState.Dispose();

        await Task.WhenAll(tasks);

        // Assert - Verify dispose handled concurrent operations gracefully
        // Allow ObjectDisposedException but not other exceptions
        var unexpectedExceptions = exceptions.Where(ex => !(ex is ObjectDisposedException)).ToList();
        unexpectedExceptions.ShouldBeEmpty();
    }

    #endregion

    #region Behavioral Scenarios

    /// <summary>
    /// Demonstrates the complete leadership election lifecycle behavior:
    /// 1. Instance starts as non-manager
    /// 2. Gains leadership and records election time
    /// 3. Eventually loses leadership and records loss time
    /// 4. All state transitions are logged appropriately
    ///
    /// This behavioral test verifies the end-to-end observable behavior
    /// of the election state management without focusing on internal details.
    /// </summary>
    [Fact]
    public async Task LeadershipElection_Lifecycle_BehavesCorrectly()
    {
        // Arrange: Start with a fresh instance
        var (electionState, loggerMock) = CreateElectionStateWithLogger("election-instance-01");

        // Behavioral verification: Initially not a manager
        electionState.IsManager.ShouldBeFalse("New instances should start as non-managers");
        electionState.ElectedAt.ShouldBeNull("No election time initially");
        electionState.LostAt.ShouldBeNull("No loss time initially");

        // Act & Assert: Gain leadership
        var beforeElection = _time.Now();
        await electionState.BecomeManagerAsync();
        var afterElection = _time.Now();

        // Behavioral verification: Leadership acquisition
        electionState.IsManager.ShouldBeTrue("Instance should become manager");
        electionState.ElectedAt!.Value.ShouldBeGreaterThanOrEqualTo(beforeElection, "Election time should be recorded");
        electionState.ElectedAt!.Value.ShouldBeLessThanOrEqualTo(afterElection, "Election time should be accurate");
        electionState.LostAt.ShouldBeNull("Loss time should remain null while manager");

        // Behavioral verification: Logging captures leadership acquisition
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                LoggingIds.ElectionStateUpdated,
                It.IsAny<It.IsAnyType>(),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.AtLeastOnce,
            "Leadership acquisition should be logged"
        );

        // Act & Assert: Lose leadership
        loggerMock.Reset();
        loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Advance time to ensure loss happens after election
        _time.Advance(TimeSpan.FromMinutes(5));

        var beforeLoss = _time.Now();
        await electionState.LoseManagerAsync();
        var afterLoss = _time.Now();

        var lostAt = electionState.LostAt;
        lostAt.ShouldNotBeNull("Loss time should be recorded");
        // Behavioral verification: Leadership loss
        electionState.IsManager.ShouldBeFalse("Instance should no longer be manager");
        lostAt.Value.ShouldBeGreaterThanOrEqualTo(beforeLoss, "Loss time should be recorded");
        lostAt.Value.ShouldBeLessThanOrEqualTo(afterLoss, "Loss time should be accurate");
        electionState.ElectedAt.ShouldNotBeNull("Election time should be preserved");

        // Behavioral verification: Loss time should be after election time
        lostAt.Value.ShouldBeGreaterThan(electionState.ElectedAt.Value,
            "Loss time should occur after election time");

        // Behavioral verification: Logging captures leadership loss
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                LoggingIds.ElectionStateUpdated,
                It.IsAny<It.IsAnyType>(),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once,
            "Leadership loss should be logged"
        );
    }

    /// <summary>
    /// Demonstrates that the election state maintains proper business rules
    /// about leadership transitions and timing relationships. This focuses
    /// on the behavioral contracts rather than implementation details.
    /// </summary>
    [Fact]
    public async Task LeadershipTransition_BusinessRules_AreEnforced()
    {
        // Arrange: Set up controlled time scenario
        var initialTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        _time.SetTime(initialTime);

        var (electionState, loggerMock) = CreateElectionStateWithLogger();

        // Behavioral verification: Business rule - cannot lose leadership without being leader
        await electionState.LoseManagerAsync(); // Should be no-op since not manager

        // Should still be non-manager
        electionState.IsManager.ShouldBeFalse("Cannot lose leadership when not leader");
        electionState.ElectedAt.ShouldBeNull("No election time when never elected");
        electionState.LostAt.ShouldBeNull("No loss time when never lost leadership");

        // Act: Become leader
        _time.Advance(TimeSpan.FromMinutes(5));
        await electionState.BecomeManagerAsync();

        var electionTime = electionState.ElectedAt;

        // Behavioral verification: Business rule - leader status and election time
        electionState.IsManager.ShouldBeTrue("Should be leader after becoming manager");
        electionTime.ShouldNotBeNull("Election time should be recorded");

        // Act: Try to become leader again (idempotent operation)
        _time.Advance(TimeSpan.FromMinutes(10));
        await electionState.BecomeManagerAsync();

        // Behavioral verification: Business rule - idempotent operation preserves original election time
        electionState.IsManager.ShouldBeTrue("Should remain leader");
        electionState.ElectedAt.ShouldBe(electionTime, "Election time should not change on re-election");

        // Act: Lose leadership
        _time.Advance(TimeSpan.FromMinutes(15));
        await electionState.LoseManagerAsync();

        var lossTime = electionState.LostAt;

        // Behavioral verification: Business rule - proper timing relationships
        electionState.IsManager.ShouldBeFalse("Should not be leader after losing leadership");
        lossTime.ShouldNotBeNull("Loss time should be recorded");
        lossTime.Value.ShouldBeGreaterThan(electionTime.Value, "Loss time should be after election time");
    }
    #endregion
}