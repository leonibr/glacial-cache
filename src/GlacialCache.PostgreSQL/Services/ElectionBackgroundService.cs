using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.Logging;

namespace GlacialCache.PostgreSQL.Services;

/// <summary>
/// Background service that manages election of a single instance as the database operations manager.
/// Uses PostgreSQL advisory locks with adaptive leadership features including
/// exponential backoff, voluntary yield, and randomized retry strategies.
/// </summary>
internal class ElectionBackgroundService : BackgroundService
{
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly ILogger<ElectionBackgroundService> _logger;
    private readonly IPostgreSQLDataSource _dataSource;
    private readonly LockOptions _lockOptions;
    private readonly ElectionState _electionState;
    private readonly Random _random;
    private readonly TimeProvider _timeProvider;

    // State management
    private NpgsqlConnection? _lockConnection;
    private readonly object _lockObject = new();
    private readonly SemaphoreSlim _electionSemaphore = new(1, 1);

    // Adaptive leadership configuration
    private readonly TimeSpan _voluntaryYieldInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _yieldWindow = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _maxBackoff = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _baseBackoff = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _jitterRange = TimeSpan.FromSeconds(1);

    public ElectionBackgroundService(
        IOptions<GlacialCachePostgreSQLOptions> options,
        ILogger<ElectionBackgroundService> logger,
        IPostgreSQLDataSource dataSource,
        ElectionState electionState,
        TimeProvider timeProvider)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _electionState = electionState ?? throw new ArgumentNullException(nameof(electionState));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _lockOptions = _options.Infrastructure.Lock;
        _random = new Random(Environment.ProcessId); // Deterministic but instance-specific

        _logger.LogElectionServiceStarted(_electionState.InstanceId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogElectionServiceStarted(_electionState.InstanceId);

        var attemptCount = 0;
        var leadershipStartTime = _timeProvider.GetUtcNow();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_electionState.IsManager)
                    {
                        // Try to acquire leadership
                        var acquired = await TryAcquireManagerRoleAsync(stoppingToken);
                        if (acquired)
                        {
                            attemptCount = 0; // Reset attempt counter on success
                            leadershipStartTime = _timeProvider.GetUtcNow();
                            _logger.LogElectionLeadershipAcquired(_electionState.InstanceId);
                        }
                        else
                        {
                            // Exponential backoff with jitter
                            attemptCount++;
                            var backoffDelay = CalculateBackoffDelay(attemptCount);
                            _logger.LogElectionBackoffAttempt(_electionState.InstanceId, attemptCount, backoffDelay);

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
                            _logger.LogElectionVoluntaryYield(_electionState.InstanceId, leadershipDuration);

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
                                _logger.LogElectionLeadershipLost(_electionState.InstanceId, "Lost advisory lock during leadership");
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
                    _logger.LogElectionServiceError(_electionState.InstanceId, ex);

                    // Release leadership if we had it
                    if (_electionState.IsManager)
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
            _logger.LogElectionServiceStopped(_electionState.InstanceId);
        }
        finally
        {
            if (_electionState.IsManager)
            {
                await ReleaseManagerRoleAsync(stoppingToken);
            }
            _logger.LogElectionServiceStopped(_electionState.InstanceId);
        }
    }

    private async Task<bool> TryAcquireManagerRoleAsync(CancellationToken cancellationToken = default)
    {
        await _electionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_electionState.IsManager)
            {
                return true; // Already the manager
            }

            var acquired = await TryAcquireAdvisoryLockAsync(cancellationToken);
            if (acquired)
            {
                await _electionState.BecomeManagerAsync(cancellationToken);
                return true;
            }

            return false;
        }
        finally
        {
            _electionSemaphore.Release();
        }
    }

    private async Task ReleaseManagerRoleAsync(CancellationToken cancellationToken = default)
    {
        await _electionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!_electionState.IsManager)
            {
                return; // Not the manager
            }

            await ReleaseAdvisoryLockAsync(cancellationToken);
            await _electionState.LoseManagerAsync(cancellationToken);
        }
        finally
        {
            _electionSemaphore.Release();
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
                _logger.LogElectionAdvisoryLockAcquired(_electionState.InstanceId);
            }
            else
            {
                _logger.LogElectionAdvisoryLockFailed(_electionState.InstanceId);
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
                "3. Or disable infrastructure creation: Set CreateInfrastructure=false on all but one instance\n" +
                "4. Manually coordinate cleanup: Choose which instances handle automatic cleanup\n" +
                "   using EnableAutomaticCleanup flag in your configuration",
                _electionState.InstanceId);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogElectionServiceError(_electionState.InstanceId, ex);
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

            _logger.LogElectionLockVerification(_electionState.InstanceId, _lockOptions.AdvisoryLockKey, stillHoldsLock);

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
        catch (PostgresException ex) when (ex.SqlState == "42501") // Insufficient privilege
        {
            _logger.LogError(ex,
                "Advisory lock permission denied for instance {InstanceId} during lock verification. " +
                "Automatic coordination disabled. For multi-instance deployments:\n" +
                "1. Grant permissions: GRANT EXECUTE ON FUNCTION pg_try_advisory_lock(bigint), " +
                "   pg_advisory_unlock(bigint), pg_advisory_lock(bigint), " +
                "   pg_try_advisory_lock_shared(bigint), pg_advisory_unlock_shared(bigint) TO user\n" +
                "2. Or disable coordination: Set EnableManagerElection=false\n" +
                "3. Or disable infrastructure creation: Set CreateInfrastructure=false on all but one instance\n" +
                "4. Manually coordinate cleanup: Choose which instances handle automatic cleanup\n" +
                "   using EnableAutomaticCleanup flag in your configuration",
                _electionState.InstanceId);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogElectionServiceError(_electionState.InstanceId, ex);
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
        catch (PostgresException ex) when (ex.SqlState == "42501") // Insufficient privilege
        {
            _logger.LogWarning(ex,
                "Advisory lock permission denied for instance {InstanceId} during lock release. " +
                "Lock cleanup may be incomplete. For multi-instance deployments:\n" +
                "1. Grant permissions: GRANT EXECUTE ON FUNCTION pg_try_advisory_lock(bigint), " +
                "   pg_advisory_unlock(bigint), pg_advisory_lock(bigint), " +
                "   pg_try_advisory_lock_shared(bigint), pg_advisory_unlock_shared(bigint) TO user\n" +
                "2. Or disable coordination: Set EnableManagerElection=false",
                _electionState.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogElectionServiceError(_electionState.InstanceId, ex);
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

    public override void Dispose()
    {
        try
        {
            _lockConnection?.Dispose();
            _electionSemaphore?.Dispose();
            _logger.LogElectionServiceDisposed(_electionState.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogElectionServiceError(_electionState.InstanceId, ex);
        }
        finally
        {
            base.Dispose();
        }
    }
}
