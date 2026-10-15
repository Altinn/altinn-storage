#nullable disable

using System;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class OutboxEventSyncPolicyTests
{
    [Theory]
    [InlineData(InstanceEventType.Created)]
    [InlineData(InstanceEventType.Deleted)]
    [InlineData(InstanceEventType.Saved)]
    [InlineData(InstanceEventType.SubstatusUpdated)]
    [InlineData(InstanceEventType.process_StartTask)]
    [InlineData(InstanceEventType.Signed)]
    public void SelectEventTypeForInstanceMutation_SingleEvent_ReturnsEventType(
        InstanceEventType eventType
    )
    {
        InstanceEventType selectedEventType =
            OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation([
                CreateEvent(eventType, DateTime.UtcNow),
            ]);

        Assert.Equal(eventType, selectedEventType);
    }

    [Fact]
    public void SelectEventTypeForInstanceMutation_DeletedAndLaterProcessEvent_SelectsDeleted()
    {
        DateTime now = DateTime.UtcNow;

        InstanceEventType selectedEventType =
            OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation([
                CreateEvent(InstanceEventType.Deleted, now),
                CreateEvent(InstanceEventType.process_StartTask, now.AddSeconds(1)),
            ]);

        Assert.Equal(InstanceEventType.Deleted, selectedEventType);
    }

    [Fact]
    public void SelectEventTypeForInstanceMutation_DeletedAndLaterSignedEvent_SelectsDeleted()
    {
        DateTime now = DateTime.UtcNow;

        InstanceEventType selectedEventType =
            OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation([
                CreateEvent(InstanceEventType.Deleted, now),
                CreateEvent(InstanceEventType.Signed, now.AddSeconds(1)),
            ]);

        Assert.Equal(InstanceEventType.Deleted, selectedEventType);
    }

    [Fact]
    public void SelectEventTypeForInstanceMutation_EventsWithSamePriority_SelectsLatestCreated()
    {
        DateTime now = DateTime.UtcNow;

        InstanceEventType selectedEventType =
            OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation([
                CreateEvent(InstanceEventType.Saved, now),
                CreateEvent(InstanceEventType.process_StartTask, now.AddSeconds(1)),
            ]);

        Assert.Equal(InstanceEventType.process_StartTask, selectedEventType);
    }

    [Fact]
    public void SelectEventTypeForInstanceMutation_TimestampsNormalizeToSameMicrosecond_PreservesOrder()
    {
        DateTime created = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        InstanceEvent first = CreateEvent(InstanceEventType.Saved, created);
        InstanceEvent second = CreateEvent(
            InstanceEventType.process_StartTask,
            created.AddTicks(1).ToLocalTime()
        );
        DateTime secondCreated = second.Created.Value;

        InstanceEventType selected = OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation([
            first,
            second,
        ]);

        Assert.Equal(InstanceEventType.Saved, selected);
        Assert.Equal(created, first.Created);
        Assert.Equal(secondCreated, second.Created);
        Assert.Equal(DateTimeKind.Local, second.Created.Value.Kind);
    }

    [Fact]
    public void SelectEventTypeForInstanceMutation_UnspecifiedTimestamp_IsComparedAsUtcWallClock()
    {
        DateTime created = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);

        InstanceEventType selected = OutboxEventSyncPolicy.SelectEventTypeForInstanceMutation([
            CreateEvent(InstanceEventType.Saved, created),
            CreateEvent(
                InstanceEventType.process_StartTask,
                DateTime.SpecifyKind(created.AddSeconds(1), DateTimeKind.Unspecified)
            ),
        ]);

        Assert.Equal(InstanceEventType.process_StartTask, selected);
    }

    private static InstanceEvent CreateEvent(InstanceEventType eventType, DateTime created) =>
        new() { EventType = eventType.ToString(), Created = created };
}
