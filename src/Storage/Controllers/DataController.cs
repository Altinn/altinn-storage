#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// API for managing the data elements of an instance
/// </summary>
[Route("storage/api/v1/instances/{instanceOwnerPartyId:int}/{instanceGuid:guid}")]
[ApiController]
public class DataController : ControllerBase
{
    private const long RequestSizeLimit = 2000 * 1024 * 1024;

    private static readonly FormOptions _defaultFormOptions = new();

    private readonly IDataRepository _dataRepository;
    private readonly IBlobRepository _blobRepository;
    private readonly IInstanceRepository _instanceRepository;
    private readonly IInstanceMutationRepository _instanceMutationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IDataService _dataService;
    private readonly IInstanceEventService _instanceEventService;
    private readonly IOnDemandClient _onDemandClient;
    private readonly string _storageBaseAndHost;
    private readonly GeneralSettings _generalSettings;
    private readonly IAuthorization _authorizationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataController"/> class
    /// </summary>
    /// <param name="dataRepository">the data repository handler</param>
    /// <param name="blobRepository">the blob repository handler</param>
    /// <param name="instanceRepository">the instance repository</param>
    /// <param name="instanceMutationRepository">the aggregate instance mutation repository.</param>
    /// <param name="applicationRepository">the application repository</param>
    /// <param name="dataService">A data service with data element related business logic.</param>
    /// <param name="instanceEventService">An instance event service with event related business logic.</param>
    /// <param name="generalSettings">the general settings.</param>
    /// <param name="onDemandClient">the ondemand client</param>
    /// <param name="authorizationService">The authorization service</param>
    public DataController(
        IDataRepository dataRepository,
        IBlobRepository blobRepository,
        IInstanceRepository instanceRepository,
        IInstanceMutationRepository instanceMutationRepository,
        IApplicationRepository applicationRepository,
        IDataService dataService,
        IInstanceEventService instanceEventService,
        IOptions<GeneralSettings> generalSettings,
        IOnDemandClient onDemandClient,
        IAuthorization authorizationService
    )
    {
        _dataRepository = dataRepository;
        _blobRepository = blobRepository;
        _instanceRepository = instanceRepository;
        _instanceMutationRepository = instanceMutationRepository;
        _applicationRepository = applicationRepository;
        _dataService = dataService;
        _instanceEventService = instanceEventService;
        _storageBaseAndHost = $"{generalSettings.Value.Hostname}/storage/api/v1/";
        _onDemandClient = onDemandClient;
        _generalSettings = generalSettings.Value;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Deletes a specific data element.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to delete.</param>
    /// <param name="delay">A boolean to indicate if the delete should be immediate or delayed following Altinn's business logic</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="ifInstanceVersionMatch">Optional expected aggregate instance version.</param>
    /// <param name="ifProcessStateVersionMatch">Optional expected process-state version.</param>
    /// <returns>The metadata of the deleted data element.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpDelete("data/{dataGuid:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElement>> Delete(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        [FromQuery] bool delay,
        CancellationToken cancellationToken,
        [FromHeader(Name = StorageHeaders.IfInstanceVersionMatch)]
            string ifInstanceVersionMatch = null,
        [FromHeader(Name = StorageHeaders.IfProcessStateVersionMatch)]
            string ifProcessStateVersionMatch = null
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(ifInstanceVersionMatch, ifProcessStateVersionMatch);
        if (preconditionError is not null)
        {
            return preconditionError;
        }

        (InstanceInternal instance, ActionResult instanceError) = await GetInstanceAsync(
            instanceGuid,
            instanceOwnerPartyId,
            false,
            cancellationToken
        );
        if (instance == null)
        {
            return instanceError;
        }

        (DataElementInternal dataElement, ActionResult dataElementError) =
            await GetDataElementAsync(instanceGuid, dataGuid, cancellationToken);
        if (dataElement == null)
        {
            return dataElementError;
        }

        bool appOwnerDeletingElement = User.GetOrg() == instance.Org;

        if (!appOwnerDeletingElement && dataElement.DeleteStatus?.IsHardDeleted == true)
        {
            return NotFound();
        }
        else if (
            delay
            && appOwnerDeletingElement
            && dataElement.DeleteStatus?.IsHardDeleted == true
        )
        {
            return dataElement.ToApiModel();
        }

        (Application application, ActionResult applicationError) = await GetApplicationAsync(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        if (application == null)
        {
            return applicationError;
        }

        (DataType dataTypeDefinition, ActionResult dataTypeError) = await GetDataTypeAsync(
            instance,
            dataElement.DataType,
            application,
            cancellationToken
        );
        if (dataTypeDefinition == null)
        {
            return dataTypeError;
        }

        if (await dataTypeDefinition.CanWrite(_authorizationService, instance) is not true)
        {
            return Forbid();
        }

        DateTime deletedTime = DateTime.UtcNow;
        dataElement.LastChanged = deletedTime;
        dataElement.LastChangedBy = User.GetUserOrOrgNo();

        if (delay)
        {
            if (dataTypeDefinition.AppLogic?.AutoDeleteOnProcessEnd != true)
            {
                return BadRequest(
                    $"DataType {dataElement.DataType} does not support delayed deletion"
                );
            }

            return await InitiateDelayedDelete(
                instance,
                dataElement,
                preconditions,
                cancellationToken
            );
        }

        try
        {
            ProcessStatusHelper.EnsureExpectedStatus(instance);

            InstanceEvent deletedEvent = _instanceEventService.BuildInstanceEvent(
                InstanceEventType.Deleted,
                instance,
                dataElement
            );
            InstanceMutationCommit mutation = new(
                [],
                [],
                [new InstanceMutationDataElementDelete(dataElement, IgnoreLock: true)],
                instance,
                [],
                preconditions.InstanceVersion,
                preconditions.ProcessStateVersion,
                InstanceEvents: [deletedEvent],
                LastChanged: deletedTime,
                LastChangedBy: dataElement.LastChangedBy
            );

            InstanceMutationApplyResult applyResult = await _instanceMutationRepository.Apply(
                instanceGuid,
                instance.InternalId,
                mutation,
                cancellationToken
            );

            await _dataService.CleanupDeletedDataElementBlobs(
                instance,
                dataElement,
                application.StorageAccountNumber,
                CancellationToken.None
            );

            VersionPreconditionHelper.WriteVersionResponseHeaders(Response, applyResult.Instance);
        }
        catch (StorageVersionMismatchException exception)
        {
            return VersionPreconditionHelper.VersionMismatch(Response, exception);
        }
        catch (RepositoryException exception) when (exception.StatusCodeSuggestion.HasValue)
        {
            return StatusCode((int)exception.StatusCodeSuggestion.Value, exception.Message);
        }

        return Ok(dataElement.ToApiModel());
    }

    /// <summary>
    /// Gets a data file from storage. The content type is the same as the file was stored with.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to retrieve.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The data file as a stream.</returns>
    [Authorize]
    [HttpGet("data/{dataGuid:guid}")]
    [RequestSizeLimit(RequestSizeLimit)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [Produces("application/json")]
    public async Task<ActionResult> Get(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken
    )
    {
        if (instanceOwnerPartyId == 0)
        {
            return BadRequest("Missing parameter value: instanceOwnerPartyId can not be empty");
        }

        (InstanceInternal instance, ActionResult instanceError) = await GetInstanceAsync(
            instanceGuid,
            instanceOwnerPartyId,
            false,
            cancellationToken
        );
        if (instance == null)
        {
            return instanceError;
        }

        if (await _authorizationService.AuthorizeEnrichedInstanceAction(instance, "read") is false)
        {
            return Forbid();
        }

        (DataElementInternal dataElement, ActionResult dataElementError) =
            await GetDataElementAsync(instanceGuid, dataGuid, cancellationToken);
        if (dataElement == null)
        {
            return dataElementError;
        }

        (Application application, ActionResult applicationError) = await GetApplicationAsync(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        if (application == null)
        {
            return applicationError;
        }

        (DataType dataTypeDefinition, ActionResult dataTypeError) = await GetDataTypeAsync(
            instance,
            dataElement.DataType,
            application,
            cancellationToken
        );
        if (dataTypeDefinition == null)
        {
            return dataTypeError;
        }

        if (await dataTypeDefinition.CanRead(_authorizationService, instance) is not true)
        {
            return Forbid();
        }

        bool appOwnerRequestingElement = User.GetOrg() == instance.Org;

        if (dataElement.DeleteStatus?.IsHardDeleted == true && !appOwnerRequestingElement)
        {
            VersionPreconditionHelper.WriteVersionResponseHeaders(Response, instance);
            return NotFound();
        }

        (string expectedBlobVersionId, ActionResult ifMatchError) = TryGetIfMatchBlobVersion();
        if (ifMatchError is not null)
        {
            return ifMatchError;
        }

        if (
            expectedBlobVersionId is not null
            && !string.Equals(
                expectedBlobVersionId,
                dataElement.BlobVersionId,
                StringComparison.Ordinal
            )
        )
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        if (!dataElement.IsRead && !appOwnerRequestingElement)
        {
            try
            {
                await _dataRepository.UpdateReadStatus(
                    instanceGuid,
                    dataGuid,
                    true,
                    cancellationToken
                );
            }
            catch (RepositoryException exception)
                when (exception.StatusCodeSuggestion == HttpStatusCode.NotFound)
            {
                VersionPreconditionHelper.WriteVersionResponseHeaders(Response, instance);
                return NotFound($"Unable to find any data element with id: {dataGuid}.");
            }
        }

        if (
            (instance.AppId.Contains(@"/a1-") || instance.AppId.Contains(@"/a2-"))
            && _generalSettings.A2UseTtdAsServiceOwner
        )
        {
            instance.Org = "ttd";
        }

        if (dataElement.BlobStoragePath.StartsWith("ondemand"))
        {
            var contentDispositionHeader = new ContentDispositionHeaderValue("inline");
            contentDispositionHeader.SetHttpFileName(dataElement.Filename);
            Response.Headers.Append(
                HeaderNames.ContentDisposition,
                contentDispositionHeader.ToString()
            );

            Stream onDemandStream = await _onDemandClient.GetStreamAsync(
                $"ondemand/{instance.AppId}/{instanceOwnerPartyId}/{instanceGuid}/{dataGuid}/"
                    + $"{LanguageHelper.GetCurrentUserLanguage(Request)}/{dataElement.BlobStoragePath.Split('/')[1]}"
            );

            VersionPreconditionHelper.WriteVersionResponseHeaders(Response, instance);
            return File(onDemandStream, dataElement.ContentType);
        }

        EnsureExpectedBlobStoragePath(dataElement, instance.AppId, instanceGuid, dataGuid);

        Stream dataStream = await _blobRepository.ReadBlob(
            instance.Org,
            dataElement.BlobStoragePath,
            application.StorageAccountNumber,
            cancellationToken
        );

        if (dataStream == null)
        {
            VersionPreconditionHelper.WriteVersionResponseHeaders(Response, instance);
            return NotFound($"Unable to read data element from blob storage for {dataGuid}");
        }

        SetBlobVersionETag(dataElement.BlobVersionId);
        VersionPreconditionHelper.WriteVersionResponseHeaders(Response, instance);

        // Migrated Altinn 2 Websa main forms should be shown inline in the browser
        if (
            instance.AppId.Contains(@"/a2-")
            && dataElement.DataType == "ref-data-as-pdf"
            && dataElement.ContentType == "text/html"
        )
        {
            var contentDispositionHeader = new ContentDispositionHeaderValue("inline");
            contentDispositionHeader.SetHttpFileName(dataElement.Filename);
            Response.Headers.Append(
                HeaderNames.ContentDisposition,
                contentDispositionHeader.ToString()
            );
            return File(dataStream, dataElement.ContentType);
        }

        return File(dataStream, dataElement.ContentType, dataElement.Filename);
    }

    /// <summary>
    /// Returns a list of data elements of an instance.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The list of data elements</returns>
    [Authorize]
    [HttpGet("dataelements")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElementList>> GetMany(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        CancellationToken cancellationToken
    )
    {
        if (instanceOwnerPartyId == 0)
        {
            return BadRequest("Missing parameter value: instanceOwnerPartyId can not be empty");
        }

        (InstanceInternal instance, ActionResult instanceError) = await GetInstanceAsync(
            instanceGuid,
            instanceOwnerPartyId,
            true,
            cancellationToken
        );
        if (instance == null)
        {
            return instanceError;
        }

        if (await _authorizationService.AuthorizeEnrichedInstanceAction(instance, "read") is false)
        {
            return Forbid();
        }

        bool appOwnerRequestingElement = User.GetOrg() == instance.Org;
        IEnumerable<DataElementInternal> visibleDataElements = appOwnerRequestingElement
            ? instance.Data
            : instance.Data.Where(de => de.DeleteStatus is not { IsHardDeleted: true });

        VersionPreconditionHelper.WriteVersionResponseHeaders(Response, instance);

        return Ok(
            new DataElementList()
            {
                DataElements = [.. visibleDataElements.Select(de => de.ToApiModel())],
            }
        );
    }

    /// <summary>
    /// Create and save the data element. The StreamContent.Headers.ContentDisposition.FileName property shall be used to set the filename on client side
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataType">The data type identifier for the data being uploaded.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="refs">An optional array of data element references.</param>
    /// <param name="generatedFromTask">An optional id of the task the data element was generated from</param>
    /// <param name="ifInstanceVersionMatch">Optional expected aggregate instance version.</param>
    /// <param name="ifProcessStateVersionMatch">Optional expected process-state version.</param>
    /// <returns>The metadata of the new data element.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpPost("data")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(RequestSizeLimit)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElement>> CreateAndUploadData(
        [FromRoute] int instanceOwnerPartyId,
        [FromRoute] Guid instanceGuid,
        [FromQuery] string dataType,
        CancellationToken cancellationToken,
        [FromQuery(Name = "refs")] List<Guid> refs = null,
        [FromQuery(Name = "generatedFromTask")] string generatedFromTask = null,
        [FromHeader(Name = StorageHeaders.IfInstanceVersionMatch)]
            string ifInstanceVersionMatch = null,
        [FromHeader(Name = StorageHeaders.IfProcessStateVersionMatch)]
            string ifProcessStateVersionMatch = null
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(ifInstanceVersionMatch, ifProcessStateVersionMatch);
        if (preconditionError is not null)
        {
            return preconditionError;
        }

        if (instanceOwnerPartyId == 0 || string.IsNullOrEmpty(dataType) || Request.Body == null)
        {
            return BadRequest(
                "Missing parameter values: instanceId, elementType or attached file content cannot be null"
            );
        }

        (InstanceInternal instance, ActionResult instanceError) = await GetInstanceAsync(
            instanceGuid,
            instanceOwnerPartyId,
            false,
            cancellationToken
        );
        if (instance == null)
        {
            return instanceError;
        }

        (Application application, ActionResult applicationError) = await GetApplicationAsync(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        if (application == null)
        {
            return applicationError;
        }

        (DataType dataTypeDefinition, ActionResult dataTypeError) = await GetDataTypeAsync(
            instance,
            dataType,
            application,
            cancellationToken
        );
        if (dataTypeDefinition == null)
        {
            return dataTypeError;
        }

        if (await dataTypeDefinition.CanWrite(_authorizationService, instance) is not true)
        {
            return Forbid();
        }

        DateTime creationTime = DateTime.UtcNow;
        var upload = await DataElementHelper.GetStream(
            Request,
            _defaultFormOptions.MultipartBoundaryLengthLimit
        );
        Stream theStream = upload.Stream;

        if (theStream == null)
        {
            return BadRequest("No data attachments found");
        }

        Guid dataGuid = Guid.NewGuid();
        string user = User.GetUserOrOrgNo();
        DataElementCreateOptions createOptions = new()
        {
            DataElementId = dataGuid,
            DataType = dataType,
            ContentType = upload.ContentType,
            Filename = HttpUtility.UrlDecode(upload.ContentFileName),
            Refs = refs,
            GeneratedFromTask = generatedFromTask,
            Created = creationTime,
            CreatedBy = user,
            FileScanResult = dataTypeDefinition.EnableFileScan
                ? FileScanResult.Pending
                : FileScanResult.NotApplicable,
            IsRead = User.GetOrg() != instance.Org,
        };

        DataElementInternal dataElement;
        DateTimeOffset blobTimestamp;
        StorageVersions versions;
        try
        {
            DataUploadResult uploadResult = await _dataService.UploadDataAndCreateDataElement(
                instance,
                theStream,
                createOptions,
                instance.InternalId,
                application.StorageAccountNumber,
                preconditions.InstanceVersion,
                preconditions.ProcessStateVersion,
                cancellationToken
            );

            dataElement = uploadResult.DataElement;
            blobTimestamp = uploadResult.BlobTimestamp;
            versions = uploadResult.Versions;
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (StorageVersionMismatchException exception)
        {
            return VersionPreconditionHelper.VersionMismatch(Response, exception);
        }
        catch (RepositoryException exception) when (exception.StatusCodeSuggestion.HasValue)
        {
            return StatusCode((int)exception.StatusCodeSuggestion.Value, exception.Message);
        }

        await _dataService.StartFileScan(
            instance,
            dataTypeDefinition,
            dataElement,
            blobTimestamp,
            application.StorageAccountNumber,
            CancellationToken.None
        );

        await _instanceEventService.DispatchEvent(InstanceEventType.Created, instance, dataElement);

        DataElement responseDataElement = dataElement.ToApiModel();
        responseDataElement.SetPlatformSelfLinks(_storageBaseAndHost, instanceOwnerPartyId);
        VersionPreconditionHelper.WriteVersionResponseHeaders(Response, versions);
        return Created(responseDataElement.SelfLinks.Platform, responseDataElement);
    }

    /// <summary>
    /// Replaces an existing data element with the attached file. The StreamContent.Headers.ContentDisposition.FileName property shall be used to set the filename on client side
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to replace.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="refs">An optional array of data element references.</param>
    /// <param name="generatedFromTask">An optional id of the task the data element was generated from</param>
    /// <param name="ifInstanceVersionMatch">Optional expected aggregate instance version.</param>
    /// <param name="ifProcessStateVersionMatch">Optional expected process-state version.</param>
    /// <returns>The metadata of the updated data element.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpPut("data/{dataGuid}")]
    [DisableFormValueModelBinding]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElement>> OverwriteData(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken,
        [FromQuery(Name = "refs")] List<Guid> refs = null,
        [FromQuery(Name = "generatedFromTask")] string generatedFromTask = null,
        [FromHeader(Name = StorageHeaders.IfInstanceVersionMatch)]
            string ifInstanceVersionMatch = null,
        [FromHeader(Name = StorageHeaders.IfProcessStateVersionMatch)]
            string ifProcessStateVersionMatch = null
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(ifInstanceVersionMatch, ifProcessStateVersionMatch);
        if (preconditionError is not null)
        {
            return preconditionError;
        }

        if (instanceOwnerPartyId == 0 || Request.Body == null)
        {
            return BadRequest(
                "Missing parameter values: instanceId, datafile or attached file content cannot be empty"
            );
        }

        (InstanceInternal instance, ActionResult instanceError) = await GetInstanceAsync(
            instanceGuid,
            instanceOwnerPartyId,
            false,
            cancellationToken
        );
        if (instance == null)
        {
            return instanceError;
        }

        (Application application, ActionResult applicationError) = await GetApplicationAsync(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        if (application == null)
        {
            return applicationError;
        }

        (DataElementInternal dataElement, ActionResult dataElementError) =
            await GetDataElementAsync(instanceGuid, dataGuid, cancellationToken);
        if (dataElement == null)
        {
            return dataElementError;
        }

        (DataType dataTypeDefinition, ActionResult dataTypeError) = await GetDataTypeAsync(
            instance,
            dataElement.DataType,
            application,
            cancellationToken
        );
        if (dataTypeDefinition == null)
        {
            return dataTypeError;
        }

        if (await dataTypeDefinition.CanWrite(_authorizationService, instance) is not true)
        {
            return Forbid();
        }

        if (dataElement.Locked)
        {
            return Conflict($"Data element {dataGuid} is locked and cannot be updated");
        }

        if (dataElement.DeleteStatus?.IsHardDeleted == true)
        {
            return Conflict($"Data element {dataGuid} is deleted and cannot be updated");
        }

        EnsureExpectedBlobStoragePath(dataElement, instance.AppId, instanceGuid, dataGuid);

        (string expectedCurrentBlobVersion, ActionResult ifMatchError) = TryGetIfMatchBlobVersion();
        if (ifMatchError is not null)
        {
            return ifMatchError;
        }

        var upload = await DataElementHelper.GetStream(
            Request,
            _defaultFormOptions.MultipartBoundaryLengthLimit
        );
        Stream theStream = upload.Stream;

        if (theStream == null)
        {
            return BadRequest("No data found in request body");
        }

        List<Reference> references = null;
        if (!string.IsNullOrEmpty(generatedFromTask))
        {
            references =
            [
                new()
                {
                    Relation = RelationType.GeneratedFrom,
                    Value = generatedFromTask,
                    ValueType = ReferenceType.Task,
                },
            ];
        }

        DateTime changedTime = DateTime.UtcNow;

        string blobVersionId = await _dataRepository.CreateBlobVersionId(
            instanceGuid,
            dataGuid,
            instance.AppId,
            instance.Org,
            application.StorageAccountNumber,
            cancellationToken
        );
        string versionedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            instanceGuid,
            blobVersionId
        );

        long blobSize;
        DateTimeOffset blobTimestamp;
        try
        {
            (blobSize, blobTimestamp) = await _blobRepository.WriteBlob(
                instance.Org,
                theStream,
                versionedBlobStoragePath,
                application.StorageAccountNumber
            );

            if (blobSize == 0)
            {
                await DataService.DeleteAllocatedBlobVersion(
                    _blobRepository,
                    _dataRepository,
                    instance.Org,
                    dataGuid,
                    versionedBlobStoragePath,
                    blobVersionId,
                    application.StorageAccountNumber
                );
                return UnprocessableEntity("Could not process attached file");
            }
        }
        catch
        {
            await DataService.DeleteAllocatedBlobVersion(
                _blobRepository,
                _dataRepository,
                instance.Org,
                dataGuid,
                versionedBlobStoragePath,
                blobVersionId,
                application.StorageAccountNumber
            );
            throw;
        }

        var updatedProperties = new Dictionary<string, object>()
        {
            { "/contentType", upload.ContentType },
            { "/filename", HttpUtility.UrlDecode(upload.ContentFileName) },
            { "/lastChangedBy", User.GetUserOrOrgNo() },
            { "/lastChanged", changedTime },
            { "/refs", refs },
            { "/references", references },
            { "/size", blobSize },
            { "/blobStoragePath", versionedBlobStoragePath },
            { "/currentBlobVersion", blobVersionId },
        };

        if (User.GetOrg() == instance.Org)
        {
            updatedProperties.Add("/isRead", false);
        }

        FileScanResult scanResult = dataTypeDefinition.EnableFileScan
            ? FileScanResult.Pending
            : FileScanResult.NotApplicable;

        updatedProperties.Add("/fileScanResult", scanResult);

        DataElementWriteResult updatedElementResult;
        try
        {
            updatedElementResult = await _dataRepository.Update(
                instanceGuid,
                dataGuid,
                updatedProperties,
                new DataElementUpdateContext
                {
                    EnforceLockCheck = true,
                    ExpectedCurrentBlobVersion = expectedCurrentBlobVersion,
                    ExpectedInstanceVersion = preconditions.InstanceVersion,
                    ExpectedProcessStateVersion = preconditions.ProcessStateVersion,
                },
                cancellationToken
            );
        }
        catch (StorageVersionMismatchException exception)
        {
            await DataService.DeleteAllocatedBlobVersion(
                _blobRepository,
                _dataRepository,
                instance.Org,
                dataGuid,
                versionedBlobStoragePath,
                blobVersionId,
                application.StorageAccountNumber
            );
            return VersionPreconditionHelper.VersionMismatch(Response, exception);
        }
        catch (DataElementBlobVersionMismatchException exception)
        {
            await DataService.DeleteAllocatedBlobVersion(
                _blobRepository,
                _dataRepository,
                instance.Org,
                dataGuid,
                versionedBlobStoragePath,
                blobVersionId,
                application.StorageAccountNumber
            );
            return StatusCode(StatusCodes.Status412PreconditionFailed, exception.Message);
        }
        catch (RepositoryException exception)
            when (exception.StatusCodeSuggestion == HttpStatusCode.Conflict)
        {
            await DataService.DeleteAllocatedBlobVersion(
                _blobRepository,
                _dataRepository,
                instance.Org,
                dataGuid,
                versionedBlobStoragePath,
                blobVersionId,
                application.StorageAccountNumber
            );
            return Conflict(exception.Message);
        }
        catch (RepositoryException exception)
            when (exception.StatusCodeSuggestion == HttpStatusCode.NotFound)
        {
            await DataService.DeleteAllocatedBlobVersion(
                _blobRepository,
                _dataRepository,
                instance.Org,
                dataGuid,
                versionedBlobStoragePath,
                blobVersionId,
                application.StorageAccountNumber
            );
            return NotFound(exception.Message);
        }

        DataElementInternal updatedElement = updatedElementResult.DataElement;
        DataElement responseDataElement = updatedElement.ToApiModel();
        responseDataElement.SetPlatformSelfLinks(_storageBaseAndHost, instanceOwnerPartyId);

        await _dataService.StartFileScan(
            instance,
            dataTypeDefinition,
            updatedElement,
            blobTimestamp,
            application.StorageAccountNumber,
            CancellationToken.None
        );

        await _instanceEventService.DispatchEvent(
            InstanceEventType.Saved,
            instance,
            updatedElement
        );

        SetBlobVersionETag(blobVersionId);
        VersionPreconditionHelper.WriteVersionResponseHeaders(
            Response,
            updatedElementResult.Versions
        );

        return Ok(responseDataElement);
    }

    /// <summary>
    /// Replaces the existing metadata for a data element with the new data element.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to update.</param>
    /// <param name="dataElement">The new metadata for the data element.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="ifInstanceVersionMatch">Optional expected aggregate instance version.</param>
    /// <param name="ifProcessStateVersionMatch">Optional expected process-state version.</param>
    /// <returns>The updated data element.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpPut("dataelements/{dataGuid}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElement>> Update(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        [FromBody] DataElement dataElement,
        CancellationToken cancellationToken,
        [FromHeader(Name = StorageHeaders.IfInstanceVersionMatch)]
            string ifInstanceVersionMatch = null,
        [FromHeader(Name = StorageHeaders.IfProcessStateVersionMatch)]
            string ifProcessStateVersionMatch = null
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(ifInstanceVersionMatch, ifProcessStateVersionMatch);
        if (preconditionError is not null)
        {
            return preconditionError;
        }

        if (
            !instanceGuid.ToString().Equals(dataElement.InstanceGuid)
            || !dataGuid.ToString().Equals(dataElement.Id)
        )
        {
            return BadRequest("Mismatch between path and dataElement content");
        }

        (InstanceInternal instance, ActionResult instanceError) = await GetInstanceAsync(
            instanceGuid,
            instanceOwnerPartyId,
            false,
            cancellationToken
        );
        if (instance == null)
        {
            return instanceError;
        }

        (DataType dataTypeDefinition, ActionResult dataTypeError) = await GetDataTypeAsync(
            instance,
            dataElement.DataType,
            cancellationToken: cancellationToken
        );
        if (dataTypeDefinition is null)
        {
            return dataTypeError;
        }

        if (await dataTypeDefinition.CanWrite(_authorizationService, instance) is not true)
        {
            return Forbid();
        }

        Dictionary<string, object> propertyList = new()
        {
            { "/locked", dataElement.Locked },
            { "/refs", dataElement.Refs },
            { "/references", dataElement.References },
            { "/tags", dataElement.Tags },
            { "/userDefinedMetadata", dataElement.UserDefinedMetadata },
            { "/metadata", dataElement.Metadata },
            { "/deleteStatus", dataElement.DeleteStatus },
            { "/lastChanged", dataElement.LastChanged },
            { "/lastChangedBy", dataElement.LastChangedBy },
        };

        DataElementWriteResult updatedDataElementResult;
        try
        {
            updatedDataElementResult = await _dataRepository.Update(
                instanceGuid,
                dataGuid,
                propertyList,
                new DataElementUpdateContext
                {
                    ExpectedInstanceVersion = preconditions.InstanceVersion,
                    ExpectedProcessStateVersion = preconditions.ProcessStateVersion,
                },
                cancellationToken: cancellationToken
            );
        }
        catch (StorageVersionMismatchException exception)
        {
            return VersionPreconditionHelper.VersionMismatch(Response, exception);
        }
        catch (RepositoryException exception) when (exception.StatusCodeSuggestion.HasValue)
        {
            return StatusCode((int)exception.StatusCodeSuggestion.Value, exception.Message);
        }

        VersionPreconditionHelper.WriteVersionResponseHeaders(
            Response,
            updatedDataElementResult.Versions
        );
        return Ok(updatedDataElementResult.DataElement.ToApiModel());
    }

    /// <summary>
    /// Sets the file scan status for an existing data element.
    /// </summary>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to update.</param>
    /// <param name="fileScanStatus">The file scan results for this data element.</param>
    /// <returns>The updated data element.</returns>
    [Authorize(Policy = "PlatformAccess")]
    [HttpPut("dataelements/{dataGuid}/filescanstatus")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    public async Task<ActionResult> SetFileScanStatus(
        Guid instanceGuid,
        Guid dataGuid,
        [FromBody] FileScanStatus fileScanStatus
    )
    {
        try
        {
            DataElementWriteResult result = await _dataRepository.UpdateFileScanStatus(
                instanceGuid,
                dataGuid,
                fileScanStatus
            );
            if (result is not null)
            {
                VersionPreconditionHelper.WriteVersionResponseHeaders(Response, result.Versions);
            }
        }
        catch (RepositoryException exception)
            when (exception.StatusCodeSuggestion == HttpStatusCode.BadRequest)
        {
            return StatusCode((int)exception.StatusCodeSuggestion.Value, exception.Message);
        }

        return Ok();
    }

    /// <summary>
    /// Checks if the data element exists in the database.
    /// </summary>
    /// <param name="dataGuid">The id of the data element.</param>
    /// <param name="cancellationToken">CancellationToken.</param>
    /// <returns>True if the data element exists, false otherwise</returns>
    [Authorize(Policy = "PlatformAccess")]
    [HttpGet("dataelementexists/{dataGuid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<bool>> DataElementExists(
        Guid dataGuid,
        CancellationToken cancellationToken
    )
    {
        bool result = await _dataRepository.Exists(dataGuid, cancellationToken);
        return Ok(result);
    }

    private async Task<(Application Application, ActionResult ErrorMessage)> GetApplicationAsync(
        string appId,
        string org,
        CancellationToken cancellationToken = default
    )
    {
        Application application = await _applicationRepository.FindOne(
            appId,
            org,
            cancellationToken
        );

        return application is null
            ? (null, NotFound($"Cannot find application {appId} in storage"))
            : (application, null);
    }

    private async Task<(InstanceInternal Instance, ActionResult ErrorMessage)> GetInstanceAsync(
        Guid instanceGuid,
        int instanceOwnerPartyId,
        bool includeDataelements,
        CancellationToken cancellationToken
    )
    {
        InstanceInternal instance = await _instanceRepository.GetOne(
            instanceGuid,
            includeDataelements,
            cancellationToken
        );

        return instance is null
            ? (
                null,
                NotFound(
                    $"Unable to find any instance with id: {instanceOwnerPartyId}/{instanceGuid}."
                )
            )
            : (instance, null);
    }

    private async Task<(
        DataElementInternal DataElement,
        ActionResult ErrorMessage
    )> GetDataElementAsync(
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken = default
    )
    {
        DataElementInternal dataElement = await _dataRepository.Read(
            instanceGuid,
            dataGuid,
            cancellationToken
        );

        return dataElement is null
            ? (null, NotFound($"Unable to find any data element with id: {dataGuid}."))
            : (dataElement, null);
    }

    private async Task<ActionResult<DataElement>> InitiateDelayedDelete(
        InstanceInternal instance,
        DataElementInternal dataElement,
        VersionPreconditions preconditions,
        CancellationToken cancellationToken
    )
    {
        DateTime deletedTime = dataElement.LastChanged.Value;
        DeleteStatus deleteStatus = new() { IsHardDeleted = true, HardDeleted = deletedTime };

        InstanceMutationApplyResult applyResult;
        try
        {
            ProcessStatusHelper.EnsureExpectedStatus(instance);

            InstanceEvent deletedEvent = _instanceEventService.BuildInstanceEvent(
                InstanceEventType.Deleted,
                instance,
                dataElement
            );
            InstanceMutationCommit mutation = new(
                [],
                [
                    new InstanceMutationDataElementUpdate(
                        dataElement.Id,
                        new Dictionary<string, object> { ["/deleteStatus"] = deleteStatus },
                        null,
                        IgnoreLock: true
                    ),
                ],
                [],
                instance,
                [],
                preconditions.InstanceVersion,
                preconditions.ProcessStateVersion,
                InstanceEvents: [deletedEvent],
                LastChanged: deletedTime,
                LastChangedBy: dataElement.LastChangedBy
            );

            applyResult = await _instanceMutationRepository.Apply(
                instance.Id,
                instance.InternalId,
                mutation,
                cancellationToken
            );
        }
        catch (StorageVersionMismatchException exception)
        {
            return VersionPreconditionHelper.VersionMismatch(Response, exception);
        }
        catch (RepositoryException exception) when (exception.StatusCodeSuggestion.HasValue)
        {
            return StatusCode((int)exception.StatusCodeSuggestion.Value, exception.Message);
        }

        InstanceInternal updatedInstance = applyResult.Instance;
        DataElementInternal updatedDataElement =
            updatedInstance.Data?.FirstOrDefault(element => element.Id == dataElement.Id)
            ?? throw new InvalidOperationException(
                "Delayed-delete apply result did not include the updated data element."
            );
        VersionPreconditionHelper.WriteVersionResponseHeaders(Response, updatedInstance);
        return Ok(updatedDataElement.ToApiModel());
    }

    private async Task<(DataType DataType, ActionResult ErrorMessage)> GetDataTypeAsync(
        InstanceInternal instance,
        string dataTypeId,
        Application application = null,
        CancellationToken cancellationToken = default
    )
    {
        if (application is null)
        {
            (application, ActionResult applicationError) = await GetApplicationAsync(
                instance.AppId,
                instance.Org,
                cancellationToken
            );
            if (application is null)
            {
                return (null, applicationError);
            }
        }

        DataType dataTypeDefinition = application.DataTypes.FirstOrDefault(e => e.Id == dataTypeId);

        return dataTypeDefinition is null
            ? (null, BadRequest("Requested element type is not declared in application metadata"))
            : (dataTypeDefinition, null);
    }

    private static void EnsureExpectedBlobStoragePath(
        DataElementInternal dataElement,
        string appId,
        Guid instanceGuid,
        Guid dataGuid
    )
    {
        if (!HasExpectedBlobStoragePath(dataElement, appId, instanceGuid, dataGuid))
        {
            throw new InvalidOperationException(
                $"Blob storage path of data element {dataGuid} was unexpected for instance {instanceGuid}."
            );
        }
    }

    private static bool HasExpectedBlobStoragePath(
        DataElementInternal dataElement,
        string appId,
        Guid instanceGuid,
        Guid dataGuid
    )
    {
        string blobStoragePath = dataElement.BlobStoragePath;
        if (string.IsNullOrEmpty(blobStoragePath))
        {
            return false;
        }

        string legacyBlobStoragePath = DataElementHelper.DataFileName(
            appId,
            instanceGuid,
            dataGuid
        );
        if (string.Equals(blobStoragePath, legacyBlobStoragePath, StringComparison.Ordinal))
        {
            return true;
        }

        string blobVersionId = dataElement.BlobVersionId;
        if (string.IsNullOrEmpty(blobVersionId))
        {
            return false;
        }

        string versionedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            appId,
            instanceGuid,
            blobVersionId
        );
        return string.Equals(blobStoragePath, versionedBlobStoragePath, StringComparison.Ordinal);
    }

    private (string BlobVersionId, ActionResult Error) TryGetIfMatchBlobVersion()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out StringValues ifMatchHeader))
        {
            return (null, null);
        }

        if (
            !EntityTagHeaderValue.TryParseList(
                [.. ifMatchHeader],
                out IList<EntityTagHeaderValue> ifMatch
            )
            || ifMatch.Count != 1
            || ifMatch[0].IsWeak
            || ifMatch[0].Equals(EntityTagHeaderValue.Any)
        )
        {
            return (null, BadRequest("If-Match must contain exactly one strong ETag."));
        }

        if (!BlobVersionId.TryParseETag(ifMatch[0].Tag.Value, out string blobVersionId))
        {
            return (null, BadRequest("If-Match ETag value must be a blob version id."));
        }

        return (blobVersionId, null);
    }

    private void SetBlobVersionETag(string blobVersionId)
    {
        string etag = BlobVersionId.ToETag(blobVersionId);
        if (etag is null)
        {
            return;
        }

        Response.Headers[HeaderNames.ETag] = etag;
    }
}
