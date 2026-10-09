#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Commits a batch of Storage-visible mutations for one instance.
/// </summary>
public interface IInstanceMutationRepository
{
    /// <summary>
    /// Admits a replay when an idempotent aggregate mutation has already been committed.
    /// Returns the replayed result with an instance snapshot, or throws when replay cannot be admitted.
    /// </summary>
    Task<InstanceMutationApplyResult> TryReplayAdmission(
        Guid instanceGuid,
        int expectedInstanceVersion,
        int currentInstanceVersion,
        int currentProcessStateVersion,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes aggregate mutation idempotency records created before the supplied timestamp.
    /// </summary>
    Task<int> DeleteIdempotencyRecordsCreatedBefore(
        DateTime createdBeforeUtc,
        int batchSize = 10_000,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Applies the mutations in one database transaction.
    /// </summary>
    Task<InstanceMutationApplyResult> Apply(
        Guid instanceGuid,
        long instanceInternalId,
        InstanceMutationCommit mutation,
        CancellationToken cancellationToken = default
    );
}
