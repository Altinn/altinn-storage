using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Messages;
using Npgsql;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Interface to talk to the outbox repository
/// </summary>
public interface IOutboxRepository
{
    /// <summary>
    /// Insert outbox message
    /// </summary>
    Task Insert(SyncInstanceToDialogportenCommand dp, NpgsqlConnection existingConnection);

    /// <summary>
    /// Polls the outbox for messages to be processed.
    /// </summary>
    /// <returns>
    /// A list of <see cref="SyncInstanceToDialogportenCommand"/> messages that are pending processing.
    /// </returns>
    Task<List<SyncInstanceToDialogportenCommand>> Poll(int maxRows);

    /// <summary>
    /// Deletes an outbox message by its instance identifier.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the instance to delete from the outbox.</param>
    Task Delete(Guid instanceId);

    /// <summary>
    /// Attempts to acquire a lease for a specific resource.
    /// </summary>
    /// <param name="resource">The name of the resource to acquire a lease for.</param>
    /// <param name="holder">The unique identifier of the lease holder.</param>
    /// <param name="leaseExpires">The duration for which the lease should be held.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains <c>true</c> if the lease was acquired; otherwise, <c>false</c>.
    /// </returns>
    Task<bool> TryAcquireLeaseAsync(string resource, Guid holder, DateTime leaseExpires);

    /// <summary>
    /// Attempts to renew a lease for a specific resource.
    /// </summary>
    /// <param name="resource">The name of the resource for which the lease should be renewed.</param>
    /// <param name="holder">The unique identifier of the lease holder.</param>
    /// <param name="leaseExpires">The duration for which the lease should be renewed.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains <c>true</c> if the lease was successfully renewed; otherwise, <c>false</c>.
    /// </returns>
    Task<bool> RenewLeaseAsync(string resource, Guid holder, DateTime leaseExpires);

    /// <summary>
    /// Releases a lease for a specific resource held by the specified holder.
    /// </summary>
    /// <param name="resource">The name of the resource for which the lease should be released.</param>
    /// <param name="holder">The unique identifier of the lease holder.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains <c>true</c> if the lease was successfully released; otherwise, <c>false</c>.
    /// </returns>
    Task<bool> ReleaseLeaseAsync(string resource, Guid holder);
}
