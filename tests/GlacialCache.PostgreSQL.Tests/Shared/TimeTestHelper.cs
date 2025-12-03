using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// Helper class for time manipulation in tests.
/// Provides a unified interface for controlling time in both unit and integration tests.
/// Uses standard .NET methods for time calculations (AddMinutes, AddHours, etc.).
/// </summary>
public class TimeTestHelper
{
    private readonly FakeTimeProvider _fakeTimeProvider;
    private readonly bool _enableContainerSync;
    private readonly PostgreSqlContainer? _container;
    private readonly ITestOutputHelper? _output;
    private readonly DateTimeOffset _initialTime;

    /// <summary>
    /// Gets the underlying FakeTimeProvider for direct access when needed.
    /// </summary>
    public FakeTimeProvider TimeProvider => _fakeTimeProvider;

    /// <summary>
    /// Gets the initial time that was set when this helper was created.
    /// </summary>
    public DateTimeOffset InitialTime => _initialTime;

    /// <summary>
    /// Constructor for unit tests (simple, no container sync)
    /// </summary>
    /// <param name="fakeTimeProvider">The fake time provider to control</param>
    public TimeTestHelper(FakeTimeProvider fakeTimeProvider)
    {
        _fakeTimeProvider = fakeTimeProvider;
        _enableContainerSync = false;
        _initialTime = fakeTimeProvider.GetUtcNow();
    }

    /// <summary>
    /// Constructor for integration tests (with optional container sync)
    /// </summary>
    /// <param name="fakeTimeProvider">The fake time provider to control</param>
    /// <param name="container">PostgreSQL container for time synchronization</param>
    /// <param name="output">Test output helper for logging</param>
    public TimeTestHelper(FakeTimeProvider fakeTimeProvider, PostgreSqlContainer container, ITestOutputHelper output)
    {
        _fakeTimeProvider = fakeTimeProvider;
        _container = container;
        _output = output;
        _enableContainerSync = true;
        _initialTime = fakeTimeProvider.GetUtcNow();
    }

    /// <summary>
    /// Advances the current time by the specified duration.
    /// </summary>
    /// <param name="duration">The amount of time to advance</param>
    /// <returns>This instance for method chaining</returns>
    public TimeTestHelper Advance(TimeSpan duration)
    {
        _fakeTimeProvider.Advance(duration);
        if (_enableContainerSync)
        {
           SetContainerTimeAsync(_fakeTimeProvider.GetUtcNow()).GetAwaiter().GetResult();
        }
        return this;
    }

    /// <summary>
    /// Sets the current time to the specified value.
    /// </summary>
    /// <param name="time">The new time to set</param>
    /// <returns>This instance for method chaining</returns>
    public TimeTestHelper SetTime(DateTimeOffset time)
    {
        _fakeTimeProvider.SetUtcNow(time);
        if (_enableContainerSync)
            _ = Task.Run(() => SetContainerTimeAsync(time));
        return this;
    }

    /// <summary>
    /// Resets the time to the initial time.
    /// </summary>
    /// <param name="initialTime">The initial time to reset to</param>
    /// <returns>This instance for method chaining</returns>
    public TimeTestHelper ResetToInitial(DateTimeOffset initialTime)
    {
        return SetTime(initialTime);
    }

    /// <summary>
    /// Gets the current time from the fake time provider.
    /// </summary>
    /// <returns>The current fake time</returns>
    public DateTimeOffset Now() => _fakeTimeProvider.GetUtcNow();

    /// <summary>
    /// Sets the PostgreSQL container's system time to match the fake time provider.
    /// This is a fire-and-forget operation that doesn't block test execution.
    /// </summary>
    /// <param name="time">The time to set in the container</param>
    private async Task SetContainerTimeAsync(DateTimeOffset time)
    {
        if (!_enableContainerSync || _container == null) return;

        try
        {
            var timeString = time.ToString("yyyy-MM-dd HH:mm:ss");
            var result = await _container.ExecAsync(new[] { "date", "-s", timeString });

            if (result.ExitCode != 0)
            {
                _output?.WriteLine($"Warning: Container time sync failed. Exit code: {result.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            _output?.WriteLine($"Warning: Container time sync error: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a TimeTestHelper for unit tests with optional initial time.
    /// </summary>
    /// <param name="initialTime">Optional initial time. If null, uses current UTC time.</param>
    /// <returns>A TimeTestHelper configured for unit tests</returns>
    public static TimeTestHelper CreateForUnitTests(DateTimeOffset? initialTime = null)
    {
        var fakeTimeProvider = new FakeTimeProvider(initialTime ?? DateTimeOffset.UtcNow);
        return new TimeTestHelper(fakeTimeProvider);
    }

    /// <summary>
    /// Creates a TimeTestHelper for integration tests with container synchronization.
    /// </summary>
    /// <param name="container">PostgreSQL container for time synchronization</param>
    /// <param name="output">Test output helper for logging</param>
    /// <param name="initialTime">Optional initial time. If null, uses current UTC time.</param>
    /// <returns>A TimeTestHelper configured for integration tests</returns>
    public static TimeTestHelper CreateForIntegrationTests(PostgreSqlContainer container, ITestOutputHelper output, DateTimeOffset? initialTime = null)
    {
        var fakeTimeProvider = new FakeTimeProvider(initialTime ?? DateTimeOffset.UtcNow);
        return new TimeTestHelper(fakeTimeProvider, container, output);
    }
}
