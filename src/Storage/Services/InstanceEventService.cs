using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Service class with business logic related to instanced events.
    /// </summary>
    public class InstanceEventService : IInstanceEventService
    {
        private readonly IInstanceEventRepository _repository;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IMessageBus _messageBus;
        private readonly WolverineSettings _wolverineSettings;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstanceEventService"/> class.
        /// </summary>
        public InstanceEventService(IInstanceEventRepository repository, IHttpContextAccessor contextAccessor, IMessageBus messageBus, IOptions<WolverineSettings> wolverineSettings, ILogger<InstanceEventService> logger)
        {
            _repository = repository;
            _contextAccessor = contextAccessor;
            _messageBus = messageBus;
            _wolverineSettings = wolverineSettings.Value;
            _logger = logger;
        }

        /// <inheritdoc/>
        public InstanceEvent BuildInstanceEvent(InstanceEventType eventType, Instance instance)
        {
            var user = _contextAccessor.HttpContext.User;

            InstanceEvent instanceEvent = new()
            {
                EventType = eventType.ToString(),
                InstanceId = instance.Id,
                InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                User = new PlatformUser
                {
                    UserId = user.GetUserId(),
                    AuthenticationLevel = user.GetAuthenticationLevel(),
                    OrgId = user.GetOrg(),
                    SystemUserId = user.GetSystemUserId(),
                    SystemUserOwnerOrgNo = user.GetSystemUserOwner(),
                },

                ProcessInfo = instance.Process,
                Created = DateTime.UtcNow,
            };

            return instanceEvent;
        }

        /// <inheritdoc/>
        public async Task DispatchEvent(InstanceEventType eventType, Instance instance)
        {
            var instanceEvent = BuildInstanceEvent(eventType, instance);

            await _repository.InsertInstanceEvent(instanceEvent);

            if (_wolverineSettings.EnableSending)
            {
                try
                {
                    using Activity? activity = Activity.Current?.Source.StartActivity("WolverineIEdispatch");
                    activity.DisplayName = "WolverineIEdispatch";
                    InstanceUpdateCommand instanceUpdateCommand = new(
                        instance.AppId,
                        instance.InstanceOwner.PartyId,
                        instance.Id.Split("/")[1],
                        instance.Created.Value,
                        false);
                    await _messageBus.PublishAsync(instanceUpdateCommand);
                }
                catch (Exception ex)
                {
                    // Log the error but do not return an error to the user
                    _logger.LogError(ex, "Failed to publish instance update command for instance {InstanceId}", instanceEvent.InstanceId);
                }
            }
        }

        /// <inheritdoc/>
        public async Task DispatchEvent(InstanceEventType eventType, Instance instance, DataElement dataElement)
        {
            var user = _contextAccessor.HttpContext.User;

            InstanceEvent instanceEvent = new()
            {
                EventType = eventType.ToString(),
                InstanceId = instance.Id,
                DataId = dataElement.Id,
                InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
                User = new PlatformUser
                {
                    UserId = user.GetUserId(),
                    AuthenticationLevel = user.GetAuthenticationLevel(),
                    OrgId = user.GetOrg(),
                    SystemUserId = user.GetSystemUserId(),
                    SystemUserOwnerOrgNo = user.GetSystemUserOwner(),
                },
                ProcessInfo = instance.Process,
                Created = DateTime.UtcNow,
            };

            await _repository.InsertInstanceEvent(instanceEvent);

            if (_wolverineSettings.EnableSending)
            {
                try
                {
                    using Activity? activity = Activity.Current?.Source.StartActivity("WolverineIEdispatch2");
                    activity.DisplayName = "WolverineIEdispatch2";
                    InstanceUpdateCommand instanceUpdateCommand = new(
                        instance.AppId,
                        instance.InstanceOwner.PartyId,
                        instance.Id.Split("/")[1],
                        instance.Created.Value,
                        false);
                    await _messageBus.PublishAsync(instanceUpdateCommand);
                }
                catch (Exception ex)
                {
                    // Log the error but do not return an error to the user
                    _logger.LogError(ex, "Failed to publish instance update command for instance {InstanceId}", instanceEvent.InstanceId);
                }
            }
        }
    }
}
