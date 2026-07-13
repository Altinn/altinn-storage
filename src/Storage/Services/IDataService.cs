#nullable disable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

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
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    Task<(string FileHash, ServiceError ServiceError)> GenerateSha256Hash(
        string org,
        Guid instanceGuid,
        Guid dataElementId,
        int? storageAccountNumber
    );

    /// <summary>
    /// Uploads a blob and creates data element metadata for its first explicit blob version.
    /// </summary>
    /// <param name="instance">The internal instance the data element belongs to.</param>
    /// <param name="stream">Data to be written to blob storage.</param>
    /// <param name="options">Metadata to use when creating the data element.</param>
    /// <param name="instanceInternalId">The internal id of the parent instance.</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations.</param>
    /// <param name="expectedInstanceVersion">Expected instance version for optimistic concurrency checks.</param>
    /// <param name="expectedProcessStateVersion">Expected process state version for optimistic concurrency checks.</param>
    /// <returns>The created data element with internal blob fields, and the blob timestamp.</returns>
    Task<DataUploadResult> UploadDataAndCreateDataElement(
        InstanceInternal instance,
        Stream stream,
        DataElementCreateOptions options,
        long instanceInternalId,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    );

    /// <summary>
    /// Delete a data element and it's blob data immediately.
    /// </summary>
    /// <param name="instance">The instance</param>
    /// <param name="dataElement">The data element</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <param name="expectedInstanceVersion">Expected instance version for optimistic concurrency checks.</param>
    /// <param name="expectedProcessStateVersion">Expected process state version for optimistic concurrency checks.</param>
    /// <returns></returns>
    Task<DataElementInternal> DeleteImmediately(
        InstanceInternal instance,
        DataElementInternal dataElement,
        int? storageAccountNumber,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    );
}
