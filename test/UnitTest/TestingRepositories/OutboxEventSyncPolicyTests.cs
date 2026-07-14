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

    private static InstanceEvent CreateEvent(InstanceEventType eventType, DateTime created) =>
        new() { EventType = eventType.ToString(), Created = created };
}
