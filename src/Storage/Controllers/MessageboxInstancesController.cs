using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.OpenApi;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Implements endpoints specifically for the Altinn II message box.
/// </summary>
[Route("storage/api/v1/sbl/instances")]
[ApiController]
[ExcludeFromPublicStorageApi]
public class MessageBoxInstancesController : ControllerBase
{
    private readonly IInstanceRepository _instanceRepository;
    private readonly IInstanceEventRepository _instanceEventRepository;
    private readonly ITextRepository _textRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IAuthorization _authorizationService;
    private readonly IApplicationService _applicationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageBoxInstancesController"/> class
    /// </summary>
    /// <param name="instanceRepository">the instance repository handler</param>
    /// <param name="instanceEventRepository">the instance event repository handler</param>
    /// <param name="textRepository">the text repository handler</param>
    /// <param name="applicationRepository">the application repository handler</param>
    /// <param name="authorizationService">the authorization service</param>
    /// <param name="applicationService">the application service</param>
    public MessageBoxInstancesController(
        IInstanceRepository instanceRepository,
        IInstanceEventRepository instanceEventRepository,
        ITextRepository textRepository,
        IApplicationRepository applicationRepository,
        IAuthorization authorizationService,
        IApplicationService applicationService
    )
    {
        _instanceRepository = instanceRepository;
        _instanceEventRepository = instanceEventRepository;
        _textRepository = textRepository;
        _applicationRepository = applicationRepository;
        _authorizationService = authorizationService;
        _applicationService = applicationService;
    }

    /// <summary>
    /// Gets all instances in a given state for a given instance owner.
    /// </summary>
    /// <param name="instanceOwnerPartyId">the instance owner id</param>
    /// <param name="instanceGuid">the instance guid</param>
    /// <param name="language"> language id en, nb, nn-NO"</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>list of instances</returns>
    [Authorize]
    [HttpGet("{instanceOwnerPartyId:int}/{instanceGuid:guid}")]
    public async Task<ActionResult> GetMessageBoxInstance(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        [FromQuery] string? language,
        CancellationToken cancellationToken
    )
    {
        string[] acceptedLanguages = { "en", "nb", "nn" };
        string languageId = "nb";

        if (language != null && acceptedLanguages.Contains(language.ToLower()))
        {
            languageId = language;
        }

        InstanceInternal? instance = await _instanceRepository.GetOne(
            instanceGuid,
            false,
            cancellationToken
        );

        if (instance == null)
        {
            return NotFound($"Could not find instance {instanceOwnerPartyId}/{instanceGuid}");
        }

        bool includeInstantiate = false;
        if (instance.Status.IsArchived)
        {
            var application = await _applicationRepository.FindOne(
                instance.AppId,
                instance.AppId.Split("/")[0],
                cancellationToken
            );
            includeInstantiate = application?.CopyInstanceSettings?.Enabled == true;
        }

        List<MessageBoxInstance> authorizedInstanceList =
            await _authorizationService.AuthorizeMesseageBoxInstances(
                [instance],
                includeInstantiate
            );
        if (authorizedInstanceList.Count <= 0)
        {
            return Forbid();
        }

        MessageBoxInstance authorizedInstance = authorizedInstanceList[0];

        // get app texts and exchange all text keys.
        List<TextResource> texts = await _textRepository.Get(
            new List<string> { instance.AppId },
            languageId
        );
        InstanceHelper.ReplaceTextKeys(
            new List<MessageBoxInstance> { authorizedInstance },
            texts,
            languageId
        );

        return Ok(authorizedInstance);
    }

    /// <summary>
    /// Gets all instances in a given state for a given instance owner.
    /// </summary>
    /// <param name="instanceOwnerPartyId">the instance owner id</param>
    /// <param name="instanceGuid">the instance guid</param>
    /// <returns>list of instances</returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_READ)]
    [HttpGet("{instanceOwnerPartyId:int}/{instanceGuid:guid}/events")]
    public async Task<ActionResult> GetMessageBoxInstanceEvents(
        [FromRoute] int instanceOwnerPartyId,
        [FromRoute] Guid instanceGuid
    )
    {
        string[] eventTypes =
        {
            InstanceEventType.Created.ToString(),
            InstanceEventType.Deleted.ToString(),
            InstanceEventType.Saved.ToString(),
            InstanceEventType.Submited.ToString(),
            InstanceEventType.Undeleted.ToString(),
            InstanceEventType.SubstatusUpdated.ToString(),
            InstanceEventType.Signed.ToString(),
            InstanceEventType.SentToSign.ToString(),
            InstanceEventType.SentToPayment.ToString(),
            InstanceEventType.SentToSendIn.ToString(),
            InstanceEventType.SentToFormFill.ToString(),
            InstanceEventType.InstanceForwarded.ToString(),
            InstanceEventType.InstanceRightRevoked.ToString(),
            InstanceEventType.NotificationSentSms.ToString(),
            InstanceEventType.MessageArchived.ToString(),
            InstanceEventType.MessageRead.ToString(),
        };

        List<InstanceEvent> allInstanceEvents = await _instanceEventRepository.ListInstanceEvents(
            instanceGuid,
            eventTypes,
            null,
            null
        );

        List<InstanceEvent> filteredInstanceEvents = InstanceEventHelper.RemoveDuplicateEvents(
            allInstanceEvents
        );

        return Ok(InstanceHelper.ConvertToSBLInstanceEvent(filteredInstanceEvents));
    }

