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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// API for managing the data elements of an instance
/// </summary>
[Route("storage/api/v1/instances/{instanceOwnerPartyId:int}/{instanceGuid:guid}")]
[ApiController]
public class DataController : ControllerBase
{
    private const long RequestSizeLimit = 2000 * 1024 * 1024;

    /// <summary>
    /// Maximum size of the mutation JSON document, whether sent as the multipart
    /// <c>mutation</c> section or as a plain <c>application/json</c> body. The document is
    /// buffered in memory before deserialization, so it must stay bounded independently of
    /// <see cref="RequestSizeLimit"/>. Matches <see cref="FormOptions.DefaultValueLengthLimit"/>.
    /// </summary>
    private const int MaxMutationJsonSize = 1024 * 1024 * 4;

    private static readonly FormOptions _defaultFormOptions = new();

    private readonly IDataRepository _dataRepository;
    private readonly IBlobRepository _blobRepository;
    private readonly IInstanceRepository _instanceRepository;
    private readonly IInstanceMutationRepository _instanceMutationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IDataService _dataService;
    private readonly IInstanceEventService _instanceEventService;
    private readonly IProcessAuthorizer _processAuthorizer;
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
    /// <param name="processAuthorizer">The process-state authorizer.</param>
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
        IAuthorization authorizationService,
        IProcessAuthorizer processAuthorizer
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
        _processAuthorizer = processAuthorizer;
    }

    /// <summary>
    /// Commits a batch of mutations for a single instance.
    /// For multipart requests, the first part must be the <c>mutation</c> JSON field;
    /// each subsequent part must match exactly one <c>contentPartName</c> in the request.
    /// Unknown, duplicate, or missing file parts are rejected with 400 Bad Request.
    /// </summary>
    /// <remarks>
    /// Process-state, presentation-text, data-value and per-data-type write authorization is
    /// evaluated against the current instance snapshot before idempotent replay is admitted.
    /// Update and delete operations whose data elements no longer exist skip the pre-replay write
    /// check and are validated after replay admission instead. Replayed responses use the instance
    /// snapshot returned by the applying transaction. Process-state mutations on instances without
    /// a current task are rejected after replay admission.
    /// </remarks>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that should be mutated.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The updated instance and current data element content ETags.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpPost("mutations")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(RequestSizeLimit)]
    [Consumes("application/json", "multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [Produces("application/json")]
    public async Task<ActionResult<InstanceMutationResponse>> CommitMutation(
        [FromRoute] int instanceOwnerPartyId,
        [FromRoute] Guid instanceGuid,
        CancellationToken cancellationToken
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(Request.Headers);
        if (preconditionError is not null)
        {
            return preconditionError;
        }

        (Guid? idempotencyKey, ActionResult idempotencyKeyError) = TryReadMutationIdempotencyKey(
            preconditions
        );
        if (idempotencyKeyError is not null)
        {
            return idempotencyKeyError;
        }

        if (User.GetUserOrOrgNo() is null)
        {
            return Forbid();
        }

        (
            InstanceMutationRequest mutationRequest,
            MultipartReader multipartReader,
            ActionResult requestError
        ) = await ReadMutationRequestEnvelope(cancellationToken);
        if (requestError is not null)
        {
            return requestError;
        }

        if (!HasMutationOperations(mutationRequest))
        {
            return BadRequest("The mutation request must contain at least one operation.");
        }

        ActionResult instanceEventError = ValidateMutationInstanceEvents(
            mutationRequest,
            instanceOwnerPartyId,
            instanceGuid
        );
        if (instanceEventError is not null)
        {
            return instanceEventError;
        }

        (InstanceInternal instanceInternal, ActionResult instanceError) = await GetInstanceAsync(
            instanceGuid,
            instanceOwnerPartyId,
            true,
            cancellationToken
        );
        if (instanceError is not null)
        {
            return instanceError;
        }

        InstanceInternal instance = instanceInternal;

        (Application application, ActionResult applicationError) = await GetApplicationAsync(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        if (application is null)
        {
            return applicationError;
        }

        Dictionary<Guid, DataElementInternal> existingDataElementsById =
            instanceInternal.Data.ToDictionary(e => Guid.Parse(e.Id), e => e);

        ActionResult authorizationError = await AuthorizeMutationRequest(
            mutationRequest,
            instance,
            existingDataElementsById,
            application
        );
        if (authorizationError is not null)
        {
            return authorizationError;
        }

        ActionResult<InstanceMutationResponse> replayResponse =
            await TryBuildReplayMutationResponse(
                instanceGuid,
                instanceInternal,
                preconditions,
                idempotencyKey,
                cancellationToken
            );
        if (replayResponse is not null)
        {
            return replayResponse;
        }

        if (
            mutationRequest.ProcessState?.State is not null
            && instance.Process?.CurrentTask is null
        )
        {
            // AuthorizeProcessNext rejects every caller when the instance has no current task
            // (ended or not-started process). Checked after replay admission so idempotent
            // retries of a process-ending mutation still replay.
            return Forbid();
        }

        (AppliedMutationWork appliedMutation, ActionResult mutationError) =
            await PrepareAndApplyMutation(
                mutationRequest,
                multipartReader,
                instanceInternal,
                existingDataElementsById,
                application,
                instanceGuid,
                preconditions,
                idempotencyKey,
                cancellationToken
            );
        if (mutationError is not null)
        {
            return mutationError;
        }

        PreparedMutationWork preparedWork = appliedMutation.PreparedWork;
        InstanceMutationApplyResult applyResult = appliedMutation.ApplyResult;
        InstanceInternal updatedInstanceInternal = applyResult.Instance;

        await RunCommittedMutationSideEffects(
            applyResult,
            preparedWork,
            updatedInstanceInternal,
            application
        );

        return BuildMutationResponse(
            updatedInstanceInternal,
            applyResult.CreatedDataElementIds,
            applyResult.Replayed
        );
    }

    private async Task<ActionResult<InstanceMutationResponse>> TryBuildReplayMutationResponse(
        Guid instanceGuid,
        InstanceInternal currentInstanceInternal,
        VersionPreconditions preconditions,
        Guid? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        if (
            idempotencyKey is null
            || preconditions.InstanceVersion is null
            || preconditions.InstanceVersion.Value
                == currentInstanceInternal.Versions.InstanceVersion
        )
        {
            return null;
        }

        try
        {
            InstanceMutationApplyResult replayAdmission =
                await _instanceMutationRepository.TryReplayAdmission(
                    instanceGuid,
                    preconditions.InstanceVersion.Value,
                    currentInstanceInternal.Versions.InstanceVersion,
                    currentInstanceInternal.Versions.ProcessStateVersion,
                    idempotencyKey.Value,
                    cancellationToken
                );

            return BuildMutationResponse(
                replayAdmission.Instance,
                replayAdmission.CreatedDataElementIds,
                replayed: true
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
    }

    private ActionResult ValidateMutationInstanceEvents(
        InstanceMutationRequest mutationRequest,
        int instanceOwnerPartyId,
        Guid instanceGuid
    )
    {
        foreach (InstanceEvent instanceEvent in mutationRequest.ProcessState?.Events ?? [])
        {
            PlatformUser user = instanceEvent.User;
            bool validUserObject = ProcessController.ValidateInstanceEventUserObject(
                user?.UserId,
                user?.OrgId,
                user?.SystemUserId,
                user?.SystemUserOwnerOrgNo,
                user?.EndUserSystemId
            );
            if (!validUserObject)
            {
                return BadRequest($"Invalid user object in {nameof(instanceEvent.User)}");
            }

            if (
                instanceEvent.InstanceId is not null
                && instanceEvent.InstanceId != $"{instanceOwnerPartyId}/{instanceGuid}"
            )
            {
                return BadRequest("Instance ID in InstanceEvent does not match the Instance ID");
            }

            instanceEvent.Created = instanceEvent.Created?.ToUniversalTime() ?? DateTime.UtcNow;
        }

        return null;
    }

    private async Task<ActionResult> AuthorizeMutationRequest(
        InstanceMutationRequest mutationRequest,
        InstanceInternal instance,
        Dictionary<Guid, DataElementInternal> existingDataElementsById,
        Application application
    )
    {
        if (
            mutationRequest.ProcessState?.State is not null
            && instance.Process?.CurrentTask is not null
            && !await _processAuthorizer.AuthorizeProcessNext(
                instance,
                mutationRequest.ProcessState.State
            )
        )
        {
            return Forbid();
        }

        if (
            mutationRequest.PresentationTexts?.Count > 0
            && !await _processAuthorizer.AuthorizePresentationTextsUpdate(instance)
        )
        {
            return Forbid();
        }

        if (
            mutationRequest.DataValues?.Count > 0
            && !await _processAuthorizer.AuthorizeDataValuesUpdate(instance)
        )
        {
            return Forbid();
        }

        HashSet<string> checkedDataTypeIds = new(StringComparer.Ordinal);
        foreach (
            string dataTypeId in EnumerateRequestedDataTypeIds(
                mutationRequest,
                existingDataElementsById
            )
        )
        {
            if (!checkedDataTypeIds.Add(dataTypeId))
            {
                continue;
            }

            // Data types not declared in the application metadata are rejected with 400 by plan
            // validation after replay admission.
            DataType dataType = application.DataTypes.FirstOrDefault(e => e.Id == dataTypeId);
            if (dataType is null)
            {
                continue;
            }

            if (await dataType.CanWrite(_authorizationService, instance) is not true)
            {
                return Forbid();
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateRequestedDataTypeIds(
        InstanceMutationRequest mutationRequest,
        Dictionary<Guid, DataElementInternal> existingDataElementsById
    )
    {
        foreach (
            InstanceMutationCreateDataElement create in mutationRequest.CreateDataElements ?? []
        )
        {
            if (!string.IsNullOrWhiteSpace(create.DataType))
            {
                yield return create.DataType;
            }
        }

        // Updates and deletes resolve the data type from the existing element; elements that no
        // longer exist (e.g. in a replayed request) are validated after replay admission.
        foreach (
            InstanceMutationUpdateDataElement update in mutationRequest.UpdateDataElements ?? []
        )
        {
            if (
                existingDataElementsById.TryGetValue(
                    update.DataElementId,
                    out DataElementInternal existingDataElement
                ) && !string.IsNullOrWhiteSpace(existingDataElement.DataType)
            )
            {
                yield return existingDataElement.DataType;
            }
        }

        foreach (
            InstanceMutationDeleteDataElement delete in mutationRequest.DeleteDataElements ?? []
        )
        {
            if (
                existingDataElementsById.TryGetValue(
                    delete.DataElementId,
                    out DataElementInternal existingDataElement
                ) && !string.IsNullOrWhiteSpace(existingDataElement.DataType)
            )
            {
                yield return existingDataElement.DataType;
            }
        }
    }

    private async Task<(AppliedMutationWork Result, ActionResult Error)> PrepareAndApplyMutation(
        InstanceMutationRequest mutationRequest,
        MultipartReader multipartReader,
        InstanceInternal instanceInternal,
        Dictionary<Guid, DataElementInternal> existingDataElementsById,
        Application application,
        Guid instanceGuid,
        VersionPreconditions preconditions,
        Guid? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        InstanceInternal instance = instanceInternal;
        ValidatedMutationPlan plan = new();
        BlobStagingScope blobStaging = new();
        bool applyAttempted = false;

        try
        {
            ActionResult validationError = await ValidateMutationPlan(
                mutationRequest,
                multipartReader,
                existingDataElementsById,
                instance,
                application,
                plan,
                cancellationToken
            );
            if (validationError is not null)
            {
                return (null, validationError);
            }

            MutationInstanceUpdates mutationUpdates = BuildMutationInstanceUpdates(
                mutationRequest,
                instance
            );

            Dictionary<string, StagedFileContent> stagedByPartName = new(StringComparer.Ordinal);
            if (multipartReader is not null)
            {
                ActionResult streamingError;
                (stagedByPartName, streamingError) = await StreamFilePartsAndStageBlobs(
                    multipartReader,
                    plan,
                    instance,
                    application,
                    blobStaging,
                    cancellationToken
                );
                if (streamingError is not null)
                {
                    await CleanupStagedBlobs(blobStaging);
                    return (null, streamingError);
                }
            }

            PreparedMutationWork preparedWork = PrepareMutationWork(
                plan,
                instanceGuid,
                instance,
                mutationUpdates.InstanceUpdates,
                mutationUpdates.InstanceUpdateProperties,
                mutationUpdates.LastChanged,
                mutationUpdates.LastChangedBy,
                preconditions,
                mutationUpdates.InstanceEvents,
                idempotencyKey,
                stagedByPartName
            );

            applyAttempted = true;
            InstanceMutationApplyResult applyResult = await _instanceMutationRepository.Apply(
                instanceGuid,
                instanceInternal.InternalId,
                preparedWork.Commit,
                cancellationToken
            );

            if (applyResult.Replayed)
            {
                await CleanupStagedBlobs(blobStaging);
            }

            return (new AppliedMutationWork(preparedWork, applyResult), null);
        }
        catch (StorageVersionMismatchException exception)
        {
            await CleanupStagedBlobs(blobStaging);
            return (null, VersionPreconditionHelper.VersionMismatch(Response, exception));
        }
        catch (DataElementBlobVersionMismatchException exception)
        {
            await CleanupStagedBlobs(blobStaging);
            VersionPreconditionHelper.WriteVersionResponseHeaders(
                Response,
                exception.CurrentInstanceVersion,
                exception.CurrentProcessStateVersion
            );

            return (null, StatusCode(StatusCodes.Status412PreconditionFailed, exception.Message));
        }
        catch (RepositoryException exception) when (exception.StatusCodeSuggestion.HasValue)
        {
            await CleanupStagedBlobs(blobStaging);
            return (null, StatusCode((int)exception.StatusCodeSuggestion.Value, exception.Message));
        }
        catch (Exception exception)
        {
            if (!applyAttempted || DataService.IndicatesDefiniteRollback(exception))
            {
                await CleanupStagedBlobs(blobStaging);
            }

            throw;
        }
    }

    private async Task<ActionResult> ValidateMutationPlan(
        InstanceMutationRequest mutationRequest,
        MultipartReader multipartReader,
        Dictionary<Guid, DataElementInternal> existingDataElementsById,
        InstanceInternal instance,
        Application application,
        ValidatedMutationPlan plan,
        CancellationToken cancellationToken
    )
    {
        ActionResult duplicateDataElementIdError = ValidateDuplicateDataElementMutationIds(
            mutationRequest
        );
        if (duplicateDataElementIdError is not null)
        {
            return duplicateDataElementIdError;
        }

        ActionResult validationError = await ValidateCreateDataElements(
            mutationRequest,
            instance,
            application,
            plan,
            cancellationToken
        );
        if (validationError is not null)
        {
            return validationError;
        }

        validationError = await ValidateUpdateDataElements(
            mutationRequest,
            existingDataElementsById,
            instance,
            application,
            plan,
            cancellationToken
        );
        if (validationError is not null)
        {
            return validationError;
        }

        if (multipartReader is null && plan.ExpectedFileParts.Count > 0)
        {
            return BadRequest("File parts require a multipart/form-data request.");
        }

        validationError = await ValidateDeleteDataElements(
            mutationRequest,
            existingDataElementsById,
            instance,
            application,
            plan,
            cancellationToken
        );
        if (validationError is not null)
        {
            return validationError;
        }

        return null;
    }

    private ActionResult ValidateDuplicateDataElementMutationIds(
        InstanceMutationRequest mutationRequest
    )
    {
        if (
            TryFindDuplicateDataElementId(
                (
                    mutationRequest.UpdateDataElements?.Select(update => update.DataElementId) ?? []
                ).Concat(
                    mutationRequest.DeleteDataElements?.Select(delete => delete.DataElementId) ?? []
                ),
                out Guid duplicateDataElementId
            )
        )
        {
            return BadRequest(
                $"dataElementId '{duplicateDataElementId}' is referenced by more than one operation."
            );
        }

        return null;
    }

    private static bool TryFindDuplicateDataElementId(
        IEnumerable<Guid> dataElementIds,
        out Guid duplicateDataElementId
    )
    {
        HashSet<Guid> seen = [];
        foreach (Guid dataElementId in dataElementIds ?? [])
        {
            if (dataElementId == Guid.Empty)
            {
                continue;
            }

            if (!seen.Add(dataElementId))
            {
                duplicateDataElementId = dataElementId;
                return true;
            }
        }

        duplicateDataElementId = Guid.Empty;
        return false;
    }

    private MutationInstanceUpdates BuildMutationInstanceUpdates(
        InstanceMutationRequest mutationRequest,
        InstanceInternal instance
    )
    {
        NormalizeEmptyValuesAsRemovals(mutationRequest.PresentationTexts);
        NormalizeEmptyValuesAsRemovals(mutationRequest.DataValues);

        List<string> instanceUpdateProperties = [];
        if (mutationRequest.PresentationTexts?.Count > 0)
        {
            instanceUpdateProperties.Add(nameof(InstanceInternal.PresentationTexts));
        }

        if (mutationRequest.DataValues?.Count > 0)
        {
            instanceUpdateProperties.Add(nameof(InstanceInternal.DataValues));
        }

        ProcessState processState = mutationRequest.ProcessState?.State;
        List<InstanceEvent> instanceEvents = [.. mutationRequest.ProcessState?.Events ?? []];
        if (processState is not null)
        {
            instanceUpdateProperties.Add(nameof(InstanceInternal.Process));
        }

        InstanceStatus instanceStatus = null;
        DateTime lastChanged = DateTime.UtcNow;
        string lastChangedBy = User.GetUserOrOrgNo();
        // Archiving instance if process was ended
        if (instance.Process?.Ended is null && processState?.Ended is not null)
        {
            instanceStatus ??= instance.Status ?? new InstanceStatus();
            instanceStatus.IsArchived = true;
            instanceStatus.Archived = processState.Ended;
            if (!instanceUpdateProperties.Contains(nameof(InstanceInternal.Status)))
            {
                instanceUpdateProperties.Add(nameof(InstanceInternal.Status));
            }

            instanceUpdateProperties.Add(nameof(InstanceStatus.IsArchived));
            instanceUpdateProperties.Add(nameof(InstanceStatus.Archived));
        }

        InstanceInternal instanceUpdates = new()
        {
            Id = instance.Id,
            InstanceOwner = instance.InstanceOwner,
            Org = instance.Org,
            AppId = instance.AppId,
            Created = instance.Created,
            Process = processState ?? instance.Process,
            Status = instanceStatus,
            CompleteConfirmations = instance.CompleteConfirmations,
            LastChanged = lastChanged,
            LastChangedBy = lastChangedBy,
            PresentationTexts = mutationRequest.PresentationTexts,
            DataValues = mutationRequest.DataValues,
        };

        return new MutationInstanceUpdates(
            instanceUpdates,
            instanceUpdateProperties,
            lastChanged,
            lastChangedBy,
            instanceEvents
        );
    }

    private async Task RunCommittedMutationSideEffects(
        InstanceMutationApplyResult applyResult,
        PreparedMutationWork preparedWork,
        InstanceInternal updatedInstanceInternal,
        Application application
    )
    {
        if (applyResult.Replayed)
        {
            return;
        }

        foreach (FileScanCandidate fileScanCandidate in preparedWork.FileScanCandidates)
        {
            await _dataService.StartFileScan(
                updatedInstanceInternal,
                fileScanCandidate.DataType,
                fileScanCandidate.DataElement,
                fileScanCandidate.BlobTimestamp,
                application.StorageAccountNumber,
                CancellationToken.None
            );
        }

        foreach (
            DataElementInternal dataElementInternal in preparedWork.PostCommitBlobCleanupDataElements
        )
        {
            await _dataService.CleanupDeletedDataElementBlobs(
                updatedInstanceInternal,
                dataElementInternal,
                application.StorageAccountNumber,
                CancellationToken.None
            );
        }
    }

    private async Task<ActionResult> ValidateCreateDataElements(
        InstanceMutationRequest mutationRequest,
        InstanceInternal instance,
        Application application,
        ValidatedMutationPlan plan,
        CancellationToken cancellationToken
    )
    {
        foreach (
            InstanceMutationCreateDataElement create in mutationRequest.CreateDataElements ?? []
        )
        {
            if (string.IsNullOrWhiteSpace(create.DataType))
            {
                return BadRequest("createDataElements[].dataType is required.");
            }

            if (string.IsNullOrWhiteSpace(create.ContentPartName))
            {
                return BadRequest("createDataElements[].contentPartName is required.");
            }

            Guid dataElementId = Guid.NewGuid();
            if (
                !plan.ExpectedFileParts.TryAdd(
                    create.ContentPartName,
                    new ExpectedFilePart(dataElementId)
                )
            )
            {
                return BadRequest(
                    $"contentPartName '{create.ContentPartName}' is referenced by more than one operation."
                );
            }

            (DataType dataType, ActionResult dataTypeError) = await GetDataTypeAsync(
                instance,
                create.DataType,
                application,
                cancellationToken
            );
            if (dataType is null)
            {
                return dataTypeError;
            }

            plan.CreateDataElements.Add(
                new PlannedCreateDataElement(create, dataElementId, dataType)
            );
        }

        return null;
    }

    private void BuildCreatedDataElements(
        ValidatedMutationPlan plan,
        IReadOnlyDictionary<string, StagedFileContent> stagedByPartName,
        Guid instanceGuid,
        InstanceInternal instance,
        DateTime lastChanged,
        string lastChangedBy,
        PreparedMutationWorkBuilder work
    )
    {
        foreach (PlannedCreateDataElement plannedCreate in plan.CreateDataElements)
        {
            InstanceMutationCreateDataElement create = plannedCreate.Create;
            if (!stagedByPartName.TryGetValue(create.ContentPartName, out StagedFileContent staged))
            {
                throw new InvalidOperationException(
                    $"Invariant violation: expected staged content for part '{create.ContentPartName}' but none was found."
                );
            }

            DataElementInternal dataElement = new()
            {
                Id = plannedCreate.DataElementId.ToString(),
                InstanceGuid = instanceGuid.ToString(),
                DataType = create.DataType,
                ContentType = FirstNonEmpty(create.ContentType, staged.ContentType),
                CreatedBy = lastChangedBy,
                Created = lastChanged,
                Filename = FirstNonEmpty(create.Filename, staged.FileName),
                Size = staged.Size,
                Refs = create.Refs,
                BlobStoragePath = staged.BlobStoragePath,
                FileScanResult = plannedCreate.DataType.EnableFileScan
                    ? FileScanResult.Pending
                    : FileScanResult.NotApplicable,
                Locked = create.Locked ?? false,
                IsRead = User.GetOrg() != instance.Org,
                References = CreateGeneratedFromTaskReferences(create.GeneratedFromTask),
                Metadata = create.Metadata,
                UserDefinedMetadata = create.UserDefinedMetadata,
                Tags = create.Tags,
                BlobVersionId = staged.BlobVersionId,
            };

            work.AddCreatedDataElement(dataElement, plannedCreate.DataType, staged.BlobTimestamp);
        }
    }

    private async Task<ActionResult> ValidateUpdateDataElements(
        InstanceMutationRequest mutationRequest,
        Dictionary<Guid, DataElementInternal> existingDataElements,
        InstanceInternal instance,
        Application application,
        ValidatedMutationPlan plan,
        CancellationToken cancellationToken
    )
    {
        foreach (
            InstanceMutationUpdateDataElement update in mutationRequest.UpdateDataElements ?? []
        )
        {
            if (update.DataElementId == Guid.Empty)
            {
                return BadRequest("updateDataElements[].dataElementId is required.");
            }

            if (
                !existingDataElements.TryGetValue(
                    update.DataElementId,
                    out DataElementInternal existingDataElement
                )
            )
            {
                return NotFound(
                    $"Unable to find any data element with id: {update.DataElementId}."
                );
            }

            (string expectedCurrentBlobVersion, ActionResult blobVersionError) =
                TryNormalizeExpectedCurrentBlobVersion(update.ExpectedCurrentBlobVersion);
            if (blobVersionError is not null)
            {
                return blobVersionError;
            }

            DataElementInternal dataElement = existingDataElement;
            (DataType dataType, ActionResult dataTypeError) = await GetDataTypeAsync(
                instance,
                dataElement.DataType,
                application,
                cancellationToken
            );
            if (dataType is null)
            {
                return dataTypeError;
            }

            Dictionary<string, object> propertyList = BuildMetadataPropertyList(update);
            bool hasContentUpdate = !string.IsNullOrWhiteSpace(update.ContentPartName);
            if (hasContentUpdate)
            {
                ActionResult contentError = PlanUpdateContent(update, dataElement);
                if (contentError is not null)
                {
                    return contentError;
                }

                if (
                    !plan.ExpectedFileParts.TryAdd(
                        update.ContentPartName,
                        new ExpectedFilePart(update.DataElementId)
                    )
                )
                {
                    return BadRequest(
                        $"contentPartName '{update.ContentPartName}' is referenced by more than one operation."
                    );
                }
            }

            if (propertyList.Count == 0 && !hasContentUpdate)
            {
                return BadRequest(
                    $"No metadata or content changes were supplied for data element {update.DataElementId}."
                );
            }

            plan.UpdateDataElements.Add(
                new PlannedUpdateDataElement(
                    update,
                    existingDataElement,
                    dataType,
                    propertyList,
                    expectedCurrentBlobVersion,
                    hasContentUpdate
                )
            );
        }

        return null;
    }

    private static ActionResult PlanUpdateContent(
        InstanceMutationUpdateDataElement update,
        DataElementInternal dataElement
    )
    {
        if (dataElement.Locked)
        {
            return new ConflictObjectResult(
                $"Data element {update.DataElementId} is locked and cannot be updated"
            );
        }

        if (dataElement.DeleteStatus?.IsHardDeleted == true)
        {
            return new ConflictObjectResult(
                $"Data element {update.DataElementId} is deleted and cannot be updated"
            );
        }

        return null;
    }

    private void BuildUpdatedDataElements(
        ValidatedMutationPlan plan,
        InstanceInternal instance,
        IReadOnlyDictionary<string, StagedFileContent> stagedByPartName,
        PreparedMutationWorkBuilder work
    )
    {
        foreach (PlannedUpdateDataElement plannedUpdate in plan.UpdateDataElements)
        {
            if (plannedUpdate.HasContentUpdate)
            {
                BuildUpdatedContent(plannedUpdate, stagedByPartName, instance, work);
            }

            work.AddUpdatedDataElement(
                new InstanceMutationDataElementUpdate(
                    plannedUpdate.Update.DataElementId,
                    plannedUpdate.PropertyList,
                    plannedUpdate.ExpectedCurrentBlobVersion,
                    IgnoreLock: !plannedUpdate.HasContentUpdate
                        && plannedUpdate.Update.Locked == false
                )
            );
        }
    }

    private void BuildUpdatedContent(
        PlannedUpdateDataElement plannedUpdate,
        IReadOnlyDictionary<string, StagedFileContent> stagedByPartName,
        InstanceInternal instance,
        PreparedMutationWorkBuilder work
    )
    {
        InstanceMutationUpdateDataElement update = plannedUpdate.Update;
        DataElementInternal dataElement = plannedUpdate.ExistingDataElement;
        if (!stagedByPartName.TryGetValue(update.ContentPartName, out StagedFileContent staged))
        {
            throw new InvalidOperationException(
                $"Invariant violation: expected staged content for part '{update.ContentPartName}' but none was found."
            );
        }

        Dictionary<string, object> propertyList = plannedUpdate.PropertyList;
        propertyList["/contentType"] = FirstNonEmpty(update.ContentType, staged.ContentType);
        propertyList["/filename"] = FirstNonEmpty(update.Filename, staged.FileName);
        propertyList["/refs"] = update.Refs;
        propertyList["/references"] = CreateGeneratedFromTaskReferences(update.GeneratedFromTask);
        propertyList["/size"] = staged.Size;
        propertyList["/blobStoragePath"] = staged.BlobStoragePath;
        propertyList["/currentBlobVersion"] = staged.BlobVersionId;
        propertyList["/fileScanResult"] = plannedUpdate.DataType.EnableFileScan
            ? FileScanResult.Pending
            : FileScanResult.NotApplicable;

        if (User.GetOrg() == instance.Org)
        {
            propertyList["/isRead"] = false;
        }

        DataElementInternal scanElement = CloneDataElementForScan(
            dataElement,
            propertyList,
            staged.BlobStoragePath
        );
        scanElement.BlobVersionId = staged.BlobVersionId;
        work.AddUpdatedContent(scanElement, plannedUpdate.DataType, staged.BlobTimestamp);
    }

    private async Task<ActionResult> ValidateDeleteDataElements(
        InstanceMutationRequest mutationRequest,
        Dictionary<Guid, DataElementInternal> existingDataElements,
        InstanceInternal instance,
        Application application,
        ValidatedMutationPlan plan,
        CancellationToken cancellationToken
    )
    {
        foreach (
            InstanceMutationDeleteDataElement delete in mutationRequest.DeleteDataElements ?? []
        )
        {
            if (delete.DataElementId == Guid.Empty)
            {
                return BadRequest("deleteDataElements[].dataElementId is required.");
            }

            if (
                !existingDataElements.TryGetValue(
                    delete.DataElementId,
                    out DataElementInternal existingDataElement
                )
            )
            {
                return NotFound(
                    $"Unable to find any data element with id: {delete.DataElementId}."
                );
            }

            DataElementInternal dataElement = existingDataElement;
            (DataType dataType, ActionResult dataTypeError) = await GetDataTypeAsync(
                instance,
                dataElement.DataType,
                application,
                cancellationToken
            );
            if (dataType is null)
            {
                return dataTypeError;
            }

            plan.DeleteDataElements.Add(
                new PlannedDeleteDataElement(existingDataElement, delete.IgnoreLock)
            );
        }

        return null;
    }

    private PreparedMutationWork PrepareMutationWork(
        ValidatedMutationPlan plan,
        Guid instanceGuid,
        InstanceInternal instance,
        InstanceInternal instanceUpdates,
        List<string> instanceUpdateProperties,
        DateTime lastChanged,
        string lastChangedBy,
        VersionPreconditions preconditions,
        List<InstanceEvent> requestInstanceEvents,
        Guid? idempotencyKey,
        IReadOnlyDictionary<string, StagedFileContent> stagedByPartName
    )
    {
        PreparedMutationWorkBuilder work = new();

        BuildCreatedDataElements(
            plan,
            stagedByPartName,
            instanceGuid,
            instance,
            lastChanged,
            lastChangedBy,
            work
        );
        BuildUpdatedDataElements(plan, instance, stagedByPartName, work);

        foreach (PlannedDeleteDataElement plannedDelete in plan.DeleteDataElements)
        {
            DataElementInternal dataElement = plannedDelete.ExistingDataElement;
            dataElement.LastChanged = lastChanged;
            dataElement.LastChangedBy = lastChangedBy;
            work.AddDeletedDataElement(plannedDelete.ExistingDataElement, plannedDelete.IgnoreLock);
        }

        return work.Build(
            instanceUpdates,
            instanceUpdateProperties,
            preconditions,
            requestInstanceEvents,
            idempotencyKey,
            lastChanged,
            lastChangedBy,
            (eventType, dataElement) =>
                _instanceEventService.BuildInstanceEvent(eventType, instanceUpdates, dataElement)
        );
    }

    private ActionResult<InstanceMutationResponse> BuildMutationResponse(
        InstanceInternal updatedInstanceInternal,
        IReadOnlyList<string> createdDataElementIds,
        bool replayed
    )
    {
        Instance updatedInstance = updatedInstanceInternal.ToApiModel();
        updatedInstance.SetPlatformSelfLinks(_storageBaseAndHost);
        VersionPreconditionHelper.WriteVersionResponseHeaders(Response, updatedInstanceInternal);

        return Ok(
            new InstanceMutationResponse
            {
                Instance = updatedInstance,
                CreatedDataElementIds = [.. createdDataElementIds],
                Replayed = replayed,
                DataElementContentEtags = BuildContentETagMap(updatedInstanceInternal),
            }
        );
    }

    /// <summary>
    /// Deletes a specific data element.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to delete.</param>
    /// <param name="delay">A boolean to indicate if the delete should be immediate or delayed following Altinn's business logic</param>
    /// <param name="cancellationToken">CancellationToken</param>
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
        CancellationToken cancellationToken
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(Request.Headers);
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

        dataElement.LastChangedBy = User.GetUserOrOrgNo();

        if (delay)
        {
            if (dataTypeDefinition.AppLogic?.AutoDeleteOnProcessEnd != true)
            {
                return BadRequest(
                    $"DataType {dataElement.DataType} does not support delayed deletion"
                );
            }

            return await InitiateDelayedDelete(instance, dataElement, preconditions);
        }

        try
        {
            await _dataService.DeleteImmediately(
                instance,
                dataElement,
                application.StorageAccountNumber,
                preconditions.InstanceVersion,
                preconditions.ProcessStateVersion
            );
            InstanceInternal updatedInstance = await _instanceRepository.GetOne(
                instanceGuid,
                false,
                cancellationToken
            );
            if (updatedInstance is not null)
            {
                VersionPreconditionHelper.WriteVersionResponseHeaders(Response, updatedInstance);
            }
        }
        catch (StorageVersionMismatchException exception)
        {
            return VersionPreconditionHelper.VersionMismatch(Response, exception);
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

        if (HasExpectedBlobStoragePath(dataElement, instance.AppId, instanceGuid, dataGuid))
        {
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
        else if (dataElement.BlobStoragePath.StartsWith("ondemand"))
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

        VersionPreconditionHelper.WriteVersionResponseHeaders(Response, instance);
        return NotFound("Unable to find requested data item");
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
                DataElements = visibleDataElements.Select(de => de.ToApiModel()).ToList(),
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
    /// <returns>The metadata of the new data element.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpPost("data")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(RequestSizeLimit)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElement>> CreateAndUploadData(
        [FromRoute] int instanceOwnerPartyId,
        [FromRoute] Guid instanceGuid,
        [FromQuery] string dataType,
        CancellationToken cancellationToken,
        [FromQuery(Name = "refs")] List<Guid> refs = null,
        [FromQuery(Name = "generatedFromTask")] string generatedFromTask = null
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(Request.Headers);
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
                cancellationToken,
                preconditions.InstanceVersion,
                preconditions.ProcessStateVersion
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
        [FromQuery(Name = "generatedFromTask")] string generatedFromTask = null
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(Request.Headers);
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

        if (!HasExpectedBlobStoragePath(dataElement, instance.AppId, instanceGuid, dataGuid))
        {
            return StatusCode(500, "Storage url does not match with instance metadata");
        }

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
            instanceGuid.ToString(),
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
                cancellationToken: cancellationToken
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
    /// <returns>The updated data element.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpPut("dataelements/{dataGuid}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElement>> Update(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        [FromBody] DataElement dataElement,
        CancellationToken cancellationToken
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(Request.Headers);
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
        catch (RepositoryException exception) when (exception.StatusCodeSuggestion.HasValue)
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

    private async Task<(
        InstanceMutationRequest Request,
        MultipartReader Reader,
        ActionResult Error
    )> ReadMutationRequestEnvelope(CancellationToken cancellationToken)
    {
        MultipartReader multipartReader = null;
        using MemoryStream mutationJsonStream = new();

        if (MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
        {
            string boundary;
            try
            {
                boundary = MultipartRequestHelper.GetBoundary(
                    MediaTypeHeaderValue.Parse(Request.ContentType),
                    _defaultFormOptions.MultipartBoundaryLengthLimit
                );
            }
            catch (InvalidDataException exception)
            {
                return (null, null, BadRequest(exception.Message));
            }

            multipartReader = new MultipartReader(boundary, Request.Body);
            MultipartSection section = await multipartReader.ReadNextSectionAsync(
                cancellationToken
            );
            if (
                section is null
                || !ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition,
                    out ContentDispositionHeaderValue contentDisposition
                )
                || !MultipartRequestHelper.HasFormDataContentDisposition(contentDisposition)
                || HeaderUtilities.RemoveQuotes(contentDisposition.Name).Value != "mutation"
            )
            {
                return (
                    null,
                    null,
                    BadRequest(
                        "Multipart aggregate mutation requests must start with a 'mutation' JSON field."
                    )
                );
            }

            if (
                !await TryReadBodyWithinLimitAsync(
                    section.Body,
                    mutationJsonStream,
                    MaxMutationJsonSize,
                    cancellationToken
                )
            )
            {
                return (null, null, BadRequest("Mutation JSON exceeds maximum allowed size."));
            }
        }
        else if (
            Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            == true
        )
        {
            if (
                !await TryReadBodyWithinLimitAsync(
                    Request.Body,
                    mutationJsonStream,
                    MaxMutationJsonSize,
                    cancellationToken
                )
            )
            {
                return (null, null, BadRequest("Mutation JSON exceeds maximum allowed size."));
            }
        }
        else
        {
            return (
                null,
                null,
                BadRequest(
                    "Aggregate mutation requests must be application/json or multipart/form-data."
                )
            );
        }

        try
        {
            mutationJsonStream.Position = 0;
            using var jsonReader = new JsonTextReader(new StreamReader(mutationJsonStream));
            JsonSerializer serializer = new() { CheckAdditionalContent = true };
            InstanceMutationRequest request = serializer.Deserialize<InstanceMutationRequest>(
                jsonReader
            );
            return request is null
                ? (null, null, BadRequest("The mutation request body is required."))
                : (request, multipartReader, null);
        }
        catch (JsonException exception)
        {
            return (
                null,
                null,
                BadRequest($"Unable to parse mutation request JSON: {exception.Message}")
            );
        }
    }

    private static async Task<bool> TryReadBodyWithinLimitAsync(
        Stream source,
        MemoryStream destination,
        int limit,
        CancellationToken cancellationToken
    )
    {
        byte[] buffer = new byte[81920];
        int totalBytes = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (totalBytes + bytesRead > limit)
            {
                return false;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytes += bytesRead;
        }

        return true;
    }

    private async Task<(
        Dictionary<string, StagedFileContent> Staged,
        ActionResult Error
    )> StreamFilePartsAndStageBlobs(
        MultipartReader reader,
        ValidatedMutationPlan plan,
        InstanceInternal instance,
        Application application,
        BlobStagingScope blobStaging,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, StagedFileContent> staged = new(StringComparer.Ordinal);

        MultipartSection section;
        try
        {
            while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
            {
                if (
                    !ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition,
                        out ContentDispositionHeaderValue contentDisposition
                    )
                )
                {
                    return (
                        null,
                        BadRequest("Multipart section is missing a Content-Disposition header.")
                    );
                }

                string name = HeaderUtilities.RemoveQuotes(contentDisposition.Name).Value;
                if (string.IsNullOrEmpty(name))
                {
                    return (
                        null,
                        BadRequest("Multipart section Content-Disposition is missing a name.")
                    );
                }

                if (name == "mutation")
                {
                    return (
                        null,
                        BadRequest(
                            "Multipart aggregate mutation requests must contain only one 'mutation' field."
                        )
                    );
                }

                if (!plan.ExpectedFileParts.TryGetValue(name, out ExpectedFilePart expected))
                {
                    return (null, BadRequest($"Unexpected multipart part '{name}'."));
                }

                if (staged.ContainsKey(name))
                {
                    return (
                        null,
                        BadRequest(
                            $"Multipart file part name '{name}' was supplied more than once."
                        )
                    );
                }

                (
                    string blobVersionId,
                    string blobStoragePath,
                    long size,
                    DateTimeOffset blobTimestamp,
                    ActionResult stageError
                ) = await StageBlob(
                    instance,
                    application,
                    expected.TargetDataElementId,
                    section.Body,
                    blobStaging,
                    cancellationToken
                );
                if (stageError is not null)
                {
                    return (null, stageError);
                }

                string fileName = HttpUtility.UrlDecode(contentDisposition.GetFilename());
                staged[name] = new StagedFileContent(
                    blobVersionId,
                    blobStoragePath,
                    size,
                    blobTimestamp,
                    section.ContentType,
                    fileName
                );
            }
        }
        catch (InvalidDataException exception)
        {
            return (null, BadRequest($"Malformed multipart body: {exception.Message}"));
        }
        catch (IOException exception)
        {
            // Streaming interleaves reading the request body with writing to blob storage, but
            // the blob SDK wraps its own failures in RequestFailedException (propagated as 500),
            // so an IOException here comes from reading the client's request body.
            return (null, BadRequest($"Error reading multipart body: {exception.Message}"));
        }

        foreach (string partName in plan.ExpectedFileParts.Keys)
        {
            if (!staged.ContainsKey(partName))
            {
                return (
                    null,
                    BadRequest($"No multipart file part named '{partName}' was supplied.")
                );
            }
        }

        return (staged, null);
    }

    private static bool HasMutationOperations(InstanceMutationRequest request) =>
        request.CreateDataElements?.Count > 0
        || request.UpdateDataElements?.Count > 0
        || request.DeleteDataElements?.Count > 0
        || request.DataValues?.Count > 0
        || request.PresentationTexts?.Count > 0
        || request.ProcessState?.State is not null
        || request.ProcessState?.Events?.Count > 0;

    private (Guid? IdempotencyKey, ActionResult Error) TryReadMutationIdempotencyKey(
        VersionPreconditions preconditions
    )
    {
        if (!Request.Headers.TryGetValue(StorageHeaders.IdempotencyKey, out StringValues values))
        {
            return (null, null);
        }

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            return (
                null,
                BadRequest($"{StorageHeaders.IdempotencyKey} must contain one non-empty value.")
            );
        }

        string idempotencyKey = values[0];
        if (!Guid.TryParse(idempotencyKey, out Guid parsedIdempotencyKey))
        {
            return (null, BadRequest($"{StorageHeaders.IdempotencyKey} must be a valid GUID."));
        }

        if (preconditions.InstanceVersion is null)
        {
            return (
                null,
                BadRequest(
                    $"{StorageHeaders.IdempotencyKey} requires {StorageHeaders.IfInstanceVersionMatch}."
                )
            );
        }

        return (parsedIdempotencyKey, null);
    }

    private static void NormalizeEmptyValuesAsRemovals(Dictionary<string, string> values)
    {
        if (values is null)
        {
            return;
        }

        foreach (
            string key in values
                .Where(entry => string.IsNullOrEmpty(entry.Value))
                .Select(entry => entry.Key)
                .ToList()
        )
        {
            values[key] = null;
        }
    }

    private async Task CleanupStagedBlobs(BlobStagingScope blobStaging)
    {
        await blobStaging.Cleanup(stagedBlob =>
            DataService.DeleteAllocatedBlobVersion(
                _blobRepository,
                _dataRepository,
                stagedBlob.Org,
                stagedBlob.DataElementId,
                stagedBlob.BlobStoragePath,
                stagedBlob.BlobVersionId,
                stagedBlob.StorageAccountNumber
            )
        );
    }

    private async Task<(
        string BlobVersionId,
        string BlobStoragePath,
        long Size,
        DateTimeOffset BlobTimestamp,
        ActionResult Error
    )> StageBlob(
        InstanceInternal instance,
        Application application,
        Guid dataElementId,
        Stream content,
        BlobStagingScope blobStaging,
        CancellationToken cancellationToken
    )
    {
        string instanceGuidStr = instance.Id;
        string blobVersionId = await _dataRepository.CreateBlobVersionId(
            new Guid(instanceGuidStr),
            dataElementId,
            instance.AppId,
            instance.Org,
            application.StorageAccountNumber,
            cancellationToken
        );
        string versionedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            instanceGuidStr,
            blobVersionId
        );

        try
        {
            (long blobSize, DateTimeOffset blobTimestamp) = await _blobRepository.WriteBlob(
                instance.Org,
                content,
                versionedBlobStoragePath,
                application.StorageAccountNumber
            );

            if (blobSize == 0)
            {
                await DataService.DeleteAllocatedBlobVersion(
                    _blobRepository,
                    _dataRepository,
                    instance.Org,
                    dataElementId,
                    versionedBlobStoragePath,
                    blobVersionId,
                    application.StorageAccountNumber
                );
                return (
                    null,
                    null,
                    0,
                    default,
                    UnprocessableEntity("Could not process attached file")
                );
            }

            blobStaging.Track(
                new StagedBlob(
                    instance.Org,
                    dataElementId,
                    blobVersionId,
                    versionedBlobStoragePath,
                    application.StorageAccountNumber
                )
            );
            return (blobVersionId, versionedBlobStoragePath, blobSize, blobTimestamp, null);
        }
        catch
        {
            await DataService.DeleteAllocatedBlobVersion(
                _blobRepository,
                _dataRepository,
                instance.Org,
                dataElementId,
                versionedBlobStoragePath,
                blobVersionId,
                application.StorageAccountNumber
            );
            throw;
        }
    }

    private static Dictionary<string, object> BuildMetadataPropertyList(
        InstanceMutationUpdateDataElement update
    )
    {
        Dictionary<string, object> propertyList = [];

        if (update.ContentType is not null)
        {
            propertyList["/contentType"] = update.ContentType;
        }

        if (update.Filename is not null)
        {
            propertyList["/filename"] = update.Filename;
        }

        if (update.Refs is not null)
        {
            propertyList["/refs"] = update.Refs;
        }

        if (update.GeneratedFromTask is not null)
        {
            propertyList["/references"] = CreateGeneratedFromTaskReferences(
                update.GeneratedFromTask
            );
        }

        if (update.Metadata is not null)
        {
            propertyList["/metadata"] = update.Metadata;
        }

        if (update.UserDefinedMetadata is not null)
        {
            propertyList["/userDefinedMetadata"] = update.UserDefinedMetadata;
        }

        if (update.Tags is not null)
        {
            propertyList["/tags"] = update.Tags;
        }

        if (update.Locked.HasValue)
        {
            propertyList["/locked"] = update.Locked.Value;
        }

        return propertyList;
    }

    private (string BlobVersionId, ActionResult Error) TryNormalizeExpectedCurrentBlobVersion(
        string expectedCurrentBlobVersion
    )
    {
        if (string.IsNullOrWhiteSpace(expectedCurrentBlobVersion))
        {
            return (null, null);
        }

        string blobVersionId = expectedCurrentBlobVersion.Trim();
        if (blobVersionId.StartsWith('"'))
        {
            if (
                !EntityTagHeaderValue.TryParseList(
                    [blobVersionId],
                    out IList<EntityTagHeaderValue> ifMatch
                )
                || ifMatch.Count != 1
                || ifMatch[0].IsWeak
                || ifMatch[0].Equals(EntityTagHeaderValue.Any)
            )
            {
                return (
                    null,
                    BadRequest(
                        "expectedCurrentBlobVersion must be a blob version id or one strong ETag."
                    )
                );
            }

            blobVersionId = ifMatch[0].Tag.Value[1..^1];
        }

        try
        {
            BlobVersionId.Decode(blobVersionId);
            return (blobVersionId, null);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return (
                null,
                BadRequest("expectedCurrentBlobVersion must identify a blob version id.")
            );
        }
    }

    private static DataElementInternal CloneDataElementForScan(
        DataElementInternal dataElement,
        Dictionary<string, object> propertyList,
        string blobStoragePath
    )
    {
        DataElementInternal clone =
            System.Text.Json.JsonSerializer.Deserialize<DataElementInternal>(
                System.Text.Json.JsonSerializer.Serialize(dataElement)
            );
        clone.BlobStoragePath = blobStoragePath;

        if (propertyList.TryGetValue("/contentType", out object contentType))
        {
            clone.ContentType = (string)contentType;
        }

        if (propertyList.TryGetValue("/filename", out object filename))
        {
            clone.Filename = (string)filename;
        }

        if (propertyList.TryGetValue("/size", out object size))
        {
            clone.Size = (long)size;
        }

        if (propertyList.TryGetValue("/fileScanResult", out object fileScanResult))
        {
            clone.FileScanResult = (FileScanResult)fileScanResult;
        }

        if (propertyList.TryGetValue("/lastChanged", out object lastChanged))
        {
            clone.LastChanged = (DateTime)lastChanged;
        }

        if (propertyList.TryGetValue("/lastChangedBy", out object lastChangedBy))
        {
            clone.LastChangedBy = (string)lastChangedBy;
        }

        return clone;
    }

    private static Dictionary<string, string> BuildContentETagMap(
        InstanceInternal updatedInstanceInternal
    ) =>
        updatedInstanceInternal
            .Data.Where(dataElement => !string.IsNullOrEmpty(dataElement.BlobVersionId))
            .ToDictionary(
                dataElement => dataElement.Id,
                dataElement => $"\"{dataElement.BlobVersionId}\""
            );

    private static string FirstNonEmpty(string primary, string fallback) =>
        string.IsNullOrEmpty(primary) ? fallback : primary;

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

    private sealed record StagedBlob(
        string Org,
        Guid DataElementId,
        string BlobVersionId,
        string BlobStoragePath,
        int? StorageAccountNumber
    );

    private sealed class BlobStagingScope
    {
        private readonly List<StagedBlob> _stagedBlobs = [];

        public void Track(StagedBlob stagedBlob) => _stagedBlobs.Add(stagedBlob);

        public async Task Cleanup(Func<StagedBlob, Task> cleanup)
        {
            foreach (StagedBlob stagedBlob in _stagedBlobs)
            {
                await cleanup(stagedBlob);
            }
        }
    }

    private sealed class ValidatedMutationPlan
    {
        public Dictionary<string, ExpectedFilePart> ExpectedFileParts { get; } =
            new(StringComparer.Ordinal);

        public List<PlannedCreateDataElement> CreateDataElements { get; } = [];

        public List<PlannedUpdateDataElement> UpdateDataElements { get; } = [];

        public List<PlannedDeleteDataElement> DeleteDataElements { get; } = [];
    }

    private sealed record PlannedCreateDataElement(
        InstanceMutationCreateDataElement Create,
        Guid DataElementId,
        DataType DataType
    );

    private sealed record PlannedUpdateDataElement(
        InstanceMutationUpdateDataElement Update,
        DataElementInternal ExistingDataElement,
        DataType DataType,
        Dictionary<string, object> PropertyList,
        string ExpectedCurrentBlobVersion,
        bool HasContentUpdate
    );

    private sealed record ExpectedFilePart(Guid TargetDataElementId);

    private sealed record StagedFileContent(
        string BlobVersionId,
        string BlobStoragePath,
        long Size,
        DateTimeOffset BlobTimestamp,
        string ContentType,
        string FileName
    );

    private sealed record PlannedDeleteDataElement(
        DataElementInternal ExistingDataElement,
        bool IgnoreLock
    );

    private sealed record PreparedMutationWork(
        InstanceMutationCommit Commit,
        IReadOnlyList<FileScanCandidate> FileScanCandidates,
        IReadOnlyList<DataElementInternal> PostCommitBlobCleanupDataElements
    );

    private sealed record AppliedMutationWork(
        PreparedMutationWork PreparedWork,
        InstanceMutationApplyResult ApplyResult
    );

    private sealed record MutationInstanceUpdates(
        InstanceInternal InstanceUpdates,
        List<string> InstanceUpdateProperties,
        DateTime LastChanged,
        string LastChangedBy,
        List<InstanceEvent> InstanceEvents
    );

    private sealed class PreparedMutationWorkBuilder
    {
        private readonly List<DataElementInternal> _createDataElements = [];

        private readonly List<InstanceMutationDataElementUpdate> _updateDataElements = [];

        private readonly List<InstanceMutationDataElementDelete> _deleteDataElements = [];

        private readonly List<FileScanCandidate> _fileScanCandidates = [];

        private readonly List<(
            InstanceEventType EventType,
            DataElementInternal DataElement
        )> _transactionalEvents = [];

        private readonly List<DataElementInternal> _postCommitBlobCleanupDataElements = [];

        public void AddCreatedDataElement(
            DataElementInternal dataElement,
            DataType dataType,
            DateTimeOffset blobTimestamp
        )
        {
            _createDataElements.Add(dataElement);
            _fileScanCandidates.Add(new FileScanCandidate(dataElement, dataType, blobTimestamp));
            _transactionalEvents.Add((InstanceEventType.Created, dataElement));
        }

        public void AddUpdatedDataElement(InstanceMutationDataElementUpdate update) =>
            _updateDataElements.Add(update);

        public void AddUpdatedContent(
            DataElementInternal dataElement,
            DataType dataType,
            DateTimeOffset blobTimestamp
        )
        {
            _fileScanCandidates.Add(new FileScanCandidate(dataElement, dataType, blobTimestamp));
            _transactionalEvents.Add((InstanceEventType.Saved, dataElement));
        }

        public void AddDeletedDataElement(DataElementInternal dataElement, bool ignoreLock)
        {
            _deleteDataElements.Add(
                new InstanceMutationDataElementDelete(dataElement, IgnoreLock: ignoreLock)
            );
            _postCommitBlobCleanupDataElements.Add(dataElement);
        }

        public PreparedMutationWork Build(
            InstanceInternal instanceUpdates,
            List<string> instanceUpdateProperties,
            VersionPreconditions preconditions,
            IEnumerable<InstanceEvent> requestInstanceEvents,
            Guid? idempotencyKey,
            DateTime lastChanged,
            string lastChangedBy,
            Func<InstanceEventType, DataElementInternal, InstanceEvent> buildInstanceEvent
        )
        {
            List<InstanceEvent> instanceEvents = [.. requestInstanceEvents];
            foreach (
                (
                    InstanceEventType eventType,
                    DataElementInternal dataElement
                ) in _transactionalEvents
            )
            {
                instanceEvents.Add(buildInstanceEvent(eventType, dataElement));
            }

            foreach (InstanceMutationDataElementDelete dataElement in _deleteDataElements)
            {
                instanceEvents.Add(
                    buildInstanceEvent(InstanceEventType.Deleted, dataElement.DataElement)
                );
            }

            InstanceMutationCommit commit = new(
                _createDataElements,
                _updateDataElements,
                _deleteDataElements,
                instanceUpdates,
                instanceUpdateProperties,
                preconditions.InstanceVersion,
                preconditions.ProcessStateVersion,
                instanceEvents,
                idempotencyKey,
                lastChanged,
                lastChangedBy
            );

            return new PreparedMutationWork(
                commit,
                [.. _fileScanCandidates],
                [.. _postCommitBlobCleanupDataElements]
            );
        }
    }

    private sealed record FileScanCandidate(
        DataElementInternal DataElement,
        DataType DataType,
        DateTimeOffset BlobTimestamp
    );

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
        VersionPreconditions preconditions
    )
    {
        DateTime deletedTime = DateTime.UtcNow;

        DeleteStatus deleteStatus = new() { IsHardDeleted = true, HardDeleted = deletedTime };

        DataElementWriteResult updatedDataElementResult;
        try
        {
            updatedDataElementResult = await _dataRepository.Update(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object>()
                {
                    { "/deleteStatus", deleteStatus },
                    { "/lastChanged", deletedTime },
                    { "/lastChangedBy", dataElement.LastChangedBy },
                },
                new DataElementUpdateContext
                {
                    ExpectedInstanceVersion = preconditions.InstanceVersion,
                    ExpectedProcessStateVersion = preconditions.ProcessStateVersion,
                }
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

        await _instanceEventService.DispatchEvent(InstanceEventType.Deleted, instance, dataElement);
        VersionPreconditionHelper.WriteVersionResponseHeaders(
            Response,
            updatedDataElementResult.Versions
        );
        return Ok(updatedDataElementResult.DataElement.ToApiModel());
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
            instanceGuid.ToString(),
            dataGuid.ToString()
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
            instanceGuid.ToString(),
            blobVersionId
        );
        return string.Equals(blobStoragePath, versionedBlobStoragePath, StringComparison.Ordinal);
    }

    private (string BlobVersionId, ActionResult Error) TryGetIfMatchBlobVersion()
    {
        if (!Request.Headers.ContainsKey(HeaderNames.IfMatch))
        {
            return (null, null);
        }

        if (
            !EntityTagHeaderValue.TryParseList(
                Request.Headers[HeaderNames.IfMatch].ToArray(),
                out IList<EntityTagHeaderValue> ifMatch
            )
            || ifMatch.Count != 1
            || ifMatch[0].IsWeak
            || ifMatch[0].Equals(EntityTagHeaderValue.Any)
        )
        {
            return (null, BadRequest("If-Match must contain exactly one strong ETag."));
        }

        string blobVersionId = ifMatch[0].Tag.Value[1..^1];
        try
        {
            BlobVersionId.Decode(blobVersionId);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return (null, BadRequest("If-Match ETag value must be a blob version id."));
        }

        return (blobVersionId, null);
    }

    private void SetBlobVersionETag(string blobVersionId)
    {
        if (string.IsNullOrEmpty(blobVersionId))
        {
            return;
        }

        Response.Headers[HeaderNames.ETag] = new EntityTagHeaderValue(
            $"\"{blobVersionId}\""
        ).ToString();
    }
}
