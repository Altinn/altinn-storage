using System;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Result from staging blob content before metadata is committed.
/// </summary>
public sealed record StagedDataElementBlob(
    DataElementInternal DataElement,
    DateTimeOffset BlobTimestamp
);
