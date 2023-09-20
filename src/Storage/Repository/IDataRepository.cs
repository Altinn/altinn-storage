using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Models;
using Azure.Storage.Blobs.Models;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Describes the implementation of a data element storage. 
    /// </summary>
    public interface IDataRepository
    {
        /// <summary>
        /// Gets all data elements for a given instance
        /// </summary>
        /// <param name="instanceGuid">the guid of the instance</param>
        /// <returns>list of data elements</returns>
        Task<List<DataElement>> ReadAll(Guid instanceGuid);

        /// <summary>
        /// Gets all data elements for given instances
        /// </summary>
        /// <param name="instanceGuids">the list of instance guids to return data elements for</param>
        /// <returns>list of data elements</returns>
        Task<Dictionary<string, List<DataElement>>> ReadAllForMultiple(List<string> instanceGuids);

        /// <summary>
        /// Creates a dataElement into the repository
        /// </summary>
        /// <param name="dataElement">the data element to insert</param>
        /// <param name="instanceInternalId">the internal id of the parent instance</param>
        /// <returns>the data element with updated id</returns>
        Task<DataElement> Create(DataElement dataElement, long instanceInternalId);

        /// <summary>
        /// Reads a data element metadata object. Not the actual data.
        /// </summary>
        /// <param name="instanceGuid">the instance guid as partitionKey</param>
        /// <param name="dataElementId">The data element guid</param>
        /// <returns>The identified data element.</returns>
        Task<DataElement> Read(Guid instanceGuid, Guid dataElementId);

        /// <summary>
        /// Deletes the data element metadata object permanently!
        /// </summary>
        /// <param name="dataElement">the element to delete</param>
        /// <returns>true if delete went well.</returns>
        Task<bool> Delete(DataElement dataElement);

        /// <summary>
        /// Deletes the data elements metadata for an instance permanently!
        /// </summary>
        /// <param name="instanceId">the parent instance id of the data elements to delete</param>
        /// <returns>true if delete went well.</returns>
        Task<bool> DeleteForInstance(string instanceId);

        /// <summary>
        /// Updates the data element with the properties provided in the dictionary
        /// </summary>
        /// <param name="instanceGuid">The instance guid</param>
        /// <param name="dataElementId">The data element id</param>
        /// <param name="propertylist">A dictionary contaning property id (key) and object (value) to be stored</param>
        /// <remarks>Dictionary can containt at most 10 entries</remarks>
        Task<DataElement> Update(Guid instanceGuid, Guid dataElementId, Dictionary<string, object> propertylist);
    }
}
