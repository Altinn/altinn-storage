#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
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

/// <summary>
/// Internal aggregate mutation prepared by the controller after blob staging.
/// </summary>
public sealed record InstanceMutationCommit(
    IReadOnlyList<DataElementInternal> CreateDataElements,
    IReadOnlyList<InstanceMutationDataElementUpdate> UpdateDataElements,
    IReadOnlyList<InstanceMutationDataElementDelete> DeleteDataElements,
    InstanceInternal InstanceUpdates,
    IReadOnlyList<string> InstanceUpdateProperties,
    int? ExpectedInstanceVersion,
    int? ExpectedProcessStateVersion,
    IReadOnlyList<InstanceEvent> InstanceEvents = null,
    Guid? IdempotencyKey = null,
    DateTime? LastChanged = null,
    string LastChangedBy = null
);

/// <summary>
/// Result from applying an aggregate mutation.
/// </summary>
public sealed record InstanceMutationApplyResult(
    bool Replayed,
    IReadOnlyList<string> CreatedDataElementIds,
    InstanceInternal Instance = null
);

/// <summary>
/// Internal data element update prepared by the controller after blob staging.
/// </summary>
public sealed record InstanceMutationDataElementUpdate(
    Guid DataElementId,
    Dictionary<string, object> Properties,
    string ExpectedCurrentBlobVersion,
    bool IgnoreLock = false
);

/// <summary>
/// Internal data element delete prepared by the controller.
/// </summary>
public sealed record InstanceMutationDataElementDelete(
    DataElementInternal DataElement,
    bool IgnoreLock = false
);
