#nullable disable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Describes the implementation of a blob storage.
/// </summary>
public interface IBlobRepository
{
    /// <summary>
    /// Create a new file in blob storage.
    /// </summary>
    /// <param name="org">The application owner id.</param>
    /// <param name="stream">Data to be written to blob storage.</param>
    /// <param name="blobStoragePath">Path to save the stream to in blob storage.</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <returns>The size of the blob, last modified timestamp, and the blob version ID assigned by Azure Blob Storage.</returns>
    Task<(long ContentLength, DateTimeOffset LastModified, string VersionId)> WriteBlob(
        string org,
        Stream stream,
        string blobStoragePath,
        int? storageAccountNumber
    );

    /// <summary>
    /// Reads a data file from blob storage
    /// </summary>
    /// <param name="org">The application owner id.</param>
    /// <param name="blobStoragePath">Path to be file to read blob storage.</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <param name="versionId">Optional blob version ID. When provided, reads the specific version instead of the current blob.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The stream with the file</returns>
    Task<Stream> ReadBlob(
        string org,
        string blobStoragePath,
        int? storageAccountNumber,
        string versionId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes the current blob, or a specific blob version when <paramref name="versionId"/> is supplied.
    /// </summary>
    /// <remarks>
    /// With blob versioning enabled, deleting without a version ID does not purge prior versions.
    /// </remarks>
    /// <param name="org">The application owner id.</param>
    /// <param name="blobStoragePath">Path to the file to delete.</param>
    /// <param name="storageAccountNumber">Alternate number to append to container name</param>
    /// <param name="versionId">Optional blob version ID. When provided, deletes only the specific version instead of the current blob.</param>
    /// <returns>A value indicating whether the delete was successful.</returns>
    Task<bool> DeleteBlob(
        string org,
        string blobStoragePath,
        int? storageAccountNumber,
        string versionId = null
    );

    /// <summary>
    /// Deletes current blobs associated with a given instance.
    /// </summary>
    /// <remarks>
    /// With blob versioning enabled, prior versions are retained.
    /// </remarks>
    /// <param name="instance">The instance to delete from</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <returns>A value indicating whether the delete was successful.</returns>///
    Task<bool> DeleteDataBlobs(Instance instance, int? storageAccountNumber);
}
