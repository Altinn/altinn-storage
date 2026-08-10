#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Altinn.Platform.Storage.Authorization;
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
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// API for committing a batch of mutations for a single instance
/// </summary>
[Route("storage/api/v1/instances/{instanceOwnerPartyId:int}/{instanceGuid:guid}/mutations")]
[ApiController]
public class InstanceMutationsController : ControllerBase
{
    private const long RequestSizeLimit = 2000 * 1024 * 1024;

    /// <summary>
    /// Maximum size of the mutation JSON document, whether sent as the multipart
    /// <c>mutation</c> section or as a plain <c>application/json</c> body. The document is
    /// buffered in memory before deserialization, so it must stay bounded independently of
    /// <see cref="RequestSizeLimit"/>.
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
    private readonly string _storageBaseAndHost;
    private readonly IAuthorization _authorizationService;
    private readonly IAuthorizationService _policyAuthorizationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceMutationsController"/> class
    /// </summary>
    /// <param name="dataRepository">the data repository handler</param>
    /// <param name="blobRepository">the blob repository handler</param>
    /// <param name="instanceRepository">the instance repository</param>
    /// <param name="instanceMutationRepository">the aggregate instance mutation repository.</param>
    /// <param name="applicationRepository">the application repository</param>
    /// <param name="dataService">A data service with data element related business logic.</param>
    /// <param name="instanceEventService">An instance event service with event related business logic.</param>
    /// <param name="generalSettings">the general settings.</param>
    /// <param name="authorizationService">The authorization service</param>
    /// <param name="policyAuthorizationService">The ASP.NET Core policy authorization service.</param>
    /// <param name="processAuthorizer">The process-state authorizer.</param>
    public InstanceMutationsController(
        IDataRepository dataRepository,
        IBlobRepository blobRepository,
        IInstanceRepository instanceRepository,
        IInstanceMutationRepository instanceMutationRepository,
        IApplicationRepository applicationRepository,
        IDataService dataService,
        IInstanceEventService instanceEventService,
        IOptions<GeneralSettings> generalSettings,
        IAuthorization authorizationService,
        IAuthorizationService policyAuthorizationService,
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
        _authorizationService = authorizationService;
        _policyAuthorizationService = policyAuthorizationService;
        _processAuthorizer = processAuthorizer;
    }

