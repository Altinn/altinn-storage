#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="DataService"/> class.
    /// </summary>
    public DataService(
        IFileScanQueueClient fileScanQueueClient,
        IDataRepository dataRepository,
        IBlobRepository blobRepository,
        IInstanceEventService instanceEventService
    )
    {
        _fileScanQueueClient = fileScanQueueClient;
        _dataRepository = dataRepository;
        _blobRepository = blobRepository;
        _instanceEventService = instanceEventService;
    }

    /// <inheritdoc/>
    public async Task StartFileScan(
        InstanceInternal instanceInternal,
        DataType dataType,
        DataElementInternal dataElement,
        DateTimeOffset blobTimestamp,
        int? storageAccountNumber,
        CancellationToken ct
    )
    {
        Instance instance = instanceInternal.Instance;

        if (dataType.EnableFileScan)
        {
            FileScanRequest fileScanRequest = new()
            {
                InstanceId = instance.Id,
                DataElementId = dataElement.DataElement.Id,
                Timestamp = blobTimestamp,
                BlobStoragePath = dataElement.DataElement.BlobStoragePath,
                BlobVersionId = dataElement.BlobVersionId,
                Filename = dataElement.DataElement.Filename,
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
        DataElementInternal dataElement = await _dataRepository.Read(instanceGuid, dataElementId);
        if (dataElement == null)
        {
            return (
                null,
                new ServiceError(404, $"DataElement not found, dataElementId: {dataElementId}")
            );
        }

        Stream filestream = await _blobRepository.ReadBlob(
            org,
            dataElement.DataElement.BlobStoragePath,
            storageAccountNumber
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
    public async Task<(
        DataElementInternal DataElement,
        DateTimeOffset BlobTimestamp
    )> UploadDataAndCreateDataElement(
        InstanceInternal instanceInternal,
        Stream stream,
        DataElementCreateOptions options,
        long instanceInternalId,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        Instance instance = instanceInternal.Instance;

        string dataElementId = options.DataElementId.ToString();
        string instanceGuid = GetInstanceGuid(instance);
        string blobVersionId = await _dataRepository.CreateBlobVersionId(
            Guid.Parse(instanceGuid),
            options.DataElementId,
            instance.AppId,
            instance.Org,
            storageAccountNumber,
            cancellationToken
        );
        string blobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            instanceGuid,
            blobVersionId
        );
        long length;
        DateTimeOffset blobTimestamp;
        try
        {
            (length, blobTimestamp) = await _blobRepository.WriteBlob(
                instance.Org,
                stream,
                blobStoragePath,
                storageAccountNumber
            );

            if (length == 0L)
            {
                throw new InvalidDataException("Empty stream provided. Cannot persist data.");
            }
        }
        catch
        {
            await DeleteAllocatedBlobVersion(
                instance.Org,
                options.DataElementId,
                blobStoragePath,
                blobVersionId,
                storageAccountNumber
            );
            throw;
        }

        DataElement dataElement = new()
        {
            Id = dataElementId,
            InstanceGuid = instanceGuid,
            DataType = options.DataType,
            ContentType = options.ContentType,
            CreatedBy = options.CreatedBy,
            Created = options.Created,
            Filename = options.Filename,
            LastChangedBy = options.CreatedBy,
            LastChanged = options.Created,
            Size = length,
            Refs = options.Refs,
            BlobStoragePath = blobStoragePath,
            FileScanResult = options.FileScanResult,
            Locked = options.Locked,
            IsRead = options.IsRead,
            References = CreateGeneratedFromTaskReferences(options.GeneratedFromTask),
        };

        DataElementInternal dataElementInternal = new(dataElement, blobVersionId);
        DataElementInternal createdDataElement = await _dataRepository.Create(
            dataElementInternal,
            instanceInternalId,
            cancellationToken
        );

        return (createdDataElement, blobTimestamp);
    }

    /// <inheritdoc/>
    public async Task DeleteImmediately(
        InstanceInternal instanceInternal,
        DataElementInternal dataElement,
        int? storageAccountNumber
    )
    {
        Instance instance = instanceInternal.Instance;
        Guid instanceGuid = new(dataElement.DataElement.InstanceGuid);
        Guid dataElementId = new(dataElement.DataElement.Id);

        DateTime deletedTime = DateTime.UtcNow;
        DeleteStatus deleteStatus = new() { IsHardDeleted = true, HardDeleted = deletedTime };
        DataElement markedDataElement = null;
        try
        {
            markedDataElement = await _dataRepository.Update(
                instanceGuid,
                dataElementId,
                new Dictionary<string, object>()
                {
                    { "/deleteStatus", deleteStatus },
                    { "/lastChanged", deletedTime },
                    { "/lastChangedBy", dataElement.DataElement.LastChangedBy },
                }
            );
        }
        catch (RepositoryException exception)
            when (exception.StatusCodeSuggestion == HttpStatusCode.NotFound)
        {
            // A concurrent delete may have removed the metadata after the caller read it.
            // Blob and metadata deletion below are idempotent and should still be attempted.
        }

        var blobVersions = await _dataRepository.ReadBlobVersions(dataElementId) ?? [];

        if (blobVersions.Count > 0)
        {
            foreach (BlobVersionReferencesInternal blobVersion in blobVersions)
            {
                var versionedBlobStoragePaths = blobVersion.BlobVersionIds.Select(versionId =>
                    BlobRepository.GetVersionedBlobPath(
                        blobVersion.AppId,
                        blobVersion.InstanceGuid.ToString(),
                        versionId
                    )
                );

                await _blobRepository.DeleteBlobs(
                    blobVersion.BlobStorageOrg,
                    versionedBlobStoragePaths,
                    blobVersion.StorageAccountNumber
                );
            }

            string legacyBlobStoragePath = DataElementHelper.DataFileName(
                instance.AppId,
                instanceGuid.ToString(),
                dataElementId.ToString()
            );

            // Best-effort cleanup for blobs created before explicit version paths were introduced.
            await _blobRepository.DeleteBlob(
                instance.Org,
                legacyBlobStoragePath,
                storageAccountNumber
            );
        }
        else
        {
            await _blobRepository.DeleteBlob(
                instance.Org,
                dataElement.DataElement.BlobStoragePath,
                storageAccountNumber
            );
        }

        DataElement deletedDataElement = markedDataElement ?? dataElement.DataElement;
        await _dataRepository.Delete(deletedDataElement);

        await _instanceEventService.DispatchEvent(
            InstanceEventType.Deleted,
            instance,
            deletedDataElement
        );
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

    private static string GetInstanceGuid(Instance instance)
    {
        return instance.Id?.Contains('/') == true ? instance.Id.Split('/')[1] : instance.Id;
    }

    private static List<Reference> CreateGeneratedFromTaskReferences(string generatedFromTask)
    {
        if (string.IsNullOrEmpty(generatedFromTask))
        {
            return null;
        }

        return
        [
            new Reference
            {
                Relation = RelationType.GeneratedFrom,
                Value = generatedFromTask,
                ValueType = ReferenceType.Task,
            },
        ];
    }

    private async Task DeleteAllocatedBlobVersion(
        string org,
        Guid dataElementId,
        string blobStoragePath,
        string blobVersionId,
        int? storageAccountNumber
    )
    {
        if (string.IsNullOrEmpty(blobVersionId))
        {
            return;
        }

        if (!string.IsNullOrEmpty(blobStoragePath))
        {
            try
            {
                await _blobRepository.DeleteBlob(org, blobStoragePath, storageAccountNumber);
            }
            catch
            {
                // Keep the allocation row so orphan cleanup can retry the blob delete later.
                return;
            }
        }

        try
        {
            await _dataRepository.DeleteBlobVersion(
                dataElementId,
                blobVersionId,
                CancellationToken.None
            );
        }
        catch
        {
            // Best-effort compensation must not hide the original upload failure.
        }
    }
}
