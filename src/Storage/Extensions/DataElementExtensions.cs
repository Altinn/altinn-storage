#nullable disable

using System;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Extensions;

/// <summary>
/// Explicit mappings between storage data elements and the HTTP API model.
/// </summary>
internal static class DataElementExtensions
{
    /// <summary>
    /// Maps a storage data element to the API model. Nested mutable values are intentionally shared.
    /// </summary>
    internal static DataElement ToApiModel(this DataElementInternal dataElement)
    {
        ArgumentNullException.ThrowIfNull(dataElement);

        return new DataElement
        {
            Id = dataElement.Id,
            InstanceGuid = dataElement.InstanceGuid,
            DataType = dataElement.DataType,
            Filename = dataElement.Filename,
            ContentType = dataElement.ContentType,
            BlobStoragePath = dataElement.BlobStoragePath,
            Size = dataElement.Size,
            ContentHash = dataElement.ContentHash,
            Locked = dataElement.Locked,
            Refs = dataElement.Refs,
            IsRead = dataElement.IsRead,
            Tags = dataElement.Tags,
            UserDefinedMetadata = dataElement.UserDefinedMetadata,
            Metadata = dataElement.Metadata,
            DeleteStatus = dataElement.DeleteStatus,
            FileScanResult = dataElement.FileScanResult,
            References = dataElement.References,
            Created = dataElement.Created,
            CreatedBy = dataElement.CreatedBy,
            LastChanged = dataElement.LastChanged,
            LastChangedBy = dataElement.LastChangedBy,
        };
    }

    /// <summary>
    /// Maps an API data element entering storage to the domain model. Nested mutable values are intentionally shared.
    /// </summary>
    internal static DataElementInternal FromApiModel(this DataElement dataElement)
    {
        ArgumentNullException.ThrowIfNull(dataElement);

        return new DataElementInternal
        {
            Id = dataElement.Id,
            InstanceGuid = dataElement.InstanceGuid,
            DataType = dataElement.DataType,
            Filename = dataElement.Filename,
            ContentType = dataElement.ContentType,
            BlobStoragePath = dataElement.BlobStoragePath,
            Size = dataElement.Size,
            ContentHash = dataElement.ContentHash,
            Locked = dataElement.Locked,
            Refs = dataElement.Refs,
            IsRead = dataElement.IsRead,
            Tags = dataElement.Tags,
            UserDefinedMetadata = dataElement.UserDefinedMetadata,
            Metadata = dataElement.Metadata,
            DeleteStatus = dataElement.DeleteStatus,
            FileScanResult = dataElement.FileScanResult,
            References = dataElement.References,
            Created = dataElement.Created,
            CreatedBy = dataElement.CreatedBy,
            LastChanged = dataElement.LastChanged,
            LastChangedBy = dataElement.LastChangedBy,
        };
    }
}
