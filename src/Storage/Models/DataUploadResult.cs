using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Result from uploading blob content and creating data element metadata.
/// </summary>
/// <param name="DataElement">The created data element.</param>
/// <param name="BlobTimestamp">When the blob upload completed.</param>
/// <param name="Versions">The instance versions after the data element was created.</param>
/// <param name="StorageAccount">The storage account the blob was written to.</param>
public sealed record DataUploadResult(
    DataElementInternal DataElement,
    DateTimeOffset BlobTimestamp,
    StorageVersions Versions,
    StorageAccount StorageAccount
);
