#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Describes the implementation of a data element storage.
/// </summary>
public interface IDataRepository
{
    /// <summary>
    /// Creates a dataElement with internal storage-only fields.
    /// </summary>
    /// <param name="dataElement">the internal data element to insert</param>
    /// <param name="instanceInternalId">the internal id of the parent instance</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>the data element with internal storage-only fields</returns>
    Task<DataElementInternal> Create(
        DataElementInternal dataElement,
        long instanceInternalId = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads a data element metadata object with internal storage-only fields.
    /// </summary>
    /// <param name="instanceGuid">the instance guid as partitionKey</param>
    /// <param name="dataElementId">The data element guid</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>The identified internal data element.</returns>
    Task<DataElementInternal> Read(
        Guid instanceGuid,
        Guid dataElementId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes the data element metadata object permanently!
    /// </summary>
    /// <param name="dataElement">the element to delete</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>true if delete went well.</returns>
    Task<bool> Delete(DataElement dataElement, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the data element metadata and blob version metadata permanently during final cleanup.
    /// </summary>
    /// <param name="dataElement">the element to delete</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>true if delete went well.</returns>
    Task<bool> DeleteForCleanup(
        DataElement dataElement,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes the data elements metadata for an instance permanently!
    /// </summary>
    /// <param name="instanceId">the parent instance id of the data elements to delete</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>true if delete went well.</returns>
    Task<bool> DeleteForInstance(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the data element with the properties provided in the dictionary
    /// </summary>
    /// <param name="instanceGuid">The instance guid</param>
    /// <param name="dataElementId">The data element id</param>
    /// <param name="propertylist">A dictionary containing property id (key) and object (value) to be stored</param>
    /// <param name="context">Storage-level context and preconditions for the update.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <remarks>Dictionary can contain at most 16 entries</remarks>
    Task<DataElement> Update(
        Guid instanceGuid,
        Guid dataElementId,
        Dictionary<string, object> propertylist,
        DataElementUpdateContext context = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates the file scan status if the scanned blob version still matches current metadata.
    /// </summary>
    /// <param name="instanceGuid">The instance guid</param>
    /// <param name="dataElementId">The data element id</param>
    /// <param name="fileScanStatus">The file scan status, optionally including the scanned blob version id.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>The updated data element, or null if no row matched the supplied blob version.</returns>
    Task<DataElement> UpdateFileScanStatus(
        Guid instanceGuid,
        Guid dataElementId,
        FileScanStatus fileScanStatus,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Allocates a blob version ID before a blob upload.
    /// </summary>
    /// <param name="instanceGuid">The instance guid for the data element.</param>
    /// <param name="dataElementId">The data element id that owns the allocated blob version.</param>
    /// <param name="appId">The application id.</param>
    /// <param name="blobStorageOrg">The org used to locate the blob container/account.</param>
    /// <param name="storageAccountNumber">Storage container number for when a Storage account has more than one container.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>The allocated version ID as a base64url-encoded UUID.</returns>
    Task<string> CreateBlobVersionId(
        Guid instanceGuid,
        Guid dataElementId,
        string appId,
        string blobStorageOrg,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes an allocated blob version if it is not referenced by current data element metadata.
    /// </summary>
    /// <param name="dataElementId">The data element id.</param>
    /// <param name="blobVersionId">The allocated blob version id as a base64url-encoded UUID.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>true if the blob version row was deleted.</returns>
    Task<bool> DeleteBlobVersion(
        Guid dataElementId,
        string blobVersionId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes orphan blob version metadata for exact version ids after blob cleanup.
    /// </summary>
    /// <param name="blobVersionIds">The blob version ids to delete as base64url-encoded UUIDs.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>The number of deleted blob version rows.</returns>
    Task<int> DeleteOrphanBlobVersions(
        IReadOnlyList<string> blobVersionIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads allocated blob versions for a data element grouped by storage context.
    /// </summary>
    /// <param name="dataElementId">The data element id.</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>The allocated blob versions grouped by storage context.</returns>
    Task<IReadOnlyList<BlobVersionReferencesInternal>> ReadBlobVersions(
        Guid dataElementId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks if a data element with given in id exists.
    /// </summary>
    /// <param name="dataElementId">The data element id</param>
    /// <param name="cancellationToken">A cancellation token to pass to async operations</param>
    /// <returns>true if data element exists.</returns>
    Task<bool> Exists(Guid dataElementId, CancellationToken cancellationToken = default);
}
