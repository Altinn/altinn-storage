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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

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
        ILogger<DataService> logger = null
    )
    {
        _fileScanQueueClient = fileScanQueueClient;
        _dataRepository = dataRepository;
        _blobRepository = blobRepository;
        _instanceEventService = instanceEventService;
        _logger = logger ?? NullLogger<DataService>.Instance;
    }

    /// <inheritdoc/>
    public async Task StartFileScan(
        InstanceInternal instance,
        DataType dataType,
        DataElementInternal dataElement,
        DateTimeOffset blobTimestamp,
        int? storageAccountNumber,
        CancellationToken ct
    )
    {
        if (dataType.EnableFileScan)
        {
            FileScanRequest fileScanRequest = new()
            {
                InstanceId = $"{instance.InstanceOwner.PartyId}/{instance.Id}",
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
            dataElement.BlobStoragePath,
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
    public async Task<DataUploadResult> UploadDataAndCreateDataElement(
        InstanceInternal instance,
        Stream stream,
        DataElementCreateOptions options,
        long instanceInternalId,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    )
    {
        string instanceGuid = instance.Id;
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
                _blobRepository,
                _dataRepository,
                instance.Org,
                options.DataElementId,
                blobStoragePath,
                blobVersionId,
                storageAccountNumber
            );
            throw;
        }

        DataElementInternal dataElement = new()
        {
            Id = options.DataElementId.ToString(),
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
            BlobVersionId = blobVersionId,
        };

        DataElementWriteResult createdDataElement;
        try
        {
            createdDataElement = await _dataRepository.Create(
                dataElement,
                instanceInternalId,
                cancellationToken,
                expectedInstanceVersion,
                expectedProcessStateVersion
            );
        }
        catch
        {
            await DeleteAllocatedBlobVersion(
                _blobRepository,
                _dataRepository,
                instance.Org,
                options.DataElementId,
                blobStoragePath,
                blobVersionId,
                storageAccountNumber
            );
            throw;
        }

        return new DataUploadResult(
            createdDataElement.DataElement,
            blobTimestamp,
            createdDataElement.Versions
        );
    }

    /// <inheritdoc/>
    public async Task<DataElementInternal> DeleteImmediately(
        InstanceInternal instance,
        DataElementInternal dataElement,
        int? storageAccountNumber,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null
    )
    {
        Guid instanceGuid = Guid.Parse(dataElement.InstanceGuid);
        Guid dataElementId = Guid.Parse(dataElement.Id);
        DateTime deletedTime = DateTime.UtcNow;
        DeleteStatus deleteStatus = new() { IsHardDeleted = true, HardDeleted = deletedTime };
        DataElementInternal markedDataElement = null;
        try
        {
            DataElementWriteResult markedDataElementResult = await _dataRepository.Update(
                instanceGuid,
                dataElementId,
                new Dictionary<string, object>
                {
                    { "/deleteStatus", deleteStatus },
                    { "/lastChanged", deletedTime },
                    { "/lastChangedBy", dataElement.LastChangedBy },
                },
                new DataElementUpdateContext
                {
                    ExpectedInstanceVersion = expectedInstanceVersion,
                    ExpectedProcessStateVersion = expectedProcessStateVersion,
                }
            );
            markedDataElement = markedDataElementResult.DataElement;
        }
        catch (RepositoryException exception)
            when (exception.StatusCodeSuggestion == HttpStatusCode.NotFound)
        {
            // A concurrent delete may have removed the metadata after the caller read it.
            // Blob and metadata deletion below are idempotent and should still be attempted.
        }

        IReadOnlyList<BlobVersionReferencesInternal> blobVersions =
            await _dataRepository.ReadBlobVersions(dataElementId) ?? [];

        if (blobVersions.Count > 0)
        {
            foreach (BlobVersionReferencesInternal blobVersion in blobVersions)
            {
                foreach (string versionId in blobVersion.BlobVersionIds)
                {
                    await _blobRepository.DeleteBlob(
                        blobVersion.BlobStorageOrg,
                        BlobRepository.GetVersionedBlobPath(
                            blobVersion.AppId,
                            blobVersion.InstanceGuid.ToString(),
                            versionId
                        ),
                        blobVersion.StorageAccountNumber
                    );
                }
            }

            string legacyBlobStoragePath = DataElementHelper.DataFileName(
                instance.AppId,
                instanceGuid.ToString(),
                dataElementId.ToString()
            );
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
                dataElement.BlobStoragePath,
                storageAccountNumber
            );
        }

        DataElementInternal deletedDataElement = markedDataElement ?? dataElement;
        await _dataRepository.Delete(deletedDataElement, CancellationToken.None);
        await _instanceEventService.DispatchEvent(
            InstanceEventType.Deleted,
            instance,
            deletedDataElement
        );

        return deletedDataElement;
    }

    /// <inheritdoc/>
    public async Task CleanupDeletedDataElementBlobs(
        InstanceInternal instance,
        DataElementInternal dataElement,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        Guid dataElementId = Guid.Parse(dataElement.Id);
        await CleanupDetachedBlobVersions(dataElementId, cancellationToken);
        await DeleteLegacyDataElementBlob(instance, dataElement, storageAccountNumber);
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

    private async Task CleanupDetachedBlobVersions(
        Guid dataElementId,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<BlobVersionReferencesInternal> detachedBlobVersions;
        try
        {
            detachedBlobVersions =
                await _dataRepository.ReadDetachedBlobVersions(dataElementId, cancellationToken)
                ?? [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Failed to read detached blob versions for deleted data element {DataElementId}; leaving cleanup for retry.",
                dataElementId
            );
            return;
        }

        Dictionary<
            (string BlobStorageOrg, int? StorageAccountNumber),
            List<(string BlobVersionId, string BlobStoragePath)>
        > blobVersionsByStorage = [];
        foreach (BlobVersionReferencesInternal blobVersion in detachedBlobVersions)
        {
            foreach (string blobVersionId in blobVersion.BlobVersionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string versionedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
                    blobVersion.AppId,
                    blobVersion.InstanceGuid.ToString(),
                    blobVersionId
                );

                (string BlobStorageOrg, int? StorageAccountNumber) storageKey = (
                    blobVersion.BlobStorageOrg,
                    blobVersion.StorageAccountNumber
                );
                if (!blobVersionsByStorage.TryGetValue(storageKey, out var blobVersionPaths))
                {
                    blobVersionPaths = [];
                    blobVersionsByStorage[storageKey] = blobVersionPaths;
                }

                blobVersionPaths.Add((blobVersionId, versionedBlobStoragePath));
            }
        }

        List<string> deletedBlobVersionIds = [];
        foreach (
            KeyValuePair<
                (string BlobStorageOrg, int? StorageAccountNumber),
                List<(string BlobVersionId, string BlobStoragePath)>
            > blobStorageGroup in blobVersionsByStorage
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> blobStoragePaths = blobStorageGroup
                .Value.Select(blobVersion => blobVersion.BlobStoragePath)
                .ToArray();
            bool[] deletedBlobs;
            try
            {
                deletedBlobs = await _blobRepository.DeleteBlobsIfExists(
                    blobStorageGroup.Key.BlobStorageOrg,
                    blobStoragePaths,
                    blobStorageGroup.Key.StorageAccountNumber,
                    cancellationToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Keep all rows from this storage batch so orphan cleanup can retry later.
                _logger.LogWarning(
                    exception,
                    "Failed to delete detached blobs in batch for {BlobStorageOrg}; leaving blob-version rows for retry.",
                    blobStorageGroup.Key.BlobStorageOrg
                );
                continue;
            }

            for (int index = 0; index < blobStorageGroup.Value.Count; index++)
            {
                if (index < deletedBlobs.Length && deletedBlobs[index])
                {
                    deletedBlobVersionIds.Add(blobStorageGroup.Value[index].BlobVersionId);
                }
            }
        }

        if (deletedBlobVersionIds.Count == 0)
        {
            return;
        }

        try
        {
            await _dataRepository.DeleteBlobVersions(
                dataElementId,
                deletedBlobVersionIds,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Metadata cleanup is best-effort after the aggregate metadata commit.
            _logger.LogWarning(
                exception,
                "Failed to delete {BlobVersionCount} detached blob-version rows for data element {DataElementId}.",
                deletedBlobVersionIds.Count,
                dataElementId
            );
        }
    }

    private async Task DeleteLegacyDataElementBlob(
        InstanceInternal instance,
        DataElementInternal dataElementInternal,
        int? storageAccountNumber
    )
    {
        string instanceGuid = instance.Id;
        string legacyBlobStoragePath =
            string.IsNullOrEmpty(dataElementInternal.BlobVersionId)
            && !string.IsNullOrEmpty(dataElementInternal.BlobStoragePath)
                ? dataElementInternal.BlobStoragePath
                : DataElementHelper.DataFileName(
                    instance.AppId,
                    instanceGuid,
                    dataElementInternal.Id
                );

        if (string.IsNullOrEmpty(legacyBlobStoragePath))
        {
            return;
        }

        try
        {
            await _blobRepository.DeleteBlob(
                instance.Org,
                legacyBlobStoragePath,
                storageAccountNumber
            );
        }
        catch (Exception exception)
        {
            // Legacy blobs have no durable blob-version cleanup row; cleanup remains best-effort.
            _logger.LogWarning(
                exception,
                "Failed to delete legacy blob {BlobStoragePath} for deleted data element {DataElementId}.",
                legacyBlobStoragePath,
                dataElementInternal.Id
            );
        }
    }

    /// <summary>
    /// Returns true when some exception in the chain proves the database transaction rolled
    /// back, so staged blob compensation is safe; false when the commit outcome is unknown
    /// and cleanup must be left to the orphan cleanup job. An inner-exception chain is the
    /// causality of one operation, so one definite link decides it; the flattened siblings of
    /// an <see cref="AggregateException"/> represent separate operations, so every sibling
    /// must be definite — one proven abort does not decide an ambiguous sibling's outcome.
    /// </summary>
    internal static bool IndicatesDefiniteRollback(Exception exception)
    {
        for (Exception current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException or RepositoryException)
            {
                return true;
            }

            if (current is AggregateException aggregateException)
            {
                var siblings = aggregateException.Flatten().InnerExceptions;
                return siblings.Count > 0 && siblings.All(IndicatesDefiniteRollback);
            }
        }

        return false;
    }

    /// <summary>
    /// Best-effort compensation after a failed write against an allocated blob version:
    /// deletes the uploaded blob (if any) and then the allocation row. Never throws, so
    /// the original failure stays visible to the caller.
    /// </summary>
    internal static async Task DeleteAllocatedBlobVersion(
        IBlobRepository blobRepository,
        IDataRepository dataRepository,
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
                await blobRepository.DeleteBlob(org, blobStoragePath, storageAccountNumber);
            }
            catch
            {
                // Keep the allocation row so orphan cleanup can retry the blob delete later.
                return;
            }
        }

        try
        {
            await dataRepository.DeleteBlobVersion(
                dataElementId,
                blobVersionId,
                CancellationToken.None
            );
        }
        catch
        {
            // Best-effort compensation must not hide the original failure.
        }
    }
}
