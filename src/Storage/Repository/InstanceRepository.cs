using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Repository operations for application instances.
    /// </summary>
    internal sealed class InstanceRepository : BaseRepository, IInstanceRepository
    {
        private const string CollectionId = "instances";
        private const string PartitionKey = "/instanceOwner/partyId";

        private readonly ILogger<InstanceRepository> _logger;
        private readonly IDataRepository _dataRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstanceRepository"/> class
        /// </summary>
        /// <param name="cosmosSettings">the configuration settings for cosmos database</param>
        /// <param name="logger">the logger</param>
        /// <param name="dataRepository">the data repository to fetch data elements from</param>
        /// <param name="cosmosClient">CosmosClient singleton</param>
        public InstanceRepository(
            IOptions<AzureCosmosSettings> cosmosSettings,
            ILogger<InstanceRepository> logger,
            IDataRepository dataRepository, 
            CosmosClient cosmosClient) 
            : base(CollectionId, PartitionKey, cosmosSettings, cosmosClient)
        {
            _logger = logger;
            _dataRepository = dataRepository;
        }

        /// <inheritdoc/>
        public async Task<Instance> Create(Instance instance)
        {
            PreProcess(instance);

            instance.Id ??= Guid.NewGuid().ToString();

            Instance instanceStored = await Container.CreateItemAsync<Instance>(instance, new PartitionKey(instance.InstanceOwner.PartyId));
            instanceStored.Id = $"{instanceStored.InstanceOwner.PartyId}/{instanceStored.Id}";

            return instanceStored;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(Instance item)
        {
            PreProcess(item);

            try
            {
                ItemResponse<Instance> response = await Container.DeleteItemAsync<Instance>(item.Id, new PartitionKey(item.InstanceOwner.PartyId));
                return response.StatusCode == HttpStatusCode.NoContent;
            }
            catch (CosmosException e)
            {
                if (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<InstanceQueryResponse> GetInstancesFromQuery(
            Dictionary<string, StringValues> queryParams,
            string continuationToken,
            int size)
        {
            InstanceQueryResponse queryResponse = new()
            {
                Count = 0,
                Instances = new List<Instance>()
            };

            while (queryResponse.Count < size)
            {
                QueryRequestOptions options = new QueryRequestOptions() { MaxBufferedItemCount = 0, MaxConcurrency = -1, MaxItemCount = size - queryResponse.Count, ResponseContinuationTokenLimitInKb = 7 };

                string tokenValue = string.IsNullOrEmpty(continuationToken) ? null : continuationToken;
                IQueryable<Instance> queryBuilder = Container.GetItemLinqQueryable<Instance>(requestOptions: options, continuationToken: tokenValue);

                try
                {
                    queryBuilder = InstanceQueryHelper.BuildQueryFromParameters(queryParams, queryBuilder, options);
                }
                catch (Exception e)
                {
                    queryResponse.Exception = e.Message;
                    return queryResponse;
                }

                try
                {
                    var iterator = queryBuilder.ToFeedIterator();

                    FeedResponse<Instance> feedResponse = await iterator.ReadNextAsync();

                    if (feedResponse.Count == 0 && !iterator.HasMoreResults)
                    {
                        queryResponse.ContinuationToken = string.Empty;
                        break;
                    }

                    List<Instance> instances = feedResponse.ToList();
                    await PostProcess(instances);
                    queryResponse.Instances.AddRange(instances);
                    queryResponse.Count += instances.Count;

                    if (string.IsNullOrEmpty(feedResponse.ContinuationToken))
                    {
                        queryResponse.ContinuationToken = string.Empty;
                        break;
                    }

                    queryResponse.ContinuationToken = feedResponse.ContinuationToken;
                    continuationToken = feedResponse.ContinuationToken;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Exception querying CosmosDB for instances");
                    queryResponse.Exception = e.Message;
                    break;
                }
            }

            return queryResponse;
        }

        /// <inheritdoc/>
        public async Task<(Instance Instance, long InternalId)> GetOne(int instanceOwnerPartyId, Guid instanceGuid, bool includeElements = true)
        {
            try
            {
                Instance instance = await Container.ReadItemAsync<Instance>(instanceGuid.ToString(), new PartitionKey(instanceOwnerPartyId.ToString()));

                await PostProcess(instance);
                return (instance, 0);
            }
            catch (CosmosException e)
            {
                if (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return (null, 0);
                }

                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<Instance> Update(Instance item)
        {
            List<DataElement> dataElements = item.Data;
            PreProcess(item);

            Instance instance = await Container.UpsertItemAsync<Instance>(item, new PartitionKey(item.InstanceOwner.PartyId));

            instance.Data = dataElements;
            instance.Id = $"{instance.InstanceOwner.PartyId}/{instance.Id}";

            return instance;
        }

        /// <inheritdoc/>
        public Task<List<Instance>> GetHardDeletedInstances()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public Task<List<DataElement>> GetHardDeletedDataElements()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Converts the instanceId (id) of the instance from {instanceOwnerPartyId}/{instanceGuid} to {instanceGuid} to use as id in cosmos.
        /// Ensures dataElements are not included in the document.
        /// </summary>
        /// <param name="instance">the instance to preprocess</param>
        private static void PreProcess(Instance instance)
        {
            instance.Id = InstanceIdToCosmosId(instance.Id);
            instance.Data = new List<DataElement>();
        }

        /// <summary>
        /// Prepares the instance for exposure to end users and app owners.
        /// </summary>
        /// <remarks>
        /// - Converts the instanceId (id) of the instance from {instanceGuid} to {instanceOwnerPartyId}/{instanceGuid} to be used outside cosmos.
        /// - Retrieves all data elements from data repository
        /// - Sets correct LastChanged/LastChangedBy by comparing instance and data elements
        /// </remarks>
        /// <param name="instance">the instance to preprocess</param>
        private async Task PostProcess(Instance instance)
        {
            Guid instanceGuid = Guid.Parse(instance.Id);
            string instanceId = $"{instance.InstanceOwner.PartyId}/{instance.Id}";

            instance.Id = instanceId;
            instance.Data = await _dataRepository.ReadAll(instanceGuid);
            if (instance.Data != null && instance.Data.Any())
            {
                SetReadStatus(instance);
            }

            (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);
            instance.LastChanged = lastChanged;
            instance.LastChangedBy = lastChangedBy;
        }

        /// <summary>
        /// Preprocess a list of instances.
        /// </summary>
        /// <param name="instances">the list of instances</param>
        private async Task PostProcess(List<Instance> instances)
        {
            Dictionary<string, Instance> instanceMap = instances.ToDictionary(key => key.Id, instance => instance);
            var instanceGuids = instances.Select(i => i.Id).ToList();

            var dataElements = await _dataRepository.ReadAllForMultiple(instanceGuids);

            foreach (var instanceGuid in instanceGuids)
            {
                instanceMap[instanceGuid].Data = dataElements[instanceGuid];
            }

            foreach (Instance instance in instances)
            {
                instance.Id = $"{instance.InstanceOwner.PartyId}/{instance.Id}";
                
                if (instance.Data != null && instance.Data.Any())
                {
                    SetReadStatus(instance);
                }

                (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);
                instance.LastChanged = lastChanged;
                instance.LastChangedBy = lastChangedBy;
            }
        }

        /// <summary>
        /// An instanceId should follow this format {int}/{guid}.
        /// Cosmos does not allow / in id.
        /// But in some old cases instanceId is just {guid}.
        /// </summary>
        /// <param name="instanceId">the id to convert to cosmos</param>
        /// <returns>the guid of the instance</returns>
        private static string InstanceIdToCosmosId(string instanceId)
        {
            string cosmosId = instanceId;

            if (instanceId != null && instanceId.Contains('/'))
            {
                cosmosId = instanceId.Split("/")[1];
            }

            return cosmosId;
        }

        private static void SetReadStatus(Instance instance)
        {
            if (instance.Status.ReadStatus == ReadStatus.Read && instance.Data.Any(d => !d.IsRead))
            {
                instance.Status.ReadStatus = ReadStatus.UpdatedSinceLastReview;
            }
            else if (instance.Status.ReadStatus == ReadStatus.Read && !instance.Data.Any(d => d.IsRead))
            {
                instance.Status.ReadStatus = ReadStatus.Unread;
            }
        }
    }
}
