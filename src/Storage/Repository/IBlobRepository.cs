#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Describes the implementation of a blob storage.
/// </summary>
public interface IBlobRepository
{
    /// <summary>
    /// Create a new file in blob storage.
    /// </summary>
    /// <param name="account">The storage account to write to.</param>
    /// <param name="stream">Data to be written to blob storage.</param>
    /// <param name="blobStoragePath">Path to save the stream to in blob storage.</param>
    /// <returns>The size of the blob and last modified timestamp.</returns>
    Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        StorageAccount account,
        Stream stream,
        string blobStoragePath
    );

    /// <summary>
    /// Reads a data file from blob storage
    /// </summary>
    /// <param name="account">The storage account holding the blob.</param>
    /// <param name="blobStoragePath">Path to the file to read from blob storage.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The stream with the file, or null when the blob does not exist.</returns>
    Task<Stream> ReadBlob(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes a blob at the supplied concrete path.
    /// </summary>
    /// <remarks>
    /// Explicit blob versions are represented as path suffixes and should be included in <paramref name="blobStoragePath"/> by the caller.
    /// </remarks>
    /// <param name="account">The storage account holding the blob.</param>
    /// <param name="blobStoragePath">Path to the file to delete.</param>
    /// <returns>A value indicating whether the delete was successful.</returns>
    Task<bool> DeleteBlob(StorageAccount account, string blobStoragePath);

    /// <summary>
    /// Deletes multiple concrete blob paths if they exist.
    /// </summary>
    /// <param name="account">The storage account holding the blobs.</param>
    /// <param name="blobStoragePaths">The blob paths to delete.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// An array matching <paramref name="blobStoragePaths"/> by index. A true value means the blob was deleted or already missing.
    /// </returns>
    Task<bool[]> DeleteBlobsIfExists(
        StorageAccount account,
        IReadOnlyList<string> blobStoragePaths,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes blobs under an explicit instance prefix in blob storage.
    /// </summary>
    /// <param name="account">The storage account holding the blobs.</param>
    /// <param name="appId">The application id used as the blob path prefix.</param>
    /// <param name="instanceGuid">The instance guid used as the blob path prefix.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A value indicating whether the delete was successful.</returns>
    Task<bool> DeleteDataBlobs(
        StorageAccount account,
        string appId,
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    );
}