    /// <summary>
    /// Commits a batch of mutations for a single instance.
    /// For multipart requests, the first part must be the <c>mutation</c> JSON field;
    /// each subsequent part must match exactly one <c>contentPartName</c> in the request.
    /// Unknown, duplicate, or missing file parts are rejected with 400 Bad Request.
    /// </summary>
    /// <remarks>
    /// After the endpoint's outer <c>InstanceWrite</c> policy admits the request, idempotent replay
    /// is checked before process-state, presentation-text, data-value, per-data-type write,
    /// complete-confirmation, and delete-instance authorization. A complete confirmation is
    /// additionally subject to the <c>InstanceComplete</c> policy and is recorded for the calling
    /// organisation only; an organisation that already has a confirmation keeps the one it has, and
    /// the remaining operations commit either way. An admitted replay is a no-op and uses the snapshot
    /// returned by replay admission. For non-replays, operation-specific authorization is evaluated
    /// against the controller's instance snapshot; data-element update and delete references missing
    /// from that snapshot are rejected by later plan validation. Process-state mutations on instances
    /// without a current task, and delete-instance mutations the application prevents from deletion,
    /// are rejected after replay admission. Delete-instance mutations check instance existence before
    /// delete authorization, so a missing instance returns 404 before a possible delete-policy 403.
    /// </remarks>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that should be mutated.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="ifInstanceVersionMatch">Optional expected aggregate instance version.</param>
    /// <param name="ifProcessStateVersionMatch">Optional expected process-state version.</param>
    /// <param name="idempotencyKeyHeader">Optional idempotency key. Requires an expected instance version.</param>
    /// <returns>The updated instance, including current blob version ids on its data elements.</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpPost]
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
        CancellationToken cancellationToken,
        [FromHeader(Name = StorageHeaders.IfInstanceVersionMatch)]
            string ifInstanceVersionMatch = null,
        [FromHeader(Name = StorageHeaders.IfProcessStateVersionMatch)]
            string ifProcessStateVersionMatch = null,
        [FromHeader(Name = StorageHeaders.IdempotencyKey)] string idempotencyKeyHeader = null
    )
    {
        (VersionPreconditions preconditions, ActionResult preconditionError) =
            VersionPreconditionHelper.TryParse(ifInstanceVersionMatch, ifProcessStateVersionMatch);
        if (preconditionError is not null)
        {
            return preconditionError;
        }

        (Guid? idempotencyKey, ActionResult idempotencyKeyError) = TryReadMutationIdempotencyKey(
            idempotencyKeyHeader,
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

        BadRequestObjectResult deleteInstanceRequestError = ValidateDeleteInstanceRequest(
            mutationRequest,
            preconditions,
            idempotencyKey
        );
        if (deleteInstanceRequestError is not null)
        {
            return deleteInstanceRequestError;
        }

        BadRequestObjectResult instanceEventError = ValidateMutationInstanceEvents(
            mutationRequest,
            instanceOwnerPartyId,
            instanceGuid
        );
        if (instanceEventError is not null)
        {
            return instanceEventError;
        }

        (InstanceInternal instance, ActionResult instanceError) = await GetInstanceAsync(
            instanceGuid,
            instanceOwnerPartyId,
            true,
            cancellationToken
        );
        if (instanceError is not null)
        {
            return instanceError;
        }

        StorageVersions snapshotVersions = instance.Versions;

        (Application application, ActionResult applicationError) = await GetApplicationAsync(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        if (application is null)
        {
            return applicationError;
        }

        Dictionary<Guid, DataElementInternal> existingDataElementsById = instance.Data.ToDictionary(
            e => e.Id,
            e => e
        );

        ActionResult<InstanceMutationResponse> replayResponse =
            await TryBuildReplayMutationResponse(
                instanceGuid,
                snapshotVersions,
                preconditions,
                idempotencyKey,
                cancellationToken
            );
        if (replayResponse is not null)
        {
            return replayResponse;
        }

        if (
            preconditions.InstanceVersion is not null
            && preconditions.InstanceVersion != snapshotVersions.InstanceVersion
        )
        {
            return VersionPreconditionHelper.VersionMismatch(
                Response,
                new InstanceVersionMismatchException(
                    snapshotVersions.InstanceVersion,
                    snapshotVersions.ProcessStateVersion
                )
            );
        }

        if (
            preconditions.ProcessStateVersion is not null
            && preconditions.ProcessStateVersion != snapshotVersions.ProcessStateVersion
        )
        {
            return VersionPreconditionHelper.VersionMismatch(
                Response,
                new ProcessStateVersionMismatchException(
                    snapshotVersions.InstanceVersion,
                    snapshotVersions.ProcessStateVersion
                )
            );
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

        if (mutationRequest.DeleteInstance is not null)
        {
            instance.Status ??= new InstanceStatus();
            if (InstanceHelper.IsPreventedFromDeletion(instance.Status, application))
            {
                return StatusCode(
                    403,
                    "Instance cannot be deleted yet due to application restrictions."
                );
            }
        }

        (AppliedMutationWork appliedMutation, ActionResult mutationError) =
            await PrepareAndApplyMutation(
                mutationRequest,
                multipartReader,
                instance,
                existingDataElementsById,
                application,
                preconditions,
                snapshotVersions.ProcessStateVersion,
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
        StorageVersions currentVersions,
        VersionPreconditions preconditions,
        Guid? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        if (
            idempotencyKey is null
            || preconditions.InstanceVersion is null
            || preconditions.InstanceVersion.Value == currentVersions.InstanceVersion
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
                    currentVersions.InstanceVersion,
                    currentVersions.ProcessStateVersion,
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

    private BadRequestObjectResult ValidateMutationInstanceEvents(
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

            instanceEvent.Created ??= DateTime.UtcNow;
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
        ActionResult deleteInstanceAuthorizationError = await AuthorizeDeleteInstanceMutation(
            mutationRequest
        );
        if (deleteInstanceAuthorizationError is not null)
        {
            return deleteInstanceAuthorizationError;
        }

        ActionResult completeConfirmationAuthorizationError =
            await AuthorizeCompleteConfirmationMutation(mutationRequest);
        if (completeConfirmationAuthorizationError is not null)
        {
            return completeConfirmationAuthorizationError;
        }

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

            if (!await dataType.CanWrite(_authorizationService, instance))
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
            InstanceMutationCreateDataElement create in (
                mutationRequest.CreateDataElements ?? []
            ).Where(create => !string.IsNullOrWhiteSpace(create.DataType))
        )
        {
            yield return create.DataType;
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
        InstanceInternal instance,
        Dictionary<Guid, DataElementInternal> existingDataElementsById,
        Application application,
        VersionPreconditions preconditions,
        int snapshotProcessStateVersion,
        Guid? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        ValidatedMutationPlan plan = new();
        BlobStagingScope blobStaging = new();
        bool applyAttempted = false;

        try
        {
            ProcessStatusHelper.EnsureExpectedStatus(
                instance,
                mutationRequest.ExpectedProcessStatus
            );

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
                instance,
                mutationUpdates,
                preconditions,
                snapshotProcessStateVersion,
                idempotencyKey,
                stagedByPartName
            );

            applyAttempted = true;
            InstanceMutationApplyResult applyResult = await _instanceMutationRepository.Apply(
                instance.Id,
                instance.InternalId,
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
        catch (ProcessStatusConflictException exception)
        {
            await CleanupStagedBlobs(blobStaging);
            return (
                null,
                new JsonResult(
                    new ProblemDetails
                    {
                        Detail = exception.Message,
                        Status = StatusCodes.Status409Conflict,
                        Title = "Process status conflict",
                        Type = "process_status_conflict",
                    }
                )
                {
                    ContentType = "application/problem+json",
                    StatusCode = StatusCodes.Status409Conflict,
                }
            );
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
        BadRequestObjectResult duplicateDataElementIdError =
            ValidateDuplicateDataElementMutationIds(mutationRequest);
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

    private BadRequestObjectResult ValidateDuplicateDataElementMutationIds(
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
        if (mutationRequest.DeleteInstance is not null)
        {
            instanceStatus = BuildHardDeleteStatus(instance.Status, lastChanged);
            instanceUpdateProperties.Add(nameof(InstanceInternal.Status));
            instanceUpdateProperties.Add(nameof(InstanceStatus.IsSoftDeleted));
            instanceUpdateProperties.Add(nameof(InstanceStatus.SoftDeleted));
            instanceUpdateProperties.Add(nameof(InstanceStatus.IsHardDeleted));
            instanceUpdateProperties.Add(nameof(InstanceStatus.HardDeleted));
        }

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

        List<CompleteConfirmation> addedCompleteConfirmations = null;
        if (mutationRequest.AddCompleteConfirmation)
        {
            addedCompleteConfirmations =
            [
                new CompleteConfirmation
                {
                    StakeholderId = User.GetOrg(),
                    ConfirmedOn = lastChanged,
                },
            ];
            instanceUpdateProperties.Add(nameof(InstanceInternal.CompleteConfirmations));
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
            CompleteConfirmations = addedCompleteConfirmations,
            LastChanged = lastChanged,
            LastChangedBy = lastChangedBy,
            PresentationTexts = mutationRequest.PresentationTexts,
            DataValues = mutationRequest.DataValues,
        };

        if (mutationRequest.DeleteInstance is not null)
        {
            instanceEvents.Add(
                _instanceEventService.BuildInstanceEvent(InstanceEventType.Deleted, instanceUpdates)
            );
        }

        if (addedCompleteConfirmations is not null)
        {
            instanceEvents.Add(
                _instanceEventService.BuildInstanceEvent(
                    InstanceEventType.ConfirmedComplete,
                    instanceUpdates
                )
            );
        }

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
        InstanceInternal instance,
        DateTime lastChanged,
        string lastChangedBy,
        PreparedMutationWorkBuilder work
    )
    {
        if (plan.CreateDataElements.Count == 0)
        {
            return;
        }

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
                Id = plannedCreate.DataElementId,
                InstanceGuid = instance.Id,
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
                    out DataElementInternal dataElement
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
                ConflictObjectResult contentError = PlanUpdateContent(update, dataElement);
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
                    dataElement,
                    dataType,
                    propertyList,
                    expectedCurrentBlobVersion,
                    hasContentUpdate
                )
            );
        }

        return null;
    }

    private static ConflictObjectResult PlanUpdateContent(
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
                    out DataElementInternal dataElement
                )
            )
            {
                return NotFound(
                    $"Unable to find any data element with id: {delete.DataElementId}."
                );
            }

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
                new PlannedDeleteDataElement(dataElement, delete.IgnoreLock)
            );
        }

        return null;
    }

    private PreparedMutationWork PrepareMutationWork(
        ValidatedMutationPlan plan,
        InstanceInternal instance,
        MutationInstanceUpdates mutationUpdates,
        VersionPreconditions preconditions,
        int snapshotProcessStateVersion,
        Guid? idempotencyKey,
        IReadOnlyDictionary<string, StagedFileContent> stagedByPartName
    )
    {
        PreparedMutationWorkBuilder work = new();

        BuildCreatedDataElements(
            plan,
            stagedByPartName,
            instance,
            mutationUpdates.LastChanged,
            mutationUpdates.LastChangedBy,
            work
        );
        BuildUpdatedDataElements(plan, instance, stagedByPartName, work);

        foreach (PlannedDeleteDataElement plannedDelete in plan.DeleteDataElements)
        {
            DataElementInternal dataElement = plannedDelete.ExistingDataElement;
            dataElement.LastChanged = mutationUpdates.LastChanged;
            dataElement.LastChangedBy = mutationUpdates.LastChangedBy;
            work.AddDeletedDataElement(plannedDelete.ExistingDataElement, plannedDelete.IgnoreLock);
        }

        return work.Build(
            mutationUpdates,
            preconditions,
            snapshotProcessStateVersion,
            idempotencyKey,
            (eventType, dataElement) =>
                _instanceEventService.BuildInstanceEvent(
                    eventType,
                    mutationUpdates.InstanceUpdates,
                    dataElement
                )
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
            }
        );
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

        foreach (
            string partName in plan.ExpectedFileParts.Keys.Where(partName =>
                !staged.ContainsKey(partName)
            )
        )
        {
            return (null, BadRequest($"No multipart file part named '{partName}' was supplied."));
        }

        return (staged, null);
    }

    private static bool HasMutationOperations(InstanceMutationRequest request) =>
        request.CreateDataElements?.Count > 0
        || request.UpdateDataElements?.Count > 0
        || request.DeleteDataElements?.Count > 0
        || request.DeleteInstance is not null
        || request.DataValues?.Count > 0
        || request.PresentationTexts?.Count > 0
        || request.AddCompleteConfirmation
        || request.ProcessState?.State is not null
        || request.ProcessState?.Events?.Count > 0;

    private BadRequestObjectResult ValidateDeleteInstanceRequest(
        InstanceMutationRequest request,
        VersionPreconditions preconditions,
        Guid? idempotencyKey
    )
    {
        if (request.DeleteInstance is null)
        {
            return null;
        }

        if (!request.DeleteInstance.Hard)
        {
            return BadRequest("deleteInstance.hard must be true.");
        }

        bool hasUnrelatedMutationOperations =
            request.CreateDataElements?.Count > 0
            || request.UpdateDataElements?.Count > 0
            || request.DataValues?.Count > 0
            || request.PresentationTexts?.Count > 0
            || request.AddCompleteConfirmation;
        bool isStandaloneDelete =
            !hasUnrelatedMutationOperations
            && request.DeleteDataElements?.Count is not > 0
            && request.ProcessState?.State is null
            && request.ProcessState?.Events?.Count is not > 0
            && request.ExpectedProcessStatus is null or ProcessStatus.Idle;
        bool isTerminalWorkflowDelete =
            !hasUnrelatedMutationOperations
            && IsTerminalWorkflowDeleteInstanceRequest(request, preconditions, idempotencyKey);
        if (!isStandaloneDelete && !isTerminalWorkflowDelete)
        {
            return BadRequest(
                "deleteInstance cannot be combined with other aggregate mutation operations."
            );
        }

        return null;
    }

    private static bool IsTerminalWorkflowDeleteInstanceRequest(
        InstanceMutationRequest request,
        VersionPreconditions preconditions,
        Guid? idempotencyKey
    )
    {
        // The process-ending workflow save is the only mixed hard-delete mutation. Its ended
        // process state, status handoff, version fences, and retry identity make it distinct
        // from an ordinary client combining deletion with unrelated aggregate operations.
        ProcessState processState = request.ProcessState?.State;
        return processState?.Ended is not null
            && processState.CurrentTask is null
            && !string.IsNullOrWhiteSpace(processState.EndEvent)
            && processState.Status == ProcessStatus.Idle
            && request.ExpectedProcessStatus == ProcessStatus.Processing
            && preconditions.InstanceVersion is not null
            && preconditions.ProcessStateVersion is not null
            && idempotencyKey is not null;
    }

    private async Task<ActionResult> AuthorizeDeleteInstanceMutation(
        InstanceMutationRequest request
    )
    {
        if (request.DeleteInstance is null)
        {
            return null;
        }

        AuthorizationResult authorizationResult = await _policyAuthorizationService.AuthorizeAsync(
            User,
            resource: null,
            policyName: AuthzConstants.POLICY_INSTANCE_DELETE
        );

        return authorizationResult.Succeeded ? null : Forbid();
    }

    private async Task<ActionResult> AuthorizeCompleteConfirmationMutation(
        InstanceMutationRequest request
    )
    {
        if (!request.AddCompleteConfirmation)
        {
            return null;
        }

        if (User.GetOrg() is null)
        {
            return Forbid();
        }

        AuthorizationResult authorizationResult = await _policyAuthorizationService.AuthorizeAsync(
            User,
            resource: null,
            policyName: AuthzConstants.POLICY_INSTANCE_COMPLETE
        );

        return authorizationResult.Succeeded ? null : Forbid();
    }

    private static InstanceStatus BuildHardDeleteStatus(InstanceStatus status, DateTime now)
    {
        status.IsHardDeleted = true;
        status.IsSoftDeleted = true;
        status.HardDeleted = now;
        status.SoftDeleted ??= now;
        return status;
    }

    private (Guid? IdempotencyKey, ActionResult Error) TryReadMutationIdempotencyKey(
        string idempotencyKeyHeader,
        VersionPreconditions preconditions
    )
    {
        if (string.IsNullOrWhiteSpace(idempotencyKeyHeader))
        {
            return (null, null);
        }

        if (!Guid.TryParse(idempotencyKeyHeader, out Guid parsedIdempotencyKey))
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
        string blobVersionId = await _dataRepository.CreateBlobVersionId(
            instance.Id,
            dataElementId,
            instance.AppId,
            instance.Org,
            application.StorageAccountNumber,
            cancellationToken
        );
        string versionedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            instance.AppId,
            instance.Id,
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
        if (!BlobVersionId.TryDecode(blobVersionId, out _))
        {
            return (
                null,
                BadRequest("expectedCurrentBlobVersion must identify a blob version id.")
            );
        }

        return (blobVersionId, null);
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

        return clone;
    }

    private static string FirstNonEmpty(string primary, string fallback) =>
        string.IsNullOrEmpty(primary) ? fallback : primary;

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
            _transactionalEvents.Add((InstanceEventType.Deleted, dataElement));
            _postCommitBlobCleanupDataElements.Add(dataElement);
        }

        public PreparedMutationWork Build(
            MutationInstanceUpdates mutationUpdates,
            VersionPreconditions preconditions,
            int snapshotProcessStateVersion,
            Guid? idempotencyKey,
            Func<InstanceEventType, DataElementInternal, InstanceEvent> buildInstanceEvent
        )
        {
            List<InstanceEvent> instanceEvents = [.. mutationUpdates.InstanceEvents];
            foreach (
                (
                    InstanceEventType eventType,
                    DataElementInternal dataElement
                ) in _transactionalEvents
            )
            {
                instanceEvents.Add(buildInstanceEvent(eventType, dataElement));
            }

            InstanceMutationCommit commit = new(
                _createDataElements,
                _updateDataElements,
                _deleteDataElements,
                mutationUpdates.InstanceUpdates,
                mutationUpdates.InstanceUpdateProperties,
                preconditions.InstanceVersion,
                snapshotProcessStateVersion,
                instanceEvents,
                idempotencyKey,
                mutationUpdates.LastChanged,
                mutationUpdates.LastChangedBy
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
}
