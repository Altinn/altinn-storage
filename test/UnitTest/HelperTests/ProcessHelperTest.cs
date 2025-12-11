using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests;

public class ProcessHelperTest
{
    [Theory]
    [MemberData(nameof(InstanceEventData_ExpectedProps))]
    public void MapInstanceEventsToProcessHistoryTest(
        InstanceEvent instanceEvent,
        string expectedPerformedBy,
        string expectedEventType,
        DateTime? expectedOccured
    )
    {
        ProcessHistoryItem actual = ProcessHelper.MapInstanceEventsToProcessHistory(
            new List<InstanceEvent> { instanceEvent }
        )[0];
        Assert.Equal(expectedPerformedBy, actual.PerformedBy);
        Assert.Equal(expectedEventType, actual.EventType);
        Assert.Equal(expectedOccured, actual.Occured);
    }

    public static readonly DateTime ReferenceTimestamp = new(2022, 1, 15, 10, 14, 15);

    public static IEnumerable<object[]> InstanceEventData_ExpectedProps =>
        new List<object[]>
        {
            new object[]
            {
                new InstanceEvent
                {
                    EventType = InstanceEventType.process_StartEvent.ToString(),
                    Created = ReferenceTimestamp,
                    ProcessInfo = new ProcessState
                    {
                        StartEvent = "StartEvent_1",
                        Started = ReferenceTimestamp.AddSeconds(1),
                    },
                    User = new PlatformUser
                    {
                        AuthenticationLevel = 2,
                        UserId = 1337,
                        NationalIdentityNumber = "16069412345",
                    },
                },
                "16069412345",
                "process_StartEvent",
                ReferenceTimestamp.AddSeconds(1),
            },
            new object[]
            {
                new InstanceEvent
                {
                    EventType = InstanceEventType.process_StartTask.ToString(),
                    Created = ReferenceTimestamp,
                    ProcessInfo = new ProcessState
                    {
                        StartEvent = "StartEvent_1",
                        Started = ReferenceTimestamp.AddSeconds(1),
                        CurrentTask = new ProcessElementInfo
                        {
                            Flow = 2,
                            Started = ReferenceTimestamp.AddSeconds(2),
                            ElementId = "Task_1",
                            Name = "Utfylling",
                        },
                    },
                    User = new PlatformUser { AuthenticationLevel = 2, OrgId = "888472312" },
                },
                "888472312",
                "process_StartTask",
                ReferenceTimestamp.AddSeconds(1),
            },
            new object[]
            {
                new InstanceEvent
                {
                    EventType = InstanceEventType.process_EndTask.ToString(),
                    Created = ReferenceTimestamp,
                    ProcessInfo = new ProcessState
                    {
                        StartEvent = "StartEvent_1",
                        Started = ReferenceTimestamp.AddSeconds(1),
                        CurrentTask = new ProcessElementInfo
                        {
                            Flow = 2,
                            Started = ReferenceTimestamp.AddSeconds(2),
                            ElementId = "Task_1",
                            Name = "Utfylling",
                            FlowType = "CompleteCurrentMoveToNext",
                        },
                    },
                    User = new PlatformUser { AuthenticationLevel = 2, OrgId = "888472312" },
                },
                "888472312",
                "process_EndTask",
                ReferenceTimestamp,
            },
            new object[]
            {
                new InstanceEvent
                {
                    EventType = InstanceEventType.process_EndEvent.ToString(),
                    Created = ReferenceTimestamp,
                    ProcessInfo = new ProcessState
                    {
                        StartEvent = "StartEvent_1",
                        Started = ReferenceTimestamp.AddSeconds(1),
                        Ended = ReferenceTimestamp.AddSeconds(2),
                        EndEvent = "EndEvent_1",
                    },
                    User = new PlatformUser { AuthenticationLevel = 2, UserId = 1337 },
                },
                string.Empty,
                "process_EndEvent",
                ReferenceTimestamp.AddSeconds(2),
            },
            new object[]
            {
                new InstanceEvent
                {
                    EventType = InstanceEventType.process_StartEvent.ToString(),
                    Created = ReferenceTimestamp,
                    User = new PlatformUser
                    {
                        AuthenticationLevel = 2,
                        UserId = 1337,
                        NationalIdentityNumber = "16069412345",
                    },
                },
                "16069412345",
                "process_StartEvent",
                ReferenceTimestamp,
            },
        };
}
