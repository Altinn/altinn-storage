#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// The repository to handle application instances
/// </summary>
public interface IInstanceRepository
{
    /// <summary>
    /// Gets fully hydrated storage-domain instances that satisfy the query parameters.
    /// </summary>
    /// <param name="queryParams">the query params</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The domain query result.</returns>
    Task<InstanceQueryResult> GetInstancesFromQuery(
        InstanceQueryParameters queryParams,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Get an instance for a given instance id
    /// </summary>
    /// <param name="instanceGuid">the instance guid</param>
    /// <param name="includeElements">whether to include data elements</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The instance for the given parameters with internal storage-only fields.</returns>
    Task<InstanceInternal> GetOne(
        Guid instanceGuid,
        bool includeElements,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Inserts a new instance.
    /// </summary>
    /// <remarks>
    /// On success, this operation mutates and returns the supplied <paramref name="instance"/>
    /// reference. Its storage-format <see cref="InstanceInternal.Id"/> is retained, or generated
    /// when null; <see cref="InstanceInternal.LastChanged"/> is truncated to microsecond precision;
    /// <see cref="InstanceInternal.Data"/> is cleared because data elements are not inserted by this
    /// operation; and <see cref="InstanceInternal.InternalId"/> is reset to 0 because the insert
    /// operation does not return the storage row identifier. A subsequent <see cref="GetOne"/> call
    /// hydrates that value.
    /// </remarks>
    /// <param name="instance">The storage-domain instance to insert.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="altinnMainVersion">the altinn main version</param>
    /// <returns>The same successfully inserted <paramref name="instance"/> reference.</returns>
    Task<InstanceInternal> Create(
        InstanceInternal instance,
        CancellationToken cancellationToken,
        int altinnMainVersion = 3
    );

    /// <summary>
    /// update existing instance
    /// </summary>
    /// <param name="instance">the instance to update</param>
    /// <param name="updateProperties">a list of which properties should be updated</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The updated instance</returns>
    Task<InstanceInternal> Update(
        InstanceInternal instance,
        List<string> updateProperties,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Delets an instance.
    /// </summary>
    /// <param name="instanceGuid">The instance identifier to delete</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>if the item is deleted or not</returns>
    Task<bool> Delete(Guid instanceGuid, CancellationToken cancellationToken);

    /// <summary>
    /// Gets hard deleted instances for cleanup
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Hard deleted instances</returns>
    Task<List<InstanceInternal>> GetHardDeletedInstances(CancellationToken cancellationToken);

    /// <summary>
    /// Gets hard deleted data elements for cleanup
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Hard deleted data elements</returns>
    Task<List<DataElementInternal>> GetHardDeletedDataElements(CancellationToken cancellationToken);
}
