#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private readonly ILogger<DataService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataService"/> class.
    /// </summary>
    public DataService(
        IFileScanQueueClient fileScanQueueClient,
        IDataRepository dataRepository,
        IBlobRepository blobRepository,
        ILogger<DataService> logger = null
    )
    {
        _fileScanQueueClient = fileScanQueueClient;
        _dataRepository = dataRepository;
        _blobRepository = blobRepository;
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
                DataElementId = dataElement.Id.ToString(),
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
        StagedDataElementBlob stagedDataElement = await StageDataElementBlob(
            instance,
            stream,
            options,
            storageAccountNumber,
            cancellationToken
        );

        DataElementWriteResult createdDataElement;
        try
        {
            createdDataElement = await _dataRepository.Create(
                stagedDataElement.DataElement,
                instanceInternalId,
                cancellationToken,
                expectedInstanceVersion,
                expectedProcessStateVersion
            );
        }
        catch (Exception exception)
        {
            if (IndicatesDefiniteRollback(exception))
            {
                await DeleteStagedDataElementBlob(
                    instance,
                    stagedDataElement.DataElement,
                    storageAccountNumber
                );
            }

            throw;
        }

        return new DataUploadResult(
            createdDataElement.DataElement,
            stagedDataElement.BlobTimestamp,
            createdDataElement.Versions
        );
    }

    /// <inheritdoc/>
    public async Task<StagedDataElementBlob> StageDataElementBlob(
        InstanceInternal instance,
        Stream stream,
        DataElementCreateOptions options,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        string blobVersionId = await _dataRepository.CreateBlobVersionId(
            instance.Id,
            options.DataElementId,
            instance.AppId,
            instance.Org,
            storageAccountNumber,
            cancellationToken
        );
        string blobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            instance.Id,
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
            Id = options.DataElementId,
            InstanceGuid = instance.Id,
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

        return new StagedDataElementBlob(dataElement, blobTimestamp);
    }

    /// <inheritdoc/>
    public Task DeleteStagedDataElementBlob(
        InstanceInternal instance,
        DataElementInternal dataElement,
        int? storageAccountNumber
    ) =>
        DeleteAllocatedBlobVersion(
            _blobRepository,
            _dataRepository,
            instance.Org,
            dataElement.Id,
            dataElement.BlobStoragePath,
            dataElement.BlobVersionId,
            storageAccountNumber
        );

    /// <inheritdoc/>
    public async Task CleanupDeletedDataElementBlobs(
        InstanceInternal instance,
        DataElementInternal dataElement,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default
    )
    {
        await CleanupDetachedBlobVersions(dataElement.Id, cancellationToken);
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
            return [];
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

        var blobVersionsByStorage = detachedBlobVersions
            .SelectMany(bv => bv.BlobVersionIds.Select(id => (bv, id)))
            .GroupBy(
                x => (x.bv.BlobStorageOrg, x.bv.StorageAccountNumber),
                x =>
                    (
                        BlobVersionId: x.id,
                        BlobStoragePath: BlobRepository.GetVersionedBlobPath(
                            x.bv.AppId,
                            x.bv.InstanceGuid,
                            x.id
                        )
                    )
            );

        List<string> deletedBlobVersionIds = [];
        foreach (var blobStorageGroup in blobVersionsByStorage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blobVersions = blobStorageGroup.ToArray();
            IReadOnlyList<string> blobStoragePaths = blobVersions
                .Select(blobVersion => blobVersion.BlobStoragePath)
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

            deletedBlobVersionIds.AddRange(
                blobVersions
                    .Where((_, index) => deletedBlobs[index])
                    .Select(blobVersion => blobVersion.BlobVersionId)
            );
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
        string legacyBlobStoragePath =
            string.IsNullOrEmpty(dataElementInternal.BlobVersionId)
            && !string.IsNullOrEmpty(dataElementInternal.BlobStoragePath)
                ? dataElementInternal.BlobStoragePath
                : DataElementHelper.DataFileName(
                    instance.AppId,
                    instance.Id,
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
