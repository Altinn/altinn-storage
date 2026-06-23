#nullable disable

using System;
using System.Collections.Generic;
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
    /// <returns>The size of the blob and last modified timestamp.</returns>
    Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        string org,
        Stream stream,
        string blobStoragePath,
        int? storageAccountNumber
    );

    /// <summary>
    /// Reads a data file from blob storage
    /// </summary>
    /// <param name="org">The application owner id.</param>
    /// <param name="blobStoragePath">Path to the file to read from blob storage.</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The stream with the file</returns>
    Task<Stream> ReadBlob(
        string org,
        string blobStoragePath,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes a blob at the supplied concrete path.
    /// </summary>
    /// <remarks>
    /// Explicit blob versions are represented as path suffixes and should be included in <paramref name="blobStoragePath"/> by the caller.
    /// </remarks>
    /// <param name="org">The application owner id.</param>
    /// <param name="blobStoragePath">Path to the file to delete.</param>
    /// <param name="storageAccountNumber">Alternate number to append to container name</param>
    /// <returns>A value indicating whether the delete was successful.</returns>
    Task<bool> DeleteBlob(string org, string blobStoragePath, int? storageAccountNumber);

    /// <summary>
    /// Deletes multiple concrete blob paths.
    /// </summary>
    /// <param name="org">The application owner id.</param>
    /// <param name="blobStoragePaths">The blob paths to delete.</param>
    /// <param name="storageAccountNumber">Alternate number to append to container name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A value indicating whether all deletes were successful.</returns>
    Task<bool> DeleteBlobs(
        string org,
        IEnumerable<string> blobStoragePaths,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes blobs associated with a given instance.
    /// </summary>
    /// <remarks>
    /// Explicit version paths under the instance prefix are deleted by this operation.
    /// </remarks>
    /// <param name="instance">The instance to delete from</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <returns>A value indicating whether the delete was successful.</returns>///
    Task<bool> DeleteDataBlobs(Instance instance, int? storageAccountNumber);

    /// <summary>
    /// Deletes blobs under an explicit instance prefix in blob storage.
    /// </summary>
    /// <param name="org">The blob storage owner id.</param>
    /// <param name="appId">The application id used as the blob path prefix.</param>
    /// <param name="instanceGuid">The instance guid used as the blob path prefix.</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A value indicating whether the delete was successful.</returns>
    Task<bool> DeleteDataBlobs(
        string org,
        string appId,
        string instanceGuid,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    );
}
