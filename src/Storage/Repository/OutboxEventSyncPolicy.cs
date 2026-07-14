#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Repository;

internal enum OutboxEventPriority
{
    Urgent = 0,
    High = 1,
    Low = 2,
}

internal static class OutboxEventSyncPolicy
{
    public static InstanceEventType SelectEventTypeForInstanceMutation(
        IEnumerable<InstanceEvent> instanceEvents
    )
    {
        InstanceEvent selectedEvent = instanceEvents
            .Select(e => new { Event = e, EventType = ParseEventType(e.EventType) })
            .OrderBy(e => GetPriority(e.EventType, instanceCreate: false))
            .ThenByDescending(e => e.Event.Created)
            .Select(e => e.Event)
            .First();

        return ParseEventType(selectedEvent.EventType);
    }

    public static OutboxEventPriority GetPriority(
        InstanceEventType eventType,
        bool instanceCreate
    ) =>
        eventType switch
        {
            InstanceEventType.Created => instanceCreate
                ? OutboxEventPriority.Urgent
                : OutboxEventPriority.High,
            InstanceEventType.Deleted => OutboxEventPriority.Urgent,
            InstanceEventType.Saved => OutboxEventPriority.Low,
            InstanceEventType.SubstatusUpdated => OutboxEventPriority.Low,
            InstanceEventType.process_StartEvent => OutboxEventPriority.Low,
            InstanceEventType.process_EndEvent => OutboxEventPriority.Low,
            InstanceEventType.process_StartTask => OutboxEventPriority.Low,
            InstanceEventType.process_EndTask => OutboxEventPriority.Low,
            InstanceEventType.process_AbandonTask => OutboxEventPriority.Low,
            _ => OutboxEventPriority.High,
        };

    private static InstanceEventType ParseEventType(string eventType) =>
        Enum.TryParse(eventType, out InstanceEventType parsedEventType)
            ? parsedEventType
            : InstanceEventType.None;
}
