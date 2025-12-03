namespace GlacialCache.PostgreSQL.Abstractions;

/// <summary>
/// Service responsible for electing a single manager instance for database operations.
/// Uses PostgreSQL advisory locks to ensure only one instance performs schema operations,
/// migrations, and maintenance tasks while allowing distributed cache operations.
/// </summary>
public interface IManagerElectionService
{
    /// <summary>
    /// Attempts to acquire the manager role using advisory locks.
    /// Returns true if this instance becomes the manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>True if this instance successfully acquired the manager role; otherwise, false.</returns>
    Task<bool> TryAcquireManagerRoleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if this instance is currently the manager.
    /// </summary>
    bool IsManager { get; }

    /// <summary>
    /// Releases the manager role and advisory lock.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task ReleaseManagerRoleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when this instance becomes the manager.
    /// </summary>
    event EventHandler<ManagerElectedEventArgs> ManagerElected;

    /// <summary>
    /// Event fired when this instance loses the manager role.
    /// </summary>
    event EventHandler<ManagerLostEventArgs> ManagerLost;
}

/// <summary>
/// Event arguments for manager election events.
/// </summary>
public class ManagerElectedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the timestamp when the instance was elected as manager.
    /// </summary>
    public DateTimeOffset ElectedAt { get; }

    /// <summary>
    /// Gets the unique identifier of the instance that was elected.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagerElectedEventArgs"/> class.
    /// </summary>
    /// <param name="electedAt">The timestamp when the instance was elected.</param>
    /// <param name="instanceId">The unique identifier of the elected instance.</param>
    public ManagerElectedEventArgs(DateTimeOffset electedAt, string instanceId)
    {
        ElectedAt = electedAt;
        InstanceId = instanceId;
    }
}

/// <summary>
/// Event arguments for manager loss events.
/// </summary>
public class ManagerLostEventArgs : EventArgs
{
    /// <summary>
    /// Gets the timestamp when the instance lost the manager role.
    /// </summary>
    public DateTimeOffset LostAt { get; }

    /// <summary>
    /// Gets the unique identifier of the instance that lost the manager role.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets the reason why the instance lost the manager role, if available.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagerLostEventArgs"/> class.
    /// </summary>
    /// <param name="lostAt">The timestamp when the instance lost the role.</param>
    /// <param name="instanceId">The unique identifier of the instance that lost the role.</param>
    /// <param name="reason">The reason for losing the role, if available.</param>
    public ManagerLostEventArgs(DateTimeOffset lostAt, string instanceId, string? reason = null)
    {
        LostAt = lostAt;
        InstanceId = instanceId;
        Reason = reason;
    }
} 