    /// <summary>
    /// Marks an instance for deletion in storage.
    /// </summary>
    /// <param name="instanceGuid">instance id</param>
    /// <param name="instanceOwnerPartyId">instance owner</param>
    /// <param name="hard">if true is marked for hard delete.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>true if instance was successfully deleted</returns>
    /// DELETE /instances/{instanceId}?instanceOwnerPartyId={instanceOwnerPartyId}?hard={bool}
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_DELETE)]
    [HttpDelete("{instanceOwnerPartyId:int}/{instanceGuid:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(
        Guid instanceGuid,
        int instanceOwnerPartyId,
        bool hard,
        CancellationToken cancellationToken
    )
    {
        string instanceId = $"{instanceOwnerPartyId}/{instanceGuid}";

        InstanceInternal? instance = await _instanceRepository.GetOne(
            instanceGuid,
            false,
            cancellationToken
        );
        if (instance == null)
        {
            return NotFound(
                $"Didn't find the object that should be deleted with instanceId={instanceId}"
            );
        }

        instance.Status ??= new InstanceStatus();

        (Application appInfo, ServiceError appInfoError) =
            await _applicationService.GetApplicationOrErrorAsync(instance.AppId);

        if (appInfoError != null)
        {
            return appInfoError.ErrorCode switch
            {
                404 => NotFound(appInfoError.ErrorMessage),
                _ => StatusCode(appInfoError.ErrorCode, appInfoError.ErrorMessage),
            };
        }

        if (InstanceHelper.IsPreventedFromDeletion(instance.Status, appInfo))
        {
            return StatusCode(
                403,
                "Instance cannot be deleted yet due to application restrictions."
            );
        }

        DateTime now = DateTime.UtcNow;

        List<string> updateProperties = [];
        updateProperties.Add(nameof(instance.Status));
        updateProperties.Add(nameof(instance.Status.IsSoftDeleted));
        updateProperties.Add(nameof(instance.Status.SoftDeleted));
        if (hard)
        {
            instance.Status.IsHardDeleted = true;
            instance.Status.IsSoftDeleted = true;
            instance.Status.HardDeleted = now;
            instance.Status.SoftDeleted ??= now;
            updateProperties.Add(nameof(instance.Status.IsHardDeleted));
            updateProperties.Add(nameof(instance.Status.HardDeleted));
        }
        else
        {
            instance.Status.IsSoftDeleted = true;
            instance.Status.SoftDeleted = now;
        }

        instance.LastChangedBy = User.GetUserOrOrgNo();
        instance.LastChanged = now;
        updateProperties.Add(nameof(instance.LastChanged));
        updateProperties.Add(nameof(instance.LastChangedBy));

        InstanceEvent instanceEvent = new InstanceEvent
        {
            Created = DateTime.UtcNow,
            EventType = InstanceEventType.Deleted.ToString(),
            InstanceId = $"{instance.InstanceOwner.PartyId}/{instance.Id}",
            InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
            User = new PlatformUser
            {
                UserId = User.GetUserId(),
                AuthenticationLevel = User.GetAuthenticationLevel(),
                OrgId = User.GetOrg(),
                SystemUserId = User.GetSystemUserId(),
                SystemUserOwnerOrgNo = User.GetSystemUserOwner(),
            },
        };

        try
        {
            await _instanceRepository.Update(
                instance,
                updateProperties,
                cancellationToken: cancellationToken
            );
            await _instanceEventRepository.InsertInstanceEvent(instanceEvent, instance);
        }
        catch (ProcessStatusConflictException e)
        {
            return Conflict(e.Message);
        }

        return Ok(true);
    }
}
