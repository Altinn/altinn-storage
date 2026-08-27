using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Result from staging blob content before metadata is committed.
/// </summary>
public sealed record StagedDataElementBlob(
    DataElementInternal DataElement,
    DateTimeOffset BlobTimestamp
);
