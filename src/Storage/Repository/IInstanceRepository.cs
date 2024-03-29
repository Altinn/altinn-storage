using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// The repository to handle application instances
    /// </summary>
    public interface IInstanceRepository
    {
        /// <summary>
        /// Gets all the instances that satisfy given query parameters
        /// </summary>
        /// <param name="queryParams">the query params</param>
        /// <param name="continuationToken">a token to get the next page, more performant than using page</param>
        /// <param name="size">The number of items per page</param>
        /// <param name="includeDataelements">Whether to include data elements</param>
        /// <returns>The query response including the list of instances</returns>
        Task<InstanceQueryResponse> GetInstancesFromQuery(Dictionary<string, StringValues> queryParams, string continuationToken, int size, bool includeDataelements);

        /// <summary>
        /// Get an instance for a given instance id
        /// </summary>
        /// <param name="instanceGuid">the instance guid</param>
        /// <param name="includeElements">whether to include data elements</param>
        /// <returns>The instance for the given parameters</returns>
        Task<(Instance Instance, long InternalId)> GetOne(Guid instanceGuid, bool includeElements);

        /// <summary>
        /// insert new instance into collection
        /// </summary>
        /// <param name="instance">the instance to base the new one on</param>
        /// <returns>The instance id</returns>
        Task<Instance> Create(Instance instance);

        /// <summary>
        /// update existing instance
        /// </summary>
        /// <param name="instance">the instance to update</param>
        /// <param name="updateProperties">a list of which properties should be updated</param>
        /// <returns>The updated instance</returns>
        Task<Instance> Update(Instance instance, List<string> updateProperties);

        /// <summary>
        /// Delets an instance.
        /// </summary>
        /// <param name="instance">The instance to delete</param>
        /// <returns>if the item is deleted or not</returns>
        Task<bool> Delete(Instance instance);

        /// <summary>
        /// Gets hard deleted instances for cleanup
        /// </summary>
        /// <returns>Hard deleted instances</returns>
        Task<List<Instance>> GetHardDeletedInstances();

        /// <summary>
        /// Gets hard deleted data elements for cleanup
        /// </summary>
        /// <returns>Hard deleted data elements</returns>
        Task<List<DataElement>> GetHardDeletedDataElements();
    }
}
