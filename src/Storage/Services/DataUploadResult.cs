#nullable enable

using System;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Result from uploading blob content and creating data element metadata.
/// </summary>
public sealed record DataUploadResult(
    DataElementInternal DataElement,
    DateTimeOffset BlobTimestamp,
    StorageVersions Versions
);
