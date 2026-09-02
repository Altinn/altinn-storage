using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Result from uploading blob content and creating data element metadata.
/// </summary>
public sealed record DataUploadResult(
    DataElementInternal DataElement,
    DateTimeOffset BlobTimestamp,
    StorageVersions Versions
);
