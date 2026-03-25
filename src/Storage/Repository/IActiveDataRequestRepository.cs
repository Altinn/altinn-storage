#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Repository for tracking active data mutation requests.
/// </summary>
public interface IActiveDataRequestRepository
{
    /// <summary>
    /// Attempts to begin a data mutation. Inserts a tracking row if the mutation is allowed.
    /// </summary>
    /// <param name="instanceGuid">The instance GUID.</param>
    /// <param name="lockToken">The lock token provided by the caller, or null if no token was provided.</param>
    /// <param name="timeout">The timeout for the tracking row (safety net for crashes).</param>
    /// <param name="cancellationToken">CancellationToken.</param>
    /// <returns>The result status and the request ID if successful.</returns>
    Task<(BeginMutationStatus Status, long? RequestId)> BeginDataMutation(
        Guid instanceGuid,
        LockToken? lockToken,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Ends a data mutation by removing the tracking row.
    /// </summary>
    /// <param name="requestId">The request ID returned by <see cref="BeginDataMutation"/>.</param>
    /// <param name="cancellationToken">CancellationToken.</param>
    Task EndDataMutation(long requestId, CancellationToken cancellationToken = default);
}
