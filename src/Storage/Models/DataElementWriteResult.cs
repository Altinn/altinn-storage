namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Result from a data-element write that also changed or observed parent instance versions.
/// </summary>
public sealed record DataElementWriteResult(
    DataElementInternal DataElement,
    StorageVersions Versions
);
