#nullable disable

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Internal data element delete prepared by the controller.
/// </summary>
public sealed record InstanceMutationDataElementDelete(
    DataElementInternal DataElement,
    bool IgnoreLock = false
);
