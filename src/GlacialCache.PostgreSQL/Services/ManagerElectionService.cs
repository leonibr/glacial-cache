using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;

namespace GlacialCache.PostgreSQL.Services;

/// <summary>
/// Manages election of a single instance as the database operations manager.
/// Uses PostgreSQL advisory locks with adaptive leadership features including
/// exponential backoff, voluntary yield, and randomized retry strategies.
/// </summary>
internal class ManagerElectionService : BackgroundService, IManagerElectionService
{
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly ILogger<ManagerElectionService> _logger;
    private readonly IPostgreSQLDataSource _dataSource;
    private readonly LockOptions _lockOptions;
    private readonly string _instanceId;
    private readonly Random _random;
    private readonly TimeProvider _timeProvider;

    // State management
    private volatile bool _isManager = false;
    private NpgsqlConnection? _lockConnection;
    private readonly object _lockObject = new();
    private readonly SemaphoreSlim _electionSemaphore = new(1, 1);

    // Adaptive leadership configuration
    private readonly TimeSpan _voluntaryYieldInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _yieldWindow = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _maxBackoff = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _baseBackoff = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _jitterRange = TimeSpan.FromSeconds(1);

    // Events
    public event EventHandler<ManagerElectedEventArgs>? ManagerElected;
    public event EventHandler<ManagerLostEventArgs>? ManagerLost;

