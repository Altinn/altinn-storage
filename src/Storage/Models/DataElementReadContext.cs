#nullable disable

using System;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A data element the caller has been authorized to read, together with everything needed to open
/// its content.
/// </summary>
/// <param name="InstanceGuid">The instance id the element was requested through.</param>
/// <param name="DataGuid">The data element id the element was requested through.</param>
/// <param name="Instance">The instance holding the data element.</param>
/// <param name="DataElement">The data element metadata.</param>
/// <param name="Application">The application the instance belongs to.</param>
public sealed record DataElementReadContext(
    Guid InstanceGuid,
    Guid DataGuid,
    InstanceInternal Instance,
    DataElementInternal DataElement,
    Application Application
)
{
    private const string OnDemandBlobStoragePathPrefix = "ondemand";

    /// <summary>
    /// Whether the content is generated per request rather than read from blob storage. Migrated
    /// Altinn 2 elements say so through their blob storage path.
    /// </summary>
    public bool IsOnDemandContent =>
        DataElement.BlobStoragePath.StartsWith(
            OnDemandBlobStoragePathPrefix,
            StringComparison.Ordinal
        );
}
