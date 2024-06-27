using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Common.PEP.Helpers;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;

using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints for the Altinn II migration
    /// </summary>
    [Route("storage/api/v1/migration")]
    [ApiController]
    public class MigrationController : ControllerBase
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IInstanceEventRepository _instanceEventRepository;
        private readonly IDataRepository _dataRepository;
        private readonly IBlobRepository _blobRepository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly ITextRepository _textRepository;
        private readonly IPartiesWithInstancesClient _partiesWithInstancesClient;
        private readonly ILogger _logger;
        private readonly IAuthorization _authorizationService;
        private readonly IInstanceEventService _instanceEventService;
        private readonly string _storageBaseAndHost;
        private readonly GeneralSettings _generalSettings;
        private readonly AzureStorageConfiguration _azureStorageSettings;
        private readonly IRegisterService _registerService;

        /// <summary>
        /// Initializes a new instance of the <see cref="MigrationController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="instanceEventRepository">the instance events repository handler</param>
        /// <param name="dataRepository">the data element repository handler</param>
        /// <param name="blobRepository">the blob repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="textRepository">the text repository handler</param>
        /// <param name="partiesWithInstancesClient">An implementation of <see cref="IPartiesWithInstancesClient"/> that can be used to send information to SBL.</param>
        /// <param name="logger">the logger</param>
        /// <param name="authorizationService">the authorization service.</param>
        /// <param name="instanceEventService">the instance event service.</param>
        /// <param name="registerService">the instance register service.</param>
        /// <param name="settings">the general settings.</param>
        /// <param name="azureStorageSettings">the azureStorage settings.</param>
        public MigrationController(
            IInstanceRepository instanceRepository,
            IInstanceEventRepository instanceEventRepository,
            IDataRepository dataRepository,
            IBlobRepository blobRepository,
            IApplicationRepository applicationRepository,
            ITextRepository textRepository,
            IPartiesWithInstancesClient partiesWithInstancesClient,
            ILogger<MigrationController> logger,
            IAuthorization authorizationService,
            IInstanceEventService instanceEventService,
            IRegisterService registerService,
            IOptions<GeneralSettings> settings,
            IOptions<AzureStorageConfiguration> azureStorageSettings)
        {
            _instanceRepository = instanceRepository;
            _instanceEventRepository = instanceEventRepository;
            _dataRepository = dataRepository;
            _blobRepository = blobRepository;
            _applicationRepository = applicationRepository;
            _textRepository = textRepository;
            _partiesWithInstancesClient = partiesWithInstancesClient;
            _logger = logger;
            _storageBaseAndHost = $"{settings.Value.Hostname}/storage/api/v1/";
            _authorizationService = authorizationService;
            _instanceEventService = instanceEventService;
            _registerService = registerService;
            _generalSettings = settings.Value;
            _azureStorageSettings = azureStorageSettings.Value;
        }

        /// <summary>
        /// Gets all instances in a given state for a given instance owner.
        /// </summary>
        /// <param name="p1">the instance owner id</param>
        /// <param name="p2">the instance guid</param>
        /// <param name="language"> language id en, nb, nn-NO"</param>
        /// <returns>list of instances</returns>
        [AllowAnonymous]
        [HttpGet]
        public async Task<ActionResult> Test()
        {
            return Ok($"Hello world");
        }

        /// <summary>
        /// Inserts new instance into the instance collection.
        /// </summary>
        /// <param name="instance">The instance details to store.</param>
        /// <returns>The stored instance.</returns>
        [AllowAnonymous]
        [HttpPost("instance")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult<Instance>> CreateInstance([FromBody] Instance instance)
        {
            // Open: what about createdby and lastchangedby?

            // TODO: Check from state table and delete all related stuff if existing

            Instance storedInstance = null;
            try
            {
                storedInstance = await _instanceRepository.Create(instance);

                return Created((string)null, storedInstance);
            }
            catch (Exception storageException)
            {
                _logger.LogError(storageException, "Unable to create migrated altinn ii instance");
                return StatusCode(500, $"Unable to create migrated instance due to {storageException.Message}");
            }
        }

        /// <summary>
        /// Inserts new data element
        /// </summary>
        /// <param name="instanceGuid">The instanceGuid.</param>
        /// <param name="timestampTicks">Element timestamp ticks</param>
        /// <param name="dataType">Element data type</param>
        /// <returns>The stored data element.</returns>
        [AllowAnonymous]
        [HttpPost("dataelement/{instanceGuid:guid}")]
        [DisableFormValueModelBinding]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult<DataElement>> CreateDataElement(
            [FromRoute]Guid instanceGuid,
            [FromQuery(Name = "timestampticks")]long timestampTicks,
            [FromQuery(Name = "datatype")]string dataType)
        {
            DateTime timestamp = new DateTime(timestampTicks, DateTimeKind.Utc).ToLocalTime();

            // Open: what about createdby and lastchangedby?

            // TODO: Check from state table and delete all related stuff if existing

            (Instance instance, long instanceId) = await _instanceRepository.GetOne(instanceGuid, false);
            if (instanceId == 0)
            {
                return BadRequest("Instance not found");
            }

            DataElement storedDataElement = null;
            try
            {
                string dataElementId;
                DataElement dataElement = new()
                {
                    Id = dataElementId = Guid.NewGuid().ToString(),
                    Created = timestamp,
                    CreatedBy = instance.CreatedBy,
                    DataType = dataType,
                    InstanceGuid = instanceGuid.ToString(),
                    IsRead = true,
                    LastChanged = timestamp,
                    LastChangedBy = instance.LastChangedBy, // TODO: Find out what to populate here
                    BlobStoragePath = DataElementHelper.DataFileName(instance.AppId, instanceGuid.ToString(), dataElementId)
                };

                (Stream theStream, dataElement.ContentType, dataElement.Filename, _) = await DataElementHelper.GetStream(Request, FormOptions.DefaultMultipartBoundaryLengthLimit);

                // TODO: Remove hard coding of ttd below
                if (Request.ContentLength > 0)
                {
                    (dataElement.Size, _) = await _blobRepository.WriteBlob("ttd" /* instance.Org */, theStream, dataElement.BlobStoragePath);
                }
                else
                {
                    dataElement.BlobStoragePath = "on-demand";
                }

                storedDataElement = await _dataRepository.Create(dataElement, instanceId);

                return Created((string)null, storedDataElement);
            }
            catch (Exception storageException)
            {
                _logger.LogError(storageException, "Unable to create migrated altinn ii data element");
                return StatusCode(500, $"Unable to create migrated data element due to {storageException.Message}");
            }
        }

        /// <summary>
        /// Inserts instance events
        /// </summary>
        /// <param name="instanceEvents">The instance events to store</param>
        /// <returns>Created</returns>
        [AllowAnonymous]
        [HttpPost("instanceevents")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult<List<InstanceEvent>>> CreateInstanceEvents([FromBody] List<InstanceEvent> instanceEvents)
        {
            try
            {
                foreach (var instanceEvent in instanceEvents)
                {
                    await _instanceEventRepository.InsertInstanceEvent(instanceEvent);
                }

                return Created();
            }
            catch (Exception storageException)
            {
                _logger.LogError(storageException, "Unable to create migrated altinn ii instance events");
                return StatusCode(500, $"Unable to create migrated instance events due to {storageException.Message}");
            }
        }

        /// <summary>
        /// Inserts new application
        /// </summary>
        /// <param name="application">The application to store.</param>
        /// <returns>The stored application.</returns>
        [AllowAnonymous]
        [HttpPost("application")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult<Instance>> CreateApplication([FromBody] Application application)
        {
            Application storedApplication = null;
            try
            {
                storedApplication = await _applicationRepository.Create(application);
                foreach (var kvp in application.Title.Where(k => new List<string> { "nb", "nn", "en" }.Contains(k.Key)))
                {
                    await _textRepository.Create(application.Org, application.Id.Split('/')[1], new TextResource()
                    {
                        Id = application.Id,
                        Language = kvp.Key,
                        Org = application.Org,
                        Resources = new() { new TextResourceElement() { Id = "ServiceName", Value = kvp.Value } }
                    });
                }

                return Created((string)null, storedApplication);
            }
            catch (Exception storageException)
            {
                _logger.LogError(storageException, "Unable to create migrated altinn ii app");
                return StatusCode(500, $"Unable to create migrated app due to {storageException.Message}");
            }
        }

        /// <summary>
        /// Upload policy.xml
        /// </summary>
        /// <param name="org">Org</param>
        /// <param name="app">App</param>
        /// <returns>Ok</returns>
        [AllowAnonymous]
        [HttpPost("policy/{org}/{app}")]
        [DisableFormValueModelBinding]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult> CreatePolicy([FromRoute] string org, [FromRoute] string app)
        {
            StorageSharedKeyCredential metadataCredentials = new StorageSharedKeyCredential(_azureStorageSettings.AccountName, _azureStorageSettings.AccountKey);
            BlobServiceClient metadataServiceClient = new BlobServiceClient(new Uri(_azureStorageSettings.BlobEndPoint), metadataCredentials);
            var metadataContainerClient = metadataServiceClient.GetBlobContainerClient("metadata");
            BlobClient blobClient =
                metadataContainerClient.GetBlobClient($"{org}/{app}/policy.xml");
            await blobClient.UploadAsync(Request.Body, true);
            return Ok();
        }
    }
}
