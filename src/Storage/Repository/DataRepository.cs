using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IDataRepository"/> using Azure CosmosDB to keep metadata
    /// </summary>
    internal sealed class DataRepository : BaseRepository, IDataRepository
    {
        private const string CollectionId = "dataElements";
        private const string PartitionKey = "/instanceGuid";

        private readonly ILogger<DataRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataRepository"/> class
        /// </summary>
        /// <param name="cosmosSettings">the configuration settings for azure cosmos database</param>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="cosmosClient">CosmosClient singleton</param>
        public DataRepository(
            IOptions<AzureCosmosSettings> cosmosSettings,
            ILogger<DataRepository> logger,
            CosmosClient cosmosClient)
            : base(CollectionId, PartitionKey, cosmosSettings, cosmosClient)
        {
            _logger = logger;
        }
 
        /// <inheritdoc/>
        public async Task<List<DataElement>> ReadAll(Guid instanceGuid)
        {
            string instanceKey = instanceGuid.ToString();

            List<DataElement> dataElements = new();

            QueryRequestOptions options = new()
            {
                MaxBufferedItemCount = 0,
                MaxConcurrency = -1,
                PartitionKey = new(instanceKey),
                MaxItemCount = 1000
            };

            FeedIterator<DataElement> query = Container.GetItemLinqQueryable<DataElement>(requestOptions: options)
                    .ToFeedIterator();

            while (query.HasMoreResults)
            {
                FeedResponse<DataElement> response = await query.ReadNextAsync();
                dataElements.AddRange(response);
            }

            return dataElements;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, List<DataElement>>> ReadAllForMultiple(List<string> instanceGuids)
        {
            Dictionary<string, List<DataElement>> dataElements = new();
            if (instanceGuids == null || instanceGuids.Count == 0)
            {
                return dataElements;
            }

            foreach (var guidString in instanceGuids)
            {
                dataElements[guidString] = new List<DataElement>();
            }

            QueryRequestOptions options = new()
            {
                MaxBufferedItemCount = 0,
                MaxConcurrency = -1,

                // Do not set PartitionKey as we are querying across all partitions
            };

            FeedIterator<DataElement> query = Container
                .GetItemLinqQueryable<DataElement>(requestOptions: options)
                .Where(x => instanceGuids.Contains(x.InstanceGuid))
                .ToFeedIterator();

            while (query.HasMoreResults)
            {
                FeedResponse<DataElement> response = await query.ReadNextAsync();
                foreach (DataElement dataElement in response)
                {
                    dataElements[dataElement.InstanceGuid].Add(dataElement);
                }
            }

            return dataElements;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Create(DataElement dataElement, long instanceInternalId = 0)
        {
            dataElement.Id ??= Guid.NewGuid().ToString();
            ItemResponse<DataElement> createdDataElement = await Container.CreateItemAsync(dataElement, new PartitionKey(dataElement.InstanceGuid));
            _logger.LogWarning("Create dateElement Id: {dataElementId}, eTag: {etag}", createdDataElement.Resource.Id, createdDataElement.ETag);
            return createdDataElement;
        }

        /// <inheritdoc/>
        public async Task<DataElement> Read(Guid instanceGuid, Guid dataElementGuid)
        {
            string instanceKey = instanceGuid.ToString();
            string dataElementKey = dataElementGuid.ToString();

            try
            {
                DataElement dataElement = await Container.ReadItemAsync<DataElement>(dataElementKey, new PartitionKey(instanceKey));
                return dataElement;
            }
            catch (CosmosException e)
            {
                if (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<DataElement> Update(Guid instanceGuid, Guid dataElementId, Dictionary<string, object> propertylist)
        {
            if (propertylist.Count > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(propertylist), "PropertyList can contain at most 10 entries.");
            }

            List<PatchOperation> operations = new();

            foreach (var entry in propertylist)
            {
                operations.Add(PatchOperation.Add(entry.Key, entry.Value));
            }

            try
            {
                ItemResponse<DataElement> response = await Container.PatchItemAsync<DataElement>(
                    id: dataElementId.ToString(),
                    partitionKey: new PartitionKey(instanceGuid.ToString()),
                    patchOperations: operations);

                return response;
            }
            catch (CosmosException e)
            {
                throw new RepositoryException(e.Message, e, e.StatusCode);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(DataElement dataElement)
        {
            var response = await Container.DeleteItemAsync<DataElement>(dataElement.Id, new PartitionKey(dataElement.InstanceGuid));

            return response.StatusCode == HttpStatusCode.NoContent;
        }
    }
}
