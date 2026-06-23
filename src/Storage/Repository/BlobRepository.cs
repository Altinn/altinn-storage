#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository;

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
    ILogger<BlobRepository> logger
) : IBlobRepository
{
    private const string _credsCacheKey = "creds";
    private readonly AzureStorageConfiguration _storageConfiguration = storageConfiguration.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<BlobRepository> _logger = logger;
    private static readonly MemoryCacheEntryOptions _cacheEntryOptionsCreds =
        new MemoryCacheEntryOptions()
            .SetPriority(CacheItemPriority.High)
            .SetAbsoluteExpiration(new TimeSpan(10, 0, 0));

    private static readonly MemoryCacheEntryOptions _cacheEntryOptionsBlobClient =
        new MemoryCacheEntryOptions()
            .SetPriority(CacheItemPriority.High)
            .SetAbsoluteExpiration(new TimeSpan(10, 0, 0));

    /// <inheritdoc/>
    public async Task<Stream> ReadBlob(
        string org,
        string blobStoragePath,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await DownloadBlobAsync(
                org,
                blobStoragePath,
                storageAccountNumber,
                cancellationToken
            );
        }
        catch (RequestFailedException requestFailedException)
        {
            switch (requestFailedException.ErrorCode)
            {
                case "AuthenticationFailed":
                    _logger.LogWarning(
                        "Authentication failed. Invalidating credentials and retrying download operation."
                    );

                    _memoryCache.Remove(_credsCacheKey);
                    _memoryCache.Remove(GetClientCacheKey(org, storageAccountNumber));

                    return await DownloadBlobAsync(
                        org,
                        blobStoragePath,
                        storageAccountNumber,
                        cancellationToken
                    );
                case "BlobNotFound":
                    _logger.LogWarning(
                        "Unable to find a blob based on the given information - {Org}: {BlobStoragePath}",
                        org,
                        blobStoragePath
                    );

                    // Returning null because the blob does not exist.
                    return null;
                case "InvalidRange":
                    _logger.LogWarning(
                        "Found possibly empty blob in storage for {Org}: {BlobStoragePath}",
                        org,
                        blobStoragePath
                    );

                    // Returning empty stream because the blob does exist, but it is empty.
                    return new MemoryStream();
                default:
                    throw;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        string org,
        Stream stream,
        string blobStoragePath,
        int? storageAccountNumber
    )
    {
        try
        {
            BlobProperties properties = await UploadFromStreamAsync(
                org,
                stream,
                blobStoragePath,
                storageAccountNumber
            );

            return (properties.ContentLength, properties.LastModified);
        }
        catch (RequestFailedException requestFailedException)
        {
            switch (requestFailedException.ErrorCode)
            {
                case "AuthenticationFailed":
                    _logger.LogWarning("Authentication failed. Invalidating credentials.");

                    _memoryCache.Remove(_credsCacheKey);
                    _memoryCache.Remove(GetClientCacheKey(org, storageAccountNumber));

                    // No use retrying upload as the original stream can't be reset back to start.
                    throw;
                default:
                    throw;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteBlob(
        string org,
        string blobStoragePath,
        int? storageAccountNumber
    )
    {
        try
        {
            return await DeleteIfExistsAsync(org, blobStoragePath, storageAccountNumber);
        }
        catch (RequestFailedException requestFailedException)
        {
            switch (requestFailedException.ErrorCode)
            {
                case "AuthenticationFailed":
                    _logger.LogWarning(
                        "Authentication failed. Invalidating credentials and retrying delete operation."
                    );

                    _memoryCache.Remove(_credsCacheKey);
                    _memoryCache.Remove(GetClientCacheKey(org, storageAccountNumber));

                    return await DeleteIfExistsAsync(org, blobStoragePath, storageAccountNumber);
                default:
                    throw;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteBlobs(
        string org,
        IEnumerable<string> blobStoragePaths,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        string[] blobStoragePathList =
            blobStoragePaths
                ?.Where(blobStoragePath => !string.IsNullOrEmpty(blobStoragePath))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            ?? [];
        if (blobStoragePathList.Length == 0)
        {
            return true;
        }

        try
        {
            await DeleteBlobsAsync(
                org,
                blobStoragePathList,
                storageAccountNumber,
                cancellationToken
            );
            return true;
        }
        catch (RequestFailedException requestFailedException)
            when (requestFailedException.ErrorCode == "AuthenticationFailed")
        {
            _logger.LogWarning(
                "Authentication failed. Invalidating credentials and retrying batch delete operation."
            );

            _memoryCache.Remove(_credsCacheKey);
            _memoryCache.Remove(GetClientCacheKey(org, storageAccountNumber));

            await DeleteBlobsAsync(
                org,
                blobStoragePathList,
                storageAccountNumber,
                cancellationToken
            );
            return true;
        }
        catch (RequestFailedException requestFailedException)
            when (requestFailedException.ErrorCode == "BlobNotFound")
        {
            _logger.LogWarning(
                "One or more blobs were not found during batch delete operation for {Org}.",
                org
            );
            return false;
        }
        catch (AggregateException aggregateException)
            when (aggregateException.InnerExceptions.All(IsBlobNotFoundException))
        {
            _logger.LogWarning(
                aggregateException,
                "One or more blobs were not found during batch delete operation for {Org}.",
                org
            );
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteDataBlobs(Instance instance, int? storageAccountNumber)
    {
        return await DeleteDataBlobs(
            instance.Org,
            instance.AppId,
            instance.Id.Split('/')[^1],
            storageAccountNumber,
            CancellationToken.None
        );
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteDataBlobs(
        string org,
        string appId,
        string instanceGuid,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        BlobContainerClient container = CreateContainerClient(org, storageAccountNumber);

        if (container == null)
        {
            _logger.LogError(
                $"BlobService // DeleteDataBlobs // Could not connect to blob container."
            );
            return false;
        }

        try
        {
            string blobPrefix = $"{appId}/{instanceGuid}";
            await foreach (
                BlobItem item in container.GetBlobsAsync(
                    BlobTraits.None,
                    BlobStates.None,
                    blobPrefix,
                    cancellationToken
                )
            )
            {
                await container.DeleteBlobIfExistsAsync(
                    item.Name,
                    DeleteSnapshotsOption.IncludeSnapshots,
                    cancellationToken: cancellationToken
                );
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "BlobService // DeleteDataBlobs // Org: {Org}", org);
            return false;
        }

        return true;
    }

    internal static string GetVersionedBlobPath(string appId, string instanceGuid, string versionId)
    {
        return $"{appId}/{instanceGuid}/data-elements/{versionId}";
    }

    private async Task<BlobProperties> UploadFromStreamAsync(
        string org,
        Stream stream,
        string fileName,
        int? storageAccountNumber
    )
    {
        BlobClient blockBlob = CreateBlobClient(org, fileName, storageAccountNumber);
        BlobUploadOptions options = new()
        {
            TransferValidation = new UploadTransferValidationOptions
            {
                ChecksumAlgorithm = StorageChecksumAlgorithm.MD5,
            },
        };
        await blockBlob.UploadAsync(stream, options);
        BlobProperties properties = await blockBlob.GetPropertiesAsync();

        return properties;
    }

    private async Task<Stream> DownloadBlobAsync(
        string org,
        string fileName,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        BlobClient blockBlob = CreateBlobClient(org, fileName, storageAccountNumber);

        Azure.Response<BlobDownloadInfo> response = await blockBlob.DownloadAsync(
            cancellationToken
        );

        return response.Value.Content;
    }

    /// <summary>
    /// Deletes the blob at the supplied concrete path.
    /// </summary>
    private async Task<bool> DeleteIfExistsAsync(
        string org,
        string fileName,
        int? storageAccountNumber
    )
    {
        BlobClient blockBlob = CreateBlobClient(org, fileName, storageAccountNumber);

        bool result = await blockBlob.DeleteIfExistsAsync();

        return result;
    }

    private async Task DeleteBlobsAsync(
        string org,
        IReadOnlyCollection<string> blobStoragePaths,
        int? storageAccountNumber,
        CancellationToken cancellationToken
    )
    {
        BlobContainerClient container = CreateContainerClient(org, storageAccountNumber);
        BlobBatchClient batchClient = CreateBlobBatchClient(org, storageAccountNumber);

        foreach (string[] blobStoragePathBatch in blobStoragePaths.Chunk(256))
        {
            Uri[] blobUris = blobStoragePathBatch
                .Select(blobStoragePath => container.GetBlobClient(blobStoragePath).Uri)
                .ToArray();

            await batchClient.DeleteBlobsAsync(
                blobUris,
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken
            );
        }
    }

    private static bool IsBlobNotFoundException(Exception exception)
    {
        return exception is RequestFailedException { ErrorCode: "BlobNotFound" };
    }

    private BlobClient CreateBlobClient(string org, string blobName, int? storageAccountNumber)
    {
        return CreateContainerClient(org, storageAccountNumber).GetBlobClient(blobName);
    }

    private BlobContainerClient CreateContainerClient(string org, int? storageAccountNumber)
    {
        if (!_storageConfiguration.AccountName.Equals("devstoreaccount1"))
        {
            string cacheKey = GetClientCacheKey(org, storageAccountNumber);
            if (!_memoryCache.TryGetValue(cacheKey, out BlobContainerClient client))
            {
                string containerName = string.Format(
                    _storageConfiguration.OrgStorageContainer,
                    org
                );
                string accountName = GetStorageAccountName(org, storageAccountNumber);

                UriBuilder fullUri = new()
                {
                    Scheme = "https",
                    Host = $"{accountName}.blob.core.windows.net",
                    Path = $"{containerName}",
                };

                client = new BlobContainerClient(fullUri.Uri, GetCachedCredentials());
                _memoryCache.Set(cacheKey, client, _cacheEntryOptionsBlobClient);
            }

            return client;
        }

        StorageSharedKeyCredential storageCredentials = new(
            _storageConfiguration.OrgStorageAccount,
            _storageConfiguration.AccountKey
        );
        Uri storageUrl = new(_storageConfiguration.BlobEndPoint);
        BlobServiceClient commonBlobClient = new(storageUrl, storageCredentials);
        BlobContainerClient blobContainerClient = commonBlobClient.GetBlobContainerClient(
            string.Format(_storageConfiguration.OrgStorageContainer, org)
        );
        return blobContainerClient;
    }

    private BlobBatchClient CreateBlobBatchClient(string org, int? storageAccountNumber)
    {
        if (!_storageConfiguration.AccountName.Equals("devstoreaccount1"))
        {
            string accountName = GetStorageAccountName(org, storageAccountNumber);
            UriBuilder fullUri = new()
            {
                Scheme = "https",
                Host = $"{accountName}.blob.core.windows.net",
            };

            return new BlobServiceClient(fullUri.Uri, GetCachedCredentials()).GetBlobBatchClient();
        }

        StorageSharedKeyCredential storageCredentials = new(
            _storageConfiguration.OrgStorageAccount,
            _storageConfiguration.AccountKey
        );
        Uri storageUrl = new(_storageConfiguration.BlobEndPoint);
        return new BlobServiceClient(storageUrl, storageCredentials).GetBlobBatchClient();
    }

    private string GetStorageAccountName(string org, int? storageAccountNumber)
    {
        string accountName = string.Format(_storageConfiguration.OrgStorageAccount, org);
        if (storageAccountNumber != null)
        {
            accountName =
                $"{accountName.AsSpan(0, accountName.Length - 2)}{(int)storageAccountNumber:D2}";
        }

        return accountName;
    }

    private DefaultAzureCredential GetCachedCredentials()
    {
        if (!_memoryCache.TryGetValue(_credsCacheKey, out DefaultAzureCredential creds))
        {
            creds = new();
            _memoryCache.Set(_credsCacheKey, creds, _cacheEntryOptionsCreds);
        }

        return creds;
    }

    private static string GetClientCacheKey(string org, int? storageAccountNumber)
    {
        return $"blob-{org}-{storageAccountNumber}";
    }
}
