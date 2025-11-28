using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

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
/// <param name="logger">the logger</param>
[Route("storage/api/v1/cleanup")]
[ApiController]
public class CleanupController(
    IInstanceRepository instanceRepository,
    IApplicationRepository applicationRepository,
    IBlobRepository blobRepository,
    IDataRepository dataRepository,
    IInstanceEventRepository instanceEventRepository,
    ILogger<CleanupController> logger
) : ControllerBase
{
    private readonly ILogger<CleanupController> _logger = logger;

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
            List<Instance> instances = await instanceRepository.GetHardDeletedInstances(
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
    /// Invoke periodic cleanup of instances for a specific app
    /// </summary>
    /// <returns>?</returns>
    [HttpDelete("cleanupinstancesforapp/{appId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult> CleanupInstancesForApp(
        string appId,
        CancellationToken cancellationToken
    )
    {
        int successfullyDeleted = 0;
        int processed = 0;
        InstanceQueryResponse instancesResponse = new() { ContinuationToken = null };

        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            InstanceQueryParameters queryParameters = new()
            {
                Size = 5000,
                AppId = appId,
                ContinuationToken = instancesResponse.ContinuationToken,
            };

            instancesResponse = await instanceRepository.GetInstancesFromQuery(
                queryParameters,
                true,
                cancellationToken
            );
            successfullyDeleted += await CleanupInstancesInternal(
                instancesResponse.Instances,
                [],
                cancellationToken
            );
            processed += (int)instancesResponse.Count;
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
        List<DataElement> dataElements = await instanceRepository.GetHardDeletedDataElements(
            cancellationToken
        );

        int successfullyDeleted = 0;

        Stopwatch stopwatch = Stopwatch.StartNew();

        Application app = null;
        Instance instance = null;
        foreach (DataElement dataElement in dataElements.OrderBy(d => d.InstanceGuid))
        {
            try
            {
                if (instance == null || instance.Id.Split('/')[1] != dataElement.InstanceGuid)
                {
                    (instance, _) = await instanceRepository.GetOne(
                        new Guid(dataElement.InstanceGuid),
                        false,
                        cancellationToken
                    );
                    app = await applicationRepository.FindOne(
                        instance.AppId,
                        instance.Org,
                        cancellationToken
                    );
                }

                if (
                    !await blobRepository.DeleteBlob(
                        dataElement.BlobStoragePath.Split('/')[0],
                        dataElement.BlobStoragePath,
                        app.StorageAccountNumber
                    )
                )
                {
                    _logger.LogError(
                        "CleanupController // CleanupDataelements // Blob not found for dataElement Id: {dataElement.Id} Blobstoragepath: {blobStoragePath}",
                        dataElement.Id,
                        dataElement.BlobStoragePath
                    );
                }

                if (!await dataRepository.Delete(dataElement, cancellationToken))
                {
                    _logger.LogError(
                        "CleanupController // CleanupDataelements // Data element not found for dataElement Id: {dataElement.Id}",
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

        stopwatch.Stop();
        _logger.LogInformation(
            "CleanupController // CleanupDataelements // {successfullyDeleted} of {count} data elements deleted in {totalSeconds} s",
            successfullyDeleted,
            dataElements.Count,
            stopwatch.Elapsed.TotalSeconds
        );

        return Ok();
    }

    private async Task<int> CleanupInstancesInternal(
        List<Instance> instances,
        List<string> autoDeleteAppIds,
        CancellationToken cancellationToken
    )
    {
        int successfullyDeleted = 0;
        foreach (Instance instance in instances)
        {
            bool blobsNoException = false;
            bool instanceEventsNoException = false;
            bool dataElementsNoException = false;

            try
            {
                Application app = await applicationRepository.FindOne(instance.AppId, instance.Org);
                blobsNoException = await blobRepository.DeleteDataBlobs(
                    instance,
                    app.StorageAccountNumber
                );

                if (blobsNoException)
                {
                    dataElementsNoException = await dataRepository.DeleteForInstance(
                        instance.Id.Split('/')[^1]
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
                    if (await instanceRepository.Delete(instance, cancellationToken))
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
