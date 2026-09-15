#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
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
/// <param name="instanceEventService">the instance event service</param>
/// <param name="dataService">the data service</param>
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
    IInstanceEventService instanceEventService,
    IDataService dataService,
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
                true,
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
        CancellationToken cancellationToken,
        [FromQuery] bool deleteBlobs = true
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
                deleteBlobs,
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
                if (instance == null || instance.Id != dataElement.InstanceGuid)
                {
                    instance = await instanceRepository.GetOne(
                        dataElement.InstanceGuid,
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
                                DataElementHelper.GetVersionedBlobPath(
                                    blobVersion.AppId,
                                    blobVersion.InstanceGuid,
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
                            blobVersion.InstanceGuid,
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
            var (blobStorageOrg, appId, storageAccountNumber) in blobVersions
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
                    blobStorageOrg,
                    appId,
                    instanceGuid,
                    storageAccountNumber,
                    cancellationToken
                )
            )
            {
                _logger.LogError(
                    "CleanupController // CleanupInstancesInternal // Error deleting blobs for instance {InstanceGuid} in blob storage org {BlobStorageOrg} with app id {AppId}",
                    instanceGuid,
                    blobStorageOrg,
                    appId
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
                    DataElementHelper.GetVersionedBlobPath(
                        orphanBlobVersion.AppId,
                        orphanBlobVersion.InstanceGuid,
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

    /// <summary>
    /// Deletes a single data element, its blobs and records a delete event.
    /// </summary>
    /// <remarks>
    /// Intended for operational use from inside the cluster and guarded by a shared secret rather
    /// than an Altinn token. The deletion is immediate and cannot be undone. No version
    /// preconditions are applied, so the delete is not rejected by a concurrent instance update.
    /// </remarks>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element belongs to.</param>
    /// <param name="dataGuid">The id of the data element to delete.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The metadata of the deleted data element.</returns>
    [HttpDelete("dataelement/{instanceOwnerPartyId:int}/{instanceGuid:guid}/{dataGuid:guid}")]
    [ServiceFilter(typeof(CleanupApiKeyFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<DataElement>> CleanupDataElement(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken
    )
    {
        InstanceInternal instance = await instanceRepository.GetOne(
            instanceGuid,
            false,
            cancellationToken
        );
        if (instance is null || instance.InstanceOwner.PartyId != instanceOwnerPartyId.ToString())
        {
            return NotFound(
                $"Unable to find any instance with id: {instanceOwnerPartyId}/{instanceGuid}."
            );
        }

        DataElementInternal dataElement = await dataRepository.Read(
            instanceGuid,
            dataGuid,
            cancellationToken
        );
        if (dataElement is null)
        {
            return NotFound($"Unable to find any data element with id: {dataGuid}.");
        }

        // Data elements are read by their own id alone, so the element has to be checked against
        // the instance in the route before anything is deleted on that instance's behalf.
        if (dataElement.InstanceGuid != instanceGuid)
        {
            return NotFound(
                $"Data element {dataGuid} does not belong to instance {instanceOwnerPartyId}/{instanceGuid}."
            );
        }

        Application application = await applicationRepository.FindOne(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        if (application is null)
        {
            return NotFound($"Cannot find application {instance.AppId} in storage");
        }

        DateTime deletedTime = DateTime.UtcNow;
        PlatformUser user = new() { OrgId = instance.Org, AuthenticationLevel = 0 };

        InstanceEvent deletedEvent = instanceEventService.BuildInstanceEvent(
            InstanceEventType.Deleted,
            instance,
            dataElement,
            user,
            "Deleted manually through CleanupController // CleanupDataElement"
        );

        InstanceMutationCommit mutation = new(
            [],
            [],
            [new InstanceMutationDataElementDelete(dataElement, IgnoreLock: true)],
            instance,
            [],
            ExpectedInstanceVersion: null,
            ExpectedProcessStateVersion: null,
            InstanceEvents: [deletedEvent],
            LastChanged: deletedTime
        );

        await instanceMutationRepository.Apply(
            instanceGuid,
            instance.InternalId,
            mutation,
            cancellationToken
        );

        await dataService.CleanupDeletedDataElementBlobs(
            instance,
            dataElement,
            application.StorageAccountNumber,
            CancellationToken.None
        );

        _logger.LogInformation(
            "CleanupController // CleanupDataElement // Deleted data element {DataElementId} ({BlobStoragePath}) on instance {InstanceId} for caller {ClientIp}",
            dataElement.Id,
            dataElement.BlobStoragePath,
            instance.Id,
            HttpContext.Connection.RemoteIpAddress
        );

        return Ok(dataElement.ToApiModel());
    }

    private async Task<int> CleanupInstancesInternal(
        List<InstanceInternal> instances,
        List<string> autoDeleteAppIds,
        bool deleteBlobs,
        CancellationToken cancellationToken
    )
    {
        int successfullyDeleted = 0;
        foreach (InstanceInternal instance in instances)
        {
            bool blobsNoException = true;
            bool instanceEventsNoException = false;
            bool dataElementsNoException = false;

            try
            {
                Application app = await applicationRepository.FindOne(instance.AppId, instance.Org);
                if (deleteBlobs)
                {
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
                            instance.Id,
                            (instance.Org, instance.AppId, app.StorageAccountNumber),
                            cancellationToken
                        );
                    }
                }

                if (blobsNoException)
                {
                    dataElementsNoException = await dataRepository.DeleteForInstance(
                        instance.Id,
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
                    if (await instanceRepository.Delete(instance.Id, cancellationToken))
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
