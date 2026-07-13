#nullable disable

using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using TextJson = System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Mutable data element metadata used by storage.
/// </summary>
public sealed class DataElementInternal
{
    /// <summary>
    /// Gets or sets the unique data element identifier.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the owning instance.
    /// </summary>
    public string InstanceGuid { get; set; }

    /// <summary>
    /// Gets or sets the data type.
    /// </summary>
    public string DataType { get; set; }

    /// <summary>
    /// Gets or sets the filename.
    /// </summary>
    public string Filename { get; set; }

    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// Gets or sets the blob storage path.
    /// </summary>
    public string BlobStoragePath { get; set; }

    /// <summary>
    /// Gets or sets the content size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the content hash.
    /// </summary>
    public string ContentHash { get; set; }

    /// <summary>
    /// Gets or sets whether the data element is locked.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Gets or sets referenced data element identifiers.
    /// </summary>
    public List<Guid> Refs { get; set; }

    /// <summary>
    /// Gets or sets whether the element has been read.
    /// </summary>
    public bool IsRead { get; set; } = true;

    /// <summary>
    /// Gets or sets data element tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets user-defined metadata.
    /// </summary>
    public List<KeyValueEntry> UserDefinedMetadata { get; set; }

    /// <summary>
    /// Gets or sets application-defined metadata.
    /// </summary>
    public List<KeyValueEntry> Metadata { get; set; }

    /// <summary>
    /// Gets or sets the delete status.
    /// </summary>
    public DeleteStatus DeleteStatus { get; set; }

    /// <summary>
    /// Gets or sets the file scan result.
    /// </summary>
    public FileScanResult FileScanResult { get; set; }

    /// <summary>
    /// Gets or sets references to other storage objects.
    /// </summary>
    public List<Reference> References { get; set; }

    /// <summary>
    /// Gets or sets when the element was created.
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// Gets or sets who created the element.
    /// </summary>
    public string CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets when the element was last changed.
    /// </summary>
    public DateTime? LastChanged { get; set; }

    /// <summary>
    /// Gets or sets who last changed the element.
    /// </summary>
    public string LastChangedBy { get; set; }

    /// <summary>
    /// Gets or sets the current blob version identifier.
    /// </summary>
    [TextJson.JsonIgnore]
    public string BlobVersionId { get; set; }
}
