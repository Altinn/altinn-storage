using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Handles cleanup of storage data
    /// </summary>
    [Route("storage/api/v1/cleanup")]
    [ApiController]
    public class CleanupController : ControllerBase
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly IBlobRepository _blobRepository;
        private readonly IDataRepository _dataRepository;
        private readonly IInstanceEventRepository _instanceEventRepository;
        private readonly ILogger<CleanupController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CleanupController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="blobRepository">the blob repository handler</param>
        /// <param name="dataRepository">the data repository handler</param>
        /// <param name="instanceEventRepository">the instance event repository handler</param>
        /// <param name="logger">the logger</param>
        public CleanupController(
            IInstanceRepository instanceRepository,
            IApplicationRepository applicationRepository,
            IBlobRepository blobRepository,
            IDataRepository dataRepository,
            IInstanceEventRepository instanceEventRepository,
            ILogger<CleanupController> logger)
        {
            _instanceRepository = instanceRepository;
            _applicationRepository = applicationRepository;
            _blobRepository = blobRepository;
            _dataRepository = dataRepository;
            _instanceEventRepository = instanceEventRepository;
            _logger = logger;
        }

        /// <summary>
        /// Invoke periodic cleanup of instances
        /// </summary>
        /// <returns>?</returns>
        [HttpDelete("cleanupinstances")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Produces("application/json")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<ActionResult> CleanupInstances()
        {
            List<Instance> instances = await _instanceRepository.GetHardDeletedInstances();
            List<string> autoDeleteAppIds = (await _applicationRepository.FindAll())
                .Where(a => instances.Select(i => i.AppId).ToList().Contains(a.Id) && a.AutoDeleteOnProcessEnd == true)
                .Select(a => a.Id).ToList();
            int successfullyDeleted = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();

            foreach (Instance instance in instances)
            {
                bool blobsNoException = false;
                bool instanceEventsNoException = false;
                bool dataElementsNoException = false;

                try
                {
                    blobsNoException = await _blobRepository.DeleteDataBlobs(instance);

                    if (blobsNoException)
                    {
                        dataElementsNoException = await _dataRepository.DeleteForInstance(instance.Id);
                    }

                    try
                    {
                        if (autoDeleteAppIds.Contains(instance.AppId))
                        {
                            await _instanceEventRepository.DeleteAllInstanceEvents(instance.Id);
                            instanceEventsNoException = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"NightlyCleanup error deleting instance events for id {instance.Id}, {ex.Message}");
                    }

                    if (dataElementsNoException
                        && (!autoDeleteAppIds.Contains(instance.AppId) || instanceEventsNoException))
                    {
                        await _instanceRepository.Delete(instance);
                        successfullyDeleted += 1;
                        _logger.LogInformation(
                            "NightlyCleanup // Run // Instance deleted: {AppId}/{InstanceId}",
                            instance.AppId,
                            $"{instance.InstanceOwner.PartyId}/{instance.Id}");
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(
                        e,
                        "NightlyCleanup // Run // Error occured when deleting instance: {AppId}/{InstanceId}",
                        instance.AppId,
                        $"{instance.InstanceOwner.PartyId}/{instance.Id}");
                }
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "NightlyCleanup // Run // {DeleteCount} of {OriginalCount} instances deleted in {Duration} s",
                successfullyDeleted,
                instances.Count,
                stopwatch.Elapsed.TotalSeconds);

            return Ok();
        }

        /// <summary>
        /// Invoke periodic cleanup of data elements
        /// </summary>
        /// <returns>?</returns>
        [HttpDelete("cleanupdataelements")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Produces("application/json")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<ActionResult> CleanupDataelements()
        {
            List<DataElement> dataElements = await _instanceRepository.GetHardDeletedDataElements();

            int successfullyDeleted = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();

            foreach (DataElement dataElement in dataElements)
            {
                bool dataBlobDeleted = false;

                try
                {
                    dataBlobDeleted = await _blobRepository.DeleteBlob(dataElement.BlobStoragePath.Split('/').First(), dataElement.BlobStoragePath);
                    if (dataBlobDeleted && await _dataRepository.Delete(dataElement))
                    {
                        successfullyDeleted++;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"NightlyCleanupDataElements // Run // Error occured when deleting dataElement Id: {dataElement.Id} Blobstoragepath: {dataElement.BlobStoragePath} \r Exception: {e.Message}");
                }
            }

            stopwatch.Stop();
            _logger.LogInformation(
                $"NightlyCleanupDataElements // Run // {successfullyDeleted} of {dataElements.Count} data elements deleted in {stopwatch.Elapsed.TotalSeconds} s");

            return Ok();
        }
    }
}
