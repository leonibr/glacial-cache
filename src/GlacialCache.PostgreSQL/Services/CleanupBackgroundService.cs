using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GlacialCache.PostgreSQL.Services;

using Abstractions;
using Configuration;
using Models;
using Logging;

/// <summary>
/// Simple background service for periodic cleanup of expired cache entries.
/// </summary>
internal class CleanupBackgroundService : BackgroundService, ICleanupBackgroundService
{
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly ILogger<CleanupBackgroundService> _logger;
    private readonly IPostgreSQLDataSource _dataSource;
    private readonly IDbRawCommands _dbRawCommands;
    private readonly ElectionState? _electionState;
    private readonly TimeProvider _timeProvider;
    private readonly PeriodicTimer _cleanupTimer;

    public CleanupBackgroundService(
        IOptionsMonitor<GlacialCachePostgreSQLOptions> options,
        ILogger<CleanupBackgroundService> logger,
        IPostgreSQLDataSource dataSource,
        IDbRawCommands dbRawCommands,
        ElectionState? electionState,
        TimeProvider timeProvider)
    {
        _options = options.CurrentValue;
        _logger = logger;
        _dataSource = dataSource;
        _dbRawCommands = dbRawCommands;
        _electionState = electionState;
        _timeProvider = timeProvider;
        _cleanupTimer = new PeriodicTimer(_options.Maintenance.CleanupInterval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check if this instance should perform cleanup based on manager election
        if (_options.Infrastructure.EnableManagerElection &&
            (_electionState == null || !_electionState.IsManager))
        {
            _logger.LogCleanupServiceSkipped();
            return;
        }

        _logger.LogCleanupServiceStarted(_options.Maintenance.CleanupInterval.TotalMinutes);

        try
        {
            while (await _cleanupTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await ExecuteCleanupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogCleanupServiceStopping();
        }
        catch (Exception ex)
        {
            _logger.LogCleanupServiceError(ex);
        }
    }

    private async Task ExecuteCleanupAsync(CancellationToken token)
    {
        // Early return if cancellation requested
        if (token.IsCancellationRequested)
            return;

        try
        {
            await using var connection = await _dataSource.GetConnectionAsync(token);

            await using var command = new NpgsqlCommand(_dbRawCommands.CleanupExpiredSql, connection);
            command.Parameters.AddWithValue("@now", _timeProvider.GetUtcNow());

            var deletedCount = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            if (deletedCount > 0)
            {
                _logger.LogCleanupCompleted(deletedCount);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown - log at debug level
            _logger.LogDebug("Cleanup operation cancelled during shutdown");
        }
        catch (Exception ex) when (IsShutdownException(ex))
        {
            // Expected shutdown scenario - log at debug level
            _logger.LogDebug(ex, "Cleanup operation interrupted during shutdown");
        }
        catch (Exception ex)
        {
            // Actual error - log at error level
            _logger.LogCleanupError(ex);
        }
    }

    private static bool IsShutdownException(Exception ex)
    {
        return ex is ObjectDisposedException ||
               (ex is NpgsqlException npgsqlEx &&
                npgsqlEx.InnerException is System.IO.EndOfStreamException);
    }

    public override void Dispose()
    {
        try
        {
            _cleanupTimer?.Dispose();
            _logger?.LogCleanupServiceDisposed();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing CleanupBackgroundService");
        }

        base.Dispose();
    }
}
