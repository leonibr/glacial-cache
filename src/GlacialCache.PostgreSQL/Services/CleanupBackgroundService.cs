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
internal class CleanupBackgroundService : BackgroundService
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
        catch (Exception ex)
        {
            _logger.LogCleanupError(ex);
        }
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
