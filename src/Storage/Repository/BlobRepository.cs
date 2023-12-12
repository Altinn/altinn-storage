using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Represents an implementation of <see cref="IBlobRepository"/>.
    /// </summary>
    public class BlobRepository : IBlobRepository
    {
        private readonly AzureStorageConfiguration _storageConfiguration;
        private readonly ISasTokenProvider _sasTokenProvider;
        private readonly ILogger<PgDataRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobRepository"/> class.
        /// </summary>
        /// <param name="sasTokenProvider">A provider that can be asked for SAS tokens.</param>
        /// <param name="storageConfiguration">the storage configuration for azure blob storage.</param>
        /// <param name="logger">The logger to use when writing to logs.</param>
        public BlobRepository(
            ISasTokenProvider sasTokenProvider,
            IOptions<AzureStorageConfiguration> storageConfiguration,
            ILogger<PgDataRepository> logger)
        {
            _storageConfiguration = storageConfiguration.Value;
            _sasTokenProvider = sasTokenProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<Stream> ReadBlob(string org, string blobStoragePath)
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
        public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(string org, Stream stream, string blobStoragePath)
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
        public async Task<bool> DeleteBlob(string org, string blobStoragePath)
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
        public async Task<bool> DeleteDataBlobs(Instance instance)
        {
            BlobContainerClient container = await CreateBlobClient(instance.Org);

            if (container == null)
            {
                _logger.LogError($"BlobService // DeleteDataBlobs // Could not connect to blob container.");
                return false;
            }

            try
            {
                await foreach (BlobItem item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{instance.AppId}/{instance.Id}", CancellationToken.None))
                {
                    container.DeleteBlobIfExists(item.Name, DeleteSnapshotsOption.IncludeSnapshots);
                }
            }
            catch (Exception e)
            {
                _sasTokenProvider.InvalidateSasToken(instance.Org);
                _logger.LogError(
                    e,
                    "BlobService // DeleteDataBlobs // Org: {instance}",
                    instance.Org);
                return false;
            }

            return true;
        }

        private async Task<BlobProperties> UploadFromStreamAsync(string org, Stream stream, string fileName)
        {
            BlobClient blockBlob = await CreateBlobClient(org, fileName);
            BlobUploadOptions options = new()
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

                UriBuilder fullUri = new()
                {
                    Scheme = "https",
                    Host = $"{accountName}.blob.core.windows.net",
                    Path = $"{containerName}/{blobName}",
                    Query = sasToken
                };

                return new BlobClient(fullUri.Uri);
            }

            StorageSharedKeyCredential storageCredentials = new(_storageConfiguration.AccountName, _storageConfiguration.AccountKey);
            Uri storageUrl = new(_storageConfiguration.BlobEndPoint);
            BlobServiceClient commonBlobClient = new(storageUrl, storageCredentials);
            BlobContainerClient blobContainerClient = commonBlobClient.GetBlobContainerClient(_storageConfiguration.StorageContainer);

            return blobContainerClient.GetBlobClient(blobName);
        }

        private async Task<BlobContainerClient> CreateBlobClient(string org)
        {
            if (!_storageConfiguration.AccountName.Equals("devstoreaccount1"))
            {
                string sasToken = await _sasTokenProvider.GetSasToken(org);

                string accountName = string.Format(_storageConfiguration.OrgStorageAccount, org);
                string containerName = string.Format(_storageConfiguration.OrgStorageContainer, org);

                UriBuilder fullUri = new()
                {
                    Scheme = "https",
                    Host = $"{accountName}.blob.core.windows.net",
                    Path = $"{containerName}",
                    Query = sasToken,
                };

                return new BlobContainerClient(fullUri.Uri, null);
            }

            StorageSharedKeyCredential storageCredentials = new(_storageConfiguration.OrgStorageAccount, _storageConfiguration.AccountKey);
            Uri storageUrl = new(_storageConfiguration.BlobEndPoint);
            BlobServiceClient commonBlobClient = new(storageUrl, storageCredentials);
            BlobContainerClient blobContainerClient = commonBlobClient.GetBlobContainerClient(string.Format(_storageConfiguration.OrgStorageContainer, org));
            return blobContainerClient;
        }
    }
}
