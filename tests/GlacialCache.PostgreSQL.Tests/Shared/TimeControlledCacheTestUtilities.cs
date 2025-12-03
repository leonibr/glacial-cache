using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Configuration;

namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// Utilities for creating time-controlled cache test scenarios.
/// Provides helpers for common time-based testing patterns.
/// </summary>
public static class TimeControlledCacheTestUtilities
{
    /// <summary>
    /// Creates a service provider configured with FakeTimeProvider for testing.
    /// </summary>
    /// <param name="fakeTimeProvider">The fake time provider to use.</param>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="configureOptions">Optional configuration action.</param>
    /// <returns>Configured service provider.</returns>
    public static IServiceProvider CreateTimeControlledServiceProvider(
        FakeTimeProvider fakeTimeProvider,
        string connectionString,
        Action<GlacialCachePostgreSQLOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();

        // Add logging for debugging
        services.AddLogging(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Debug)
                   .AddFilter("GlacialCache.PostgreSQL", LogLevel.Trace));

        // Register the FakeTimeProvider
        services.AddSingleton<System.TimeProvider>(fakeTimeProvider);

        // Add GlacialCache with time-controlled configuration
        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = connectionString;
            options.Cache.SchemaName = "test_time_controlled";
            options.Cache.TableName = "test_cache";
            options.Cache.EnableEdgeCaseLogging = true;

            // Configure shorter intervals for faster testing
            options.Infrastructure.Lock.LockTimeout = TimeSpan.FromSeconds(10);
            options.Maintenance.CleanupInterval = TimeSpan.FromSeconds(2);

            // Allow custom configuration
            configureOptions?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates test data with various expiration scenarios for time-controlled testing.
    /// </summary>
    /// <param name="baseTime">The base time to use for calculations.</param>
    /// <returns>Dictionary of test entries with different expiration patterns.</returns>
    public static Dictionary<string, (object Value, DateTimeOffset? AbsoluteExpiration, TimeSpan? SlidingExpiration)>
        CreateTimeBasedTestData(DateTimeOffset baseTime)
    {
        return new Dictionary<string, (object, DateTimeOffset?, TimeSpan?)>
        {
            ["immediate-expire"] = ("value1", baseTime.AddSeconds(-1), null),
            ["short-lived"] = ("value2", baseTime.AddMinutes(2), null),
            ["medium-lived"] = ("value3", baseTime.AddMinutes(10), null),
            ["long-lived"] = ("value4", baseTime.AddHours(1), null),
            ["sliding-short"] = ("value5", null, TimeSpan.FromMinutes(5)),
            ["sliding-medium"] = ("value6", null, TimeSpan.FromMinutes(15)),
            ["sliding-long"] = ("value7", null, TimeSpan.FromHours(1)),
            ["both-policies"] = ("value8", baseTime.AddMinutes(30), TimeSpan.FromMinutes(20)),
            ["no-expiration"] = ("value9", null, null)
        };
    }

    /// <summary>
    /// Simulates a realistic cache usage pattern with time advancement.
    /// </summary>
    /// <param name="fakeTimeProvider">The fake time provider to advance.</param>
    /// <param name="scenarios">List of time advancement scenarios.</param>
    public static void SimulateTimeProgression(
        FakeTimeProvider fakeTimeProvider,
        params (TimeSpan Advance, string Description)[] scenarios)
    {
        foreach (var (advance, description) in scenarios)
        {
            fakeTimeProvider.Advance(advance);
            // Could add logging here if needed
        }
    }

    /// <summary>
    /// Creates a time-based test scenario with predictable intervals.
    /// </summary>
    public static class TimeScenarios
    {
        public static readonly TimeSpan QuickExpiration = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ShortExpiration = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan MediumExpiration = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan LongExpiration = TimeSpan.FromHours(1);

        public static readonly TimeSpan QuickAdvance = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan ShortAdvance = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan MediumAdvance = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan LongAdvance = TimeSpan.FromMinutes(30);
    }
}
