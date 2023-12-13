using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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
        private readonly bool _usePostgreSQL;

        /// <summary>
        /// Initializes a new instance of the <see cref="CleanupController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="blobRepository">the blob repository handler</param>
        /// <param name="dataRepository">the data repository handler</param>
        /// <param name="instanceEventRepository">the instance event repository handler</param>
        /// <param name="logger">the logger</param>
        /// <param name="generalSettings">the general settings.</param>
        public CleanupController(
            IInstanceRepository instanceRepository,
            IApplicationRepository applicationRepository,
            IBlobRepository blobRepository,
            IDataRepository dataRepository,
            IInstanceEventRepository instanceEventRepository,
            ILogger<CleanupController> logger,
            IOptions<GeneralSettings> generalSettings)
        {
            _instanceRepository = instanceRepository;
            _applicationRepository = applicationRepository;
            _blobRepository = blobRepository;
            _dataRepository = dataRepository;
            _instanceEventRepository = instanceEventRepository;
            _logger = logger;
            _usePostgreSQL = generalSettings.Value.UsePostgreSQL;
        }

        /// <summary>
        /// Invoke periodic cleanup of instances
        /// </summary>
        /// <returns>?</returns>
        [HttpDelete("cleanupinstances")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<ActionResult> CleanupInstances()
        {
            _logger.LogError("CleanupController init");
            try
            {
                if (!_usePostgreSQL)
                {
                    return Ok();
                }

                List<Instance> instances = await _instanceRepository.GetHardDeletedInstances();
                List<string> autoDeleteAppIds = (await _applicationRepository.FindAll())
                    .Where(a => instances.Select(i => i.AppId).ToList().Contains(a.Id) && a.AutoDeleteOnProcessEnd)
                    .Select(a => a.Id).ToList();

                Stopwatch stopwatch = Stopwatch.StartNew();
                int successfullyDeleted = await CleanupInstancesInternal(instances, autoDeleteAppIds);
                stopwatch.Stop();

                _logger.LogError(
                    "CleanupController// CleanupInstances // {DeleteCount} of {OriginalCount} instances deleted in {Duration} s",
                    successfullyDeleted,
                    instances.Count,
                    stopwatch.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CleanupController error");
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
        public async Task<ActionResult> CleanupInstancesForApp(string appId)
        {
            if (!_usePostgreSQL)
            {
                return Ok();
            }

            int successfullyDeleted = 0;
            int processed = 0;
            InstanceQueryResponse instancesResponse = new() { ContinuationToken = null };
            Dictionary<string, StringValues> options = new() { { "appId", appId } };

            Stopwatch stopwatch = Stopwatch.StartNew();
            do
            {
                instancesResponse = await _instanceRepository.GetInstancesFromQuery(options, instancesResponse.ContinuationToken, 5000);
                successfullyDeleted += await CleanupInstancesInternal(instancesResponse.Instances, new List<string>());
                processed += (int)instancesResponse.Count;
            }
            while (instancesResponse.ContinuationToken != null);
            stopwatch.Stop();

            _logger.LogError(
                "CleanupController // CleanupInstancesForApp // {DeleteCount} of {OriginalCount} instances deleted in {Duration} s",
                successfullyDeleted,
                processed,
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
            if (!_usePostgreSQL)
            {
                return Ok();
            }

            List<DataElement> dataElements = await _instanceRepository.GetHardDeletedDataElements();

            int successfullyDeleted = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();

            foreach (DataElement dataElement in dataElements)
            {
                bool dataBlobDeleted = false;

                try
                {
                    dataBlobDeleted = await _blobRepository.DeleteBlob(dataElement.BlobStoragePath.Split('/')[0], dataElement.BlobStoragePath);
                    if (dataBlobDeleted && await _dataRepository.Delete(dataElement))
                    {
                        successfullyDeleted++;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(
                        e,
                        "CleanupController // CleanupDataelements // Error occured when deleting dataElement Id: {dataElement.Id} Blobstoragepath: {blobStoragePath}",
                        dataElement.Id,
                        dataElement.BlobStoragePath);
                }
            }

            stopwatch.Stop();
            _logger.LogError(
                "CleanupController // CleanupDataelements // {successfullyDeleted} of {count} data elements deleted in {totalSeconds} s",
                successfullyDeleted,
                dataElements.Count,
                stopwatch.Elapsed.TotalSeconds);

            return Ok();
        }

        private async Task<int> CleanupInstancesInternal(List<Instance> instances, List<string> autoDeleteAppIds)
        {
            int successfullyDeleted = 0;
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
                        dataElementsNoException = await _dataRepository.DeleteForInstance(instance.Id.Split('/')[^1]);
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
                        _logger.LogError(
                            "CleanupController // CleanupInstancesInternal // Error deleting instance events for id {id}, {message}",
                            instance.Id,
                            ex.Message);
                    }

                    if (dataElementsNoException
                        && (!autoDeleteAppIds.Contains(instance.AppId) || instanceEventsNoException))
                    {
                        await _instanceRepository.Delete(instance);
                        successfullyDeleted += 1;
                        _logger.LogError(
                            "CleanupController // CleanupInstancesInternal // Instance deleted: {AppId}/{InstanceId}",
                            instance.AppId,
                            $"{instance.InstanceOwner.PartyId}/{instance.Id}");
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(
                        e,
                        "CleanupController // CleanupInstancesInternal // Error occured when deleting instance: {AppId}/{InstanceId}",
                        instance.AppId,
                        $"{instance.InstanceOwner.PartyId}/{instance.Id}");
                }
            }

            return successfullyDeleted;
        }
    }
}
