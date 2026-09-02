using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

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
    IReadOnlyList<InstanceEvent> InstanceEvents,
    Guid? IdempotencyKey = null,
    DateTime? LastChanged = null,
    string? LastChangedBy = null
);
