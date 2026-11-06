#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class PgInstanceMutationRepositoryTests
{
    [Fact]
    public void Serialize_Operations_WritesNaturalContractAndNativeBlobVersions()
    {
        Guid createId = Guid.NewGuid();
        Guid updateId = Guid.NewGuid();
        Guid deleteId = Guid.NewGuid();
        Guid blobVersion = Guid.NewGuid();
        Guid expectedBlobVersion = Guid.NewGuid();
        DataElementInternal element = new()
        {
            Id = createId,
            DataType = "main",
            IsRead = false,
        };
        string creates = InstanceMutationJson.Serialize<IReadOnlyList<DataElementCreation>>([
            new(element, blobVersion),
        ]);
        string updates = InstanceMutationJson.Serialize<IReadOnlyList<DataElementUpdate>>([
            new(
                updateId,
                new() { Locked = false },
                expectedBlobVersion,
                IgnoreLock: true,
                NewBlobVersion: blobVersion
            ),
        ]);
        string deletes = InstanceMutationJson.Serialize<IReadOnlyList<DataElementDeletion>>([
            new(deleteId, IgnoreLock: true),
        ]);

        using JsonDocument createDocument = JsonDocument.Parse(creates);
        JsonElement create = AssertSingleArrayItem(createDocument.RootElement);
        Assert.Equal(createId, create.GetProperty("Element").GetProperty("Id").GetGuid());
        Assert.Equal(blobVersion, create.GetProperty("BlobVersion").GetGuid());
        Assert.False(create.GetProperty("Element").GetProperty("IsRead").GetBoolean());
        Assert.False(create.TryGetProperty("DataElementId", out _));
        using JsonDocument updateDocument = JsonDocument.Parse(updates);
        JsonElement update = AssertSingleArrayItem(updateDocument.RootElement);
        Assert.Equal(updateId, update.GetProperty("DataElementId").GetGuid());
        Assert.Equal(
            expectedBlobVersion,
            update.GetProperty("ExpectedCurrentBlobVersion").GetGuid()
        );
        Assert.Equal(blobVersion, update.GetProperty("NewBlobVersion").GetGuid());
        Assert.True(update.GetProperty("IgnoreLock").GetBoolean());
        Assert.False(update.GetProperty("Changes").GetProperty("Locked").GetBoolean());
        Assert.False(update.GetProperty("Changes").TryGetProperty("NewBlobVersion", out _));
        using JsonDocument deleteDocument = JsonDocument.Parse(deletes);
        JsonElement delete = AssertSingleArrayItem(deleteDocument.RootElement);
        Assert.Equal(deleteId, delete.GetProperty("DataElementId").GetGuid());
        Assert.True(delete.GetProperty("IgnoreLock").GetBoolean());
    }

    [Fact]
    public void Serialize_Patches_DistinguishesOmissionNullFalseZeroAndEmpty()
    {
        InstanceMutationChanges instanceChanges = new()
        {
            DueBefore = Change<DateTime?>.Set(null),
            DataValues = new Dictionary<string, string> { ["remove"] = null, ["set"] = "value" },
            PresentationTexts = new Dictionary<string, string>(),
            Confirmed = false,
            Status = new() { IsArchived = false, Archived = Change<DateTime?>.Set(null) },
        };
        InstanceMutationDataElementChanges elementChanges = new()
        {
            Locked = false,
            IsRead = false,
            Size = 0,
            ContentType = Change<string>.Set(null),
            Refs = Change<IReadOnlyList<Guid>>.Set(null),
            Tags = Change<IReadOnlyList<string>>.Set([]),
            Metadata = Change<IReadOnlyList<KeyValueEntry>>.Set([]),
            FileScanResult = FileScanResult.NotApplicable,
        };

        using JsonDocument instanceDocument = JsonDocument.Parse(
            InstanceMutationJson.Serialize(instanceChanges)
        );
        JsonElement instance = instanceDocument.RootElement;
        Assert.Equal(JsonValueKind.Null, instance.GetProperty("DueBefore").ValueKind);
        Assert.False(instance.TryGetProperty("VisibleAfter", out _));
        Assert.False(instance.TryGetProperty("Process", out _));
        Assert.False(instance.GetProperty("Confirmed").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            instance.GetProperty("DataValues").GetProperty("remove").ValueKind
        );
        Assert.Equal("value", instance.GetProperty("DataValues").GetProperty("set").GetString());
        Assert.Empty(instance.GetProperty("PresentationTexts").EnumerateObject());
        JsonElement status = instance.GetProperty("Status");
        Assert.False(status.GetProperty("IsArchived").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("Archived").ValueKind);
        Assert.False(status.TryGetProperty("IsSoftDeleted", out _));
        using JsonDocument elementDocument = JsonDocument.Parse(
            InstanceMutationJson.Serialize(elementChanges)
        );
        JsonElement element = elementDocument.RootElement;
        Assert.False(element.GetProperty("Locked").GetBoolean());
        Assert.False(element.GetProperty("IsRead").GetBoolean());
        Assert.Equal(0, element.GetProperty("Size").GetInt64());
        Assert.Equal(JsonValueKind.Null, element.GetProperty("ContentType").ValueKind);
        Assert.Equal(JsonValueKind.Null, element.GetProperty("Refs").ValueKind);
        Assert.Empty(element.GetProperty("Tags").EnumerateArray());
        Assert.Empty(element.GetProperty("Metadata").EnumerateArray());
        Assert.Equal("NotApplicable", element.GetProperty("FileScanResult").GetString());
        Assert.False(element.TryGetProperty("Filename", out _));
        Assert.False(element.TryGetProperty("References", out _));
        Assert.False(element.TryGetProperty("IsEmpty", out _));
    }

    [Fact]
    public void Serialize_ReplacementObjects_PreservesNestedNullValues()
    {
        InstanceMutationChanges instanceChanges = new()
        {
            Process = new() { CurrentTask = new() { ElementId = "Task_1" } },
            Status = new() { Substatus = new() { Label = "label" } },
        };
        InstanceMutationDataElementChanges elementChanges = new()
        {
            DeleteStatus = Change<DeleteStatus>.Set(new() { IsHardDeleted = true }),
            Metadata = Change<IReadOnlyList<KeyValueEntry>>.Set([new() { Key = "key" }]),
        };

        using JsonDocument instanceDocument = JsonDocument.Parse(
            InstanceMutationJson.Serialize(instanceChanges)
        );
        JsonElement instance = instanceDocument.RootElement;
        Assert.Equal(
            JsonValueKind.Null,
            instance.GetProperty("Process").GetProperty("Ended").ValueKind
        );
        Assert.Equal(
            JsonValueKind.Null,
            instance.GetProperty("Process").GetProperty("CurrentTask").GetProperty("Name").ValueKind
        );
        Assert.Equal(
            JsonValueKind.Null,
            instance
                .GetProperty("Status")
                .GetProperty("Substatus")
                .GetProperty("Description")
                .ValueKind
        );
        using JsonDocument elementDocument = JsonDocument.Parse(
            InstanceMutationJson.Serialize(elementChanges)
        );
        JsonElement element = elementDocument.RootElement;
        Assert.Equal(
            JsonValueKind.Null,
            element.GetProperty("DeleteStatus").GetProperty("HardDeleted").ValueKind
        );
        Assert.Equal(
            JsonValueKind.Null,
            AssertSingleArrayItem(element.GetProperty("Metadata")).GetProperty("Value").ValueKind
        );
    }

    [Fact]
    public void SerializeAndSelectDelivery_PreservesInputsAndSuppliedIdentities()
    {
        DateTime written = WithKind(DateTimeKind.Unspecified, 9, 123, 7);
        ProcessState process = new()
        {
            Started = written,
            CurrentTask = new() { Started = written },
        };
        DeleteStatus deleteStatus = new() { IsHardDeleted = true, HardDeleted = written };
        InstanceMutationCommit mutation = new()
        {
            Stamp = new(written, "actor"),
            CreateDataElements =
            [
                new(
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Created = written,
                        LastChanged = written,
                        LastChangedBy = "previous",
                        DeleteStatus = deleteStatus,
                    },
                    Guid.NewGuid()
                ),
            ],
            UpdateDataElements =
            [
                new(
                    Guid.NewGuid(),
                    new() { DeleteStatus = Change<DeleteStatus>.Set(deleteStatus) },
                    null
                ),
            ],
            InstanceChanges = new()
            {
                Process = process,
                Status = new() { Archived = Change<DateTime?>.Set(written) },
                CompleteConfirmations = [new() { StakeholderId = "ttd", ConfirmedOn = written }],
            },
            InstanceEvents =
            [
                new()
                {
                    Id = Guid.NewGuid(),
                    InstanceId = "5000/supplied",
                    EventType = InstanceEventType.Saved.ToString(),
                    Created = written,
                    ProcessInfo = process,
                },
                new()
                {
                    Id = Guid.Empty,
                    InstanceId = "5000/supplied",
                    EventType = InstanceEventType.Deleted.ToString(),
                    Created = written.AddMinutes(1),
                },
            ],
        };
        string before = JsonSerializer.Serialize(mutation);

        InstanceMutationJson.Serialize(mutation.CreateDataElements);
        InstanceMutationJson.Serialize(mutation.UpdateDataElements);
        InstanceMutationJson.Serialize(mutation.InstanceChanges);
        string events = InstanceMutationJson.Serialize(mutation.InstanceEvents);
        CreateOutboxFactory().TryBuildMutationDelivery(mutation.InstanceEvents);
        MutationTimestamp.NormalizeForPostgres(mutation.Stamp.LastChanged);

        Assert.Equal(before, JsonSerializer.Serialize(mutation));
        using JsonDocument document = JsonDocument.Parse(events);
        Assert.Equal(
            mutation.InstanceEvents[0].Id,
            document.RootElement[0].GetProperty("Id").GetGuid()
        );
        Assert.Equal(Guid.Empty, document.RootElement[1].GetProperty("Id").GetGuid());
        Assert.Equal(
            "5000/supplied",
            document.RootElement[0].GetProperty("InstanceId").GetString()
        );
    }

    [Fact]
    public void Serialize_NullAndEmptyInstanceChanges_DistinguishesSqlNullAndEmptyUpdate()
    {
        Assert.Null(InstanceMutationJson.Serialize<InstanceMutationChanges>(null));
        Assert.Equal("{}", InstanceMutationJson.Serialize(new InstanceMutationChanges()));
        Assert.Equal(
            "{}",
            InstanceMutationJson.Serialize(new InstanceMutationDataElementChanges())
        );
        Assert.Equal("[]", InstanceMutationJson.Serialize(Array.Empty<DataElementUpdate>()));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Serialize_NestedTimestamps_WritesUtcMicroseconds(DateTimeKind kind)
    {
        DateTime written = WithKind(kind, 9, 123, 7);
        ProcessState process = new()
        {
            Started = written,
            Ended = written,
            CurrentTask = new() { Started = written, Ended = written },
        };
        InstanceMutationChanges changes = new()
        {
            Created = Change<DateTime?>.Set(written),
            DueBefore = Change<DateTime?>.Set(written),
            VisibleAfter = Change<DateTime?>.Set(written),
            Status = new()
            {
                Archived = Change<DateTime?>.Set(written),
                SoftDeleted = Change<DateTime?>.Set(written),
                HardDeleted = Change<DateTime?>.Set(written),
            },
            Process = process,
            CompleteConfirmations = [new() { ConfirmedOn = written }],
        };

        using JsonDocument document = JsonDocument.Parse(InstanceMutationJson.Serialize(changes));
        JsonElement root = document.RootElement;
        AssertUtcJsonTimestamp(root, "Created", written);
        AssertUtcJsonTimestamp(root, "DueBefore", written);
        AssertUtcJsonTimestamp(root, "VisibleAfter", written);
        AssertUtcJsonTimestamp(root.GetProperty("Status"), "Archived", written);
        AssertUtcJsonTimestamp(root.GetProperty("Status"), "SoftDeleted", written);
        AssertUtcJsonTimestamp(root.GetProperty("Status"), "HardDeleted", written);
        AssertProcessTimestamps(root.GetProperty("Process"), written);
        AssertUtcJsonTimestamp(
            AssertSingleArrayItem(root.GetProperty("CompleteConfirmations")),
            "ConfirmedOn",
            written
        );
        using JsonDocument createDocument = JsonDocument.Parse(
            InstanceMutationJson.Serialize(
                new DataElementCreation(
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Created = written,
                        LastChanged = written,
                        DeleteStatus = new() { HardDeleted = written },
                    },
                    null
                )
            )
        );
        JsonElement element = createDocument.RootElement.GetProperty("Element");
        AssertUtcJsonTimestamp(element, "Created", written);
        AssertUtcJsonTimestamp(element, "LastChanged", written);
        AssertUtcJsonTimestamp(element.GetProperty("DeleteStatus"), "HardDeleted", written);
        using JsonDocument eventDocument = JsonDocument.Parse(
            InstanceMutationJson.Serialize(
                new InstanceEvent { Created = written, ProcessInfo = process }
            )
        );
        AssertUtcJsonTimestamp(eventDocument.RootElement, "Created", written);
        AssertProcessTimestamps(eventDocument.RootElement.GetProperty("ProcessInfo"), written);
    }

    [Fact]
    public void Serialize_DefaultConfirmedOn_WritesUtcTimestamp()
    {
        using JsonDocument document = JsonDocument.Parse(
            InstanceMutationJson.Serialize(new CompleteConfirmation())
        );

        JsonElement timestamp = document.RootElement.GetProperty("ConfirmedOn");
        Assert.EndsWith("Z", timestamp.GetString(), StringComparison.Ordinal);
        Assert.Equal(default, timestamp.GetDateTime());
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void NormalizeForPostgres_PreservesUtcInstantAndTruncatesToMicroseconds(
        DateTimeKind kind
    )
    {
        DateTime written = WithKind(kind, 9, 123, 7);
        DateTime expected =
            kind == DateTimeKind.Local
                ? written.ToUniversalTime()
                : DateTime.SpecifyKind(written, DateTimeKind.Utc);

        DateTime normalized = MutationTimestamp.NormalizeForPostgres(written);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(
            new DateTime(expected.Ticks - expected.Ticks % 10, DateTimeKind.Utc),
            normalized
        );
    }

    [Theory]
    [InlineData(InstanceEventType.Saved, null, 17)]
    [InlineData(InstanceEventType.Signed, null, 11)]
    [InlineData(InstanceEventType.Deleted, null, 3)]
    [InlineData(InstanceEventType.Created, null, 11)]
    [InlineData(InstanceEventType.Created, "/storage/instances/1/data", 11)]
    [InlineData(InstanceEventType.Created, "/storage/instances/1/mutations", 3)]
    public void TryBuildMutationDelivery_PreservesPriorityDelaysAndHttpCreatedClassification(
        InstanceEventType eventType,
        string requestPath,
        int expectedDelay
    )
    {
        InstanceMutationOutboxDelivery delivery = CreateOutboxFactory(requestPath: requestPath)
            .TryBuildMutationDelivery([new() { EventType = eventType.ToString() }]);

        Assert.Equal(expectedDelay, delivery.DelaySeconds);
        Assert.Equal(eventType, delivery.EventType);
        using JsonDocument document = JsonDocument.Parse(InstanceMutationJson.Serialize(delivery));
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.Equal(expectedDelay, document.RootElement.GetProperty("DelaySeconds").GetInt32());
        Assert.Equal((int)eventType, document.RootElement.GetProperty("EventType").GetInt32());
    }

    [Fact]
    public void TryBuildMutationDelivery_WithoutEventsOrSending_ProducesNoDelivery()
    {
        Assert.Null(CreateOutboxFactory().TryBuildMutationDelivery([]));
        Assert.Null(
            CreateOutboxFactory(enableSending: false)
                .TryBuildMutationDelivery([new() { EventType = "Saved" }])
        );
    }

    [Theory]
    [InlineData("create")]
    [InlineData("event-id")]
    [InlineData("event-identity")]
    public async Task Apply_UnpreparedIdentity_RejectsBeforeDatabaseAccess(string missingIdentity)
    {
        InstanceMutationCommit mutation = new() { Stamp = new(DateTime.UtcNow, null) };
        mutation = missingIdentity switch
        {
            "create" => mutation with { CreateDataElements = [new(new(), null)] },
            "event-id" => mutation with
            {
                InstanceEvents = [new() { InstanceId = "5000/instance" }],
            },
            _ => mutation with { InstanceEvents = [new() { Id = Guid.NewGuid() }] },
        };
        PgInstanceMutationRepository repository = new(null, CreateOutboxFactory());

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.Apply(Guid.NewGuid(), 1, mutation)
        );

        Assert.Equal("mutation", exception.ParamName);
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
        Guid instanceGuid = Guid.NewGuid();
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
        Guid instanceGuid = Guid.NewGuid();
        PostgresException postgresException = new(
            """{"code":"process_status_conflict","currentInstanceVersion":12,"currentProcessStateVersion":4,"currentProcessStatus":"processing"}""",
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
        Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
        Assert.Contains("processing", exception.Message, StringComparison.Ordinal);
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
        Guid instanceGuid = Guid.NewGuid();
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
        Guid instanceGuid = Guid.NewGuid();
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

    private static OutboxInsertRowFactory CreateOutboxFactory(
        bool enableSending = true,
        string requestPath = null
    )
    {
        IHttpContextAccessor accessor = requestPath is null
            ? null
            : new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (accessor is not null)
        {
            accessor.HttpContext.Request.Path = requestPath;
        }

        return new OutboxInsertRowFactory(
            Options.Create(
                new WolverineSettings
                {
                    EnableSending = enableSending,
                    UrgentPriorityDelaySecs = 3,
                    HighPriorityDelaySecs = 11,
                    LowPriorityDelaySecs = 17,
                }
            ),
            accessor
        );
    }

    private static JsonElement AssertSingleArrayItem(JsonElement root)
    {
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(1, root.GetArrayLength());
        return root[0];
    }

    private static DateTime WithKind(DateTimeKind kind, int second, int millisecond, int ticks)
    {
        return new DateTime(2026, 5, 6, 7, 8, second, millisecond, kind).AddTicks(ticks);
    }

    private static void AssertProcessTimestamps(JsonElement process, DateTime written)
    {
        AssertUtcJsonTimestamp(process, "Started", written);
        AssertUtcJsonTimestamp(process, "Ended", written);
        AssertUtcJsonTimestamp(process.GetProperty("CurrentTask"), "Started", written);
        AssertUtcJsonTimestamp(process.GetProperty("CurrentTask"), "Ended", written);
    }

    private static void AssertUtcJsonTimestamp(
        JsonElement element,
        string propertyName,
        DateTime written
    )
    {
        JsonElement property = element.GetProperty(propertyName);
        Assert.EndsWith("Z", property.GetString(), StringComparison.Ordinal);
        Assert.Equal(MutationTimestamp.NormalizeForPostgres(written), property.GetDateTime());
    }
}
