#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Azure;
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
    private const string _authenticationFailedErrorCode = "AuthenticationFailed";
    private const int _blobBatchDeleteLimit = 256;
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
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    )
    {
        AzureBlobProviderInfo info = GetAzureBlobProviderInfo(account);
        try
        {
            return await DownloadBlobAsync(info, blobStoragePath, cancellationToken);
        }
        catch (RequestFailedException requestFailedException)
        {
            switch (requestFailedException.ErrorCode)
            {
                case _authenticationFailedErrorCode:
                    _logger.LogWarning(
                        "Authentication failed. Invalidating credentials and retrying download operation."
                    );

                    InvalidateCachedClients(info);

                    return await DownloadBlobAsync(info, blobStoragePath, cancellationToken);
                case "BlobNotFound":
                    _logger.LogWarning(
                        "Unable to find a blob based on the given information - {Container}: {BlobStoragePath}",
                        info.ContainerUri,
                        blobStoragePath
                    );

                    // Returning null because the blob does not exist.
                    return null;
                case "InvalidRange":
                    _logger.LogWarning(
                        "Found possibly empty blob in storage for {Container}: {BlobStoragePath}",
                        info.ContainerUri,
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
        StorageAccount account,
        Stream stream,
        string blobStoragePath
    )
    {
        AzureBlobProviderInfo info = GetAzureBlobProviderInfo(account);
        try
        {
            var blobProps = await UploadFromStreamAsync(info, stream, blobStoragePath);
            return (blobProps.ContentLength, blobProps.LastModified);
        }
        catch (RequestFailedException requestFailedException)
        {
            switch (requestFailedException.ErrorCode)
            {
                case _authenticationFailedErrorCode:
                    _logger.LogWarning("Authentication failed. Invalidating credentials.");

                    InvalidateCachedClients(info);

                    // No use retrying upload as the original stream can't be reset back to start.
                    throw;
                default:
                    throw;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteBlob(StorageAccount account, string blobStoragePath)
    {
        AzureBlobProviderInfo info = GetAzureBlobProviderInfo(account);
        try
        {
            return await DeleteIfExistsAsync(info, blobStoragePath);
        }
        catch (RequestFailedException requestFailedException)
        {
            switch (requestFailedException.ErrorCode)
            {
                case _authenticationFailedErrorCode:
                    _logger.LogWarning(
                        "Authentication failed. Invalidating credentials and retrying delete operation."
                    );

                    InvalidateCachedClients(info);

                    return await DeleteIfExistsAsync(info, blobStoragePath);
                default:
                    throw;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool[]> DeleteBlobsIfExists(
        StorageAccount account,
        IReadOnlyList<string> blobStoragePaths,
        CancellationToken cancellationToken = default
    )
    {
        AzureBlobProviderInfo info = GetAzureBlobProviderInfo(account);
        blobStoragePaths ??= [];
        if (blobStoragePaths.Count == 0)
        {
            return [];
        }

        try
        {
            return await DeleteBlobsIfExistsAsync(info, blobStoragePaths, cancellationToken);
        }
        catch (RequestFailedException requestFailedException)
            when (requestFailedException.ErrorCode == _authenticationFailedErrorCode)
        {
            _logger.LogWarning(
                requestFailedException,
                "Authentication failed. Invalidating credentials and retrying per-path batch delete operation."
            );

            InvalidateCachedClients(info);

            try
            {
                return await DeleteBlobsIfExistsAsync(info, blobStoragePaths, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to delete blobs after authentication retry for {Container}; marking delete outcomes as unknown.",
                    info.ContainerUri
                );
                return new bool[blobStoragePaths.Count];
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteDataBlobs(
        StorageAccount account,
        string appId,
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    )
    {
        AzureBlobProviderInfo info = GetAzureBlobProviderInfo(account);
        BlobContainerClient container = CreateContainerClient(info);

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
            _logger.LogError(
                e,
                "BlobService // DeleteDataBlobs // Container: {Container}",
                info.ContainerUri
            );
            return false;
        }

        return true;
    }

    private static AzureBlobProviderInfo GetAzureBlobProviderInfo(StorageAccount account)
    {
        return account.ProviderInfo switch
        {
            AzureBlobProviderInfo info => info,
            _ => throw new NotSupportedException(
                $"Storage account provider is not supported: {account.ProviderInfo?.GetType().Name}"
            ),
        };
    }

    private async Task<BlobProperties> UploadFromStreamAsync(
        AzureBlobProviderInfo info,
        Stream stream,
        string fileName
    )
    {
        BlobClient blockBlob = CreateBlobClient(info, fileName);
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
        AzureBlobProviderInfo info,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        BlobClient blockBlob = CreateBlobClient(info, fileName);

        Azure.Response<BlobDownloadInfo> response = await blockBlob.DownloadAsync(
            cancellationToken
        );

        return response.Value.Content;
    }

    /// <summary>
    /// Deletes the blob at the supplied concrete path.
    /// </summary>
    private async Task<bool> DeleteIfExistsAsync(AzureBlobProviderInfo info, string fileName)
    {
        BlobClient blockBlob = CreateBlobClient(info, fileName);

        bool result = await blockBlob.DeleteIfExistsAsync();

        return result;
    }

    private async Task<bool[]> DeleteBlobsIfExistsAsync(
        AzureBlobProviderInfo info,
        IReadOnlyList<string> blobStoragePaths,
        CancellationToken cancellationToken
    )
    {
        bool[] deletedBlobStoragePaths = new bool[blobStoragePaths.Count];
        BlobContainerClient container = CreateContainerClient(info);
        BlobBatchClient batchClient = CreateBlobBatchClient(info);

        for (
            int batchStartIndex = 0;
            batchStartIndex < blobStoragePaths.Count;
            batchStartIndex += _blobBatchDeleteLimit
        )
        {
            BlobBatch batch = batchClient.CreateBatch();
            int batchLength = Math.Min(
                _blobBatchDeleteLimit,
                blobStoragePaths.Count - batchStartIndex
            );
            List<(int ResultIndex, string BlobStoragePath, Response Response)> deleteResponses = [];

            for (int index = batchStartIndex; index < batchStartIndex + batchLength; index++)
            {
                string blobStoragePath = blobStoragePaths[index];
                if (string.IsNullOrEmpty(blobStoragePath))
                {
                    continue;
                }

                Uri blobUri = container.GetBlobClient(blobStoragePath).Uri;
                Response response = batch.DeleteBlob(
                    blobUri,
                    DeleteSnapshotsOption.IncludeSnapshots
                );
                deleteResponses.Add((index, blobStoragePath, response));
            }

            if (deleteResponses.Count == 0)
            {
                continue;
            }

            try
            {
                await batchClient.SubmitBatchAsync(
                    batch,
                    throwOnAnyFailure: false,
                    cancellationToken
                );
            }
            catch (RequestFailedException requestFailedException)
                when (requestFailedException.ErrorCode == _authenticationFailedErrorCode)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to submit blob batch delete operation for {BlobCount} blobs in {Container}; leaving blob-version rows for retry.",
                    deleteResponses.Count,
                    info.ContainerUri
                );
                continue;
            }

            foreach (
                (int resultIndex, string blobStoragePath, Response response) in deleteResponses
            )
            {
                if (IsSafeBatchDeleteStatus(response.Status))
                {
                    deletedBlobStoragePaths[resultIndex] = true;
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to delete detached blob {BlobStoragePath} in batch operation. Status: {Status}; leaving blob-version row for retry.",
                        blobStoragePath,
                        response.Status
                    );
                }
            }
        }

        return deletedBlobStoragePaths;
    }

    private static bool IsSafeBatchDeleteStatus(int statusCode)
    {
        return statusCode is 202 or 404;
    }

    private BlobClient CreateBlobClient(AzureBlobProviderInfo info, string blobName)
    {
        return CreateContainerClient(info).GetBlobClient(blobName);
    }

    private BlobContainerClient CreateContainerClient(AzureBlobProviderInfo info)
    {
        string cacheKey = GetClientCacheKey(info);
        if (!_memoryCache.TryGetValue(cacheKey, out BlobContainerClient client))
        {
            client = UsesSharedKey(info)
                ? new BlobContainerClient(info.ContainerUri, CreateSharedKeyCredential())
                : new BlobContainerClient(info.ContainerUri, GetCachedCredentials());
            _memoryCache.Set(cacheKey, client, _cacheEntryOptionsBlobClient);
        }

        return client;
    }

    private BlobBatchClient CreateBlobBatchClient(AzureBlobProviderInfo info)
    {
        BlobServiceClient serviceClient = UsesSharedKey(info)
            ? new BlobServiceClient(info.ServiceUri, CreateSharedKeyCredential())
            : new BlobServiceClient(info.ServiceUri, GetCachedCredentials());
        return serviceClient.GetBlobBatchClient();
    }

    /// <summary>
    /// Gets whether the account is served by the configured blob endpoint (Azurite), which is
    /// accessed with the configured account key instead of an Azure identity.
    /// </summary>
    private bool UsesSharedKey(AzureBlobProviderInfo info)
    {
        return info.Endpoint is not null
            && Uri.TryCreate(
                _storageConfiguration.BlobEndPoint,
                UriKind.Absolute,
                out Uri configured
            )
            && string.Equals(
                info.Endpoint.Host,
                configured.Host,
                StringComparison.OrdinalIgnoreCase
            )
            && info.Endpoint.Port == configured.Port;
    }

    private StorageSharedKeyCredential CreateSharedKeyCredential()
    {
        return new StorageSharedKeyCredential(
            _storageConfiguration.AccountName,
            _storageConfiguration.AccountKey
        );
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

    private void InvalidateCachedClients(AzureBlobProviderInfo info)
    {
        _memoryCache.Remove(_credsCacheKey);
        _memoryCache.Remove(GetClientCacheKey(info));
    }

    private static string GetClientCacheKey(AzureBlobProviderInfo info)
    {
        return $"blob-{info.ContainerUri}";
    }
}
