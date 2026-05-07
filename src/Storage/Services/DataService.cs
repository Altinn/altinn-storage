#nullable disable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Service class with business logic related to data blobs and their metadata documents.
/// </summary>
public class DataService : IDataService
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly IFileScanQueueClient _fileScanQueueClient;
    private readonly IDataRepository _dataRepository;
    private readonly IBlobRepository _blobRepository;
    private readonly IInstanceEventService _instanceEventService;
    private readonly ILogger<DataService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataService"/> class.
    /// </summary>
    public DataService(
        IFileScanQueueClient fileScanQueueClient,
        IDataRepository dataRepository,
        IBlobRepository blobRepository,
        IInstanceEventService instanceEventService,
        ILogger<DataService> logger
    )
    {
        _fileScanQueueClient = fileScanQueueClient;
        _dataRepository = dataRepository;
        _blobRepository = blobRepository;
        _instanceEventService = instanceEventService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartFileScan(
        Instance instance,
        DataType dataType,
        DataElement dataElement,
        DateTimeOffset blobTimestamp,
        int? storageAccountNumber,
        CancellationToken ct
    )
    {
        if (dataType.EnableFileScan)
        {
            FileScanRequest fileScanRequest = new()
            {
                InstanceId = instance.Id,
                DataElementId = dataElement.Id,
                Timestamp = blobTimestamp,
                BlobStoragePath = dataElement.BlobStoragePath,
                BlobVersionId = dataElement.BlobVersionId,
                Filename = dataElement.Filename,
                Org = instance.Org,
                StorageAccountNumber = storageAccountNumber,
            };

            string serialisedRequest = JsonSerializer.Serialize(
                fileScanRequest,
                _jsonSerializerOptions
            );

            await _fileScanQueueClient.EnqueueFileScan(serialisedRequest, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<(string FileHash, ServiceError ServiceError)> GenerateSha256Hash(
        string org,
        Guid instanceGuid,
        Guid dataElementId,
        int? storageAccountNumber
    )
    {
        DataElement dataElement = await _dataRepository.Read(instanceGuid, dataElementId);
        if (dataElement == null)
        {
            return (
                null,
                new ServiceError(404, $"DataElement not found, dataElementId: {dataElementId}")
            );
        }

        Stream filestream = await _blobRepository.ReadBlob(
            org,
            dataElement.BlobStoragePath,
            storageAccountNumber,
            dataElement.BlobVersionId
        );
        if (filestream == null || !filestream.CanRead)
        {
            return (
                null,
                new ServiceError(404, $"Failed reading file, dataElementId: {dataElementId}")
            );
        }

        using var sha256 = SHA256.Create();
        var digest = await sha256.ComputeHashAsync(filestream);
        return (FormatShaDigest(digest), null);
    }

    /// <inheritdoc/>
    public async Task UploadDataAndCreateDataElement(
        string org,
        Stream stream,
        DataElement dataElement,
        long instanceInternalId,
        int? storageAccountNumber
    )
    {
        (long length, _, string blobVersionId) = await _blobRepository.WriteBlob(
            org,
            stream,
            dataElement.BlobStoragePath,
            storageAccountNumber
        );
        dataElement.Size = length;
        dataElement.BlobVersionId = blobVersionId;

        try
        {
            await _dataRepository.Create(dataElement, instanceInternalId);
        }
        catch
        {
            await DeleteBlobBestEffort(
                org,
                dataElement.BlobStoragePath,
                storageAccountNumber,
                blobVersionId,
                dataElement.InstanceGuid,
                dataElement.Id
            );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<DataElement> DeleteImmediately(
        Instance instance,
        DataElement dataElement,
        int? storageAccountNumber
    )
    {
        await _dataRepository.Delete(dataElement);

        string storageFileName = DataElementHelper.DataFileName(
            instance.AppId,
            dataElement.InstanceGuid,
            dataElement.Id
        );

        await DeleteBlobBestEffort(
            instance.Org,
            storageFileName,
            storageAccountNumber,
            null,
            dataElement.InstanceGuid,
            dataElement.Id
        );

        await _instanceEventService.DispatchEvent(InstanceEventType.Deleted, instance, dataElement);

        return dataElement;
    }

    private async Task DeleteBlobBestEffort(
        string org,
        string blobStoragePath,
        int? storageAccountNumber,
        string blobVersionId,
        string instanceGuid,
        string dataElementId
    )
    {
        try
        {
            bool deleted = await _blobRepository.DeleteBlob(
                org,
                blobStoragePath,
                storageAccountNumber,
                blobVersionId
            );

            if (!deleted)
            {
                _logger.LogWarning(
                    "DataService // DeleteBlob returned false for org {Org}, blob path {BlobPath}, blob version {BlobVersionId}, instance {InstanceGuid}, data element {DataElementId}.",
                    org,
                    blobStoragePath,
                    blobVersionId,
                    instanceGuid,
                    dataElementId
                );
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "DataService // DeleteBlob failed for org {Org}, blob path {BlobPath}, blob version {BlobVersionId}, instance {InstanceGuid}, data element {DataElementId}.",
                org,
                blobStoragePath,
                blobVersionId,
                instanceGuid,
                dataElementId
            );
        }
    }

    /// <summary>
    /// Formats a SHA digest with common best best practice:<br/>
    /// Lowercase hexadecimal representation without delimiters
    /// </summary>
    /// <param name="digest">The hash code (digest) to format</param>
    /// <returns>String representation of the digest</returns>
    private static string FormatShaDigest(byte[] digest)
    {
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
