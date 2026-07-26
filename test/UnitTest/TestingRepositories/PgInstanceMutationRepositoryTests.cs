#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class PgInstanceMutationRepositoryTests
{
    [Fact]
    public void BuildPayloads_RepresentativeMutation_WritesSemanticJsonStructure()
    {
        Guid instanceGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid createElementId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Guid updateElementId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        Guid deleteElementId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        Guid createBlobVersion = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid newBlobVersion = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid expectedBlobVersion = Guid.Parse("33333333-3333-3333-3333-333333333333");
        DateTime created = UtcWithExtraTicks(2026, 1, 2, 3, 4, 5, 123, 7);
        DateTime lastChanged = UtcWithExtraTicks(2026, 1, 2, 3, 4, 6, 234, 8);
        DateTime hardDeleted = UtcWithExtraTicks(2026, 1, 2, 3, 4, 7, 345, 9);
        DateTime eventCreated = UtcWithExtraTicks(2026, 1, 2, 3, 4, 8, 456, 6);

        InstanceMutationCommit mutation = new(
            [
                new DataElement
                {
                    Id = createElementId.ToString(),
                    DataType = "main",
                    ContentType = "application/json",
                    Created = created,
                    LastChanged = lastChanged,
                    LastChangedBy = "1001",
                    DeleteStatus = new DeleteStatus
                    {
                        IsHardDeleted = true,
                        HardDeleted = hardDeleted,
                    },
                }.FromApiModel(BlobVersionId.Encode(createBlobVersion)),
            ],
            [
                new InstanceMutationDataElementUpdate(
                    updateElementId,
                    new Dictionary<string, object>
                    {
                        ["/contentType"] = "application/xml",
                        ["/isRead"] = false,
                        ["/currentBlobVersion"] = BlobVersionId.Encode(newBlobVersion),
                    },
                    BlobVersionId.Encode(expectedBlobVersion),
                    IgnoreLock: true
                ),
            ],
            [
                new InstanceMutationDataElementDelete(
                    new DataElementInternal
                    {
                        Id = deleteElementId.ToString(),
                        LastChangedBy = "3003",
                    },
                    IgnoreLock: true
                ),
            ],
            new InstanceInternal
            {
                Id = instanceGuid.ToString(),
                AppId = "ttd/app",
                Org = "ttd",
                InstanceOwner = new InstanceOwner { PartyId = "5000" },
                Created = created,
                LastChanged = lastChanged,
                LastChangedBy = "4004",
                DataValues = new Dictionary<string, string> { ["case"] = "42" },
                Status = new InstanceStatus { IsHardDeleted = true, HardDeleted = hardDeleted },
            },
            [
                nameof(InstanceInternal.Status),
                nameof(InstanceStatus.IsHardDeleted),
                nameof(InstanceStatus.HardDeleted),
                nameof(InstanceInternal.LastChanged),
                nameof(InstanceInternal.LastChangedBy),
                nameof(InstanceInternal.DataValues),
            ],
            12,
            4,
            [
                new InstanceEvent
                {
                    EventType = InstanceEventType.Saved.ToString(),
                    Created = eventCreated,
                    ProcessInfo = new ProcessState
                    {
                        Started = eventCreated,
                        CurrentTask = new ProcessElementInfo
                        {
                            ElementId = "Task_1",
                            Started = eventCreated,
                            Ended = eventCreated,
                        },
                        Ended = eventCreated,
                    },
                },
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    Created = eventCreated.AddMinutes(1),
                },
            ]
        );

        string createPayload = PgInstanceMutationRepository.BuildCreateElementsPayload(
            mutation.CreateDataElements
        );
        string updatePayload = PgInstanceMutationRepository.BuildUpdateElementsPayload(
            mutation.UpdateDataElements
        );
        string deletePayload = PgInstanceMutationRepository.BuildDeleteElementsPayload(
            mutation.DeleteDataElements
        );
        string instanceUpdatesPayload = PgInstanceMutationRepository.BuildInstanceUpdatesPayload(
            mutation
        );
        string eventsPayload = PgInstanceMutationRepository.BuildEventsPayload(
            instanceGuid,
            mutation
        );
        string outboxPayload = InvokeOutboxPayload(instanceGuid, mutation);

        using JsonDocument createDocument = JsonDocument.Parse(createPayload);
        JsonElement createItem = AssertSingleArrayItem(createDocument.RootElement);
        Assert.Equal(createElementId.ToString(), createItem.GetProperty("elementId").GetString());
        Assert.Equal(
            createBlobVersion.ToString(),
            createItem.GetProperty("blobVersion").GetString()
        );
        JsonElement createdElement = AssertObjectProperty(createItem, "element");
        Assert.Equal(createElementId.ToString(), createdElement.GetProperty("Id").GetString());
        Assert.Equal(Normalize(created), createdElement.GetProperty("Created").GetDateTime());
        Assert.Equal(
            Normalize(hardDeleted),
            createdElement.GetProperty("DeleteStatus").GetProperty("HardDeleted").GetDateTime()
        );
        Assert.False(createdElement.TryGetProperty("LastChanged", out _));
        Assert.False(createdElement.TryGetProperty("LastChangedBy", out _));

        using JsonDocument updateDocument = JsonDocument.Parse(updatePayload);
        JsonElement updateItem = AssertSingleArrayItem(updateDocument.RootElement);
        Assert.Equal(updateElementId.ToString(), updateItem.GetProperty("elementId").GetString());
        JsonElement elementChanges = AssertObjectProperty(updateItem, "elementChanges");
        Assert.Equal("application/xml", elementChanges.GetProperty("ContentType").GetString());
        Assert.False(elementChanges.GetProperty("IsRead").GetBoolean());
        Assert.False(elementChanges.TryGetProperty("LastChanged", out _));
        Assert.False(elementChanges.TryGetProperty("LastChangedBy", out _));
        Assert.False(updateItem.TryGetProperty("instanceChanges", out _));
        Assert.False(updateItem.TryGetProperty("isReadChangedToFalse", out _));
        Assert.False(updateItem.TryGetProperty("lastChanged", out _));
        Assert.Equal(
            newBlobVersion.ToString(),
            updateItem.GetProperty("newBlobVersion").GetString()
        );
        Assert.Equal(
            expectedBlobVersion.ToString(),
            updateItem.GetProperty("expectedBlobVersion").GetString()
        );
        Assert.True(updateItem.GetProperty("ignoreLock").GetBoolean());

        using JsonDocument deleteDocument = JsonDocument.Parse(deletePayload);
        JsonElement deleteItem = AssertSingleArrayItem(deleteDocument.RootElement);
        Assert.Equal(deleteElementId.ToString(), deleteItem.GetProperty("elementId").GetString());
        Assert.True(deleteItem.GetProperty("ignoreLock").GetBoolean());
        Assert.False(deleteItem.TryGetProperty("lastChangedBy", out _));

        using JsonDocument instanceUpdatesDocument = JsonDocument.Parse(instanceUpdatesPayload);
        JsonElement instanceUpdate = AssertObject(instanceUpdatesDocument.RootElement);
        JsonElement topLevelSimpleProps = AssertObjectProperty(
            instanceUpdate,
            "toplevelsimpleprops"
        );
        Assert.False(topLevelSimpleProps.TryGetProperty("LastChanged", out _));
        Assert.False(topLevelSimpleProps.TryGetProperty("LastChangedBy", out _));
        JsonElement status = AssertObjectProperty(instanceUpdate, "status");
        Assert.True(status.GetProperty("IsHardDeleted").GetBoolean());
        Assert.Equal(Normalize(hardDeleted), status.GetProperty("HardDeleted").GetDateTime());
        Assert.False(instanceUpdate.TryGetProperty("lastchanged", out _));
        JsonElement dataValues = AssertObjectProperty(instanceUpdate, "datavalues");
        Assert.Equal("42", dataValues.GetProperty("case").GetString());

        using JsonDocument eventsDocument = JsonDocument.Parse(eventsPayload);
        Assert.Equal(2, eventsDocument.RootElement.GetArrayLength());
        JsonElement savedEvent = eventsDocument.RootElement[0];
        Assert.Equal(
            $"{mutation.InstanceUpdates.InstanceOwner.PartyId}/{instanceGuid}",
            savedEvent.GetProperty("InstanceId").GetString()
        );
        Assert.Equal(
            InstanceEventType.Saved.ToString(),
            savedEvent.GetProperty("EventType").GetString()
        );
        Assert.NotEqual(Guid.Empty, savedEvent.GetProperty("Id").GetGuid());
        Assert.Equal(Normalize(eventCreated), savedEvent.GetProperty("Created").GetDateTime());
        JsonElement processInfo = AssertObjectProperty(savedEvent, "ProcessInfo");
        Assert.Equal(
            Normalize(eventCreated),
            processInfo.GetProperty("CurrentTask").GetProperty("Started").GetDateTime()
        );
        Assert.Equal(
            InstanceEventType.Deleted.ToString(),
            eventsDocument.RootElement[1].GetProperty("EventType").GetString()
        );

        using JsonDocument outboxDocument = JsonDocument.Parse(outboxPayload);
        Assert.Equal("ttd/app", outboxDocument.RootElement.GetProperty("appid").GetString());
        Assert.Equal(5000, outboxDocument.RootElement.GetProperty("partyid").GetInt64());
        Assert.Equal(3, outboxDocument.RootElement.GetProperty("delaySeconds").GetInt32());
        Assert.Equal(
            Normalize(created),
            outboxDocument.RootElement.GetProperty("instancecreated").GetDateTime()
        );
        Assert.False(outboxDocument.RootElement.GetProperty("ismigration").GetBoolean());
        Assert.Equal(
            (int)InstanceEventType.Deleted,
            outboxDocument.RootElement.GetProperty("instanceeventtype").GetInt32()
        );
    }

    [Fact]
    public void BuildInstanceUpdatesPayload_ProcessEndArchive_WritesProcessAndStatusInFlatObject()
    {
        Guid instanceGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        DateTime lastChanged = UtcWithExtraTicks(2026, 2, 3, 4, 5, 6, 123, 4);
        DateTime archived = UtcWithExtraTicks(2026, 2, 3, 4, 6, 7, 234, 5);
        DateTime processEnded = UtcWithExtraTicks(2026, 2, 3, 4, 7, 8, 345, 6);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = instanceGuid.ToString(),
                AppId = "ttd/app",
                Org = "ttd",
                InstanceOwner = new InstanceOwner { PartyId = "5000" },
                LastChanged = lastChanged,
                LastChangedBy = "4004",
                Status = new InstanceStatus { IsArchived = true, Archived = archived },
                Process = new ProcessState
                {
                    Ended = processEnded,
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_Archive" },
                },
            },
            [
                nameof(InstanceInternal.Process),
                nameof(InstanceInternal.LastChanged),
                nameof(InstanceInternal.LastChangedBy),
                nameof(InstanceInternal.Status),
                nameof(InstanceStatus.IsArchived),
                nameof(InstanceStatus.Archived),
            ],
            null,
            null
        );

        string payload = PgInstanceMutationRepository.BuildInstanceUpdatesPayload(mutation);

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement item = AssertObject(document.RootElement);
        JsonElement topLevelSimpleProps = AssertObjectProperty(item, "toplevelsimpleprops");
        Assert.False(topLevelSimpleProps.TryGetProperty("LastChanged", out _));
        Assert.False(topLevelSimpleProps.TryGetProperty("LastChangedBy", out _));
        JsonElement status = AssertObjectProperty(item, "status");
        Assert.True(status.GetProperty("IsArchived").GetBoolean());
        Assert.Equal(Normalize(archived), status.GetProperty("Archived").GetDateTime());
        JsonElement process = AssertObjectProperty(item, "process");
        Assert.Equal(Normalize(processEnded), process.GetProperty("Ended").GetDateTime());
        Assert.Equal(
            "Task_Archive",
            process.GetProperty("CurrentTask").GetProperty("ElementId").GetString()
        );
        Assert.Equal("Task_Archive", item.GetProperty("taskid").GetString());
        Assert.False(item.TryGetProperty("lastchanged", out _));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("confirmed").ValueKind);
    }

    [Fact]
    public void BuildInstanceUpdatesPayload_MultipleBranches_WritesOneFlatObject()
    {
        Guid instanceGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        DateTime lastChanged = UtcWithExtraTicks(2026, 3, 4, 5, 6, 7, 123, 4);
        DateTime archived = UtcWithExtraTicks(2026, 3, 4, 5, 7, 8, 234, 5);
        DateTime processStarted = UtcWithExtraTicks(2026, 3, 4, 5, 8, 9, 345, 6);
        DateTime confirmedOn = UtcWithExtraTicks(2026, 3, 4, 5, 9, 10, 456, 7);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = instanceGuid.ToString(),
                AppId = "ttd/app",
                Org = "ttd",
                InstanceOwner = new InstanceOwner { PartyId = "5000" },
                LastChanged = lastChanged,
                LastChangedBy = "4004",
                DataValues = new Dictionary<string, string> { ["case"] = "42" },
                PresentationTexts = new Dictionary<string, string> { ["title"] = "Archive" },
                CompleteConfirmations =
                [
                    new CompleteConfirmation { StakeholderId = "ttd", ConfirmedOn = confirmedOn },
                ],
                Status = new InstanceStatus
                {
                    IsArchived = true,
                    Archived = archived,
                    Substatus = new Substatus
                    {
                        Label = "substatus-label",
                        Description = "substatus-description",
                    },
                },
                Process = new ProcessState
                {
                    Started = processStarted,
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_Shape" },
                },
            },
            [
                nameof(InstanceInternal.Process),
                nameof(InstanceInternal.LastChanged),
                nameof(InstanceInternal.LastChangedBy),
                nameof(InstanceInternal.Status),
                nameof(InstanceStatus.IsArchived),
                nameof(InstanceStatus.Archived),
                nameof(InstanceInternal.DataValues),
                nameof(InstanceInternal.PresentationTexts),
                nameof(InstanceInternal.CompleteConfirmations),
                nameof(InstanceStatus.Substatus),
            ],
            null,
            null
        );

        string payload = PgInstanceMutationRepository.BuildInstanceUpdatesPayload(mutation);

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = AssertObject(document.RootElement);

        AssertSharedInstanceUpdateScalars(root, "Task_Shape", true);
        JsonElement topLevelSimpleProps = AssertObjectProperty(root, "toplevelsimpleprops");
        Assert.False(topLevelSimpleProps.TryGetProperty("LastChanged", out _));
        Assert.False(topLevelSimpleProps.TryGetProperty("LastChangedBy", out _));
        JsonElement process = AssertObjectProperty(root, "process");
        Assert.Equal(
            "Task_Shape",
            process.GetProperty("CurrentTask").GetProperty("ElementId").GetString()
        );
        JsonElement status = AssertObjectProperty(root, "status");
        Assert.True(status.GetProperty("IsArchived").GetBoolean());
        Assert.Equal(Normalize(archived), status.GetProperty("Archived").GetDateTime());

        JsonElement dataValues = AssertObjectProperty(root, "datavalues");
        Assert.Equal("42", dataValues.GetProperty("case").GetString());

        JsonElement presentationTexts = AssertObjectProperty(root, "presentationtexts");
        Assert.Equal("Archive", presentationTexts.GetProperty("title").GetString());

        JsonElement completeConfirmations = root.GetProperty("completeconfirmations");
        Assert.Equal(JsonValueKind.Array, completeConfirmations.ValueKind);
        Assert.Single(completeConfirmations.EnumerateArray());

        JsonElement substatus = AssertObjectProperty(root, "substatus");
        Assert.Equal("substatus-label", substatus.GetProperty("Label").GetString());
        Assert.Equal("substatus-description", substatus.GetProperty("Description").GetString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void CreateApplyMutationException_NonObjectMessageJson_ThrowsContractDrift(
        string messageText
    )
    {
        UnreachableException exception = AssertApplyMutationContractDrift(messageText);

        Assert.Contains("MESSAGE was not a JSON object", exception.Message);
    }

    [Fact]
    public void CreateApplyMutationException_MissingCode_ThrowsContractDrift()
    {
        UnreachableException exception = AssertApplyMutationContractDrift(
            """{"currentInstanceVersion":12,"currentProcessStateVersion":4}"""
        );

        Assert.Contains("missing required property 'code'", exception.Message);
    }

    [Theory]
    [InlineData(
        """{"code":"instance_version_mismatch","currentInstanceVersion":"12","currentProcessStateVersion":4}"""
    )]
    [InlineData(
        """{"code":"instance_version_mismatch","currentInstanceVersion":12,"currentProcessStateVersion":"4"}"""
    )]
    public void CreateApplyMutationException_NonNumericVersionProperty_ThrowsContractDrift(
        string messageText
    )
    {
        UnreachableException exception = AssertApplyMutationContractDrift(messageText);

        Assert.Contains("was not an integer", exception.Message);
    }

    [Fact]
    public void CreateApplyMutationException_IdempotencyKeyInstanceMismatch_ReturnsConflict()
    {
        Guid instanceGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        PostgresException postgresException = new(
            """{"code":"idempotency_key_instance_mismatch","currentInstanceVersion":12,"currentProcessStateVersion":4}""",
            "ERROR",
            "ERROR",
            "AM001"
        );

        RepositoryException exception = Assert.IsType<RepositoryException>(
            PgInstanceMutationRepository.CreateApplyMutationException(
                instanceGuid,
                postgresException
            )
        );
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal("Idempotency key was already used for another instance.", exception.Message);
    }

    [Fact]
    public void CreateApplyMutationException_ProcessStatusConflict_ReturnsTypedConflictWithCurrentStatus()
    {
        Guid instanceGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        PostgresException postgresException = new(
            """{"code":"process_status_conflict","currentInstanceVersion":12,"currentProcessStateVersion":4,"currentProcessStatus":"future-status"}""",
            "ERROR",
            "ERROR",
            "AM001"
        );

        ProcessStatusConflictException exception = Assert.IsType<ProcessStatusConflictException>(
            PgInstanceMutationRepository.CreateApplyMutationException(
                instanceGuid,
                postgresException
            )
        );
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal("future-status", exception.CurrentProcessStatus);
        Assert.Contains("future-status", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaySnapshotVersionDrift_ReturnsVersionMismatchWithActualSnapshotVersions()
    {
        InstanceInternal instance = InstanceInternalTestFactory.Create(
            new Instance(),
            [],
            InternalId: 123L,
            versions: new StorageVersions(14, 10)
        );

        InstanceVersionMismatchException exception =
            Assert.Throws<InstanceVersionMismatchException>(() =>
                PgInstanceMutationRepository.EnsureReplaySnapshotMatchesAdmission(instance, 13, 9)
            );
        Assert.Equal(14, exception.CurrentInstanceVersion);
        Assert.Equal(10, exception.CurrentProcessStateVersion);
    }

    [Theory]
    [InlineData("idempotency_key_not_found")]
    [InlineData("instance_already_advanced")]
    public void CreateApplyMutationException_IdempotencyReplayVersionCodes_ReturnVersionMismatch(
        string code
    )
    {
        Guid instanceGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        PostgresException postgresException = new(
            $$"""{"code":"{{code}}","currentInstanceVersion":12,"currentProcessStateVersion":4}""",
            "ERROR",
            "ERROR",
            "AM001"
        );

        InstanceVersionMismatchException exception =
            Assert.IsType<InstanceVersionMismatchException>(
                PgInstanceMutationRepository.CreateApplyMutationException(
                    instanceGuid,
                    postgresException
                )
            );
        Assert.Equal(12, exception.CurrentInstanceVersion);
        Assert.Equal(4, exception.CurrentProcessStateVersion);
    }

    [Fact]
    public void CreateApplyMutationException_InstanceHardDeleted_ReturnsGeneralNotFoundMessage()
    {
        Guid instanceGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        PostgresException postgresException = new(
            """{"code":"instance_hard_deleted","currentInstanceVersion":12,"currentProcessStateVersion":4}""",
            "ERROR",
            "ERROR",
            "AM001"
        );

        RepositoryException exception = Assert.IsType<RepositoryException>(
            PgInstanceMutationRepository.CreateApplyMutationException(
                instanceGuid,
                postgresException
            )
        );
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
        Assert.Equal(
            $"Instance {instanceGuid} is deleted and cannot be modified.",
            exception.Message
        );
    }

    private static UnreachableException AssertApplyMutationContractDrift(string messageText)
    {
        PostgresException postgresException = new(messageText, "ERROR", "ERROR", "AM001");

        UnreachableException exception = Assert.Throws<UnreachableException>(() =>
            PgInstanceMutationRepository.CreateApplyMutationException(
                Guid.NewGuid(),
                postgresException
            )
        );

        Assert.Same(postgresException, exception.InnerException);
        return exception;
    }

    private static string InvokeOutboxPayload(Guid instanceGuid, InstanceMutationCommit mutation)
    {
        OutboxInsertRowFactory outboxInsertRowFactory = new(
            Options.Create(
                new WolverineSettings
                {
                    EnableSending = true,
                    UrgentPriorityDelaySecs = 3,
                    HighPriorityDelaySecs = 11,
                    LowPriorityDelaySecs = 17,
                }
            )
        );
        PgInstanceMutationRepository repository = new(null, outboxInsertRowFactory);

        return repository.BuildOutboxPayload(instanceGuid, mutation);
    }

    private static JsonElement AssertSingleArrayItem(JsonElement root)
    {
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(1, root.GetArrayLength());
        return root[0];
    }

    private static JsonElement AssertObject(JsonElement root)
    {
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        return root;
    }

    private static JsonElement AssertObjectProperty(JsonElement element, string propertyName)
    {
        JsonElement property = element.GetProperty(propertyName);
        Assert.Equal(JsonValueKind.Object, property.ValueKind);
        return property;
    }

    private static void AssertSharedInstanceUpdateScalars(
        JsonElement element,
        string taskId,
        bool confirmed
    )
    {
        Assert.False(element.TryGetProperty("lastchanged", out _));
        Assert.Equal(taskId, element.GetProperty("taskid").GetString());
        Assert.Equal(confirmed, element.GetProperty("confirmed").GetBoolean());
    }

    private static DateTime UtcWithExtraTicks(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int millisecond,
        int ticks
    ) =>
        new DateTime(
            year,
            month,
            day,
            hour,
            minute,
            second,
            millisecond,
            DateTimeKind.Utc
        ).AddTicks(ticks);

    private static DateTime Normalize(DateTime value) =>
        new(
            (value.Ticks / TimeSpan.TicksPerMicrosecond) * TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc
        );
}
