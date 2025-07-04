#nullable enable
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
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Wolverine;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints specifically for the Altinn II message box.
    /// </summary>
    [Route("storage/api/v1/sbl/instances")]
    [ApiController]
    public class MessageBoxInstancesController : ControllerBase
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IInstanceEventRepository _instanceEventRepository;
        private readonly ITextRepository _textRepository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly IAuthorization _authorizationService;
        private readonly IApplicationService _applicationService;
        private readonly IMessageBus _messageBus;
        private readonly WolverineSettings _wolverineSettings;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageBoxInstancesController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="instanceEventRepository">the instance event repository handler</param>
        /// <param name="textRepository">the text repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="authorizationService">the authorization service</param>
        /// <param name="applicationService">the application service</param>
        /// <param name="messageBus">Wolverines abstraction for sending messages</param>
        /// <param name="wolverineSettings">Wolverine settings</param>
        /// <param name="logger">The logger</param>
        public MessageBoxInstancesController(
            IInstanceRepository instanceRepository,
            IInstanceEventRepository instanceEventRepository,
            ITextRepository textRepository,
            IApplicationRepository applicationRepository,
            IAuthorization authorizationService,
            IApplicationService applicationService,
            IMessageBus messageBus,
            IOptions<WolverineSettings> wolverineSettings,
            ILogger<MessageBoxInstancesController> logger)
        {
            _instanceRepository = instanceRepository;
            _instanceEventRepository = instanceEventRepository;
            _textRepository = textRepository;
            _applicationRepository = applicationRepository;
            _authorizationService = authorizationService;
            _applicationService = applicationService;
            _messageBus = messageBus;
            _wolverineSettings = wolverineSettings.Value;
            _logger = logger;
        }

        /// <summary>
        /// Search through instances to find match based on query params.
        /// </summary>
        /// <param name="queryModel">Object with query-params</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>List of messagebox instances</returns>
        [Authorize]
        [HttpPost("search")]
        public async Task<ActionResult> SearchMessageBoxInstances([FromBody] MessageBoxQueryModel queryModel, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(queryModel.ArchiveReference))
            {
                if ((queryModel.IncludeActive == queryModel.IncludeArchived) && (queryModel.IncludeActive == queryModel.IncludeDeleted))
                {
                    queryModel.IncludeDeleted = true;
                    queryModel.IncludeArchived = true;
                }
                else if (queryModel.IncludeActive && !queryModel.IncludeArchived && !queryModel.IncludeDeleted)
                {
                    return Ok(new List<MessageBoxInstance>());
                }

                queryModel.IncludeActive = false;
            }

            try
            {
                InstanceQueryParameters queryParams = GetQueryParams(queryModel);
                GetStatusFromQueryParams(queryModel.IncludeActive, queryModel.IncludeArchived, queryModel.IncludeDeleted, queryParams);
                queryParams.Size = 5000;
                queryParams.IsHardDeleted = false;
                queryParams.SortBy = "desc:lastChanged";

                if (!string.IsNullOrEmpty(queryModel.SearchString))
                {
                    queryParams.AppIds = await MatchStringToAppTitle(queryModel.SearchString);
                }

                InstanceQueryResponse queryResponse = await _instanceRepository.GetInstancesFromQuery(queryParams, false, cancellationToken);

                AddQueryModelToTelemetry(queryModel);

                if (queryResponse?.Exception != null)
                {
                    return StatusCode(cancellationToken.IsCancellationRequested ? 499 : 500, queryResponse.Exception);
                }

                return await ProcessQueryResponse(queryResponse, queryModel.Language, cancellationToken);
            }
            catch (Exception e)
            {
                return StatusCode(cancellationToken.IsCancellationRequested ? 499 : 500, $"Unable to perform query on instances due to: {e.Message}");
            }
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
            [FromQuery] string language,
            CancellationToken cancellationToken)
        {
            string[] acceptedLanguages = { "en", "nb", "nn" };
            string languageId = "nb";

            if (language != null && acceptedLanguages.Contains(language.ToLower()))
            {
                languageId = language;
            }

            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, false, cancellationToken);

            if (instance == null)
            {
                return NotFound($"Could not find instance {instanceOwnerPartyId}/{instanceGuid}");
            }

            bool includeInstantiate = false;
            if (instance.Status.IsArchived)
            {
                var application = await _applicationRepository.FindOne(instance.AppId, instance.AppId.Split("/")[0]);
                includeInstantiate = application?.CopyInstanceSettings?.Enabled == true;
            }

            List<MessageBoxInstance> authorizedInstanceList =
                await _authorizationService.AuthorizeMesseageBoxInstances(new List<Instance> { instance }, includeInstantiate);
            if (authorizedInstanceList.Count <= 0)
            {
                return Forbid();
            }

            MessageBoxInstance authorizedInstance = authorizedInstanceList[0];

            // get app texts and exchange all text keys.
            List<TextResource> texts = await _textRepository.Get(new List<string> { instance.AppId }, languageId);
            InstanceHelper.ReplaceTextKeys(new List<MessageBoxInstance> { authorizedInstance }, texts, languageId);

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
            [FromRoute] Guid instanceGuid)
        {
            string instanceId = $"{instanceOwnerPartyId}/{instanceGuid}";
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

            if (string.IsNullOrEmpty(instanceId))
            {
                return BadRequest("Unable to perform query.");
            }

            List<InstanceEvent> allInstanceEvents =
                await _instanceEventRepository.ListInstanceEvents(instanceId, eventTypes, null, null);

            List<InstanceEvent> filteredInstanceEvents = InstanceEventHelper.RemoveDuplicateEvents(allInstanceEvents);

            return Ok(InstanceHelper.ConvertToSBLInstanceEvent(filteredInstanceEvents));
        }

        /// <summary>
        /// Restore a soft deleted instance
        /// </summary>
        /// <param name="instanceOwnerPartyId">instance owner</param>
        /// <param name="instanceGuid">instance id</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>True if the instance was restored.</returns>
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_DELETE)]
        [HttpPut("{instanceOwnerPartyId:int}/{instanceGuid:guid}/undelete")]
        public async Task<ActionResult> Undelete(int instanceOwnerPartyId, Guid instanceGuid, CancellationToken cancellationToken)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, false, cancellationToken);

            if (instance == null)
            {
                return NotFound($"Didn't find the object that should be restored with instanceId={instanceOwnerPartyId}/{instanceGuid}");
            }

            if (instance.Status.IsHardDeleted)
            {
                return NotFound("Instance was permanently deleted and cannot be restored.");
            }
            else if (instance.Status.IsSoftDeleted)
            {
                List<string> updateProperties = [];
                updateProperties.Add(nameof(instance.LastChanged));
                updateProperties.Add(nameof(instance.LastChangedBy));
                updateProperties.Add(nameof(instance.Status));
                updateProperties.Add(nameof(instance.Status.IsSoftDeleted));
                updateProperties.Add(nameof(instance.Status.SoftDeleted));
                instance.LastChangedBy = User.GetUserOrOrgNo();
                instance.LastChanged = DateTime.UtcNow;
                instance.Status.IsSoftDeleted = false;
                instance.Status.SoftDeleted = null;

                InstanceEvent instanceEvent = new InstanceEvent
                {
                    Created = DateTime.UtcNow,
                    EventType = InstanceEventType.Undeleted.ToString(),
                    InstanceId = instance.Id,
                    InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                    User = new PlatformUser
                    {
                        UserId = User.GetUserId(),
                        AuthenticationLevel = User.GetAuthenticationLevel(),
                        OrgId = User.GetOrg(),
                        SystemUserId = User.GetSystemUserId(),
                        SystemUserOwnerOrgNo = User.GetSystemUserOwner(),
                    }
                };

                await _instanceRepository.Update(instance, updateProperties, cancellationToken);
                await _instanceEventRepository.InsertInstanceEvent(instanceEvent);

                if (_wolverineSettings.EnableSending)
                {
                    try
                    {
                        using Activity? activity = Activity.Current?.Source.StartActivity("WolverineUndelete");
                        SyncInstanceToDialogportenCommand instanceUpdateCommand = new(
                            instance.AppId,
                            instance.InstanceOwner.PartyId, 
                            instanceEvent.InstanceId.Split("/")[1], 
                            instance.Created!.Value,
                            false);
                        await _messageBus.PublishAsync(instanceUpdateCommand);
                    }
                    catch (Exception ex)
                    {
                        // Log the error but do not return an error to the user
                        _logger.LogError(ex, "Failed to publish instance update command for instance {InstanceId}", instanceEvent.InstanceId);
                    }
                }

                return Ok(true);
            }

            return Ok(true);
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
        public async Task<ActionResult> Delete(Guid instanceGuid, int instanceOwnerPartyId, bool hard, CancellationToken cancellationToken)
        {
            string instanceId = $"{instanceOwnerPartyId}/{instanceGuid}";

            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, false, cancellationToken);
            if (instance == null)
            {
                return NotFound($"Didn't find the object that should be deleted with instanceId={instanceId}");
            }

            instance.Status ??= new InstanceStatus();

            (Application appInfo, ServiceError appInfoError) = await _applicationService.GetApplicationOrErrorAsync(instance.AppId);

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
                return StatusCode(403, "Instance cannot be deleted yet due to application restrictions.");
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
                InstanceId = instance.Id,
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

            await _instanceRepository.Update(instance, updateProperties, cancellationToken);
            await _instanceEventRepository.InsertInstanceEvent(instanceEvent);

            if (_wolverineSettings.EnableSending)
            {
                try
                {
                    using Activity? activity = Activity.Current?.Source.StartActivity("WolverineDelete");
                    SyncInstanceToDialogportenCommand instanceUpdateCommand = new(
                        instance.AppId, 
                        instance.InstanceOwner.PartyId,
                        instanceGuid.ToString(), // Instance.Id is NOT in the format "partyId/instanceGuid"
                        instance.Created!.Value,
                        false);
                    await _messageBus.PublishAsync(instanceUpdateCommand);
                }
                catch (Exception ex)
                {
                    // Log the error but do not return an error to the user
                    _logger.LogError(ex, "Failed to publish instance update command for instance {InstanceId}", instanceId);
                }
            }
            
            return Ok(true);
        }

        private async Task<StringValues> MatchStringToAppTitle(string searchString)
        {
            List<string> appIds = new List<string>();

            Dictionary<string, string> appTitles = await _applicationRepository.GetAllAppTitles();
            appIds.AddRange(appTitles.Where(entry => entry.Value.Contains(searchString.Trim(), StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Key));
            return new StringValues(appIds.ToArray());
        }

        private static void GetStatusFromQueryParams(
           bool includeActive,
           bool includeArchived,
           bool includeDeleted,
           InstanceQueryParameters queryParams)
        {
            if ((includeActive == includeArchived) && (includeActive == includeDeleted))
            {
                // no filter required
            }
            else if (!includeArchived && !includeDeleted)
            {
                queryParams.IsArchived = false;
                queryParams.IsSoftDeleted = false;
            }
            else if (!includeActive && !includeDeleted)
            {
                queryParams.IsArchived = true;
                queryParams.IsSoftDeleted = false;
            }
            else if (!includeActive && !includeArchived)
            {
                queryParams.IsSoftDeleted = true;
            }
            else if (includeActive && includeArchived)
            {
                queryParams.IsSoftDeleted = false;
            }
            else if (includeArchived)
            {
                queryParams.IsArchivedOrSoftDeleted = true;
            }
            else
            {
                queryParams.IsActiveOrSoftDeleted = true;
            }
        }

        private async Task RemoveHiddenInstances(List<Instance> instances)
        {
            List<string> appIds = instances.Select(i => i.AppId).Distinct().ToList();
            Dictionary<string, Application> apps = new();

            foreach (string id in appIds)
            {
                apps.Add(id, await _applicationRepository.FindOne(id, id.Split("/")[0]));
            }

            instances.RemoveAll(i => i.VisibleAfter > DateTime.UtcNow);

            if (apps.Count > 0)
            {
                InstanceHelper.RemoveHiddenInstances(apps, instances);
            }
        }

        private static InstanceQueryParameters GetQueryParams(MessageBoxQueryModel queryModel)
        {
            string dateTimeFormat = "yyyy-MM-ddTHH:mm:ss";

            InstanceQueryParameters queryParams = new();
            if (queryModel.FromLastChanged != null || queryModel.ToLastChanged != null)
            {
                queryParams.LastChanged = new string[(queryModel.FromLastChanged == null || queryModel.ToLastChanged == null) ? 1 : 2];
            }

            if (queryModel.InstanceOwnerPartyIdList.Count == 1)
            {
                queryParams.InstanceOwnerPartyId = queryModel.InstanceOwnerPartyIdList.First();
            }
            else
            {
                queryParams.InstanceOwnerPartyIds = queryModel.InstanceOwnerPartyIdList.Cast<int?>().ToArray();
            }

            if (!string.IsNullOrEmpty(queryModel.AppId))
            {
                queryParams.AppId = queryModel.AppId;
            }

            if (queryModel.FromLastChanged != null)
            {
                queryParams.LastChanged[0] = $"gte:{queryModel.FromLastChanged?.ToString(dateTimeFormat, CultureInfo.InvariantCulture)}";
            }

            if (queryModel.ToLastChanged != null)
            {
                queryParams.LastChanged[queryModel.FromLastChanged != null ? 1 : 0] = $"lte:{queryModel.ToLastChanged?.ToString(dateTimeFormat, CultureInfo.InvariantCulture)}";
            }

            if (queryModel.FromCreated != null)
            {
                queryParams.MsgBoxInterval = [$"gte:{queryModel.FromCreated?.ToString(dateTimeFormat, CultureInfo.InvariantCulture)}"];
            }

            if (queryModel.ToCreated != null)
            {
                if (queryParams.MsgBoxInterval == null || queryParams.MsgBoxInterval.Length == 0)
                {
                    queryParams.MsgBoxInterval = [$"lte:{queryModel.ToCreated?.ToString(dateTimeFormat, CultureInfo.InvariantCulture)}"];
                }
                else
                {
                    queryParams.MsgBoxInterval = queryParams.MsgBoxInterval.Concat([$"lte:{queryModel.ToCreated?.ToString(dateTimeFormat, CultureInfo.InvariantCulture)}"]).ToArray();
                }
            }

            if (!string.IsNullOrEmpty(queryModel.SearchString))
            {
                queryParams.SearchString = queryModel.SearchString;
            }

            if (!string.IsNullOrEmpty(queryModel.ArchiveReference))
            {
                queryParams.ArchiveReference = queryModel.ArchiveReference;
            }

            if (queryModel.FilterMigrated ?? false)
            {
                queryParams.MainVersionInclude = 3;
            }

            return queryParams;
        }

        private async Task<ActionResult> ProcessQueryResponse(InstanceQueryResponse? queryResponse, string language, CancellationToken cancellationToken)
        {
            string[] acceptedLanguages = { "en", "nb", "nn" };

            string languageId = "nb";

            if (language != null && acceptedLanguages.Contains(language.ToLower()))
            {
                languageId = language.ToLower();
            }

            if (queryResponse == null || queryResponse.Count <= 0)
            {
                return Ok(new List<MessageBoxInstance>());
            }

            List<Instance> allInstances = queryResponse.Instances;
            await RemoveHiddenInstances(allInstances);

            if (allInstances.Count == 0)
            {
                return Ok(new List<MessageBoxInstance>());
            }

            allInstances.ForEach(i =>
            {
                if (i.Status.IsArchived || i.Status.IsSoftDeleted)
                {
                    i.DueBefore = null;
                }
            });

            if (cancellationToken.IsCancellationRequested)
            {
                return StatusCode(499, "Request was cancelled.");
            }

            List<MessageBoxInstance> authorizedInstances =
                await _authorizationService.AuthorizeMesseageBoxInstances(allInstances, false);

            if (authorizedInstances.Count == 0)
            {
                return Ok(new List<MessageBoxInstance>());
            }

            List<string> appIds = authorizedInstances.Select(i => InstanceHelper.GetAppId(i)).Distinct().ToList();

            List<TextResource> texts = await _textRepository.Get(appIds, languageId);
            InstanceHelper.ReplaceTextKeys(authorizedInstances, texts, languageId);

            return Ok(authorizedInstances);
        }

        private void AddQueryModelToTelemetry(MessageBoxQueryModel queryModel)
        {
            int maxCountInTag = 800; // Max property size and trace payload size in Application Insights is 8192 bytes

            if (queryModel.InstanceOwnerPartyIdList.Count < maxCountInTag)
            {
                Activity.Current?.AddTag("search.queryModel", JsonSerializer.Serialize(queryModel));
            }
            else
            {
                Activity.Current?.AddTag("search.queryModel", JsonSerializer.Serialize(queryModel.CloneWithEmptyInstanceOwnerPartyIdList()));

                Activity.Current?.AddTag(
                    "search.queryModel.instanceOwnerPartyIdList",
                    $"Too large to log here. Logged in separate trace. Size: {queryModel.InstanceOwnerPartyIdList.Count}");

                for (int i = 0; i <= queryModel.InstanceOwnerPartyIdList.Count / maxCountInTag; i++)
                {
                    StringBuilder parties = new();
                    for (int j = i * maxCountInTag; j < (i + 1) * maxCountInTag && j < queryModel.InstanceOwnerPartyIdList.Count; j++)
                    {
                        parties.Append(queryModel.InstanceOwnerPartyIdList[j]);
                        parties.Append(',');
                    }

                    if (parties.Length > 0)
                    {
                        _logger.LogInformation("InstanceOwnerPartyIdList {I}: {Parties}", i, parties.ToString()[..^1]);
                    }
                }
            }
        }
    }
}
