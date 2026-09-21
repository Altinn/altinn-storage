#nullable disable

using System;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A data element that the caller has permission to read. It also holds the instance and the
/// application metadata that are necessary to open the content.
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
    /// Tells if the system generates the content for each request. If this value is false, the
    /// system reads the content from blob storage. The blob storage path identifies a migrated
    /// Altinn 2 element.
    /// </summary>
    public bool IsOnDemandContent =>
        DataElement.BlobStoragePath.StartsWith(
            OnDemandBlobStoragePathPrefix,
            StringComparison.Ordinal
        );
}
