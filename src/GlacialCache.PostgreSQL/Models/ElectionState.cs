using System.Threading;
using Microsoft.Extensions.Logging;
using GlacialCache.Logging;

namespace GlacialCache.PostgreSQL.Models;

/// <summary>
/// Thread-safe state container for managing election state.
/// This class is the single source of truth for whether this instance is the manager.
/// </summary>
public class ElectionState
{
    private readonly SemaphoreSlim _stateSemaphore = new(1, 1);
    private readonly ILogger<ElectionState> _logger;
    private readonly TimeProvider _timeProvider;

    private volatile bool _isManager;
    private DateTimeOffset? _electedAt;
    private DateTimeOffset? _lostAt;

    /// <summary>
    /// Gets the unique identifier for this instance.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets whether this instance is currently the manager.
    /// </summary>
    public bool IsManager => _isManager;

    /// <summary>
    /// Gets the timestamp when this instance was last elected as manager.
    /// </summary>
    public DateTimeOffset? ElectedAt => _electedAt;

    /// <summary>
    /// Gets the timestamp when this instance last lost the manager role.
    /// </summary>
    public DateTimeOffset? LostAt => _lostAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElectionState"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="timeProvider">The time provider for timestamp operations.</param>
    /// <param name="instanceId">The unique identifier for this instance.</param>
    public ElectionState(ILogger<ElectionState> logger, TimeProvider timeProvider, string instanceId)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));

        _logger.LogElectionStateInitialized(instanceId);
    }

    /// <summary>
    /// Updates the state to indicate this instance has become the manager.
    /// This method is thread-safe.
    /// </summary>
    public async Task BecomeManagerAsync(CancellationToken cancellationToken = default)
    {
        await _stateSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isManager)
            {
                // Already the manager
                return;
            }

            _isManager = true;
            _electedAt = _timeProvider.GetUtcNow();
            _lostAt = null;

            _logger.LogElectionStateUpdated("Manager", InstanceId);
        }
        finally
        {
            _stateSemaphore.Release();
        }
    }

    /// <summary>
    /// Updates the state to indicate this instance has lost the manager role.
    /// This method is thread-safe.
    /// </summary>
    public async Task LoseManagerAsync(CancellationToken cancellationToken = default)
    {
        await _stateSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!_isManager)
            {
                // Not the manager
                return;
            }

            _isManager = false;
            _lostAt = _timeProvider.GetUtcNow();

            _logger.LogElectionStateUpdated("Non-Manager", InstanceId);
        }
        finally
        {
            _stateSemaphore.Release();
        }
    }

    /// <summary>
    /// Gets a snapshot of the current election state in a thread-safe manner.
    /// </summary>
    /// <returns>A tuple containing the current state values.</returns>
    public async Task<(bool IsManager, DateTimeOffset? ElectedAt, DateTimeOffset? LostAt)> GetStateSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _stateSemaphore.WaitAsync(cancellationToken);
        try
        {
            return (_isManager, _electedAt, _lostAt);
        }
        finally
        {
            _stateSemaphore.Release();
        }
    }

    /// <summary>
    /// Performs thread-safe state update operations.
    /// </summary>
    /// <param name="updateAction">The action to perform on the state within the semaphore lock.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public async Task UpdateStateAsync(Action<ElectionStateUpdater> updateAction, CancellationToken cancellationToken = default)
    {
        await _stateSemaphore.WaitAsync(cancellationToken);
        try
        {
            var updater = new ElectionStateUpdater(this);
            updateAction(updater);

            // Log state changes if they occurred
            _logger.LogElectionStateUpdated(_isManager ? "Manager" : "Non-Manager", InstanceId);
        }
        finally
        {
            _stateSemaphore.Release();
        }
    }

    /// <summary>
    /// Helper class for updating election state within a semaphore lock.
    /// </summary>
    public class ElectionStateUpdater
    {
        private readonly ElectionState _electionState;

        internal ElectionStateUpdater(ElectionState electionState)
        {
            _electionState = electionState;
        }

        /// <summary>
        /// Sets this instance as the manager.
        /// </summary>
        public void BecomeManager()
        {
            _electionState._isManager = true;
            _electionState._electedAt = _electionState._timeProvider.GetUtcNow();
            _electionState._lostAt = null;
        }

        /// <summary>
        /// Removes manager role from this instance.
        /// </summary>
        public void LoseManager()
        {
            _electionState._isManager = false;
            _electionState._lostAt = _electionState._timeProvider.GetUtcNow();
        }
    }

    /// <summary>
    /// Disposes resources used by the election state.
    /// </summary>
    public void Dispose()
    {
        _stateSemaphore?.Dispose();
        _logger.LogElectionServiceDisposed(InstanceId);
    }
}

