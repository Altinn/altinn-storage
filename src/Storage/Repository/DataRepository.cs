using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;

using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IDataRepository"/> using Azure CosmosDB to keep metadata
    /// and Azure Blob storage to keep the actual data. Blob storage is again split based on application owner.
    /// </summary>
    internal sealed class DataRepository : BaseRepository, IDataRepository
    {
        private const string CollectionId = "dataElements";
        private const string PartitionKey = "/instanceGuid";

        private readonly AzureStorageConfiguration _storageConfiguration;
        private readonly ISasTokenProvider _sasTokenProvider;
        private readonly ILogger<DataRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataRepository"/> class
        /// </summary>
        /// <param name="sasTokenProvider">A provider that can be asked for SAS tokens.</param>
        /// <param name="cosmosSettings">the configuration settings for azure cosmos database</param>
        /// <param name="storageConfiguration">the storage configuration for azure blob storage</param>
        /// <param name="logger">The logger to use when writing to logs.</param>
        /// <param name="cosmosClient">CosmosClient singleton</param>
        public DataRepository(
            ISasTokenProvider sasTokenProvider,
            IOptions<AzureCosmosSettings> cosmosSettings,
            IOptions<AzureStorageConfiguration> storageConfiguration,
            ILogger<DataRepository> logger,
            CosmosClient cosmosClient)
            : base(CollectionId, PartitionKey, cosmosSettings, cosmosClient)
        {
            _storageConfiguration = storageConfiguration.Value;
            _sasTokenProvider = sasTokenProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteDataToStorage(string org, Stream stream, string blobStoragePath)
        {
            try
            {
                var blobProps = await UploadFromStreamAsync(org, stream, blobStoragePath);
                return (blobProps.ContentLength, blobProps.LastModified);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating SAS token.");

                        _sasTokenProvider.InvalidateSasToken(org);

                        // No use retrying upload as the original stream can't be reset back to start.
                        throw;
                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Stream> ReadDataFromStorage(string org, string blobStoragePath)
        {
            try
            {
                return await DownloadBlobAsync(org, blobStoragePath);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating SAS token and retrying download operation.");

                        _sasTokenProvider.InvalidateSasToken(org);

                        return await DownloadBlobAsync(org, blobStoragePath);
                    case "BlobNotFound":
                        _logger.LogWarning("Unable to find a blob based on the given information - {org}: {blobStoragePath}", org, blobStoragePath);

                        // Returning null because the blob does not exist.
                        return null;
                    case "InvalidRange":
                        _logger.LogWarning("Found possibly empty blob in storage for {org}: {blobStoragePath}", org, blobStoragePath);

                        // Returning empty stream because the blob does exist, but it is empty.
                        return new MemoryStream();
                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteDataInStorage(string org, string blobStoragePath)
        {
            try
            {
                return await DeleteIfExistsAsync(org, blobStoragePath);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating SAS token and retrying delete operation.");

                        _sasTokenProvider.InvalidateSasToken(org);

                        return await DeleteIfExistsAsync(org, blobStoragePath);
                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<List<DataElement>> ReadAll(Guid instanceGuid)
        {
            string instanceKey = instanceGuid.ToString();

            List<DataElement> dataElements = new();

            QueryRequestOptions options = new QueryRequestOptions()
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

            QueryRequestOptions options = new QueryRequestOptions()
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
        public async Task<DataElement> Create(DataElement dataElement)
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
        public async Task<DataElement> Update(DataElement dataElement)
        {
            List<PatchOperation> operations = new()
            {
                PatchOperation.Add("/filename", dataElement.Filename),
                PatchOperation.Add("/dataType", dataElement.DataType),
                PatchOperation.Add("/contentType", dataElement.ContentType),
                PatchOperation.Add("/selfLinks", dataElement.SelfLinks),
                PatchOperation.Add("/size", dataElement.Size),
                PatchOperation.Add("/contentHash", dataElement.ContentHash),
                PatchOperation.Add("/locked", dataElement.Locked),
                PatchOperation.Add("/refs", dataElement.Refs),
                PatchOperation.Add("/isRead", dataElement.IsRead),
                PatchOperation.Add("/tags", dataElement.Tags),
                PatchOperation.Add("/deleteStatus", dataElement.DeleteStatus)
            };

            ItemResponse<DataElement> response = await Container.PatchItemAsync<DataElement>(
                id: dataElement.Id,
                partitionKey: new PartitionKey(dataElement.InstanceGuid),
                patchOperations: operations);

            return response;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(DataElement dataElement)
        {
            var response = await Container.DeleteItemAsync<DataElement>(dataElement.Id, new PartitionKey(dataElement.InstanceGuid));

            return response.StatusCode == HttpStatusCode.NoContent;
        }

        /// <inheritdoc/>
        public async Task<bool> SetFileScanStatus(string instanceGuid, string dataElementId, FileScanStatus status)
        {
            List<PatchOperation> operations = new()
            {
                PatchOperation.Add("/fileScanResult", status.FileScanResult.ToString()),
            };

            ItemResponse<DataElement> response = await Container.PatchItemAsync<DataElement>(
                id: dataElementId,
                partitionKey: new PartitionKey(instanceGuid),
                patchOperations: operations);

            return response.StatusCode == HttpStatusCode.OK;
        }

        private async Task<BlobProperties> UploadFromStreamAsync(string org, Stream stream, string fileName)
        {
            BlobClient blockBlob = await CreateBlobClient(org, fileName);
            BlobUploadOptions options = new BlobUploadOptions
            {
                TransferValidation = new UploadTransferValidationOptions { ChecksumAlgorithm = StorageChecksumAlgorithm.MD5 }
            };
            await blockBlob.UploadAsync(stream, options);
            BlobProperties properties = await blockBlob.GetPropertiesAsync();

            return properties;
        }

        private async Task<Stream> DownloadBlobAsync(string org, string fileName)
        {
            BlobClient blockBlob = await CreateBlobClient(org, fileName);

            Azure.Response<BlobDownloadInfo> response = await blockBlob.DownloadAsync();

            return response.Value.Content;
        }

        private async Task<bool> DeleteIfExistsAsync(string org, string fileName)
        {
            BlobClient blockBlob = await CreateBlobClient(org, fileName);

            bool result = await blockBlob.DeleteIfExistsAsync();

            return result;
        }

        private async Task<BlobClient> CreateBlobClient(string org, string blobName)
        {
            if (!_storageConfiguration.AccountName.StartsWith("devstoreaccount1"))
            {
                string sasToken = await _sasTokenProvider.GetSasToken(org);

                string accountName = string.Format(_storageConfiguration.OrgStorageAccount, org);
                string containerName = string.Format(_storageConfiguration.OrgStorageContainer, org);

                UriBuilder fullUri = new UriBuilder
                {
                    Scheme = "https",
                    Host = $"{accountName}.blob.core.windows.net",
                    Path = $"{containerName}/{blobName}",
                    Query = sasToken
                };

                return new BlobClient(fullUri.Uri);
            }

            StorageSharedKeyCredential storageCredentials = new StorageSharedKeyCredential(_storageConfiguration.AccountName, _storageConfiguration.AccountKey);
            Uri storageUrl = new Uri(_storageConfiguration.BlobEndPoint);
            BlobServiceClient commonBlobClient = new BlobServiceClient(storageUrl, storageCredentials);
            BlobContainerClient blobContainerClient = commonBlobClient.GetBlobContainerClient(_storageConfiguration.StorageContainer);

            return blobContainerClient.GetBlobClient(blobName);
        }
    }
}
