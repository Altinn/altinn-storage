using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Internal data element update prepared by the controller after blob staging.
/// </summary>
public sealed record DataElementUpdate(
    Guid DataElementId,
    InstanceMutationDataElementChanges Changes,
    Guid? ExpectedCurrentBlobVersion,
    bool IgnoreLock = false,
    Guid? NewBlobVersion = null
);
