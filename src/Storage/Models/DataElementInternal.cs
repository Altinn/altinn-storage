using System;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Internal data element metadata with storage-only fields.
/// </summary>
public sealed record DataElementInternal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataElementInternal"/> class.
    /// </summary>
    public DataElementInternal(DataElement dataElement, string? blobVersionId)
    {
        DataElement = dataElement ?? throw new ArgumentNullException(nameof(dataElement));
        BlobVersionId = blobVersionId;
    }

    /// <summary>
    /// Gets the public data element metadata.
    /// </summary>
    public DataElement DataElement { get; }

    /// <summary>
    /// Gets the current blob version id.
    /// </summary>
    public string? BlobVersionId { get; }
}
