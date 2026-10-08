using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Aggregate mutation prepared after blob staging.
/// </summary>
public sealed record InstanceMutationCommit
{
    /// <summary>
    /// Gets the shared timestamp and actor of the mutation.
    /// </summary>
    public required InstanceMutationStamp Stamp { get; init; }

    /// <summary>
    /// Gets the elements to create, in request order.
    /// </summary>
    public IReadOnlyList<DataElementCreation> CreateDataElements { get; init; } = [];

    /// <summary>
    /// Gets the changes to existing data elements.
    /// </summary>
    public IReadOnlyList<DataElementUpdate> UpdateDataElements { get; init; } = [];

    /// <summary>
    /// Gets the elements to delete.
    /// </summary>
    public IReadOnlyList<DataElementDeletion> DeleteDataElements { get; init; } = [];

    /// <summary>
    /// Gets the instance changes. Null omits the instance update; an empty change still counts as an update.
    /// </summary>
    public InstanceMutationChanges? InstanceChanges { get; init; }

    /// <summary>
    /// Gets the expected instance version, when fenced.
    /// </summary>
    public int? ExpectedInstanceVersion { get; init; }

    /// <summary>
    /// Gets the expected process state version, when fenced.
    /// </summary>
    public int? ExpectedProcessStateVersion { get; init; }

    /// <summary>
    /// Gets the instance events in persistence order.
    /// </summary>
    public IReadOnlyList<InstanceEvent> InstanceEvents { get; init; } = [];

    /// <summary>
    /// Gets the optional idempotency key.
    /// </summary>
    public Guid? IdempotencyKey { get; init; }
}
