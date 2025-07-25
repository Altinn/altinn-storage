using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Filters;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

using Azure.Storage;
using Azure.Storage.Blobs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using static Altinn.Platform.Storage.Clients.PdfGeneratorClient;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints for the Altinn II migration
    /// </summary>
    [Route("storage/api/v1/migration")]
    [ApiController]
    [ServiceFilter(typeof(ClientIpCheckActionFilterAttribute))]
    [ExcludeFromCodeCoverage]
    public class MigrationController : ControllerBase
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IInstanceEventRepository _instanceEventRepository;
        private readonly IDataRepository _dataRepository;
        private readonly IBlobRepository _blobRepository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly IA2Repository _a2Repository;
        private readonly ITextRepository _textRepository;
        private readonly ILogger _logger;
        private readonly GeneralSettings _generalSettings;
        private readonly AzureStorageConfiguration _azureStorageSettings;
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="MigrationController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="instanceEventRepository">the instance events repository handler</param>
        /// <param name="dataRepository">the data element repository handler</param>
        /// <param name="blobRepository">the blob repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="a2repository">the a2repository handler</param>
        /// <param name="textRepository">the text repository handler</param>
        /// <param name="logger">the logger</param>
        /// <param name="settings">the general settings.</param>
        /// <param name="azureStorageSettings">the azureStorage settings.</param>
        /// <param name="generalSettings">Settings with endpoint uri</param>
        /// <param name="httpClient">The HttpClient to use in communication with the PDF generator service.</param>
        /// <param name="memoryCache">the memory cache</param>
        public MigrationController(
            IInstanceRepository instanceRepository,
            IInstanceEventRepository instanceEventRepository,
            IDataRepository dataRepository,
            IBlobRepository blobRepository,
            IApplicationRepository applicationRepository,
            IA2Repository a2repository,
            ITextRepository textRepository,
            ILogger<MigrationController> logger,
            IOptions<GeneralSettings> settings,
            IOptions<AzureStorageConfiguration> azureStorageSettings,
            IOptions<GeneralSettings> generalSettings,
            HttpClient httpClient,
            IMemoryCache memoryCache)
        {
            _instanceRepository = instanceRepository;
            _instanceEventRepository = instanceEventRepository;
            _dataRepository = dataRepository;
            _blobRepository = blobRepository;
            _applicationRepository = applicationRepository;
            _a2Repository = a2repository;
            _textRepository = textRepository;
            _logger = logger;
            _generalSettings = settings.Value;
            _azureStorageSettings = azureStorageSettings.Value;
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(generalSettings.Value.PdfGeneratorEndpoint);
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// Inserts new instance into the instance collection.
        /// </summary>
        /// <param name="instance">The instance details to store.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>The stored instance.</returns>
        [AllowAnonymous]
        [HttpPost("instance")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult<Instance>> CreateInstance([FromBody] Instance instance, CancellationToken cancellationToken)
        {
            Instance storedInstance;
            try
            {
                int a1ArchiveReference = instance.DataValues.ContainsKey("A1ArchRef") ? int.Parse(instance.DataValues["A1ArchRef"]) : -1;
                int a2ArchiveReference = instance.DataValues.ContainsKey("A2ArchRef") ? int.Parse(instance.DataValues["A2ArchRef"]) : -1;
                bool isA1 = a1ArchiveReference > -1;
                if (!isA1 && a2ArchiveReference == -1)
                {
                    throw new Exception($"Internal error - no archive reference found for {instance.Id}");
                }

                string instanceId = isA1 ? await _a2Repository.GetA1MigrationInstanceId(a1ArchiveReference) : await _a2Repository.GetA2MigrationInstanceId(a2ArchiveReference);
                if (instanceId != null)
                {
                    await CleanupOldMigrationInternal(instanceId, cancellationToken);
                }

                if (isA1)
                {
                    await _a2Repository.CreateA1MigrationState(a1ArchiveReference);
                }
                else
                {
                    await _a2Repository.CreateA2MigrationState(a2ArchiveReference);
                }

                storedInstance = await _instanceRepository.Create(instance, cancellationToken, isA1 ? 1 : 2);

                if (isA1)
                {
                    await _a2Repository.UpdateStartA1MigrationState(a1ArchiveReference, storedInstance.Id.Split('/')[^1]);
                }
                else
                {
                    await _a2Repository.UpdateStartA2MigrationState(a2ArchiveReference, storedInstance.Id.Split('/')[^1]);
                }

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
        /// <param name="createdTicks">Element created timestamp ticks</param>
        /// <param name="changedTicks">Element last changed timestamp ticks</param>
        /// <param name="dataType">Element data type</param>
        /// <param name="formid">A2 form id</param>
        /// <param name="lformid">A2 logical form id</param>
        /// <param name="presenationText">A2 presentation text</param>
        /// <param name="visiblePages">Semicolon separated list of visible pages</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>The stored data element.</returns>
        [AllowAnonymous]
        [HttpPost("dataelement/{instanceGuid:guid}")]
        [DisableFormValueModelBinding]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        [DisableRequestSizeLimit]
        public async Task<ActionResult<DataElement>> CreateDataElement(
            [FromRoute]Guid instanceGuid,
            [FromQuery(Name = "createdticks")]long createdTicks,
            [FromQuery(Name = "changedticks")]long changedTicks,
            [FromQuery(Name = "datatype")]string dataType,
            [FromQuery(Name = "formid")]string formid,
            [FromQuery(Name = "lformid")]string lformid,
            [FromQuery(Name = "prestext")]string presenationText,
            [FromQuery(Name = "vispages")]string visiblePages,
            CancellationToken cancellationToken)
        {
            DateTime created = new DateTime(createdTicks, DateTimeKind.Utc).ToLocalTime();
            DateTime lastChanged = new DateTime(changedTicks, DateTimeKind.Utc).ToLocalTime();
            (Instance instance, long instanceId) = await _instanceRepository.GetOne(instanceGuid, false, cancellationToken);
            if (instanceId == 0)
            {
                return BadRequest("Instance not found");
            }

            Application app = await _applicationRepository.FindOne(instance.AppId, instance.Org);
            bool isA2 = app.Id.Contains("/a2-");
            if (!isA2 && !app.Id.Contains("/a1-"))
            {
                throw new Exception($"Internal error. Can't determine app type for {app.Id}");
            }

            DataElement storedDataElement;
            try
            {
                string dataElementId = Guid.NewGuid().ToString();
                DataElement dataElement = new()
                {
                    Id = dataElementId,
                    Created = created,
                    CreatedBy = instance.CreatedBy,
                    DataType = dataType,
                    InstanceGuid = instanceGuid.ToString(),
                    IsRead = true,
                    LastChanged = lastChanged,
                    LastChangedBy = instance.LastChangedBy,
                    BlobStoragePath = DataElementHelper.DataFileName(instance.AppId, instanceGuid.ToString(), dataElementId),
                    Metadata = formid == null ? null : new()
                    {
                        new() { Key = isA2 ? "formid" : "dataformid", Value = formid },
                        lformid != null ? new() { Key = "lformid", Value = lformid } : null
                    }
                };

                if (presenationText != null)
                {
                    dataElement.Metadata.Add(new() { Key = isA2 ? "A2PresVal" : "A1PresVal", Value = HttpUtility.UrlDecode(presenationText) });
                }

                if (visiblePages != null)
                {
                    dataElement.Metadata.Add(new() { Key = "A2VisiblePages", Value = visiblePages });
                }

                (Stream theStream, dataElement.ContentType, dataElement.Filename, _) = await DataElementHelper.GetStream(Request, FormOptions.DefaultMultipartBoundaryLengthLimit);

                if (Request.ContentLength > 0 || dataElement.DataType == "binary-data")
                {
                    (dataElement.Size, _) = await _blobRepository.WriteBlob(
                        $"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : instance.Org)}",
                        theStream,
                        dataElement.BlobStoragePath,
                        app.StorageAccountNumber);
                }
                else
                {
                    dataElement.BlobStoragePath = dataElement.DataType switch
                    {
                        "signature-presentation" => "ondemand/signature",
                        "ref-data-as-pdf" => "ondemand/formdatapdf",
                        "ref-data-as-html" => "ondemand/formdatahtml",
                        "ref-summary-data-as-html" => "ondemand/formsummaryhtml",
                        "payment-presentation" => "ondemand/payment",
                        _ => throw new ArgumentException(dataElement.DataType),
                    };
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
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult<List<InstanceEvent>>> CreateInstanceEvents([FromBody] List<InstanceEvent> instanceEvents)
        {
            try
            {
                foreach (var instanceEvent in instanceEvents.Where(ie => !string.IsNullOrEmpty(ie.EventType)))
                {
                    await _instanceEventRepository.InsertInstanceEvent(instanceEvent);
                }

                await _a2Repository.UpdateCompleteMigrationState(instanceEvents[0].InstanceId);
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
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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
                        Resources = [new TextResourceElement() { Id = "ServiceName", Value = kvp.Value }]
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
        /// Inserts new text
        /// </summary>
        /// <param name="org">The app owner org</param>
        /// <param name="app">The application that owns the text resource</param>
        /// <param name="language">Language</param>
        /// <param name="key">Text key</param>
        /// <returns>The stored application.</returns>
        [AllowAnonymous]
        [HttpPost("text/{org}/{app}/{language}/{key}")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult<Instance>> CreateText([FromRoute] string org, [FromRoute] string app, [FromRoute] string language, [FromRoute] string key)
        {
            try
            {
                string cacheKey = $"tid:{org}-{app}-{language}";
                if (_memoryCache.Get(cacheKey) != null)
                {
                    _memoryCache.Remove(cacheKey);
                }

                TextResource textResource = await _textRepository.Get(org, app, language);
                using var reader = new StreamReader(Request.Body);
                if (textResource == null)
                {
                    textResource = await _textRepository.Create(org, app, new TextResource()
                    {
                        Id = (await _applicationRepository.FindOne($"{org}/{app}", org)).Id,
                        Language = language,
                        Org = org,
                        Resources = [new TextResourceElement() { Id = key, Value = await reader.ReadToEndAsync() }]
                    });
                }
                else
                {
                    var resource = textResource.Resources.Find(resource => resource.Id == key);
                    if (resource == null)
                    {
                        textResource.Resources.Add(new() { Id = key, Value = await reader.ReadToEndAsync() });
                    }
                    else
                    {
                        resource.Value = await reader.ReadToEndAsync();
                    }

                    textResource = await _textRepository.Update(org, app, textResource);
                }

                return Created((string)null, textResource);
            }
            catch (Exception storageException)
            {
                _logger.LogError(storageException, "Unable to create migrated altinn ii app text");
                return StatusCode(500, $"Unable to create migrated app text due to {storageException.Message}");
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
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Produces("application/json")]
        public async Task<ActionResult> CreatePolicy([FromRoute] string org, [FromRoute] string app)
        {
            StorageSharedKeyCredential metadataCredentials = new(_azureStorageSettings.AccountName, _azureStorageSettings.AccountKey);
            BlobServiceClient metadataServiceClient = new(new Uri(_azureStorageSettings.BlobEndPoint), metadataCredentials);
            var metadataContainerClient = metadataServiceClient.GetBlobContainerClient("metadata");
            BlobClient blobClient =
                metadataContainerClient.GetBlobClient($"{org}/{app}/policy.xml");
            await blobClient.UploadAsync(Request.Body, true);
            return Created();
        }

        /// <summary>
        /// Upload xls
        /// </summary>
        /// <param name="name">Name</param>
        /// <param name="language">Language</param>
        /// <param name="version">Version</param>
        /// <returns>Ok</returns>
        [AllowAnonymous]
        [HttpPost("codelist/{name}/{language}/{version}")]
        [DisableFormValueModelBinding]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [Produces("application/json")]
        public async Task<ActionResult> CreateCodelist([FromRoute] string name, [FromRoute] string language, [FromRoute] int version)
        {
            using var reader = new StreamReader(Request.Body);
            await _a2Repository.CreateCodelist(name, language, version, await reader.ReadToEndAsync());
            return Created();
        }

        /// <summary>
        /// Upload image
        /// </summary>
        /// <returns>Ok</returns>
        [AllowAnonymous]
        [HttpPost("pdfimage")]
        [DisableFormValueModelBinding]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [Produces("application/json")]
        public async Task<ActionResult> CreateImage()
        {
            string filename = HttpUtility.UrlDecode(ContentDispositionHeaderValue.Parse(Request.Headers.ContentDisposition.ToString()).GetFilename());
            byte[] buffer = new byte[(int)Request.ContentLength];
            await Request.Body.ReadExactlyAsync(buffer);
            await _a2Repository.CreateImage(filename, buffer);
            return Created();
        }

        /// <summary>
        /// Upload xls
        /// </summary>
        /// <param name="org">Org</param>
        /// <param name="app">App</param>
        /// <param name="lformid">A2 logical form id</param>
        /// <param name="pagenumber">Page number</param>
        /// <param name="language">Language</param>
        /// <param name="xsltype">Xsl type</param>
        /// <param name="isPortrait">Format: portrait vs landscape</param>
        /// <returns>Ok</returns>
        [AllowAnonymous]
        [HttpPost("xsl/{org}/{app}/{lformid}/{pagenumber}/{language}/{xsltype}/{isportrait}")]
        [DisableFormValueModelBinding]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [Produces("application/json")]
        public async Task<ActionResult> CreateXsl([FromRoute] string org, [FromRoute] string app, [FromRoute] int lformid, [FromRoute] int pagenumber, [FromRoute] string language, [FromRoute] int xsltype, [FromRoute(Name = "isportrait")] bool isPortrait)
        {
            using var reader = new StreamReader(Request.Body);
            await _a2Repository.CreateXsl(org, app, lformid, language, pagenumber, await reader.ReadToEndAsync(), xsltype, isPortrait);
            return Created();
        }

        /// <summary>
        /// Delete migrated instance and releated data
        /// </summary>
        /// <param name="instanceGuid">Migrated instance to delete</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>Ok</returns>
        [AllowAnonymous]
        [HttpPost("delete/{instanceGuid:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces("application/json")]
        public async Task<ActionResult> CleanupOldMigration([FromRoute] Guid instanceGuid, CancellationToken cancellationToken)
        {
            if (!await CleanupOldMigrationInternal(instanceGuid.ToString(), cancellationToken))
            {
                return BadRequest();
            }

            return Ok();
        }

        /// <summary>
        /// Proxy call to pdf generator - used for local testing
        /// </summary>
        /// <param name="request">Pdf request</param>
        /// <returns>Pdf as stream</returns>
        [AllowAnonymous]
        [HttpPost("pdfproxy")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Produces("application/json")]
        public async Task<Stream> ProxyGeneratePdf([FromBody] PdfGeneratorRequest request)
        {
            var httpResponseMessage = await _httpClient.PostAsJsonAsync(_httpClient.BaseAddress, request);
            return await httpResponseMessage.Content.ReadAsStreamAsync();
        }

        private async Task<bool> CleanupOldMigrationInternal(string instanceId, CancellationToken cancellationToken)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(new Guid(instanceId), false, cancellationToken);
            if (instance == null || (!instance.DataValues.ContainsKey("A1ArchRef") && !instance.DataValues.ContainsKey("A2ArchRef")))
            {
                return false;
            }

            Application app = await _applicationRepository.FindOne(instance.AppId, instance.Org);

            if (_generalSettings.A2UseTtdAsServiceOwner)
            {
                instance.Org = "ttd";
            }

            instance.Id = instanceId;
            await _blobRepository.DeleteDataBlobs(instance, app.StorageAccountNumber);
            await _dataRepository.DeleteForInstance(instanceId);
            await _instanceEventRepository.DeleteAllInstanceEvents(instanceId);
            await _instanceRepository.Delete(instance, cancellationToken);
            await _a2Repository.DeleteMigrationState(instanceId);

            return true;
        }
    }
}
