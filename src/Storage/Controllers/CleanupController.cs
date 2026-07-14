#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Handles cleanup of storage data
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CleanupController"/> class
/// </remarks>
/// <param name="instanceRepository">the instance repository handler</param>
/// <param name="applicationRepository">the application repository handler</param>
/// <param name="blobRepository">the blob repository handler</param>
/// <param name="dataRepository">the data repository handler</param>
/// <param name="instanceEventRepository">the instance event repository handler</param>
/// <param name="instanceMutationRepository">the instance mutation repository handler</param>
/// <param name="cleanupSettings">the cleanup settings</param>
/// <param name="logger">the logger</param>
[Route("storage/api/v1/cleanup")]
[ApiController]
public class CleanupController(
    IInstanceRepository instanceRepository,
    IApplicationRepository applicationRepository,
    IBlobRepository blobRepository,
    IDataRepository dataRepository,
    IInstanceEventRepository instanceEventRepository,
    IInstanceMutationRepository instanceMutationRepository,
    IOptions<StorageCleanupSettings> cleanupSettings,
    ILogger<CleanupController> logger
) : ControllerBase
{
    private readonly ILogger<CleanupController> _logger = logger;
    private readonly StorageCleanupSettings _cleanupSettings = cleanupSettings.Value;

    /// <summary>
    /// Invoke periodic cleanup of instances
    /// </summary>
    /// <returns>?</returns>
    [HttpDelete("cleanupinstances")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult> CleanupInstances(CancellationToken cancellationToken)
    {
        try
        {
            List<InstanceInternal> instances = await instanceRepository.GetHardDeletedInstances(
                cancellationToken
            );
            List<string> autoDeleteAppIds = (await applicationRepository.FindAll())
                .Where(a =>
                    instances.Select(i => i.AppId).ToList().Contains(a.Id)
                    && a.AutoDeleteOnProcessEnd
                )
                .Select(a => a.Id)
                .ToList();

            Stopwatch stopwatch = Stopwatch.StartNew();
            int successfullyDeleted = await CleanupInstancesInternal(
                instances,
                autoDeleteAppIds,
                cancellationToken
            );
            stopwatch.Stop();

            _logger.LogInformation(
                "CleanupController// CleanupInstances // {DeleteCount} of {OriginalCount} instances deleted in {Duration} s",
                successfullyDeleted,
                instances.Count,
                stopwatch.Elapsed.TotalSeconds
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CleanupController error");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "CleanupController error: " + ex.Message
            );
        }

        return Ok();
    }

    /// <summary>
    /// Invoke periodic cleanup of aggregate mutation idempotency records.
    /// </summary>
    [HttpDelete("cleanupinstancemutationidempotency")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult> CleanupInstanceMutationIdempotency(
        CancellationToken cancellationToken
    )
    {
        try
        {
            TimeSpan retention = GetInstanceMutationIdempotencyRetention();
            DateTime deleteBeforeUtc = DateTime.UtcNow - retention;

            Stopwatch stopwatch = Stopwatch.StartNew();
            int deleted = await instanceMutationRepository.DeleteIdempotencyRecordsCreatedBefore(
                deleteBeforeUtc,
                cancellationToken: cancellationToken
            );
            stopwatch.Stop();

            _logger.LogInformation(
                "CleanupController // CleanupInstanceMutationIdempotency // {DeleteCount} idempotency records older than {RetentionHours} hours deleted in {Duration} s",
                deleted,
                retention.TotalHours,
                stopwatch.Elapsed.TotalSeconds
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CleanupController idempotency cleanup error");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "CleanupController idempotency cleanup error: " + ex.Message
            );
        }

        return Ok();
    }

    /// <summary>
    /// Invoke periodic cleanup of instances for a specific app
    /// </summary>
    /// <returns>?</returns>
    [HttpDelete("cleanupinstancesforapp/{org}/{app}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult> CleanupInstancesForApp(
        string org,
        string app,
        CancellationToken cancellationToken
    )
    {
        int successfullyDeleted = 0;
        int processed = 0;
        InstanceQueryResult instancesResponse = new() { ContinuationToken = null };

        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            InstanceQueryParameters queryParameters = new()
            {
                Size = 5000,
                AppId = $"{org}/{app}",
                ContinuationToken = instancesResponse.ContinuationToken,
                IncludeDataElements = true,
            };

            instancesResponse = await instanceRepository.GetInstancesFromQuery(
                queryParameters,
                cancellationToken
            );
            successfullyDeleted += await CleanupInstancesInternal(
                instancesResponse.Instances,
                [],
                cancellationToken
            );
            processed += instancesResponse.Instances.Count;
        } while (instancesResponse.ContinuationToken != null);
        stopwatch.Stop();

        _logger.LogInformation(
            "CleanupController // CleanupInstancesForApp // {DeleteCount} of {OriginalCount} instances deleted in {Duration} s",
            successfullyDeleted,
            processed,
            stopwatch.Elapsed.TotalSeconds
        );

        return Ok();
    }

    private TimeSpan GetInstanceMutationIdempotencyRetention()
    {
        int configuredHours = _cleanupSettings.InstanceMutationIdempotencyRetentionHours;
        int retentionHours = Math.Max(
            configuredHours,
            StorageCleanupSettings.MinimumInstanceMutationIdempotencyRetentionHours
        );
        if (retentionHours != configuredHours)
        {
            _logger.LogWarning(
                "CleanupController // CleanupInstanceMutationIdempotency // Configured retention {ConfiguredHours} hours is below the minimum {MinimumHours} hours; using the minimum.",
                configuredHours,
                StorageCleanupSettings.MinimumInstanceMutationIdempotencyRetentionHours
            );
        }

        return TimeSpan.FromHours(retentionHours);
    }

    /// <summary>
    /// Invoke periodic cleanup of data elements
    /// </summary>
    /// <returns>?</returns>
    [HttpDelete("cleanupdataelements")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [Produces("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult> CleanupDataelements(CancellationToken cancellationToken)
    {
        List<DeletedDataElementInternal> dataElements =
            await instanceRepository.GetHardDeletedDataElements(cancellationToken);

        int successfullyDeleted = 0;

        Stopwatch stopwatch = Stopwatch.StartNew();

        Application app = null;
        InstanceInternal instance = null;
        foreach (
            DeletedDataElementInternal deletedDataElement in dataElements.OrderBy(d =>
                d.DataElement.InstanceGuid
            )
        )
        {
            DataElementInternal dataElement = deletedDataElement.DataElement;
            try
            {
                if (
                    instance == null
                    || Guid.Parse(instance.Id) != Guid.Parse(dataElement.InstanceGuid)
                )
                {
                    instance = await instanceRepository.GetOne(
                        Guid.Parse(dataElement.InstanceGuid),
                        false,
                        cancellationToken
                    );
                    if (instance is null)
                    {
                        _logger.LogError(
                            "CleanupController // CleanupDataelements // Instance not found for dataElement Id: {DataElementId}",
                            dataElement.Id
                        );
                        continue;
                    }

                    app = await applicationRepository.FindOne(
                        instance.AppId,
                        instance.Org,
                        cancellationToken
                    );
                }

                string currentBlobStoragePath = dataElement.BlobStoragePath;
                bool hasBlobVersions = deletedDataElement.BlobVersions.Count > 0;
                if (
                    !hasBlobVersions
                    && !await blobRepository.DeleteBlob(
                        currentBlobStoragePath.Split('/')[0],
                        currentBlobStoragePath,
                        app.StorageAccountNumber
                    )
                )
                {
                    _logger.LogError(
                        "CleanupController // CleanupDataelements // Blob not found for dataElement Id: {DataElementId} Blobstoragepath: {BlobStoragePath}",
                        dataElement.Id,
                        dataElement.BlobStoragePath
                    );
                }

                if (hasBlobVersions)
                {
                    foreach (
                        BlobVersionReferencesInternal blobVersion in deletedDataElement.BlobVersions
                    )
                    {
                        List<string> versionedBlobStoragePaths =
                        [
                            .. blobVersion.BlobVersionIds.Select(versionId =>
                                BlobRepository.GetVersionedBlobPath(
                                    blobVersion.AppId,
                                    blobVersion.InstanceGuid.ToString(),
                                    versionId
                                )
                            ),
                        ];

                        bool[] blobsDeleted = await blobRepository.DeleteBlobsIfExists(
                            blobVersion.BlobStorageOrg,
                            versionedBlobStoragePaths,
                            blobVersion.StorageAccountNumber,
                            cancellationToken
                        );
                        if (!blobsDeleted.All(deleted => deleted))
                        {
                            _logger.LogError(
                                "CleanupController // CleanupDataelements // One or more blob deletes failed or had unknown outcome for dataElement Id: {DataElementId} Blobstoragepath: {BlobStoragePath}",
                                dataElement.Id,
                                dataElement.BlobStoragePath
                            );
                        }

                        string legacyBlobStoragePath = DataElementHelper.DataFileName(
                            blobVersion.AppId,
                            blobVersion.InstanceGuid.ToString(),
                            dataElement.Id
                        );
                        await blobRepository.DeleteBlob(
                            blobVersion.BlobStorageOrg,
                            legacyBlobStoragePath,
                            blobVersion.StorageAccountNumber
                        );
                    }
                }

                if (!await dataRepository.DeleteForCleanup(dataElement, cancellationToken))
                {
                    _logger.LogError(
                        "CleanupController // CleanupDataelements // Data element not found for dataElement Id: {DataElementId}",
                        dataElement.Id
                    );
                }
                else
                {
                    successfullyDeleted++;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "CleanupController // CleanupDataelements // Error occured when deleting dataElement Id: {Id} Blobstoragepath: {Blobstoragepath}",
                    dataElement.Id,
                    dataElement.BlobStoragePath
                );
                stopwatch.Stop();
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    string.Format(
                        "CleanupController // CleanupDataelements // Error occured when deleting dataElement Id: {0} Blobstoragepath: {1}",
                        dataElement.Id,
                        dataElement.BlobStoragePath
                    )
                );
            }
        }

        List<BlobVersionReferencesInternal> orphanBlobVersions =
            await instanceRepository.GetOrphanBlobVersionsForCleanup(cancellationToken);

        int orphanBlobVersionsDeleted;
        try
        {
            orphanBlobVersionsDeleted = await CleanupOrphanBlobVersionsInternal(
                orphanBlobVersions,
                cancellationToken
            );
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "CleanupController // CleanupDataelements // Error occured when deleting orphan blob versions"
            );
            stopwatch.Stop();
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "CleanupController // CleanupDataelements // Error occured when deleting orphan blob versions"
            );
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "CleanupController // CleanupDataelements // {SuccessfullyDeleted} of {Count} data elements and {OrphanBlobVersionsDeleted} orphan blob versions deleted in {TotalSeconds} s",
            successfullyDeleted,
            dataElements.Count,
            orphanBlobVersionsDeleted,
            stopwatch.Elapsed.TotalSeconds
        );

        return Ok();
    }

    private async Task<bool> DeleteVersionedInstanceBlobPrefixesInternal(
        Guid instanceGuid,
        (string BlobStorageOrg, string AppId, int? StorageAccountNumber) currentContext,
        CancellationToken cancellationToken
    )
    {
        List<BlobVersionReferencesInternal> blobVersions =
            await instanceRepository.GetBlobVersionsForInstance(instanceGuid, cancellationToken);

        foreach (
            var context in blobVersions
                .Where(blobVersion => blobVersion.BlobVersionIds.Count > 0)
                .Select(blobVersion =>
                    (
                        blobVersion.BlobStorageOrg,
                        blobVersion.AppId,
                        blobVersion.StorageAccountNumber
                    )
                )
                .Distinct()
                .Where(context => context != currentContext)
        )
        {
            if (
                !await blobRepository.DeleteDataBlobs(
                    context.BlobStorageOrg,
                    context.AppId,
                    instanceGuid.ToString(),
                    context.StorageAccountNumber,
                    cancellationToken
                )
            )
            {
                _logger.LogError(
                    "CleanupController // CleanupInstancesInternal // Error deleting blobs for instance {InstanceGuid} in blob storage org {BlobStorageOrg} with app id {AppId}",
                    instanceGuid,
                    context.BlobStorageOrg,
                    context.AppId
                );
                return false;
            }
        }

        return true;
    }

    private async Task<int> CleanupOrphanBlobVersionsInternal(
        List<BlobVersionReferencesInternal> orphanBlobVersions,
        CancellationToken cancellationToken
    )
    {
        int successfullyDeleted = 0;
        foreach (BlobVersionReferencesInternal orphanBlobVersion in orphanBlobVersions)
        {
            List<string> versionedBlobStoragePaths =
            [
                .. orphanBlobVersion.BlobVersionIds.Select(versionId =>
                    BlobRepository.GetVersionedBlobPath(
                        orphanBlobVersion.AppId,
                        orphanBlobVersion.InstanceGuid.ToString(),
                        versionId
                    )
                ),
            ];

            bool[] blobsDeleted = await blobRepository.DeleteBlobsIfExists(
                orphanBlobVersion.BlobStorageOrg,
                versionedBlobStoragePaths,
                orphanBlobVersion.StorageAccountNumber,
                cancellationToken
            );

            List<string> deletedVersionIds =
            [
                .. orphanBlobVersion.BlobVersionIds.Where((_, index) => blobsDeleted[index]),
            ];

            if (deletedVersionIds.Count != orphanBlobVersion.BlobVersionIds.Count)
            {
                _logger.LogWarning(
                    "CleanupController // CleanupDataelements // One or more orphan blob deletes failed or had unknown outcome for instance {InstanceGuid}",
                    orphanBlobVersion.InstanceGuid
                );
            }

            if (deletedVersionIds.Count == 0)
            {
                continue;
            }

            successfullyDeleted += await dataRepository.DeleteOrphanBlobVersions(
                deletedVersionIds,
                cancellationToken
            );
        }

        return successfullyDeleted;
    }

    private async Task<int> CleanupInstancesInternal(
        List<InstanceInternal> instances,
        List<string> autoDeleteAppIds,
        CancellationToken cancellationToken
    )
    {
        int successfullyDeleted = 0;
        foreach (InstanceInternal instance in instances)
        {
            bool blobsNoException = false;
            bool instanceEventsNoException = false;
            bool dataElementsNoException = false;

            try
            {
                Guid instanceGuid = Guid.Parse(instance.Id);
                Application app = await applicationRepository.FindOne(instance.AppId, instance.Org);
                blobsNoException = await blobRepository.DeleteDataBlobs(
                    instance.Org,
                    instance.AppId,
                    instance.Id,
                    app.StorageAccountNumber,
                    CancellationToken.None
                );

                if (blobsNoException)
                {
                    blobsNoException = await DeleteVersionedInstanceBlobPrefixesInternal(
                        instanceGuid,
                        (instance.Org, instance.AppId, app.StorageAccountNumber),
                        cancellationToken
                    );
                }

                if (blobsNoException)
                {
                    dataElementsNoException = await dataRepository.DeleteForInstance(
                        instanceGuid.ToString(),
                        cancellationToken
                    );
                }

                try
                {
                    if (autoDeleteAppIds.Contains(instance.AppId))
                    {
                        await instanceEventRepository.DeleteAllInstanceEvents(instance.Id);
                        instanceEventsNoException = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "CleanupController // CleanupInstancesInternal // Error deleting instance events for id {id}",
                        instance.Id
                    );
                }

                if (
                    dataElementsNoException
                    && (!autoDeleteAppIds.Contains(instance.AppId) || instanceEventsNoException)
                )
                {
                    if (await instanceRepository.Delete(instanceGuid, cancellationToken))
                    {
                        successfullyDeleted += 1;
                    }
                    else
                    {
                        _logger.LogError(
                            "CleanupController // CleanupInstancesInternal // Instance not found for id {id}",
                            instance.Id
                        );
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "CleanupController // CleanupInstancesInternal // Error occured when deleting instance: {AppId}/{InstanceId}",
                    instance.AppId,
                    $"{instance.InstanceOwner.PartyId}/{instance.Id}"
                );
            }
        }

        return successfullyDeleted;
    }
}
