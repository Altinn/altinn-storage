#nullable disable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// This interface describes the required methods and features of a data service implementation.
/// </summary>
public interface IDataService
{
    /// <summary>
    /// Trigger malware scan of the blob associated with the given data element.
    /// </summary>
    /// <param name="instance">The metadata document for the parent instance for the data element.</param>
    /// <param name="dataType">
    /// The data type properties document for the data type of the blob to be scanned for malware.
    /// </param>
    /// <param name="dataElement">The data element metadata document.</param>
    /// <param name="blobTimestamp">Timestamp when blob upload completed.</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <param name="ct">A cancellation token should the request be cancelled.</param>
    /// <returns>A task representing the asynconous call to file scan service.</returns>
    Task StartFileScan(
        InstanceInternal instance,
        DataType dataType,
        DataElementInternal dataElement,
        DateTimeOffset blobTimestamp,
        int? storageAccountNumber,
        CancellationToken ct
    );

    /// <summary>
    /// Create SHA-256 hash of the blob associated with the given data element.
    /// </summary>
    /// <param name="org">The application owner id.</param>
    /// <param name="instanceGuid">the instance guid.</param>
    /// <param name="dataElementId">The data element guid.</param>
    /// <param name="application">The application the instance belongs to.</param>
    Task<(string FileHash, ServiceError ServiceError)> GenerateSha256Hash(
        string org,
        Guid instanceGuid,
        Guid dataElementId,
        Application application
    );

    /// <summary>
    /// Uploads a blob and creates data element metadata for its first explicit blob version.
    /// </summary>
    /// <param name="instance">The internal instance the data element belongs to.</param>
    /// <param name="stream">Data to be written to blob storage.</param>
    /// <param name="options">Metadata to use when creating the data element.</param>
    /// <param name="instanceInternalId">The internal id of the parent instance.</param>
    /// <param name="application">The application the instance belongs to, selecting the storage account to write to.</param>
    /// <param name="expectedInstanceVersion">Expected instance version for optimistic concurrency checks.</param>
    /// <param name="expectedProcessStateVersion">Expected process state version for optimistic concurrency checks.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations.</param>
    /// <returns>
    /// The created data element with internal blob fields, the blob timestamp and the storage account written to.
    /// </returns>
    Task<DataUploadResult> UploadDataAndCreateDataElement(
        InstanceInternal instance,
        Stream stream,
        DataElementCreateOptions options,
        long instanceInternalId,
        Application application,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Uploads a blob to the storage account new blobs of the application are written to, and allocates
    /// its explicit blob version without creating metadata.
    /// </summary>
    Task<StagedDataElementBlob> StageDataElementBlob(
        InstanceInternal instance,
        Stream stream,
        DataElementCreateOptions options,
        Application application,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Best-effort cleanup for a staged blob whose metadata was not committed.
    /// </summary>
    /// <param name="instance">The instance the blob was staged for.</param>
    /// <param name="dataElement">The staged data element.</param>
    /// <param name="storageAccount">The storage account the blob was staged in.</param>
    Task DeleteStagedDataElementBlob(
        InstanceInternal instance,
        DataElementInternal dataElement,
        StorageAccount storageAccount
    );

    /// <summary>
    /// Best-effort cleanup for blobs belonging to data element metadata that has already been deleted.
    /// </summary>
    /// <remarks>
    /// Detached explicit blob-version rows are removed only after their physical blob has been deleted.
    /// Legacy non-versioned blobs are deleted directly because they have no blob-version row to retry from.
    /// </remarks>
    /// <param name="instance">The instance the data element belonged to.</param>
    /// <param name="dataElement">The deleted data element.</param>
    /// <param name="storageAccountNumber">The application's storage account number, locating a legacy blob.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations.</param>
    Task CleanupDeletedDataElementBlobs(
        InstanceInternal instance,
        DataElementInternal dataElement,
        int storageAccountNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Best-effort delete of the legacy non-versioned blob of data element metadata that has already been deleted.
    /// </summary>
    /// <remarks>
    /// Aggregate mutations use this operation to keep the element's original blob versions readable for workflow retries.
    /// Orphan cleanup removes those detached versions after its grace period. Legacy blobs have no blob-version row
    /// for orphan cleanup, so they are deleted here.
    /// </remarks>
    /// <param name="instance">The instance the data element belonged to.</param>
    /// <param name="dataElement">The deleted data element.</param>
    /// <param name="storageAccountNumber">The application's storage account number, locating the legacy blob.</param>
    Task DeleteLegacyDataElementBlob(
        InstanceInternal instance,
        DataElementInternal dataElement,
        int storageAccountNumber
    );
}
