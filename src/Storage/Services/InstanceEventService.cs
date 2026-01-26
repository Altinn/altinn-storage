#nullable enable
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Service class with business logic related to instanced events.
/// </summary>
public class InstanceEventService : IInstanceEventService
{
    private readonly IInstanceEventRepository _repository;
    private readonly IHttpContextAccessor _contextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceEventService"/> class.
    /// </summary>
    public InstanceEventService(
        IInstanceEventRepository repository,
        IHttpContextAccessor contextAccessor
    )
    {
        _repository = repository;
        _contextAccessor = contextAccessor;
    }

    /// <inheritdoc/>
    public InstanceEvent BuildInstanceEvent(InstanceEventType eventType, Instance instance)
    {
        var user = _contextAccessor.HttpContext!.User;

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

        await _repository.InsertInstanceEvent(instanceEvent, instance);
    }

    /// <inheritdoc/>
    public async Task DispatchEvent(
        InstanceEventType eventType,
        Instance instance,
        DataElement dataElement
    )
    {
        ClaimsPrincipal user = _contextAccessor.HttpContext!.User;

        int? userId = user.GetUserId();
        string? orgId = user.GetOrg();
        Guid? systemUserId = user.GetSystemUserId();
        string? systemUserOwnerOrgNo = user.GetSystemUserOwner();

        if (userId is null && orgId is null && systemUserId is null)
        {
            throw new InvalidOperationException(
                "Cannot dispatch event, missing a user to perform the event on behalf of"
            );
        }

        if (user.TryParseAuthenticationLevel(out int authenticationLevel) is false)
        {
            throw new InvalidOperationException(
                "Cannot dispatch event without AuthenticationLevel"
            );
        }

        InstanceEvent instanceEvent = new()
        {
            EventType = eventType.ToString(),
            InstanceId = instance.Id,
            DataId = dataElement.Id,
            InstanceOwnerPartyId = instance.InstanceOwner.PartyId,
            User = new PlatformUser
            {
                AuthenticationLevel = authenticationLevel,
                UserId = userId,
                OrgId = orgId,
                SystemUserId = systemUserId,
                SystemUserOwnerOrgNo = systemUserOwnerOrgNo,
            },
            ProcessInfo = instance.Process,
            Created = DateTime.UtcNow,
        };

        await _repository.InsertInstanceEvent(instanceEvent, instance);
    }
}
