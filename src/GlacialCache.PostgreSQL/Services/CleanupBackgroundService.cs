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
internal class CleanupBackgroundService : BackgroundService, ICleanupBackgroundService, IAsyncDisposable
{
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly ILogger<CleanupBackgroundService> _logger;
    private readonly IPostgreSQLDataSource _dataSource;
    private readonly IDbRawCommands _dbRawCommands;
    private readonly ElectionState? _electionState;
    private readonly TimeProvider _timeProvider;
    private readonly PeriodicTimer _cleanupTimer;
    private bool _disposed = false;

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
        _cleanupTimer = new PeriodicTimer(_options.Maintenance.CleanupInterval, timeProvider);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogCleanupServiceStarted(_options.Maintenance.CleanupInterval.TotalMinutes);

        try
        {
            while (await _cleanupTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (_options.Infrastructure.EnableManagerElection &&
                    (_electionState == null || !_electionState.IsManager))
                {
                    _logger.LogCleanupServiceSkipped();
                    continue;
                }

                await ExecuteCleanupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via cancellation token
            _logger.LogCleanupServiceStopping();
        }
        catch (ObjectDisposedException)
        {
            // Normal shutdown via timer disposal
            _logger.LogCleanupServiceStopping();
        }
        catch (Exception ex)
        {
            // Actual error
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

            await using var command = CreateCleanupCommand(
                _dbRawCommands.CleanupExpiredSql,
                connection,
                _timeProvider.GetUtcNow(),
                _options.Maintenance.MaxCleanupBatchSize);

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

    internal static NpgsqlCommand CreateCleanupCommand(
        string sql,
        NpgsqlConnection connection,
        DateTimeOffset now,
        int maxBatchSize)
    {
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@maxBatchSize", maxBatchSize);
        return command;
    }

    private static bool IsShutdownException(Exception ex)
    {
        return ex is ObjectDisposedException ||
               (ex is NpgsqlException npgsqlEx &&
                npgsqlEx.InnerException is System.IO.EndOfStreamException);
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _cleanupTimer?.Dispose();
            _logger?.LogCleanupServiceDisposed();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing CleanupBackgroundService");
        }
        finally
        {
            base.Dispose();
        }
    }

    /// <summary>
    /// Asynchronously disposes the cleanup background service, ensuring graceful shutdown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Stop the service gracefully first
            await StopAsync(CancellationToken.None).ConfigureAwait(false);

            _cleanupTimer?.Dispose();
            _logger?.LogCleanupServiceDisposed();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error async disposing CleanupBackgroundService");
        }

        GC.SuppressFinalize(this);
    }
}
