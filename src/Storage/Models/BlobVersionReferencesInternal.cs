using System;
using System.Collections.Generic;
using System.Linq;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Blob version metadata needed to locate and clean up versioned blobs.
/// </summary>
public sealed record BlobVersionReferencesInternal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BlobVersionReferencesInternal"/> class.
    /// </summary>
    public BlobVersionReferencesInternal(
        Guid instanceGuid,
        string appId,
        string blobStorageOrg,
        int? storageAccountNumber,
        IEnumerable<string>? blobVersionIds
    )
    {
        InstanceGuid = instanceGuid;
        AppId = appId ?? throw new ArgumentNullException(nameof(appId));
        BlobStorageOrg = blobStorageOrg ?? throw new ArgumentNullException(nameof(blobStorageOrg));
        StorageAccountNumber = storageAccountNumber;
        BlobVersionIds =
            blobVersionIds
                ?.Where(versionId => !string.IsNullOrEmpty(versionId))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            ?? [];
    }

    /// <summary>
    /// Gets the instance guid stored with the blob version rows.
    /// </summary>
    public Guid InstanceGuid { get; }

    /// <summary>
    /// Gets the application id used as the blob path prefix.
    /// </summary>
    public string AppId { get; }

    /// <summary>
    /// Gets the org used to locate the blob container/account.
    /// </summary>
    public string BlobStorageOrg { get; }

    /// <summary>
    /// Gets the storage account number, if any.
    /// </summary>
    public int? StorageAccountNumber { get; }

    /// <summary>
    /// Gets the blob version ids in this storage context.
    /// </summary>
    public IReadOnlyList<string> BlobVersionIds { get; }
}
