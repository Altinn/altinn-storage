﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IBlobRepository"/>.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="BlobRepository"/> class.
    /// </remarks>
    /// <param name="memoryCache">Memory cache</param>
    /// <param name="storageConfiguration">the storage configuration for azure blob storage.</param>
    /// <param name="logger">The logger to use when writing to logs.</param>
    public class BlobRepository(
        IMemoryCache memoryCache,
        IOptions<AzureStorageConfiguration> storageConfiguration,
        ILogger<BlobRepository> logger) : IBlobRepository
    {
        private const string _credsCacheKey = "creds";
        private readonly AzureStorageConfiguration _storageConfiguration = storageConfiguration.Value;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly ILogger<BlobRepository> _logger = logger;
        private static readonly MemoryCacheEntryOptions _cacheEntryOptionsCreds = new MemoryCacheEntryOptions()
            .SetPriority(CacheItemPriority.High)
            .SetAbsoluteExpiration(new TimeSpan(10, 0, 0));

        private static readonly MemoryCacheEntryOptions _cacheEntryOptionsBlobClient = new MemoryCacheEntryOptions()
            .SetPriority(CacheItemPriority.High)
            .SetAbsoluteExpiration(new TimeSpan(10, 0, 0));

        /// <inheritdoc/>
        public async Task<Stream> ReadBlob(string org, string blobStoragePath, int? storageContainerNumber)
        {
            try
            {
                return await DownloadBlobAsync(org, blobStoragePath, storageContainerNumber);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating credentials and retrying download operation.");

                        _memoryCache.Remove(_credsCacheKey);
                        _memoryCache.Remove(GetClientCacheKey(org, storageContainerNumber));

                        return await DownloadBlobAsync(org, blobStoragePath, storageContainerNumber);
                    case "BlobNotFound":
                        _logger.LogWarning("Unable to find a blob based on the given information - {Org}: {BlobStoragePath}", org, blobStoragePath);

                        // Returning null because the blob does not exist.
                        return null;
                    case "InvalidRange":
                        _logger.LogWarning("Found possibly empty blob in storage for {Org}: {BlobStoragePath}", org, blobStoragePath);

                        // Returning empty stream because the blob does exist, but it is empty.
                        return new MemoryStream();
                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(string org, Stream stream, string blobStoragePath, int? storageContainerNumber)
        {
            try
            {
                var blobProps = await UploadFromStreamAsync(org, stream, blobStoragePath, storageContainerNumber);
                return (blobProps.ContentLength, blobProps.LastModified);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating credentials.");

                        _memoryCache.Remove(_credsCacheKey);
                        _memoryCache.Remove(GetClientCacheKey(org, storageContainerNumber));

                        // No use retrying upload as the original stream can't be reset back to start.
                        throw;
                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteBlob(string org, string blobStoragePath, int? storageContainerNumber)
        {
            try
            {
                return await DeleteIfExistsAsync(org, blobStoragePath, storageContainerNumber);
            }
            catch (RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case "AuthenticationFailed":
                        _logger.LogWarning("Authentication failed. Invalidating credentials and retrying delete operation.");

                        _memoryCache.Remove(_credsCacheKey);
                        _memoryCache.Remove(GetClientCacheKey(org, storageContainerNumber));

                        return await DeleteIfExistsAsync(org, blobStoragePath, storageContainerNumber);
                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteDataBlobs(Instance instance, int? storageContainerNumber)
        {
            BlobContainerClient container = CreateContainerClient(instance.Org, storageContainerNumber);

            if (container == null)
            {
                _logger.LogError($"BlobService // DeleteDataBlobs // Could not connect to blob container.");
                return false;
            }

            try
            {
                await foreach (BlobItem item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{instance.AppId}/{instance.Id}", CancellationToken.None))
                {
                    await container.DeleteBlobIfExistsAsync(item.Name, DeleteSnapshotsOption.IncludeSnapshots);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "BlobService // DeleteDataBlobs // Org: {Instance}",
                    instance.Org);
                return false;
            }

            return true;
        }

        private async Task<BlobProperties> UploadFromStreamAsync(string org, Stream stream, string fileName, int? storageContainerNumber)
        {
            BlobClient blockBlob = CreateBlobClient(org, fileName, storageContainerNumber);
            BlobUploadOptions options = new()
            {
                TransferValidation = new UploadTransferValidationOptions { ChecksumAlgorithm = StorageChecksumAlgorithm.MD5 }
            };
            await blockBlob.UploadAsync(stream, options);
            BlobProperties properties = await blockBlob.GetPropertiesAsync();

            return properties;
        }

        private async Task<Stream> DownloadBlobAsync(string org, string fileName, int? storageContainerNumber)
        {
            BlobClient blockBlob = CreateBlobClient(org, fileName, storageContainerNumber);

            Azure.Response<BlobDownloadInfo> response = await blockBlob.DownloadAsync();

            return response.Value.Content;
        }

        private async Task<bool> DeleteIfExistsAsync(string org, string fileName, int? storageContainerNumber)
        {
            BlobClient blockBlob = CreateBlobClient(org, fileName, storageContainerNumber);

            bool result = await blockBlob.DeleteIfExistsAsync();

            return result;
        }

        private BlobClient CreateBlobClient(string org, string blobName, int? storageContainerNumber)
        {
            return CreateContainerClient(org, storageContainerNumber).GetBlobClient(blobName);
        }

        private BlobContainerClient CreateContainerClient(string org, int? storageContainerNumber)
        {
            if (!_storageConfiguration.AccountName.Equals("devstoreaccount1"))
            {
                string cacheKey = GetClientCacheKey(org, storageContainerNumber);
                if (!_memoryCache.TryGetValue(cacheKey, out BlobContainerClient client))
                {
                    string accountName = string.Format(_storageConfiguration.OrgStorageAccount, org);
                    string containerName = string.Format(_storageConfiguration.OrgStorageContainer, org)
                        + (storageContainerNumber != null ? $"-{storageContainerNumber}" : null);

                    UriBuilder fullUri = new()
                    {
                        Scheme = "https",
                        Host = $"{accountName}.blob.core.windows.net",
                        Path = $"{containerName}"
                    };

                    client = new BlobContainerClient(fullUri.Uri, GetCachedCredentials());
                    _memoryCache.Set(cacheKey, client, _cacheEntryOptionsBlobClient);
                }

                return client;
            }

            StorageSharedKeyCredential storageCredentials = new(_storageConfiguration.OrgStorageAccount, _storageConfiguration.AccountKey);
            Uri storageUrl = new(_storageConfiguration.BlobEndPoint);
            BlobServiceClient commonBlobClient = new(storageUrl, storageCredentials);
            BlobContainerClient blobContainerClient = commonBlobClient.GetBlobContainerClient(string.Format(_storageConfiguration.OrgStorageContainer, org));
            return blobContainerClient;
        }

        private TokenCredential GetCachedCredentials()
        {
            if (!_memoryCache.TryGetValue(_credsCacheKey, out DefaultAzureCredential creds))
            {
                creds = new();
                _memoryCache.Set(_credsCacheKey, creds, _cacheEntryOptionsCreds);
            }

            return creds;
        }

        private static string GetClientCacheKey(string org, int? storageContainerNumber)
        {
            return $"blob-{org}-{storageContainerNumber}";
        }
    }
}