    // Pre-compiled logging delegates
    private static readonly Action<ILogger, string, Exception?> LogManagerElected =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(4001, "ManagerElected"),
        "Instance {InstanceId} elected as manager for database operations");

    private static readonly Action<ILogger, string, string?, Exception?> LogManagerLost =
        LoggerMessage.Define<string, string?>(LogLevel.Information, new EventId(4002, "ManagerLost"),
        "Instance {InstanceId} lost manager role. Reason: {Reason}");

    private static readonly Action<ILogger, string, int, TimeSpan, Exception?> LogBackoffAttempt =
        LoggerMessage.Define<string, int, TimeSpan>(LogLevel.Debug, new EventId(4003, "BackoffAttempt"),
        "Instance {InstanceId} backing off for attempt {AttemptCount} with delay {Delay}");

    private static readonly Action<ILogger, string, TimeSpan, Exception?> LogVoluntaryYield =
        LoggerMessage.Define<string, TimeSpan>(LogLevel.Information, new EventId(4004, "VoluntaryYield"),
        "Instance {InstanceId} voluntarily yielding leadership after {Duration}");

    private static readonly Action<ILogger, string, Exception?> LogAdvisoryLockAcquired =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(4005, "AdvisoryLockAcquired"),
        "Advisory lock acquired for instance {InstanceId}");

    private static readonly Action<ILogger, string, Exception?> LogAdvisoryLockFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4006, "AdvisoryLockFailed"),
        "Failed to acquire advisory lock for instance {InstanceId}");

    public bool IsManager => _isManager;

    public ManagerElectionService(
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        ILogger<ManagerElectionService> logger,
        IPostgreSQLDataSource dataSource,
        TimeProvider timeProvider)
    {
        _options = options?.CurrentValue ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _lockOptions = _options.Infrastructure.Lock;
        _instanceId = Environment.MachineName + "_" + Environment.ProcessId;
        _random = new Random(Environment.ProcessId); // Deterministic but instance-specific

        _logger.LogInformation("ManagerElectionService initialized for instance {InstanceId} with lock key {LockKey}",
            _instanceId, _lockOptions.AdvisoryLockKey);
    }

    // Test constructor for creating instances with custom instance IDs
    internal ManagerElectionService(
        IOptions<GlacialCachePostgreSQLOptions> options,
        ILogger<ManagerElectionService> logger,
        IPostgreSQLDataSource dataSource,
        string customInstanceId,
        TimeProvider? timeProvider = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lockOptions = _options.Infrastructure.Lock;
        _instanceId = customInstanceId ?? throw new ArgumentNullException(nameof(customInstanceId));
        _random = new Random(customInstanceId.GetHashCode()); // Deterministic based on instance ID

        _logger.LogInformation("ManagerElectionService initialized for instance {InstanceId} with lock key {LockKey}",
            _instanceId, _lockOptions.AdvisoryLockKey);
    }

    public async Task<bool> TryAcquireManagerRoleAsync(CancellationToken cancellationToken = default)
    {
        await _electionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isManager)
            {
                return true; // Already the manager
            }

            var acquired = await TryAcquireAdvisoryLockAsync(cancellationToken);
            if (acquired)
            {
                _isManager = true;
                OnManagerElected();
                LogManagerElected(_logger, _instanceId, null);
                return true;
            }

            return false;
        }
        finally
        {
            _electionSemaphore.Release();
        }
    }

    public async Task ReleaseManagerRoleAsync(CancellationToken cancellationToken = default)
    {
        await _electionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!_isManager)
            {
                return; // Not the manager
            }

            await ReleaseAdvisoryLockAsync(cancellationToken);
            _isManager = false;
            OnManagerLost("Manual release");
            LogManagerLost(_logger, _instanceId, "Manual release", null);
        }
        finally
        {
            _electionSemaphore.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ManagerElectionService started for instance {InstanceId}", _instanceId);

        var attemptCount = 0;
        var leadershipStartTime = _timeProvider.GetUtcNow();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_isManager)
                    {
                        // Try to acquire leadership
                        var acquired = await TryAcquireManagerRoleAsync(stoppingToken);
                        if (acquired)
                        {
                            attemptCount = 0; // Reset attempt counter on success
                            leadershipStartTime = _timeProvider.GetUtcNow();
                            _logger.LogInformation("Instance {InstanceId} became manager", _instanceId);
                        }
                        else
                        {
                            // Exponential backoff with jitter
                            attemptCount++;
                            var backoffDelay = CalculateBackoffDelay(attemptCount);
                            LogBackoffAttempt(_logger, _instanceId, attemptCount, backoffDelay, null);

                            // Add small random offset to desynchronize instances
                            var jitter = TimeSpan.FromMilliseconds(_random.Next(-500, 500));
                            await Task.Delay(backoffDelay + jitter, stoppingToken);
                        }
                    }
                    else
                    {
                        // Check if we should voluntarily yield leadership
                        var leadershipDuration = _timeProvider.GetUtcNow() - leadershipStartTime;
                        if (leadershipDuration >= _voluntaryYieldInterval)
                        {
                            LogVoluntaryYield(_logger, _instanceId, leadershipDuration, null);

                            // Voluntarily release leadership
                            await ReleaseManagerRoleAsync(stoppingToken);

                            // Wait in yield window before trying to re-acquire
                            await Task.Delay(_yieldWindow, stoppingToken);

                            attemptCount = 0; // Reset for next attempt
                        }
                        else
                        {
                            // Verify we still hold the lock
                            if (!await VerifyAdvisoryLockAsync(stoppingToken))
                            {
                                _logger.LogWarning("Lost advisory lock during leadership, releasing role");
                                await ReleaseManagerRoleAsync(stoppingToken);
                                attemptCount = 0;
                            }
                            else
                            {
                                // Continue as manager - check every 30 seconds
                                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in manager election loop for instance {InstanceId}", _instanceId);

                    // Release leadership if we had it
                    if (_isManager)
                    {
                        await ReleaseManagerRoleAsync(stoppingToken);
                    }

                    // Back off before retrying
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ManagerElectionService stopping due to cancellation");
        }
        finally
        {
            if (_isManager)
            {
                await ReleaseManagerRoleAsync(stoppingToken);
            }
            _logger.LogInformation("ManagerElectionService stopped for instance {InstanceId}", _instanceId);
        }
    }

    private async Task<bool> TryAcquireAdvisoryLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            _lockConnection = await _dataSource.GetConnectionAsync(cancellationToken);

            // Use PostgreSQL advisory lock with timeout
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(@lockKey)", _lockConnection);
            command.Parameters.AddWithValue("@lockKey", _lockOptions.AdvisoryLockKey);
            command.CommandTimeout = (int)_lockOptions.LockTimeout.TotalSeconds;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var acquired = Convert.ToBoolean(result);

            if (acquired)
            {
                LogAdvisoryLockAcquired(_logger, _instanceId, null);
            }
            else
            {
                LogAdvisoryLockFailed(_logger, _instanceId, null);
            }

            return acquired;
        }
        catch (PostgresException ex) when (ex.SqlState == "42501") // Insufficient privilege
        {
            _logger.LogError(ex,
                "Advisory lock permission denied for instance {InstanceId}. " +
                "Automatic coordination disabled. For multi-instance deployments:\n" +
                "1. Grant permissions: GRANT EXECUTE ON FUNCTION pg_try_advisory_lock(bigint), " +
                "   pg_advisory_unlock(bigint), pg_advisory_lock(bigint), " +
                "   pg_try_advisory_lock_shared(bigint), pg_advisory_unlock_shared(bigint) TO user\n" +
                "2. Or disable coordination: Set EnableManagerElection=false\n" +
                "3. Manually coordinate cleanup: Choose which instances handle automatic cleanup\n" +
                "   using EnableAutomaticCleanup flag in your configuration",
                _instanceId);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error acquiring advisory lock for instance {InstanceId}", _instanceId);
            return false;
        }
    }

    private async Task<bool> VerifyAdvisoryLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_lockConnection == null)
                return false;

            // Try to acquire the same lock again - if we already hold it, this will return true
            // If we don't hold it, this will return false
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(@lockKey)", _lockConnection);
            command.Parameters.AddWithValue("@lockKey", _lockOptions.AdvisoryLockKey);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var stillHoldsLock = Convert.ToBoolean(result);

            // If we successfully acquired the lock again, we need to release it immediately
            // since we're just checking if we still hold it
            if (stillHoldsLock)
            {
                await using var releaseCommand = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(@lockKey)", _lockConnection);
                releaseCommand.Parameters.AddWithValue("@lockKey", _lockOptions.AdvisoryLockKey);
                await releaseCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            return stillHoldsLock;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error verifying advisory lock for instance {InstanceId}", _instanceId);
            return false;
        }
    }

    private async Task ReleaseAdvisoryLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_lockConnection != null)
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(@lockKey)", _lockConnection);
                command.Parameters.AddWithValue("@lockKey", _lockOptions.AdvisoryLockKey);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error releasing advisory lock for instance {InstanceId}", _instanceId);
        }
        finally
        {
            _lockConnection?.Dispose();
            _lockConnection = null;
        }
    }

    private TimeSpan CalculateBackoffDelay(int attemptCount)
    {
        // Exponential backoff: min(60s, 5s * 2^attemptCount)
        var exponentialDelay = _baseBackoff * Math.Pow(2, attemptCount - 1);
        var cappedDelay = TimeSpan.FromSeconds(Math.Min(exponentialDelay.TotalSeconds, _maxBackoff.TotalSeconds));

        // Add jitter: ±1 second
        var jitter = TimeSpan.FromMilliseconds(_random.Next(-1000, 1000));
        return cappedDelay + jitter;
    }

    private void OnManagerElected()
    {
        ManagerElected?.Invoke(this, new ManagerElectedEventArgs(_timeProvider.GetUtcNow(), _instanceId));
    }

    private void OnManagerLost(string reason)
    {
        ManagerLost?.Invoke(this, new ManagerLostEventArgs(_timeProvider.GetUtcNow(), _instanceId, reason));
    }

    public override void Dispose()
    {
        try
        {
            _lockConnection?.Dispose();
            _electionSemaphore?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing ManagerElectionService");
        }
        finally
        {
            base.Dispose();
        }
    }
}