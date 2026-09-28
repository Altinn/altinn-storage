using System;
using System.Collections.Generic;
using System.Linq;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Hard-deleted data element metadata needed by final cleanup.
/// </summary>
public sealed record DeletedDataElementInternal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeletedDataElementInternal"/> class.
    /// </summary>
    public DeletedDataElementInternal(
        DataElementInternal dataElement,
        IEnumerable<BlobVersionReferencesInternal>? blobVersions
    )
    {
        DataElement = dataElement ?? throw new ArgumentNullException(nameof(dataElement));
        BlobVersions =
            blobVersions?.Where(version => version.BlobVersionIds.Count > 0).ToArray() ?? [];
    }

    /// <summary>
    /// Gets the hard-deleted data element metadata.
    /// </summary>
    public DataElementInternal DataElement { get; }

    /// <summary>
    /// Gets all allocated blob versions for the data element grouped by storage context.
    /// </summary>
    public IReadOnlyList<BlobVersionReferencesInternal> BlobVersions { get; }
}
