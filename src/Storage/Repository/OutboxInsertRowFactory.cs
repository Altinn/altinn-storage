#nullable disable

using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Builds outbox rows for Dialogporten synchronization.
/// </summary>
/// <param name="wolverineSettings">Wolverine/outbox delivery settings.</param>
/// <param name="contextAccessor">Optional HTTP context used to disambiguate instance creation events.</param>
public sealed class OutboxInsertRowFactory(
    IOptions<WolverineSettings> wolverineSettings,
    IHttpContextAccessor contextAccessor = null
)
{
    private readonly WolverineSettings _wolverineSettings = wolverineSettings.Value;

    internal OutboxInsertRow TryBuild(SyncInstanceToDialogportenCommand command)
    {
        if (!_wolverineSettings.EnableSending)
        {
            return null;
        }

        return new OutboxInsertRow(
            Guid.Parse(command.InstanceId),
            command.AppId,
            long.Parse(command.PartyId),
            GetEventDelaySecs(command.EventType, IsInstanceCreate(command.EventType)),
            command.InstanceCreatedAt,
            command.IsMigration,
            command.EventType
        );
    }

    internal InstanceMutationOutboxDelivery TryBuildMutationDelivery(
        IReadOnlyList<InstanceEvent> events
    )
    {
        if (!_wolverineSettings.EnableSending || events.Count == 0)
        {
            return null;
        }

        InstanceEventType eventType = OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation(
            events
        );
        return new InstanceMutationOutboxDelivery(
            GetEventDelaySecs(eventType, IsInstanceCreate(eventType)),
            eventType
        );
    }

    private bool IsInstanceCreate(InstanceEventType eventType)
    {
        return eventType == InstanceEventType.Created
            && !(
                contextAccessor?.HttpContext?.Request.Path.Value?.EndsWith(
                    "/data",
                    StringComparison.OrdinalIgnoreCase
                ) ?? true
            );
    }

    private int GetEventDelaySecs(InstanceEventType eventType, bool instanceCreate) =>
        OutboxEventSyncPolicy.GetPriority(eventType, instanceCreate) switch
        {
            OutboxEventPriority.Urgent => _wolverineSettings.UrgentPriorityDelaySecs,
            OutboxEventPriority.High => _wolverineSettings.HighPriorityDelaySecs,
            OutboxEventPriority.Low => _wolverineSettings.LowPriorityDelaySecs,
            _ => _wolverineSettings.HighPriorityDelaySecs,
        };
}
