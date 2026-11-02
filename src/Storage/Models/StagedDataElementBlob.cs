using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Result from staging blob content before metadata is committed.
/// </summary>
/// <param name="DataElement">The data element to commit, pointing at the staged blob version.</param>
/// <param name="BlobTimestamp">When the blob upload completed.</param>
/// <param name="StorageAccount">The storage account the blob was written to.</param>
public sealed record StagedDataElementBlob(
    DataElementInternal DataElement,
    DateTimeOffset BlobTimestamp,
    StorageAccount StorageAccount
);
