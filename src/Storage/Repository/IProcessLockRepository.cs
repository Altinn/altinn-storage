#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// The repository to handle process locks
/// </summary>
public interface IProcessLockRepository
{
    /// <summary>
    /// Attempts to acquire a process lock for an instance
    /// </summary>
    /// <param name="instanceInternalId">The instance internal ID</param>
    /// <param name="ttlSeconds">Lock time to live in seconds</param>
    /// <param name="userId">The ID of the user acquiring the lock</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The lock ID if successful, null otherwise</returns>
    Task<Guid?> TryAcquireLock(
        long instanceInternalId,
        int ttlSeconds,
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates the expiration of an existing process lock
    /// </summary>
    /// <param name="lockId">The lock ID</param>
    /// <param name="ttlSeconds">New time to live in seconds</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>True if the lock was updated, false otherwise</returns>
    Task<bool> UpdateLockExpiration(
        Guid lockId,
        int ttlSeconds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the details of a lock
    /// </summary>
    /// <param name="lockId">The lock ID</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The lock details if they exist, null otherwise</returns>
    Task<ProcessLock?> Get(Guid lockId, CancellationToken cancellationToken = default);
}
