#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using WolverineSettings = Altinn.Platform.Storage.Configuration.WolverineSettings;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class DataTests(DataElementFixture dataElementFixture)
    : IClassFixture<DataElementFixture>,
        IAsyncLifetime
{
    private const string _dataElement1 = "cdb627fd-c586-41f5-99db-bae38daa2b59";
    private const string _dataElement2 = "d03b4a04-f0df-4ead-be92-aa7a68959dab";
    private const string _dataElement3 = "5ebeb498-677d-476f-8cab-b788a0fd0640";
    private const string _contentType = "unittestContentType";
    private static readonly DateTime _frozenTime = new(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);

    private static readonly List<KeyValueEntry> _originalEntries =
    [
        new() { Key = "key1", Value = "value1" },
        new() { Key = "key2", Value = "value2" },
    ];

    private static readonly List<KeyValueEntry> _replacementEntries =
    [
        new() { Key = "key3", Value = "value3" },
        new() { Key = "key4", Value = "value4" },
    ];

    private static readonly List<string> _originalTags = ["s1", "s2"];
    private static readonly List<string> _replacementTags = ["s3", "s4"];

    private long _instanceInternalId;
    private InstanceInternal _instance;
    private Guid _instanceGuid;

    public async Task InitializeAsync()
    {
        string sql =
            "delete from storage.instance_mutation_idempotency; delete from storage.instanceevents; delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;";

        await PostgresUtil.RunSql(sql);
        await PostgresUtil.FreezeTime(_frozenTime);

        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Status.IsSoftDeleted = true;
        InstanceInternal newInstance = await dataElementFixture.InstanceRepo.Create(
            instance,
            CancellationToken.None
        );
        _instance = await dataElementFixture.InstanceRepo.GetOne(
            newInstance.Id,
            false,
            CancellationToken.None
        );
        _instanceInternalId = _instance.InternalId;
        _instanceGuid = _instance.Id;
    }

    public async Task DisposeAsync()
    {
        await PostgresUtil.UnfreezeTime();
    }

    /// <summary>
    /// Test create and change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Create_Change_Instance_Readstatus_Ok()
    {
        // Arrange
        DateTime lastChanged = _frozenTime;
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.LastChanged = lastChanged;

        // Act
        DataElementInternal dataElement = await CreateDataElement(element);
        InstanceInternal instance = await dataElementFixture.InstanceRepo.GetOne(
            dataElement.InstanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        Assert.True(await dataElementFixture.DataRepo.Exists(dataElement.Id));
        Assert.Equal(ReadStatus.UpdatedSinceLastReview, instance.Status.ReadStatus);
        Assert.Equal(dataElement.LastChangedBy, instance.LastChangedBy);
        Assert.Equal(instance.LastChanged, dataElement.LastChanged);
        Assert.Equal(lastChanged, dataElement.LastChanged);
        Assert.Equal(lastChanged, await ReadInstanceLastChangedColumn());
    }

    /// <summary>
    /// Test create and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Create_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        await SetInstanceReadStatus(ReadStatus.Unread);

        // Act
        DataElementInternal dataElement = await CreateDataElement();

        // Assert
        InstanceInternal instance = await ReadInstance();
        Assert.True(await dataElementFixture.DataRepo.Exists(dataElement.Id));
        Assert.Equal(ReadStatus.Unread, instance.Status.ReadStatus);
        Assert.Equal(dataElement.LastChangedBy, instance.LastChangedBy);
        Assert.Equal(dataElement.LastChanged, instance.LastChanged);
        Assert.Equal(dataElement.LastChanged, await ReadInstanceLastChangedColumn());
    }

    /// <summary>
    /// Test update, insert metadata
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Metadata_Insert_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement();

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            dataElement.InstanceGuid,
            dataElement.Id,
            new Dictionary<string, object> { { "/metadata", _originalEntries } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(_originalEntries),
            JsonSerializer.Serialize(updatedElement.Metadata)
        );
    }

    /// <summary>
    /// Test update, replace metadata
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Metadata_Replace_Ok()
    {
        // Arrange
        DataElement initialDataElement = TestDataUtil.GetDataElement(_dataElement1);
        initialDataElement.Metadata = _originalEntries;
        DataElementInternal dataElement = await CreateDataElement(initialDataElement);

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            dataElement.InstanceGuid,
            dataElement.Id,
            new Dictionary<string, object> { { "/metadata", _replacementEntries } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(_replacementEntries),
            JsonSerializer.Serialize(updatedElement.Metadata)
        );
    }

    /// <summary>
    /// Test update, insert metadata
    /// </summary>
    [Fact]
    public async Task DataElement_Update_UserDefinedMetadata_Insert_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement();

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            dataElement.InstanceGuid,
            dataElement.Id,
            new Dictionary<string, object> { { "/userDefinedMetadata", _originalEntries } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(_originalEntries),
            JsonSerializer.Serialize(updatedElement.UserDefinedMetadata)
        );
    }

    /// <summary>
    /// Test update, replace metadata
    /// </summary>
    [Fact]
    public async Task DataElement_Update_UserDefinedMetadata_Replace_Ok()
    {
        // Arrange
        DataElement initialDataElement = TestDataUtil.GetDataElement(_dataElement1);
        initialDataElement.UserDefinedMetadata = _originalEntries;
        DataElementInternal dataElement = await CreateDataElement(initialDataElement);

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            dataElement.InstanceGuid,
            dataElement.Id,
            new Dictionary<string, object> { { "/userDefinedMetadata", _replacementEntries } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(_replacementEntries),
            JsonSerializer.Serialize(updatedElement.UserDefinedMetadata)
        );
    }

    /// <summary>
    /// Test update, insert tags
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Tags_Insert_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement();

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            dataElement.InstanceGuid,
            dataElement.Id,
            new Dictionary<string, object> { { "/tags", _originalTags } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(_originalTags),
            JsonSerializer.Serialize(updatedElement.Tags)
        );
    }

    /// <summary>
    /// Test update, replace tags
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Tags_Replace_Ok()
    {
        // Arrange
        DataElement initialDataElement = TestDataUtil.GetDataElement(_dataElement1);
        initialDataElement.Tags = _originalTags;
        DataElementInternal dataElement = await CreateDataElement(initialDataElement);

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            dataElement.InstanceGuid,
            dataElement.Id,
            new Dictionary<string, object> { { "/tags", _replacementTags } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(_replacementTags),
            JsonSerializer.Serialize(updatedElement.Tags)
        );
    }

    /// <summary>
    /// Test update, set delete status
    /// </summary>
    [Fact]
    public async Task DataElement_Update_DeleteStatus_Ok()
    {
        // Arrange
        DateTime hardDeleted = _frozenTime;
        DeleteStatus deleteStatus = new() { IsHardDeleted = true, HardDeleted = hardDeleted };
        DataElementInternal dataElement = await CreateDataElement();

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            dataElement.InstanceGuid,
            dataElement.Id,
            new Dictionary<string, object> { { "/deleteStatus", deleteStatus } }
        );

        // Assert
        Assert.True(updatedElement.DeleteStatus.IsHardDeleted);
        Assert.Equal(hardDeleted, updatedElement.DeleteStatus.HardDeleted);
    }

    /// <summary>
    /// Test update and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Update_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement();
        DateTime seededLastChanged =
            _instance.LastChanged
            ?? throw new InvalidOperationException(
                "The seeded instance is expected to have LastChanged set."
            );
        string restoreValues =
            """{"Status": {"ReadStatus": 0},"LastChanged": "<lastChanged>","LastChangedBy": "<lastChangedBy>"}"""
                .Replace("<lastChanged>", seededLastChanged.ToString("o"))
                .Replace("<lastChangedBy>", _instance.LastChangedBy);
        await PostgresUtil.RunSql(
            $"update storage.instances set instance = instance || '{restoreValues}', lastChanged = '{seededLastChanged:o}' where alternateid = '{_instanceGuid}';"
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            dataElement.InstanceGuid,
            dataElement.Id,
            new Dictionary<string, object> { { "/contentType", _contentType } }
        );

        // Assert
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Empty,
            dataElement.Id
        );
        InstanceInternal instance = await ReadInstance();
        Assert.Equal(_contentType, readElement.ContentType);
        Assert.Equal(_contentType, updatedElement.ContentType);
        Assert.Equal(ReadStatus.Unread, instance.Status.ReadStatus);
        Assert.Equal(_instance.LastChangedBy, instance.LastChangedBy);
        Assert.Equal(_instance.LastChanged, instance.LastChanged);
        Assert.Equal(_instance.LastChanged, await ReadInstanceLastChangedColumn());
    }

    /// <summary>
    /// Test update and change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Change_Instance_Readstatus_Ok()
    {
        // Arrange
        DateTime lastChanged = _frozenTime;
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.LastChanged = lastChanged;
        DataElementInternal dataElement = await CreateDataElement(element);
        await SetInstanceReadStatus(ReadStatus.Read);

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            _instanceGuid,
            dataElement.Id,
            new Dictionary<string, object>
            {
                { "/contentType", _contentType },
                { "/isRead", false },
                { "/lastChanged", dataElement.LastChanged },
                { "/lastChangedBy", dataElement.LastChangedBy },
            }
        );
        InstanceInternal instance = await dataElementFixture.InstanceRepo.GetOne(
            updatedElement.InstanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Empty,
            dataElement.Id
        );
        Assert.Equal(_contentType, readElement.ContentType);
        Assert.Equal(_contentType, updatedElement.ContentType);
        Assert.Equal(ReadStatus.Unread, instance.Status.ReadStatus);
        Assert.Equal(dataElement.LastChangedBy, instance.LastChangedBy);
        Assert.Equal(instance.LastChanged, updatedElement.LastChanged);
        Assert.Equal(lastChanged, updatedElement.LastChanged);
        Assert.Equal(lastChanged, await ReadInstanceLastChangedColumn());
    }

    [Fact]
    public async Task DataElement_Update_BlobVersionId_LockedDataElement_ThrowsConflictAndDoesNotUpdateInstance()
    {
        // Arrange
        string contentType = $"locked-{Guid.NewGuid()}";
        string lastChangedBy = $"locked-user-{Guid.NewGuid()}";
        DateTime lastChanged = DateTime.UtcNow;
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "locked-test-setup";
        element.Locked = true;
        DataElement dataElement = await CreateLegacyDataElement(element);
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            UpdateDataElement(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object>()
                {
                    { "/contentType", contentType },
                    {
                        "/blobStoragePath",
                        DataElementHelper.GetVersionedBlobPath(
                            _instance.AppId,
                            new Guid(dataElement.InstanceGuid),
                            blobVersionId
                        )
                    },
                    { "/currentBlobVersion", blobVersionId },
                    { "/lastChanged", lastChanged },
                    { "/lastChangedBy", lastChangedBy },
                },
                new DataElementUpdateContext { IgnoreLock = false }
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        int dataCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}' and element ->> 'ContentType' = '{contentType}'"
        );
        int instanceCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.instances where alternateid = '{dataElement.InstanceGuid}' and instance -> 'LastChangedBy' = '\"{lastChangedBy}\"'"
        );
        Assert.Equal(0, dataCount);
        Assert.Equal(0, instanceCount);
        Assert.Equal(0, await CountAttachedBlobVersionRows(blobVersionId));
    }

    [Fact]
    public async Task DataElement_Update_BlobVersionId_HardDeletedDataElement_ThrowsNotFoundAndDoesNotUpdateInstance()
    {
        // Arrange
        string contentType = $"hard-deleted-{Guid.NewGuid()}";
        string lastChangedBy = $"hard-deleted-user-{Guid.NewGuid()}";
        DateTime lastChanged = DateTime.UtcNow;
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "hard-deleted-test-setup";
        element.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        DataElement dataElement = await CreateLegacyDataElement(element);
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            UpdateDataElement(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object>()
                {
                    { "/contentType", contentType },
                    {
                        "/blobStoragePath",
                        DataElementHelper.GetVersionedBlobPath(
                            _instance.AppId,
                            new Guid(dataElement.InstanceGuid),
                            blobVersionId
                        )
                    },
                    { "/currentBlobVersion", blobVersionId },
                    { "/lastChanged", lastChanged },
                    { "/lastChangedBy", lastChangedBy },
                },
                new DataElementUpdateContext { IgnoreLock = false }
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
        int dataCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}' and element ->> 'ContentType' = '{contentType}'"
        );
        int instanceCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.instances where alternateid = '{dataElement.InstanceGuid}' and instance -> 'LastChangedBy' = '\"{lastChangedBy}\"'"
        );
        Assert.Equal(0, dataCount);
        Assert.Equal(0, instanceCount);
        Assert.Equal(0, await CountAttachedBlobVersionRows(blobVersionId));
    }

    [Fact]
    public async Task DataElement_Create_HardDeletedInstance_ThrowsNotFoundAndDoesNotAttachBlobVersion()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "hard-deleted-instance-create-test-setup";
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            blobVersionId
        );
        await SetInstanceHardDeleted(Guid.Parse(element.InstanceGuid));

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            CreateDataElement(element.FromApiModel(blobVersionId), _instanceInternalId)
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
        int dataCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelements where alternateid = '{element.Id}'"
        );
        int attachedVersionCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id = '{BlobVersionId.Decode(blobVersionId)}' and detachedat is null"
        );
        Assert.Equal(0, dataCount);
        Assert.Equal(0, attachedVersionCount);
    }

    [Fact]
    public async Task DataElement_Update_HardDeletedInstance_ThrowsNotFoundAndDoesNotUpdateElement()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        element.IsRead = false;
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "hard-deleted-instance-update-test-setup";
        DataElement dataElement = await CreateLegacyDataElement(element);
        await SetInstanceHardDeleted(Guid.Parse(dataElement.InstanceGuid));

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            UpdateDataElement(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object>() { { "/isRead", true } }
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );
        Assert.False(readElement.IsRead);
    }

    [Fact]
    public async Task DataElement_Update_IsRead_LockedDataElement_UpdatesIsRead()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        element.IsRead = false;
        element.Locked = true;
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "isread-locked-test-setup";
        DataElement dataElement = await CreateLegacyDataElement(element);

        // Act
        DataElement updatedElement = (
            await UpdateDataElement(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object>() { { "/isRead", true } }
            )
        ).ToApiModel();

        // Assert
        Assert.True(updatedElement.IsRead);
        Assert.True(updatedElement.Locked);
    }

    [Fact]
    public async Task DataElement_Update_Tags_HardDeletedDataElement_ThrowsNotFoundAndDoesNotUpdateElement()
    {
        // Arrange
        List<string> orgTags = ["s1", "s2"];
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        element.Tags = orgTags;
        element.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "tags-harddeleted-test-setup";
        DataElement dataElement = await CreateLegacyDataElement(element);

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            UpdateDataElement(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object>()
                {
                    {
                        "/tags",
                        new List<string> { "s3" }
                    },
                }
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );
        Assert.Equal(JsonSerializer.Serialize(orgTags), JsonSerializer.Serialize(readElement.Tags));
    }

    [Fact]
    public async Task DataElement_UpdateReadStatus_ToFalse_UpdatesAggregateReadStatusWithoutBumpingVersions()
    {
        // Arrange
        Guid instanceGuid = _instanceGuid;
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = instanceGuid.ToString();
        element.IsRead = true;
        DataElement dataElement = await CreateLegacyDataElement(element);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await dataElementFixture.DataRepo.UpdateReadStatus(
            instanceGuid,
            Guid.Parse(dataElement.Id),
            false
        );
        InstanceInternal instanceInternal = await ReadInstance();

        // Assert
        Assert.False(result.DataElement.IsRead);
        Assert.Equal(ReadStatus.Unread, instanceInternal.Status.ReadStatus);
        Assert.Equal(previousInstanceVersion, result.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, result.Versions.ProcessStateVersion);
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(previousProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_UpdateReadStatus_ToFalse_WhenOtherElementStillRead_KeepsAggregateReadStatusAndVersions()
    {
        // Arrange
        Guid instanceGuid = _instanceGuid;
        DataElement targetElement = TestDataUtil.GetDataElement(_dataElement1);
        targetElement.Id = Guid.NewGuid().ToString();
        targetElement.InstanceGuid = instanceGuid.ToString();
        targetElement.IsRead = true;
        DataElement otherElement = TestDataUtil.GetDataElement(_dataElement2);
        otherElement.Id = Guid.NewGuid().ToString();
        otherElement.InstanceGuid = instanceGuid.ToString();
        otherElement.IsRead = true;
        DataElement targetDataElement = await CreateLegacyDataElement(targetElement);
        DataElement otherDataElement = await CreateLegacyDataElement(otherElement);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await dataElementFixture.DataRepo.UpdateReadStatus(
            instanceGuid,
            Guid.Parse(targetDataElement.Id),
            false
        );
        DataElementInternal readOtherElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(otherDataElement.Id)
        );
        InstanceInternal instanceInternal = await ReadInstance();

        // Assert
        Assert.False(result.DataElement.IsRead);
        Assert.True(readOtherElement.IsRead);
        Assert.Equal(ReadStatus.Read, instanceInternal.Status.ReadStatus);
        Assert.Equal(previousInstanceVersion, result.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, result.Versions.ProcessStateVersion);
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(previousProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_UpdateReadStatus_ToTrue_DoesNotChangeAggregateReadStatusOrBumpVersions()
    {
        // Arrange
        Guid instanceGuid = _instanceGuid;
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = instanceGuid.ToString();
        element.IsRead = false;
        DataElement dataElement = await CreateLegacyDataElement(element);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Unread);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await dataElementFixture.DataRepo.UpdateReadStatus(
            instanceGuid,
            Guid.Parse(dataElement.Id),
            true
        );
        InstanceInternal instanceInternal = await ReadInstance();

        // Assert
        Assert.True(result.DataElement.IsRead);
        Assert.Equal(ReadStatus.Unread, instanceInternal.Status.ReadStatus);
        Assert.Equal(previousInstanceVersion, result.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, result.Versions.ProcessStateVersion);
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(previousProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_UpdateFileScanStatus_MatchingBlobVersion_UpdatesStatus()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            blobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementWriteResult updateResult =
            await dataElementFixture.DataRepo.UpdateFileScanStatus(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new FileScanStatus
                {
                    FileScanResult = FileScanResult.Clean,
                    BlobVersionId = blobVersionId,
                }
            );

        // Assert
        Assert.NotNull(updateResult);
        Assert.Equal(FileScanResult.Clean, updateResult.DataElement.FileScanResult);
    }

    [Fact]
    public async Task DataElement_UpdateFileScanStatus_StaleBlobVersion_DoesNotUpdateStatus()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.FileScanResult = FileScanResult.Pending;
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        string staleBlobVersionId = BlobVersionId.Encode(Guid.NewGuid());
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            blobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementWriteResult updateResult =
            await dataElementFixture.DataRepo.UpdateFileScanStatus(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new FileScanStatus
                {
                    FileScanResult = FileScanResult.Clean,
                    BlobVersionId = staleBlobVersionId,
                }
            );

        // Assert
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );
        Assert.Null(updateResult);
        Assert.Equal(FileScanResult.Pending, readElement.FileScanResult);
    }

    [Fact]
    public async Task DataElement_UpdateFileScanStatus_MissingElement_IsSuccessfulNoOp()
    {
        // Arrange
        Guid instanceGuid = _instanceGuid;
        Guid missingDataElementId = Guid.NewGuid();

        // Act
        DataElementWriteResult updateResult =
            await dataElementFixture.DataRepo.UpdateFileScanStatus(
                instanceGuid,
                missingDataElementId,
                new FileScanStatus
                {
                    FileScanResult = FileScanResult.Clean,
                    BlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7()),
                }
            );

        // Assert
        Assert.Null(updateResult);
        Assert.False(await dataElementFixture.DataRepo.Exists(missingDataElementId));
    }

    [Fact]
    public async Task DataElement_UpdateFileScanStatus_HardDeletedInstanceAndElement_UpdatesStatus()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instanceGuid.ToString();
        element.FileScanResult = FileScanResult.Pending;
        element.DeleteStatus = new DeleteStatus { IsHardDeleted = true, HardDeleted = _frozenTime };
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            blobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();
        Guid instanceGuid = Guid.Parse(dataElement.InstanceGuid);
        await SetInstanceHardDeleted(instanceGuid);
        StorageVersions expectedVersions = new(
            await ReadInstanceVersion(instanceGuid),
            await ReadProcessStateVersion(instanceGuid)
        );

        // Act
        DataElementWriteResult updateResult =
            await dataElementFixture.DataRepo.UpdateFileScanStatus(
                instanceGuid,
                Guid.Parse(dataElement.Id),
                new FileScanStatus
                {
                    FileScanResult = FileScanResult.Clean,
                    BlobVersionId = blobVersionId,
                }
            );

        // Assert
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(dataElement.Id)
        );
        Assert.NotNull(updateResult);
        Assert.Equal(expectedVersions, updateResult.Versions);
        Assert.Equal(FileScanResult.Clean, updateResult.DataElement.FileScanResult);
        Assert.True(updateResult.DataElement.DeleteStatus.IsHardDeleted);
        Assert.Equal(FileScanResult.Clean, readElement.FileScanResult);
        Assert.True(readElement.DeleteStatus.IsHardDeleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DataElement_UpdateFileScanStatus_MissingBlobVersion_UpdatesStatus(
        string blobVersionId
    )
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.FileScanResult = FileScanResult.Pending;
        string currentBlobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            currentBlobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementWriteResult updateResult =
            await dataElementFixture.DataRepo.UpdateFileScanStatus(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new FileScanStatus
                {
                    FileScanResult = FileScanResult.Clean,
                    BlobVersionId = blobVersionId,
                }
            );

        // Assert
        Assert.NotNull(updateResult);
        Assert.Equal(FileScanResult.Clean, updateResult.DataElement.FileScanResult);
    }

    [Fact]
    public async Task DataElement_UpdateFileScanStatus_InvalidBlobVersion_ThrowsBadRequest()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.FileScanResult = FileScanResult.Pending;
        string currentBlobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            currentBlobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            dataElementFixture.DataRepo.UpdateFileScanStatus(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new FileScanStatus
                {
                    FileScanResult = FileScanResult.Clean,
                    BlobVersionId = "not-a-valid-version",
                }
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCodeSuggestion);
    }

    [Fact]
    public async Task DataElement_Update_BlobVersionId_UpdatesAndPersistsCurrentVersionId()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.BlobStoragePath = $"{_instance.AppId}/{_instance.Id}/data/{element.Id}";
        string firstVersion = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        string secondVersion = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            firstVersion
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(firstVersion),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();
        string versionedBlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            secondVersion
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>
            {
                { "/blobStoragePath", versionedBlobStoragePath },
                { "/currentBlobVersion", secondVersion },
            }
        );
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );

        // Assert
        Assert.NotNull(updatedElement);
        Assert.Equal(versionedBlobStoragePath, readElement.BlobStoragePath);
        Assert.Equal(secondVersion, readElement.BlobVersionId);
        JsonObject serializedReadElement = JsonSerializer.SerializeToNode(readElement).AsObject();
        Assert.False(serializedReadElement.ContainsKey(nameof(DataElementInternal.BlobVersionId)));
    }

    [Fact]
    public async Task DataElement_Update_ExpectedBlobVersionMismatch_WinsBeforeProcessStatusConflictWithoutUpdate()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        string originalContentType = $"original-{Guid.NewGuid()}";
        string newContentType = $"updated-{Guid.NewGuid()}";
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = instanceGuid.ToString();
        element.ContentType = originalContentType;
        element.BlobStoragePath = $"ttd/app/{element.InstanceGuid}/data/{element.Id}";
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "expected-version-test-setup";
        string currentBlobVersionId = await CreateBlobVersionId(instanceGuid, element.Id);
        string replacementBlobVersionId = await CreateBlobVersionId(instanceGuid, element.Id);
        string expectedBlobVersionId = BlobVersionId.Encode(Guid.NewGuid());
        element.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            currentBlobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();
        string replacementBlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(element.InstanceGuid),
            replacementBlobVersionId
        );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        RepositoryException exception =
            await Assert.ThrowsAsync<DataElementBlobVersionMismatchException>(() =>
                UpdateDataElement(
                    instanceGuid,
                    Guid.Parse(dataElement.Id),
                    new Dictionary<string, object>
                    {
                        ["/contentType"] = newContentType,
                        ["/blobStoragePath"] = replacementBlobStoragePath,
                        ["/currentBlobVersion"] = replacementBlobVersionId,
                    },
                    new DataElementUpdateContext
                    {
                        ExpectedCurrentBlobVersion = expectedBlobVersionId,
                    }
                )
            );

        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(dataElement.Id)
        );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(originalContentType, readElement.ContentType);
        Assert.Equal(currentBlobVersionId, readElement.BlobVersionId);
        Assert.Equal(0, await CountAttachedBlobVersionRows(replacementBlobVersionId));
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Theory]
    [InlineData("process-absent")]
    [InlineData("process-null")]
    [InlineData("status-absent")]
    [InlineData("status-null")]
    [InlineData("process-string")]
    public async Task DataElement_Create_IdleProcessRepresentations_SucceedsAndBumpsInstanceVersion(
        string processRepresentation
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessRepresentation(instanceGuid, processRepresentation);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement3);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        dataElement.LastChanged = DateTime.UtcNow;
        dataElement.LastChangedBy = "process-status-idle-test";

        // Act
        DataElementWriteResult result = await dataElementFixture.DataRepo.Create(
            dataElement.FromApiModel(),
            _instanceInternalId
        );

        // Assert
        Assert.Equal(previousInstanceVersion + 1, result.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, result.Versions.ProcessStateVersion);
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
    }

    [Theory]
    [InlineData(ProcessStatus.Processing)]
    public async Task DataElement_Create_NonIdleProcessStatus_ConflictsWithoutMutationOrVersionBump(
        ProcessStatus currentStatus
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, currentStatus);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement3);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();

        // Act
        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                dataElementFixture.DataRepo.Create(dataElement.FromApiModel(), _instanceInternalId)
            );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(currentStatus, exception.CurrentProcessStatus);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_Create_ProcessingStatusWithUnavailableBlob_ReportsProcessStatusConflictFirst()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        string unavailableBlobVersion = BlobVersionId.Encode(Guid.CreateVersion7());
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement3);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        dataElement.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            instanceGuid,
            unavailableBlobVersion
        );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                dataElementFixture.DataRepo.Create(
                    dataElement.FromApiModel(unavailableBlobVersion),
                    _instanceInternalId
                )
            );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
        Assert.Equal(0, await CountBlobVersionRows(unavailableBlobVersion));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_Create_BlobVersion_AttachesOnceAndConflictedRetryDoesNotMutate()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement3);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        dataElement.LastChanged = DateTime.UtcNow;
        dataElement.LastChangedBy = "attach-once-test";
        string blobVersion = await CreateBlobVersionId(instanceGuid, dataElement.Id);
        dataElement.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            instanceGuid,
            blobVersion
        );

        // Act
        DataElementWriteResult result = await dataElementFixture.DataRepo.Create(
            dataElement.FromApiModel(blobVersion),
            _instanceInternalId
        );
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        ProcessStatusConflictException statusException =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                dataElementFixture.DataRepo.Create(
                    dataElement.FromApiModel(blobVersion),
                    _instanceInternalId
                )
            );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Idle);

        // Assert
        Assert.Equal(blobVersion, result.DataElement.BlobVersionId);
        Assert.Equal(ProcessStatus.Processing, statusException.CurrentProcessStatus);
        Assert.Equal(1, await CountBlobVersionRows(blobVersion));
        Assert.Equal(1, await CountAttachedBlobVersionRows(blobVersion));
        Assert.Equal(
            1,
            await PostgresUtil.RunCountQuery(
                $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'"
            )
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_Create_ProcessingStatusWithAvailableBlob_ConflictsWithoutAttachmentOrVersionBump()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement3);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        string availableBlobVersion = await CreateBlobVersionId(instanceGuid, dataElement.Id);
        dataElement.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            instanceGuid,
            availableBlobVersion
        );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                dataElementFixture.DataRepo.Create(
                    dataElement.FromApiModel(availableBlobVersion),
                    _instanceInternalId
                )
            );

        // Assert
        Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
        Assert.Equal(0, await CountAttachedBlobVersionRows(availableBlobVersion));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_Create_StaleInstanceVersionWinsBeforeProcessStatusConflict()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement3);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        string availableBlobVersion = await CreateBlobVersionId(instanceGuid, dataElement.Id);
        dataElement.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            instanceGuid,
            availableBlobVersion
        );

        // Act
        InstanceVersionMismatchException exception =
            await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
                dataElementFixture.DataRepo.Create(
                    dataElement.FromApiModel(availableBlobVersion),
                    _instanceInternalId,
                    expectedInstanceVersion: currentInstanceVersion - 1
                )
            );

        // Assert
        Assert.Equal(currentInstanceVersion, exception.CurrentInstanceVersion);
        Assert.Equal(currentProcessStateVersion, exception.CurrentProcessStateVersion);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
        Assert.Equal(0, await CountAttachedBlobVersionRows(availableBlobVersion));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_UpdateMetadata_ProcessingStatus_ConflictsWithoutMutationOrVersionBump()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        DataElement createdDataElement = await CreateLegacyDataElement(dataElement);
        List<KeyValueEntry> replacementMetadata = [new() { Key = "blocked", Value = "metadata" }];
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                UpdateDataElement(
                    instanceGuid,
                    Guid.Parse(createdDataElement.Id),
                    new Dictionary<string, object> { ["/metadata"] = replacementMetadata }
                )
            );

        // Assert
        DataElementInternal storedDataElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(createdDataElement.Id)
        );
        Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
        Assert.NotEqual(
            JsonSerializer.Serialize(replacementMetadata),
            JsonSerializer.Serialize(storedDataElement.Metadata)
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_UpdateContent_ProcessingStatus_ConflictsWithoutBlobAttachOrVersionBump()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        (DataElement createdDataElement, string currentBlobVersion) =
            await CreateVersionedDataElement(dataElement);
        string replacementBlobVersion = await CreateBlobVersionId(
            instanceGuid,
            createdDataElement.Id
        );
        string replacementStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            instanceGuid,
            replacementBlobVersion
        );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                UpdateDataElement(
                    instanceGuid,
                    Guid.Parse(createdDataElement.Id),
                    new Dictionary<string, object>
                    {
                        ["/contentType"] = "blocked/content",
                        ["/blobStoragePath"] = replacementStoragePath,
                        ["/currentBlobVersion"] = replacementBlobVersion,
                    },
                    new DataElementUpdateContext
                    {
                        IgnoreLock = false,
                        ExpectedCurrentBlobVersion = currentBlobVersion,
                    }
                )
            );

        // Assert
        DataElementInternal storedDataElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(createdDataElement.Id)
        );
        Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
        Assert.Equal(currentBlobVersion, storedDataElement.BlobVersionId);
        Assert.NotEqual("blocked/content", storedDataElement.ContentType);
        Assert.Equal(0, await CountAttachedBlobVersionRows(replacementBlobVersion));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_UpdateContent_ProcessingStatusWithUnavailableBlob_ReportsProcessStatusConflictFirst()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        (DataElement createdDataElement, string currentBlobVersion) =
            await CreateVersionedDataElement(dataElement);
        string unavailableBlobVersion = BlobVersionId.Encode(Guid.CreateVersion7());
        string unavailableStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            instanceGuid,
            unavailableBlobVersion
        );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                UpdateDataElement(
                    instanceGuid,
                    Guid.Parse(createdDataElement.Id),
                    new Dictionary<string, object>
                    {
                        ["/contentType"] = "blocked/unavailable-content",
                        ["/blobStoragePath"] = unavailableStoragePath,
                        ["/currentBlobVersion"] = unavailableBlobVersion,
                    },
                    new DataElementUpdateContext
                    {
                        IgnoreLock = false,
                        ExpectedCurrentBlobVersion = currentBlobVersion,
                    }
                )
            );

        // Assert
        DataElementInternal storedDataElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(createdDataElement.Id)
        );
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
        Assert.Equal(currentBlobVersion, storedDataElement.BlobVersionId);
        Assert.NotEqual("blocked/unavailable-content", storedDataElement.ContentType);
        Assert.Equal(0, await CountBlobVersionRows(unavailableBlobVersion));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_Update_BlobVersion_AttachesOnceAndConflictedRetryDoesNotMutate()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        (DataElement createdDataElement, string currentBlobVersion) =
            await CreateVersionedDataElement(dataElement);
        string replacementBlobVersion = await CreateBlobVersionId(
            instanceGuid,
            createdDataElement.Id
        );
        string replacementStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            instanceGuid,
            replacementBlobVersion
        );

        // Act
        DataElementInternal updatedDataElement = await UpdateDataElement(
            instanceGuid,
            Guid.Parse(createdDataElement.Id),
            new Dictionary<string, object>
            {
                ["/blobStoragePath"] = replacementStoragePath,
                ["/currentBlobVersion"] = replacementBlobVersion,
            },
            new DataElementUpdateContext { ExpectedCurrentBlobVersion = currentBlobVersion }
        );
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        ProcessStatusConflictException statusException =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                UpdateDataElement(
                    instanceGuid,
                    Guid.Parse(createdDataElement.Id),
                    new Dictionary<string, object>
                    {
                        ["/contentType"] = "must/not/commit",
                        ["/currentBlobVersion"] = replacementBlobVersion,
                    },
                    new DataElementUpdateContext
                    {
                        ExpectedCurrentBlobVersion = replacementBlobVersion,
                    }
                )
            );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Idle);

        // Assert
        DataElementInternal storedDataElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(createdDataElement.Id)
        );
        Assert.Equal(replacementBlobVersion, updatedDataElement.BlobVersionId);
        Assert.Equal(ProcessStatus.Processing, statusException.CurrentProcessStatus);
        Assert.Equal(replacementBlobVersion, storedDataElement.BlobVersionId);
        Assert.NotEqual("must/not/commit", storedDataElement.ContentType);
        Assert.Equal(1, await CountBlobVersionRows(replacementBlobVersion));
        Assert.Equal(1, await CountAttachedBlobVersionRows(replacementBlobVersion));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_Update_StaleInstanceVersionWinsBeforeProcessStatusConflict()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        (DataElement createdDataElement, string currentBlobVersion) =
            await CreateVersionedDataElement(dataElement);
        string replacementBlobVersion = await CreateBlobVersionId(
            instanceGuid,
            createdDataElement.Id
        );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        InstanceVersionMismatchException exception =
            await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
                UpdateDataElement(
                    instanceGuid,
                    Guid.Parse(createdDataElement.Id),
                    new Dictionary<string, object>
                    {
                        ["/contentType"] = "must/not/commit",
                        ["/currentBlobVersion"] = replacementBlobVersion,
                    },
                    new DataElementUpdateContext
                    {
                        ExpectedCurrentBlobVersion = currentBlobVersion,
                        ExpectedInstanceVersion = currentInstanceVersion - 1,
                    }
                )
            );

        // Assert
        DataElementInternal storedDataElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(createdDataElement.Id)
        );
        Assert.Equal(currentInstanceVersion, exception.CurrentInstanceVersion);
        Assert.Equal(currentProcessStateVersion, exception.CurrentProcessStateVersion);
        Assert.Equal(currentBlobVersion, storedDataElement.BlobVersionId);
        Assert.NotEqual("must/not/commit", storedDataElement.ContentType);
        Assert.Equal(0, await CountAttachedBlobVersionRows(replacementBlobVersion));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DataElement_UpdateLockStatus_Idle_SucceedsWithoutBumpingVersions(bool locked)
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        dataElement.Locked = !locked;
        DataElement createdDataElement = await CreateLegacyDataElement(dataElement);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await dataElementFixture.DataRepo.UpdateLockStatus(
            instanceGuid,
            Guid.Parse(createdDataElement.Id),
            locked
        );

        // Assert
        Assert.Equal(locked, result.DataElement.Locked);
        Assert.Equal(previousInstanceVersion, result.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, result.Versions.ProcessStateVersion);
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(previousProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Theory]
    [InlineData(true, ProcessStatus.Processing)]
    [InlineData(false, ProcessStatus.Processing)]
    public async Task DataElement_UpdateLockStatus_NonIdleStatus_ConflictsWithoutMutation(
        bool locked,
        ProcessStatus currentStatus
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        dataElement.Locked = !locked;
        DataElement createdDataElement = await CreateLegacyDataElement(dataElement);
        await SetStoredProcessStatus(instanceGuid, currentStatus);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                dataElementFixture.DataRepo.UpdateLockStatus(
                    instanceGuid,
                    Guid.Parse(createdDataElement.Id),
                    locked
                )
            );

        // Assert
        DataElementInternal storedDataElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(createdDataElement.Id)
        );
        Assert.Equal(currentStatus, exception.CurrentProcessStatus);
        Assert.Equal(!locked, storedDataElement.Locked);
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_UpdateReadStatus_ProcessingStatus_RemainsExemptAndDoesNotBumpVersions()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        dataElement.IsRead = false;
        DataElement createdDataElement = await CreateLegacyDataElement(dataElement);
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await dataElementFixture.DataRepo.UpdateReadStatus(
            instanceGuid,
            Guid.Parse(createdDataElement.Id),
            true
        );

        // Assert
        Assert.True(result.DataElement.IsRead);
        Assert.Equal(currentInstanceVersion, result.Versions.InstanceVersion);
        Assert.Equal(currentProcessStateVersion, result.Versions.ProcessStateVersion);
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_UpdateFileScanStatus_ProcessingStatus_RemainsExemptAndDoesNotBumpVersions()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        dataElement.FileScanResult = FileScanResult.Pending;
        (DataElement createdDataElement, string currentBlobVersion) =
            await CreateVersionedDataElement(dataElement);
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await dataElementFixture.DataRepo.UpdateFileScanStatus(
            instanceGuid,
            Guid.Parse(createdDataElement.Id),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean,
                BlobVersionId = currentBlobVersion,
            }
        );

        // Assert
        Assert.NotNull(result);
        Assert.Equal(FileScanResult.Clean, result.DataElement.FileScanResult);
        Assert.Equal(currentInstanceVersion, result.Versions.InstanceVersion);
        Assert.Equal(currentProcessStateVersion, result.Versions.ProcessStateVersion);
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task DataElement_Create_RacingProcessingTransition_SerializesAndDoesNotMutate()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElementInternal dataElement = await PrepareAggregateCreateDataElement(instanceGuid);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        await using NpgsqlConnection gateConnection =
            await dataElementFixture.DataSource.OpenConnectionAsync();
        await using NpgsqlTransaction gateTransaction =
            await gateConnection.BeginTransactionAsync();
        await using (
            NpgsqlCommand lockCommand = new(
                "select 1 from storage.instances where alternateid = $1 for update",
                gateConnection,
                gateTransaction
            )
        )
        {
            lockCommand.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
            Assert.Equal(1, Convert.ToInt32(await lockCommand.ExecuteScalarAsync()));

            Task<DataElementWriteResult> createTask = dataElementFixture.DataRepo.Create(
                dataElement,
                _instanceInternalId
            );

            try
            {
                await WaitForBlockedDatabaseCalls("storage.insertdataelement_v3", expectedCount: 1);
            }
            catch
            {
                await gateTransaction.RollbackAsync();
                try
                {
                    await createTask;
                }
                catch
                {
                    // Observe the task before propagating the synchronization failure.
                }

                throw;
            }

            await using NpgsqlCommand transitionCommand = new(
                """
                update storage.instances
                set instance = jsonb_set(
                        instance,
                        '{Process}',
                        (case
                            when jsonb_typeof(instance -> 'Process') = 'object'
                            then instance -> 'Process'
                            else '{}'::jsonb
                        end) || jsonb_build_object('Status', 'processing')
                    ),
                    instance_version = instance_version + 1,
                    process_state_version = process_state_version + 1
                where alternateid = $1
                """,
                gateConnection,
                gateTransaction
            );
            transitionCommand.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
            Assert.Equal(1, await transitionCommand.ExecuteNonQueryAsync());
            await gateTransaction.CommitAsync();

            ProcessStatusConflictException exception =
                await Assert.ThrowsAsync<ProcessStatusConflictException>(() => createTask);

            // Assert
            Assert.Equal(ProcessStatus.Processing, exception.CurrentProcessStatus);
            Assert.False(await dataElementFixture.DataRepo.Exists(dataElement.Id));
            Assert.Equal(0, await CountAttachedBlobVersionRows(dataElement.BlobVersionId));
            Assert.Equal(previousInstanceVersion + 1, await ReadInstanceVersion(instanceGuid));
            Assert.Equal(
                previousProcessStateVersion + 1,
                await ReadProcessStateVersion(instanceGuid)
            );
        }
    }

    [Fact]
    public async Task CreateBlobVersionId_CreatesUnattachedUuidV7Rows()
    {
        Guid instanceGuid = _instance.Id;
        Guid dataElementId = Guid.NewGuid();

        string firstVersion = await CreateBlobVersionId(instanceGuid, dataElementId.ToString());
        string secondVersion = await CreateBlobVersionId(instanceGuid, dataElementId.ToString());
        Guid firstVersionUuid = BlobVersionId.Decode(firstVersion);
        Guid secondVersionUuid = BlobVersionId.Decode(secondVersion);

        Assert.Equal(22, firstVersion.Length);
        Assert.Equal(22, secondVersion.Length);
        Assert.Equal(7, firstVersionUuid.Version);
        Assert.Equal(7, secondVersionUuid.Version);
        Assert.NotEqual(firstVersion, secondVersion);

        int versionCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id in ('{firstVersionUuid}', '{secondVersionUuid}') and dataelementid = '{dataElementId}' and detachedat is not null and instanceguid = '{instanceGuid}' and appid = '{_instance.AppId}' and blobstorageorg = '{_instance.Org}'"
        );
        Assert.Equal(2, versionCount);
        Assert.Equal(_frozenTime, await ReadBlobVersionDetachedAt(firstVersion));
        Assert.Equal(_frozenTime, await ReadBlobVersionDetachedAt(secondVersion));
    }

    [Fact]
    public async Task DataElement_Update_NewBlobVersion_DetachesTheSupersededVersion()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        (DataElement createdDataElement, string supersededBlobVersion) =
            await CreateVersionedDataElement(dataElement);
        string replacementBlobVersion = await CreateBlobVersionId(
            instanceGuid,
            createdDataElement.Id
        );

        // Act
        DataElementInternal updatedDataElement = await UpdateDataElement(
            instanceGuid,
            Guid.Parse(createdDataElement.Id),
            new Dictionary<string, object>
            {
                ["/blobStoragePath"] = DataElementHelper.GetVersionedBlobPath(
                    _instance.AppId,
                    instanceGuid,
                    replacementBlobVersion
                ),
                ["/currentBlobVersion"] = replacementBlobVersion,
            },
            new DataElementUpdateContext { ExpectedCurrentBlobVersion = supersededBlobVersion }
        );

        // Assert
        Assert.Equal(replacementBlobVersion, updatedDataElement.BlobVersionId);
        Assert.Equal(1, await CountAttachedBlobVersionRows(replacementBlobVersion));
        Assert.Equal(1, await CountDetachedBlobVersionRows(supersededBlobVersion));
        Assert.Equal(1, await CountAttachedBlobVersionRowsForDataElement(createdDataElement.Id));
        Assert.Equal(_frozenTime, await ReadBlobVersionDetachedAt(supersededBlobVersion));
    }

    [Fact]
    public async Task GetOrphanBlobVersionsForCleanup_MeasuresTheGraceWindowFromDetach()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await PostgresUtil.FreezeTime(_frozenTime.AddDays(-8));
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.Id = Guid.NewGuid().ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        (DataElement createdDataElement, string supersededBlobVersion) =
            await CreateVersionedDataElement(dataElement);
        string abandonedBlobVersion = await CreateBlobVersionId(instanceGuid);

        await PostgresUtil.FreezeTime(_frozenTime);
        string replacementBlobVersion = await CreateBlobVersionId(
            instanceGuid,
            createdDataElement.Id
        );
        await UpdateDataElement(
            instanceGuid,
            Guid.Parse(createdDataElement.Id),
            new Dictionary<string, object> { ["/currentBlobVersion"] = replacementBlobVersion },
            new DataElementUpdateContext { ExpectedCurrentBlobVersion = supersededBlobVersion }
        );

        // Act
        List<BlobVersionReferencesInternal> orphanBlobVersions =
            await dataElementFixture.InstanceRepo.GetOrphanBlobVersionsForCleanup(
                CancellationToken.None
            );

        // Assert
        BlobVersionReferencesInternal orphanGroup = Assert.Single(orphanBlobVersions);
        Assert.Equal([abandonedBlobVersion], orphanGroup.BlobVersionIds);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateNewBlobVersion_DetachesTheSupersededVersion()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toUpdate = TestDataUtil.GetDataElement(_dataElement1);
        toUpdate.Id = Guid.NewGuid().ToString();
        toUpdate.InstanceGuid = instanceGuid.ToString();
        (toUpdate, string supersededBlobVersion) = await CreateVersionedDataElement(toUpdate);
        string replacementBlobVersion = await CreateBlobVersionId(instanceGuid, toUpdate.Id);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);

        // Act
        await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload(
                Guid.Parse(toUpdate.Id),
                expectedBlobVersion: supersededBlobVersion,
                newBlobVersion: replacementBlobVersion
            ),
            null,
            null,
            null,
            null
        );

        // Assert
        DataElementInternal updatedDataElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(toUpdate.Id)
        );
        Assert.Equal(replacementBlobVersion, updatedDataElement.BlobVersionId);
        Assert.Equal(1, await CountAttachedBlobVersionRows(replacementBlobVersion));
        Assert.Equal(1, await CountDetachedBlobVersionRows(supersededBlobVersion));
        Assert.Equal(1, await CountAttachedBlobVersionRowsForDataElement(toUpdate.Id));
        Assert.Equal(_frozenTime, await ReadBlobVersionDetachedAt(supersededBlobVersion));
    }

    [Fact]
    public async Task DeleteOrphanBlobVersions_DeletesExactUnattachedVersions()
    {
        Guid instanceGuid = _instance.Id;
        string firstVersion = await CreateBlobVersionId(instanceGuid);
        string secondVersion = await CreateBlobVersionId(instanceGuid);
        Guid firstVersionUuid = BlobVersionId.Decode(firstVersion);
        Guid secondVersionUuid = BlobVersionId.Decode(secondVersion);

        int deletedFirst = await dataElementFixture.DataRepo.DeleteOrphanBlobVersions([
            firstVersion,
        ]);
        int versionCountAfterFirstDelete = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id in ('{firstVersionUuid}', '{secondVersionUuid}')"
        );

        int deletedSecond = await dataElementFixture.DataRepo.DeleteOrphanBlobVersions([
            secondVersion,
        ]);
        int versionCountAfterSecondDelete = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id in ('{firstVersionUuid}', '{secondVersionUuid}')"
        );

        Assert.Equal(1, deletedFirst);
        Assert.Equal(1, versionCountAfterFirstDelete);
        Assert.Equal(1, deletedSecond);
        Assert.Equal(0, versionCountAfterSecondDelete);
    }

    [Fact]
    public async Task DeleteBlobVersions_DeletesExactUnattachedVersionsForDataElement()
    {
        Guid instanceGuid = _instance.Id;
        Guid dataElementId = Guid.NewGuid();
        Guid otherDataElementId = Guid.NewGuid();
        string firstVersion = await CreateBlobVersionId(instanceGuid, dataElementId.ToString());
        string secondVersion = await CreateBlobVersionId(instanceGuid, dataElementId.ToString());
        string otherDataElementVersion = await CreateBlobVersionId(
            instanceGuid,
            otherDataElementId.ToString()
        );

        int deleted = await dataElementFixture.DataRepo.DeleteBlobVersions(
            dataElementId,
            [firstVersion, otherDataElementVersion]
        );

        Assert.Equal(1, deleted);
        Assert.Equal(0, await CountBlobVersionRows(firstVersion));
        Assert.Equal(1, await CountBlobVersionRows(secondVersion));
        Assert.Equal(1, await CountBlobVersionRows(otherDataElementVersion));
    }

    [Fact]
    public async Task DeleteBlobVersions_AttachedBlobVersion_KeepsRow()
    {
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        (DataElement dataElement, string blobVersionId) = await CreateVersionedDataElement(element);

        int deleted = await dataElementFixture.DataRepo.DeleteBlobVersions(
            Guid.Parse(dataElement.Id),
            [blobVersionId]
        );

        Assert.Equal(0, deleted);
        Assert.Equal(1, await CountBlobVersionRows(blobVersionId));
        Assert.Equal(1, await CountAttachedBlobVersionRows(blobVersionId));
    }

    /// <summary>
    /// The deployed baseline's delete path calls deletedataelement_v2 directly. It must
    /// detach the element's blob-version rows so the orphan cleanup job can reclaim the
    /// physical blob when a baseline pod deletes a versioned element during the transition.
    /// </summary>
    [Fact]
    public async Task DeleteDataElementV2Sql_VersionedDataElement_DeletesRowAndDetachesBlobVersions()
    {
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        (DataElement dataElement, string blobVersionId) = await CreateVersionedDataElement(element);

        int deleteCount = await PostgresUtil.RunQuery<int>(
            $"select storage.deletedataelement_v2('{dataElement.Id}', '{dataElement.InstanceGuid}', 'baseline-pod')"
        );

        Assert.Equal(1, deleteCount);
        Assert.Equal(
            0,
            await PostgresUtil.RunCountQuery(
                $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'"
            )
        );
        Assert.Equal(1, await CountBlobVersionRows(blobVersionId));
        Assert.Equal(1, await CountDetachedBlobVersionRows(blobVersionId));
    }

    [Fact]
    public async Task GetOne_InstanceNotFound_ReturnsNull()
    {
        // Arrange
        Guid nonExistentInstanceGuid = Guid.NewGuid();

        // Act
        InstanceInternal instance = await dataElementFixture.InstanceRepo.GetOne(
            nonExistentInstanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        Assert.Null(instance);
    }

    /// <summary>
    /// Test read
    /// </summary>
    [Fact]
    public async Task DataElement_Read_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement();

        // Act
        DataElementInternal readDataelement = await dataElementFixture.DataRepo.Read(
            Guid.Empty,
            dataElement.Id
        );

        // Assert
        Assert.Equal(dataElement.Id, readDataelement.Id);
    }

    /// <summary>
    /// Test delete and change instance read status
    /// </summary>
    [Fact]
    public async Task DeleteBlobVersion_AttachedBlobVersion_ReturnsFalseAndKeepsRow()
    {
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        (DataElement dataElement, string blobVersionId) = await CreateVersionedDataElement(element);

        int versionCountBeforeDelete = await CountBlobVersionRows(blobVersionId);
        int attachedVersionCountBeforeDelete = await CountAttachedBlobVersionRows(blobVersionId);

        bool deleted = await dataElementFixture.DataRepo.DeleteBlobVersion(
            Guid.Parse(dataElement.Id),
            blobVersionId
        );

        Assert.False(deleted);
        Assert.Equal(1, versionCountBeforeDelete);
        Assert.Equal(1, attachedVersionCountBeforeDelete);
        Assert.Equal(1, await CountBlobVersionRows(blobVersionId));
        Assert.Equal(1, await CountAttachedBlobVersionRows(blobVersionId));
    }

    /// <summary>
    /// Test delete and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_DeleteForCleanup_VersionedDataElement_DetachesBlobVersionRows()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.ToString();
        (DataElement dataElement, string blobVersionId) = await CreateVersionedDataElement(element);

        // Act
        int versionCountBeforeDelete = await CountBlobVersionRows(blobVersionId);
        bool deleted = await dataElementFixture.DataRepo.DeleteForCleanup(
            dataElement.FromApiModel()
        );
        int versionCountAfterDelete = await CountBlobVersionRows(blobVersionId);
        int detachedVersionCountAfterDelete = await CountDetachedBlobVersionRows(blobVersionId);

        // Assert
        Assert.True(deleted);
        Assert.Equal(1, versionCountBeforeDelete);
        Assert.Equal(1, versionCountAfterDelete);
        Assert.Equal(1, detachedVersionCountAfterDelete);
    }

    /// <summary>
    /// Test DeleteForInstance
    /// </summary>
    [Fact]
    public async Task DataElement_DeleteForInstance_Ok()
    {
        // Arrange
        await CreateDataElement();
        await CreateDataElement(_dataElement2);

        // Act
        bool deleted = await dataElementFixture.DataRepo.DeleteForInstance(_instanceGuid);

        // Assert
        InstanceInternal instance = await ReadInstance(includeElements: true);
        Assert.True(deleted);
        Assert.Empty(instance.Data);
    }

    [Fact]
    public async Task DataElement_DeleteForInstance_DetachesBlobVersionRowsForCleanup()
    {
        (_, string firstBlobVersionId) = await CreateVersionedDataElement(
            TestDataUtil.GetDataElement(_dataElement1)
        );
        (_, string secondBlobVersionId) = await CreateVersionedDataElement(
            TestDataUtil.GetDataElement(_dataElement2)
        );
        int firstVersionCountBeforeDelete = await CountBlobVersionRows(firstBlobVersionId);
        int secondVersionCountBeforeDelete = await CountBlobVersionRows(secondBlobVersionId);

        bool deleted = await dataElementFixture.DataRepo.DeleteForInstance(_instance.Id);

        int dataElementCountAfterDelete = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelements where instanceguid = '{_instance.Id}'"
        );
        int firstVersionCountAfterDelete = await CountBlobVersionRows(firstBlobVersionId);
        int secondVersionCountAfterDelete = await CountBlobVersionRows(secondBlobVersionId);
        int firstDetachedVersionCountAfterDelete = await CountDetachedBlobVersionRows(
            firstBlobVersionId
        );
        int secondDetachedVersionCountAfterDelete = await CountDetachedBlobVersionRows(
            secondBlobVersionId
        );

        Assert.True(deleted);
        Assert.Equal(1, firstVersionCountBeforeDelete);
        Assert.Equal(1, secondVersionCountBeforeDelete);
        Assert.Equal(0, dataElementCountAfterDelete);
        Assert.Equal(1, firstVersionCountAfterDelete);
        Assert.Equal(1, secondVersionCountAfterDelete);
        Assert.Equal(1, firstDetachedVersionCountAfterDelete);
        Assert.Equal(1, secondDetachedVersionCountAfterDelete);
    }

    /// <summary>
    /// Test update, fail if too many properties
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Too_Many_Properties_Throws_Exception()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement();
        const int numberOfAllowedProperties = 16;

        var tooManyPropertiesDictionary = Enumerable
            .Range(1, numberOfAllowedProperties + 1) // Add one extra property to make it fail.
            .ToDictionary(i => $"Key{i}", object (i) => $"Value{i}");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await UpdateDataElement(Guid.Empty, dataElement.Id, tooManyPropertiesDictionary);
        });
    }

    /// <summary>
    /// Test exists
    /// </summary>
    [Fact]
    public async Task DataElement_Exists_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement();

        // Act
        bool result = await dataElementFixture.DataRepo.Exists(dataElement.Id);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Test exists with no matching data element
    /// </summary>
    [Fact]
    public async Task DataElement_Exists_NotExists_Ok()
    {
        // Act
        bool result = await dataElementFixture.DataRepo.Exists(Guid.Parse(_dataElement1));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AggregateMutation_MixedOperations_CommitsAllChanges()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement existing = TestDataUtil.GetDataElement(_dataElement1);
        (existing, string existingVersion) = await CreateVersionedDataElement(existing);

        DataElement toDelete = TestDataUtil.GetDataElement(_dataElement2);
        (toDelete, _) = await CreateVersionedDataElement(toDelete);

        DataElementInternal toCreate = await PrepareAggregateCreateDataElement(
            instanceGuid,
            Guid.Parse(_dataElement3)
        );

        string updateVersion = await CreateBlobVersionId(instanceGuid, existing.Id);
        string updateBlobPath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(existing.InstanceGuid),
            updateVersion
        );

        InstanceInternal instanceUpdates = new()
        {
            Id = _instance.Id,
            InstanceOwner = _instance.InstanceOwner,
            Org = _instance.Org,
            AppId = _instance.AppId,
            Process = _instance.Process,
            LastChanged = DateTime.UnixEpoch,
            DataValues = new Dictionary<string, string> { ["data-value"] = "stored" },
            PresentationTexts = new Dictionary<string, string> { ["presentation"] = "shown" },
        };
        InstanceMutationCommit mutation = new(
            [toCreate],
            [
                new InstanceMutationDataElementUpdate(
                    Guid.Parse(existing.Id),
                    new Dictionary<string, object>
                    {
                        ["/blobStoragePath"] = updateBlobPath,
                        ["/currentBlobVersion"] = updateVersion,
                    },
                    existingVersion,
                    IgnoreLock: false
                ),
            ],
            [new InstanceMutationDataElementDelete(toDelete.FromApiModel(), IgnoreLock: false)],
            instanceUpdates,
            [nameof(InstanceInternal.PresentationTexts), nameof(InstanceInternal.DataValues)],
            null,
            null,
            []
        );

        // Act
        DateTime applyStarted = new(
            (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMicrosecond) * TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc
        );
        InstanceMutationApplyResult applyResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                mutation
            );
        DateTime applyCompleted = new(
            (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMicrosecond) * TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc
        );
        InstanceInternal updatedInternal = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            true,
            CancellationToken.None
        );
        DataElementInternal updatedExisting = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(existing.Id)
        );

        // Assert
        Assert.False(applyResult.Replayed);
        Assert.NotNull(applyResult.Instance);
        Assert.InRange((DateTime)applyResult.Instance.LastChanged, applyStarted, applyCompleted);
        Assert.Equal(updatedInternal.Versions, applyResult.Instance.Versions);
        Assert.Equal(updatedInternal.Data.Count, applyResult.Instance.Data.Count);
        Assert.Contains(updatedInternal.Data, d => d.Id == toCreate.Id);
        Assert.DoesNotContain(updatedInternal.Data, d => d.Id == new Guid(toDelete.Id));
        Assert.Contains(applyResult.Instance.Data, d => d.Id == toCreate.Id);
        Assert.DoesNotContain(applyResult.Instance.Data, d => d.Id == new Guid(toDelete.Id));
        Assert.Equal(updateVersion, updatedExisting.BlobVersionId);
        Assert.Equal("stored", updatedInternal.DataValues["data-value"]);
        Assert.Equal("shown", updatedInternal.PresentationTexts["presentation"]);
    }

    [Fact]
    public async Task AggregateMutation_DeleteDataElementAndEvent_CommitsDeletedEvent()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toDelete = TestDataUtil.GetDataElement(_dataElement2);
        (toDelete, _) = await CreateVersionedDataElement(toDelete);

        InstanceMutationCommit mutation = new(
            [],
            [],
            [new InstanceMutationDataElementDelete(toDelete.FromApiModel(), IgnoreLock: false)],
            new InstanceInternal
            {
                Id = _instance.Id,
                AppId = _instance.AppId,
                Org = _instance.Org,
                InstanceOwner = _instance.InstanceOwner,
                Created = _instance.Created,
            },
            [],
            null,
            null,
            [
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    DataId = toDelete.Id,
                    Created = DateTime.UtcNow,
                },
            ]
        );

        // Act
        await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation
        );

        // Assert
        InstanceInternal updatedInternal = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            true,
            CancellationToken.None
        );
        Assert.DoesNotContain(updatedInternal.Data, d => d.Id == new Guid(toDelete.Id));
        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted.ToString())
        );
    }

    [Fact]
    public async Task AggregateMutation_ApplyWithInstanceEvent_WritesOutboxRow()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await PostgresUtil.RunSql(
            $"delete from storage.outbox where instanceid = '{instanceGuid}'"
        );
        PgInstanceMutationRepository repository = new(
            dataElementFixture.DataSource,
            new OutboxInsertRowFactory(
                Options.Create(new WolverineSettings { EnableSending = true })
            )
        );
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                AppId = _instance.AppId,
                Org = _instance.Org,
                InstanceOwner = _instance.InstanceOwner,
                Created = _instance.Created,
            },
            [],
            null,
            null,
            [
                new InstanceEvent
                {
                    EventType = InstanceEventType.Saved.ToString(),
                    Created = DateTime.UtcNow,
                },
            ]
        );

        // Act
        await repository.Apply(instanceGuid, _instanceInternalId, mutation);

        // Assert
        Assert.Equal(1, await CountOutboxRows(instanceGuid, InstanceEventType.Saved));
    }

    [Fact]
    public async Task AggregateMutation_ApplyWithInstanceEvent_StampsOutboxValidFromAtSqlInsertTime()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await PostgresUtil.RunSql(
            $"delete from storage.outbox where instanceid = '{instanceGuid}'"
        );
        const int delaySeconds = 123;
        DateTimeOffset frozenAt = new(
            new DateTime(2026, 1, 2, 3, 4, 5, 123, DateTimeKind.Utc).AddTicks(4560)
        );
        PgInstanceMutationRepository repository = new(
            dataElementFixture.DataSource,
            new OutboxInsertRowFactory(
                Options.Create(
                    new WolverineSettings
                    {
                        EnableSending = true,
                        LowPriorityDelaySecs = delaySeconds,
                    }
                )
            )
        );
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                AppId = _instance.AppId,
                Org = _instance.Org,
                InstanceOwner = _instance.InstanceOwner,
                Created = _instance.Created,
            },
            [],
            null,
            null,
            [
                new InstanceEvent
                {
                    EventType = InstanceEventType.Saved.ToString(),
                    Created = DateTime.UtcNow,
                },
            ]
        );

        await PostgresUtil.FreezeTime(frozenAt);
        try
        {
            // Act
            await repository.Apply(instanceGuid, _instanceInternalId, mutation);

            // Assert
            Assert.Equal(
                frozenAt.UtcDateTime.AddSeconds(delaySeconds),
                await ReadOutboxValidFrom(instanceGuid)
            );
        }
        finally
        {
            await PostgresUtil.UnfreezeTime();
        }
    }

    [Fact]
    public async Task AggregateMutation_DeleteDataElementIdempotentReplay_DoesNotDuplicateDeletedEvent()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toDelete = TestDataUtil.GetDataElement(_dataElement2);
        (toDelete, _) = await CreateVersionedDataElement(toDelete);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();

        InstanceMutationCommit firstMutation = CreateDeleteMutation(
            toDelete,
            previousInstanceVersion,
            idempotencyKey
        );
        InstanceMutationCommit retryMutation = CreateDeleteMutation(
            toDelete,
            previousInstanceVersion,
            idempotencyKey
        );

        // Act
        InstanceMutationApplyResult firstResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                firstMutation
            );
        InstanceMutationApplyResult retryResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                retryMutation
            );

        // Assert
        Assert.False(firstResult.Replayed);
        Assert.True(retryResult.Replayed);
        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted.ToString())
        );
    }

    [Fact]
    public async Task AggregateMutation_DeleteInstanceRetryOnHardDeletedInstance_ReplayAdmissionAndApplySucceed()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();
        DateTime deletedAt = new(
            (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMicrosecond) * TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc
        );
        InstanceMutationCommit firstMutation = CreateDeleteInstanceMutation(
            deletedAt,
            previousInstanceVersion,
            idempotencyKey
        );
        InstanceMutationCommit retryMutation = CreateDeleteInstanceMutation(
            deletedAt,
            previousInstanceVersion,
            idempotencyKey
        );

        // Act
        InstanceMutationApplyResult firstResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                firstMutation
            );
        InstanceMutationApplyResult replayAdmission =
            await dataElementFixture.InstanceMutationRepo.TryReplayAdmission(
                instanceGuid,
                previousInstanceVersion,
                firstResult.Instance.Versions.InstanceVersion,
                firstResult.Instance.Versions.ProcessStateVersion,
                idempotencyKey
            );
        InstanceMutationApplyResult retryResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                retryMutation
            );
        InstanceInternal updatedInternal = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        Assert.False(firstResult.Replayed);
        Assert.True(replayAdmission.Replayed);
        Assert.Equal(firstResult.Instance.Versions, replayAdmission.Instance.Versions);
        Assert.True(replayAdmission.Instance.Status.IsHardDeleted);
        Assert.True(retryResult.Replayed);
        Assert.Equal(firstResult.Instance.Versions, retryResult.Instance.Versions);
        Assert.True(updatedInternal.Status.IsHardDeleted);
        Assert.True(updatedInternal.Status.IsSoftDeleted);
        Assert.Equal(deletedAt, updatedInternal.Status.HardDeleted);
        Assert.Equal(deletedAt, updatedInternal.Status.SoftDeleted);
        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted.ToString())
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AggregateMutation_TerminalHardDelete_CommitsEndedIdleDeleteAndCleanupAtomically(
        bool deleteDataElement
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement dataElement = null;
        string blobVersionId = null;
        if (deleteDataElement)
        {
            (dataElement, blobVersionId) = await CreateVersionedDataElement(
                TestDataUtil.GetDataElement(_dataElement2)
            );
        }

        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();
        DateTime processEnded = new(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        DateTime deletedAt = new(2026, 6, 7, 8, 9, 11, DateTimeKind.Utc);
        InstanceMutationCommit mutation = CreateTerminalDeleteInstanceMutation(
            processEnded,
            deletedAt,
            previousInstanceVersion,
            previousProcessStateVersion,
            idempotencyKey,
            dataElement
        );

        // Act
        InstanceMutationApplyResult firstResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                mutation
            );
        InstanceMutationApplyResult replayAdmission =
            await dataElementFixture.InstanceMutationRepo.TryReplayAdmission(
                instanceGuid,
                previousInstanceVersion,
                firstResult.Instance.Versions.InstanceVersion,
                firstResult.Instance.Versions.ProcessStateVersion,
                idempotencyKey
            );
        InstanceMutationApplyResult replayResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                mutation
            );
        InstanceInternal persistedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        using JsonDocument rawInstance = JsonDocument.Parse(
            await ReadStoredInstanceJson(instanceGuid)
        );

        // Assert
        Assert.False(firstResult.Replayed);
        Assert.True(replayAdmission.Replayed);
        Assert.Equal(firstResult.Instance.Versions, replayAdmission.Instance.Versions);
        Assert.True(replayAdmission.Instance.Status.IsHardDeleted);
        Assert.Equal(ProcessStatus.Idle, replayAdmission.Instance.Process.Status);
        Assert.True(replayResult.Replayed);
        Assert.Equal(firstResult.Instance.Versions, replayResult.Instance.Versions);
        Assert.Equal(previousInstanceVersion + 1, firstResult.Instance.Versions.InstanceVersion);
        Assert.Equal(
            previousProcessStateVersion + 1,
            firstResult.Instance.Versions.ProcessStateVersion
        );
        Assert.Equal(ProcessStatus.Idle, firstResult.Instance.Process.Status);
        Assert.Equal(processEnded, firstResult.Instance.Process.Ended);
        Assert.Equal("EndEvent_1", firstResult.Instance.Process.EndEvent);
        Assert.Null(firstResult.Instance.Process.CurrentTask);
        Assert.True(firstResult.Instance.Status.IsHardDeleted);
        Assert.True(firstResult.Instance.Status.IsSoftDeleted);
        Assert.Equal(deletedAt, firstResult.Instance.Status.HardDeleted);
        Assert.Equal(deletedAt, firstResult.Instance.Status.SoftDeleted);
        Assert.Equal(processEnded, firstResult.Instance.Status.Archived);
        Assert.True(firstResult.Instance.Status.IsArchived);
        Assert.Equal(firstResult.Instance.Versions, persistedInstance.Versions);
        Assert.Equal(ProcessStatus.Idle, persistedInstance.Process.Status);
        Assert.Equal(processEnded, persistedInstance.Process.Ended);
        Assert.Equal("EndEvent_1", persistedInstance.Process.EndEvent);
        Assert.Null(persistedInstance.Process.CurrentTask);
        Assert.True(persistedInstance.Status.IsHardDeleted);
        Assert.Equal(1, await CountInstanceRowsWithNullTask(instanceGuid));

        JsonElement rawProcess = rawInstance.RootElement.GetProperty("Process");
        JsonElement rawStatus = rawInstance.RootElement.GetProperty("Status");
        Assert.Equal("idle", rawProcess.GetProperty("Status").GetString());
        Assert.Equal(processEnded, rawProcess.GetProperty("Ended").GetDateTime());
        Assert.Equal("EndEvent_1", rawProcess.GetProperty("EndEvent").GetString());
        Assert.False(rawProcess.TryGetProperty("CurrentTask", out _));
        Assert.True(rawStatus.GetProperty("IsHardDeleted").GetBoolean());
        Assert.True(rawStatus.GetProperty("IsSoftDeleted").GetBoolean());

        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.process_EndEvent.ToString())
        );
        Assert.Equal(
            deleteDataElement ? 2 : 1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted.ToString())
        );
        Assert.Equal(1, await CountIdempotencyRecords(idempotencyKey));
        if (deleteDataElement)
        {
            Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
            Assert.Equal(1, await CountDetachedBlobVersionRows(blobVersionId));
        }
    }

    [Theory]
    [InlineData("instance-version")]
    [InlineData("process-state-version")]
    [InlineData("process-status")]
    [InlineData("element-locked")]
    public async Task AggregateMutation_TerminalHardDelete_FailureRollsBackEntireMutation(
        string failure
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        (DataElement dataElement, string blobVersionId) = await CreateVersionedDataElement(
            TestDataUtil.GetDataElement(_dataElement2)
        );
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        if (failure == "element-locked")
        {
            await SetDataElementLocked(instanceGuid, dataElement.Id, locked: true);
        }

        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        string previousInstanceJson = await ReadStoredInstanceJson(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();
        InstanceMutationCommit mutation = CreateTerminalDeleteInstanceMutation(
            new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc),
            new DateTime(2026, 6, 7, 8, 9, 11, DateTimeKind.Utc),
            failure == "instance-version" ? currentInstanceVersion - 1 : currentInstanceVersion,
            failure == "process-state-version"
                ? currentProcessStateVersion - 1
                : currentProcessStateVersion,
            idempotencyKey,
            dataElement
        );
        if (failure == "process-status")
        {
            mutation = mutation with
            {
                ExpectedInstanceVersion = null,
                ExpectedProcessStateVersion = null,
            };
        }

        // Act
        Exception exception = await Record.ExceptionAsync(() =>
            dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                mutation
            )
        );

        // Assert
        switch (failure)
        {
            case "instance-version":
                Assert.IsType<InstanceVersionMismatchException>(exception);
                break;
            case "process-state-version":
                Assert.IsType<ProcessStateVersionMismatchException>(exception);
                break;
            case "process-status":
                ProcessStatusConflictException statusException =
                    Assert.IsType<ProcessStatusConflictException>(exception);
                Assert.Equal(ProcessStatus.Processing, statusException.CurrentProcessStatus);
                break;
            case "element-locked":
                RepositoryException lockedException = Assert.IsType<RepositoryException>(exception);
                Assert.Equal(HttpStatusCode.Conflict, lockedException.StatusCodeSuggestion);
                Assert.Contains("locked", lockedException.Message, StringComparison.Ordinal);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(failure),
                    failure,
                    "Unknown terminal mutation failure."
                );
        }

        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
        Assert.Equal(previousInstanceJson, await ReadStoredInstanceJson(instanceGuid));
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
        Assert.Equal(1, await CountAttachedBlobVersionRows(blobVersionId));
        Assert.Equal(
            0,
            await CountInstanceEvents(instanceGuid, InstanceEventType.process_EndEvent.ToString())
        );
        Assert.Equal(
            0,
            await CountInstanceEvents(instanceGuid, InstanceEventType.Deleted.ToString())
        );
        Assert.Equal(0, await CountIdempotencyRecords(idempotencyKey));
    }

    [Fact]
    public async Task AggregateMutation_StaleContentVersion_RollsBackEarlierOperations()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement existing = TestDataUtil.GetDataElement(_dataElement1);
        (existing, string currentVersion) = await CreateVersionedDataElement(existing);

        DataElementInternal toCreate = await PrepareAggregateCreateDataElement(
            instanceGuid,
            Guid.Parse(_dataElement2)
        );
        string staleExpectedVersion = await CreateBlobVersionId(instanceGuid, existing.Id);
        string updateVersion = await CreateBlobVersionId(instanceGuid, existing.Id);

        InstanceMutationCommit mutation = new(
            [toCreate],
            [
                new InstanceMutationDataElementUpdate(
                    Guid.Parse(existing.Id),
                    new Dictionary<string, object>
                    {
                        ["/blobStoragePath"] = DataElementHelper.GetVersionedBlobPath(
                            _instance.AppId,
                            new Guid(existing.InstanceGuid),
                            updateVersion
                        ),
                        ["/currentBlobVersion"] = updateVersion,
                    },
                    staleExpectedVersion,
                    IgnoreLock: false
                ),
            ],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            null,
            null,
            []
        );

        // Act
        await Assert.ThrowsAsync<DataElementBlobVersionMismatchException>(() =>
            dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                mutation
            )
        );

        // Assert
        Assert.False(await dataElementFixture.DataRepo.Exists(toCreate.Id));
        DataElementInternal unchangedExisting = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(existing.Id)
        );
        Assert.Equal(currentVersion, unchangedExisting.BlobVersionId);
        Assert.Equal(0, await CountAttachedBlobVersionRows(toCreate.BlobVersionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AggregateMutation_UpdateHardDeletedDataElement_MapsToDataElementNotUpdated(
        bool ignoreLock
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement hardDeletedElement = TestDataUtil.GetDataElement(_dataElement1);
        hardDeletedElement.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        (hardDeletedElement, string currentVersion) = await CreateVersionedDataElement(
            hardDeletedElement
        );
        InstanceMutationCommit mutation = new(
            [],
            [
                new InstanceMutationDataElementUpdate(
                    Guid.Parse(hardDeletedElement.Id),
                    new Dictionary<string, object> { ["/tags"] = new List<string> { "new" } },
                    currentVersion,
                    IgnoreLock: ignoreLock
                ),
            ],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            null,
            null,
            []
        );

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                mutation
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
        Assert.Equal(
            $"Data element {hardDeletedElement.Id} is deleted and cannot be updated.",
            exception.Message
        );
    }

    [Fact]
    public async Task AggregateMutation_CreateDataElementOnHardDeletedInstance_MapsToInstanceDeleted()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElementInternal toCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        await SetInstanceHardDeleted(instanceGuid);
        InstanceMutationCommit mutation = new(
            [toCreate],
            [],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            currentInstanceVersion,
            null,
            []
        );

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                mutation
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
        Assert.Equal(
            $"Instance {instanceGuid} is deleted and cannot be modified.",
            exception.Message
        );
        Assert.False(await dataElementFixture.DataRepo.Exists(toCreate.Id));
        Assert.Equal(0, await CountAttachedBlobVersionRows(toCreate.BlobVersionId));
    }

    [Fact]
    public async Task AggregateMutation_IdempotentReplay_ReturnsFirstCommittedBlob()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement existing = TestDataUtil.GetDataElement(_dataElement1);
        (existing, string currentVersion) = await CreateVersionedDataElement(existing);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();

        string firstUpdateVersion = await CreateBlobVersionId(instanceGuid, existing.Id);
        string retryUpdateVersion = await CreateBlobVersionId(instanceGuid, existing.Id);

        InstanceMutationCommit firstMutation = CreateContentUpdateMutation(
            existing,
            currentVersion,
            firstUpdateVersion,
            previousInstanceVersion,
            idempotencyKey
        );
        InstanceMutationCommit retryMutation = CreateContentUpdateMutation(
            existing,
            currentVersion,
            retryUpdateVersion,
            previousInstanceVersion,
            idempotencyKey
        );

        // Act
        InstanceMutationApplyResult firstResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                firstMutation
            );
        InstanceMutationApplyResult earlyReplayResult =
            await dataElementFixture.InstanceMutationRepo.TryReplayAdmission(
                instanceGuid,
                previousInstanceVersion,
                firstResult.Instance.Versions.InstanceVersion,
                firstResult.Instance.Versions.ProcessStateVersion,
                idempotencyKey
            );
        InstanceVersionMismatchException wrongPreviousVersionException =
            await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
                dataElementFixture.InstanceMutationRepo.TryReplayAdmission(
                    instanceGuid,
                    previousInstanceVersion + 1,
                    firstResult.Instance.Versions.InstanceVersion,
                    firstResult.Instance.Versions.ProcessStateVersion,
                    idempotencyKey
                )
            );
        InstanceMutationApplyResult retryResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                retryMutation
            );
        DataElementInternal updatedExisting = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(existing.Id)
        );

        // Assert
        Assert.False(firstResult.Replayed);
        Assert.True(earlyReplayResult.Replayed);
        Assert.True(retryResult.Replayed);
        Assert.Equal(
            firstResult.Instance.Versions.InstanceVersion,
            wrongPreviousVersionException.CurrentInstanceVersion
        );
        Assert.Equal(firstUpdateVersion, updatedExisting.BlobVersionId);
        Assert.Equal(1, await CountAttachedBlobVersionRows(firstUpdateVersion));
        Assert.Equal(0, await CountAttachedBlobVersionRows(retryUpdateVersion));
    }

    [Fact]
    public async Task AggregateMutation_CreateDataElementsIdempotentReplay_ReturnsCreatedDataElementIds()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();
        Guid missingIdempotencyKey = Guid.NewGuid();
        DataElementInternal firstCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        DataElementInternal secondCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        DataElementInternal retryFirstCreate = await PrepareAggregateCreateDataElement(
            instanceGuid
        );
        DataElementInternal retrySecondCreate = await PrepareAggregateCreateDataElement(
            instanceGuid
        );

        string[] expectedCreatedIds = [firstCreate.Id.ToString(), secondCreate.Id.ToString()];
        string[] retryCreatedIds =
        [
            retryFirstCreate.Id.ToString(),
            retrySecondCreate.Id.ToString(),
        ];

        InstanceMutationCommit firstMutation = CreateCreateMutation(
            [firstCreate, secondCreate],
            previousInstanceVersion,
            idempotencyKey
        );
        InstanceMutationCommit retryMutation = CreateCreateMutation(
            [retryFirstCreate, retrySecondCreate],
            previousInstanceVersion,
            idempotencyKey
        );

        // Act
        InstanceMutationApplyResult firstResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                firstMutation
            );
        InstanceMutationApplyResult earlyReplayResult =
            await dataElementFixture.InstanceMutationRepo.TryReplayAdmission(
                instanceGuid,
                previousInstanceVersion,
                firstResult.Instance.Versions.InstanceVersion,
                firstResult.Instance.Versions.ProcessStateVersion,
                idempotencyKey
            );
        InstanceMutationApplyResult retryResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                retryMutation
            );
        InstanceVersionMismatchException noRecordException =
            await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
                dataElementFixture.InstanceMutationRepo.TryReplayAdmission(
                    instanceGuid,
                    previousInstanceVersion,
                    firstResult.Instance.Versions.InstanceVersion,
                    firstResult.Instance.Versions.ProcessStateVersion,
                    missingIdempotencyKey
                )
            );

        // Assert
        Assert.False(firstResult.Replayed);
        Assert.NotNull(firstResult);
        Assert.Equal(expectedCreatedIds, firstResult.CreatedDataElementIds);
        Assert.True(earlyReplayResult.Replayed);
        Assert.NotNull(earlyReplayResult.Instance);
        Assert.Equal(expectedCreatedIds, earlyReplayResult.CreatedDataElementIds);
        Assert.True(retryResult.Replayed);
        Assert.NotNull(retryResult.Instance);
        Assert.Equal(expectedCreatedIds, retryResult.CreatedDataElementIds);
        Assert.Equal(firstResult.Instance.Versions, earlyReplayResult.Instance.Versions);
        Assert.Equal(firstResult.Instance.Versions, retryResult.Instance.Versions);
        Assert.All(
            expectedCreatedIds,
            createdId =>
                Assert.Contains(
                    retryResult.Instance.Data,
                    dataElement => dataElement.Id.ToString() == createdId
                )
        );
        Assert.DoesNotContain(retryCreatedIds[0], retryResult.CreatedDataElementIds);
        Assert.DoesNotContain(retryCreatedIds[1], retryResult.CreatedDataElementIds);
        Assert.Equal(
            firstResult.Instance.Versions.InstanceVersion,
            noRecordException.CurrentInstanceVersion
        );
    }

    [Fact]
    public async Task AggregateMutation_IdempotencyKeyForDifferentInstance_ReturnsConflict()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        Guid idempotencyKey = Guid.NewGuid();
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        await PostgresUtil.RunSql(
            $"""
            insert into storage.instance_mutation_idempotency
                (idempotency_key, instance, previous_instance_version, produced_instance_version, created_data_element_ids)
            values
                ('{idempotencyKey}', '{Guid.NewGuid()}', {previousInstanceVersion}, {previousInstanceVersion}, ARRAY[]::text[]);
            """
        );
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            previousInstanceVersion,
            null,
            [],
            idempotencyKey
        );

        // Act
        RepositoryException replayException = await Assert.ThrowsAsync<RepositoryException>(() =>
            dataElementFixture.InstanceMutationRepo.TryReplayAdmission(
                instanceGuid,
                previousInstanceVersion,
                previousInstanceVersion,
                previousProcessStateVersion,
                idempotencyKey
            )
        );
        RepositoryException applyException = await Assert.ThrowsAsync<RepositoryException>(() =>
            dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                mutation
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, replayException.StatusCodeSuggestion);
        Assert.Equal(
            "Idempotency key was already used for another instance.",
            replayException.Message
        );
        Assert.Equal(HttpStatusCode.Conflict, applyException.StatusCodeSuggestion);
        Assert.Equal(replayException.Message, applyException.Message);
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task AggregateMutation_DeleteIdempotencyRecordsCreatedBefore_DeletesOnlyExpiredRecords()
    {
        // Arrange
        Guid instanceGuid = Guid.NewGuid();
        Guid expiredKey1 = Guid.NewGuid();
        Guid expiredKey2 = Guid.NewGuid();
        Guid expiredKey3 = Guid.NewGuid();
        Guid boundaryKey = Guid.NewGuid();
        Guid freshKey = Guid.NewGuid();
        DateTime cutoffUtc = new(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        await PostgresUtil.RunSql(
            $"""
            insert into storage.instance_mutation_idempotency
                (instance, previous_instance_version, idempotency_key, produced_instance_version, created, created_data_element_ids)
            values
                ('{instanceGuid}', 1, '{expiredKey1}', 2, '{cutoffUtc.AddSeconds(
                -1
            ):O}', ARRAY[]::text[]),
                ('{instanceGuid}', 2, '{expiredKey2}', 3, '{cutoffUtc.AddSeconds(
                -2
            ):O}', ARRAY[]::text[]),
                ('{instanceGuid}', 3, '{expiredKey3}', 4, '{cutoffUtc.AddSeconds(
                -3
            ):O}', ARRAY[]::text[]),
                ('{instanceGuid}', 4, '{boundaryKey}', 5, '{cutoffUtc:O}', ARRAY[]::text[]),
                ('{instanceGuid}', 5, '{freshKey}', 6, '{cutoffUtc.AddSeconds(
                1
            ):O}', ARRAY[]::text[]);
            """
        );

        // Act
        int deleted =
            await dataElementFixture.InstanceMutationRepo.DeleteIdempotencyRecordsCreatedBefore(
                cutoffUtc,
                batchSize: 2,
                cancellationToken: CancellationToken.None
            );

        // Assert
        Assert.Equal(3, deleted);
        Assert.Equal(0, await CountIdempotencyRecords(expiredKey1));
        Assert.Equal(0, await CountIdempotencyRecords(expiredKey2));
        Assert.Equal(0, await CountIdempotencyRecords(expiredKey3));
        Assert.Equal(1, await CountIdempotencyRecords(boundaryKey));
        Assert.Equal(1, await CountIdempotencyRecords(freshKey));
    }

    [Fact]
    public async Task AggregateMutation_ProcessEndArchive_CommitsProcessAndStatusInOneInstanceUpdate()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DateTime processEnded = new(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        DateTime lastChanged = new(2026, 5, 6, 7, 9, 10, DateTimeKind.Utc);
        ProcessState processState = new()
        {
            Ended = processEnded,
            EndEvent = "EndEvent_1",
            CurrentTask = new ProcessElementInfo { ElementId = "Task_Archive" },
        };
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                AppId = _instance.AppId,
                Org = _instance.Org,
                InstanceOwner = _instance.InstanceOwner,
                Created = _instance.Created,
                LastChanged = lastChanged,
                LastChangedBy = "aggregate-process-end",
                Process = processState,
                Status = new InstanceStatus { IsArchived = true, Archived = processEnded },
            },
            [
                nameof(InstanceInternal.Process),
                nameof(InstanceInternal.LastChanged),
                nameof(InstanceInternal.LastChangedBy),
                nameof(InstanceInternal.Status),
                nameof(InstanceStatus.IsArchived),
                nameof(InstanceStatus.Archived),
            ],
            previousInstanceVersion,
            previousProcessStateVersion,
            []
        );

        // Act
        InstanceMutationApplyResult result = await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation,
            CancellationToken.None
        );
        InstanceInternal updatedInstanceInternal = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        Assert.False(result.Replayed);
        Assert.NotNull(result.Instance);
        Assert.Equal(previousInstanceVersion + 1, result.Instance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion + 1, result.Instance.Versions.ProcessStateVersion);
        Assert.Equal(updatedInstanceInternal.Versions, result.Instance.Versions);
        Assert.Equal("EndEvent_1", updatedInstanceInternal.Process.EndEvent);
        Assert.Equal(processEnded, updatedInstanceInternal.Process.Ended);
        Assert.True(updatedInstanceInternal.Status.IsArchived);
        Assert.Equal(processEnded, updatedInstanceInternal.Status.Archived);
        Assert.Equal("aggregate-process-end", updatedInstanceInternal.LastChangedBy);
        Assert.Equal("Task_Archive", await ReadInstanceTaskId(instanceGuid));
    }

    [Theory]
    [InlineData("status-absent")]
    [InlineData("status-null")]
    [InlineData("process-absent")]
    [InlineData("process-null")]
    public async Task AggregateMutation_AcquireFromIdleWithoutProcessStateVersionFence_SetsProcessingAndBumpsBothVersions(
        string idleRepresentation
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessRepresentation(instanceGuid, idleRepresentation);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                Process = new ProcessState { Status = ProcessStatus.Processing },
            },
            [nameof(InstanceInternal.Process)],
            previousInstanceVersion,
            null,
            [],
            IdempotencyKey: idempotencyKey
        );

        // Act
        InstanceMutationApplyResult result = await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation
        );

        // Assert
        Assert.False(result.Replayed);
        Assert.Equal(ProcessStatus.Processing, result.Instance.Process.Status);
        Assert.Equal(previousInstanceVersion + 1, result.Instance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion + 1, result.Instance.Versions.ProcessStateVersion);
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
    }

    [Theory]
    [InlineData(ProcessStatus.Processing)]
    public async Task AggregateMutation_NonIdleWithoutVersionFences_ReturnsConflictWithCurrentStatus(
        ProcessStatus currentStatus
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, currentStatus);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            null,
            null,
            []
        );

        // Act
        ProcessStatusConflictException exception =
            await Assert.ThrowsAsync<ProcessStatusConflictException>(() =>
                dataElementFixture.InstanceMutationRepo.Apply(
                    instanceGuid,
                    _instanceInternalId,
                    mutation
                )
            );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(currentStatus, exception.CurrentProcessStatus);
        Assert.Contains(
            currentStatus.ToString().ToLowerInvariant(),
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
        Assert.Equal(
            currentStatus.ToString().ToLowerInvariant(),
            await ReadStoredProcessStatus(instanceGuid)
        );
    }

    [Fact]
    public async Task AggregateMutation_NonIdleWithCurrentProcessStateVersionFence_Applies()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                DataValues = new Dictionary<string, string> { ["fenced-write"] = "applied" },
            },
            [nameof(InstanceInternal.DataValues)],
            currentInstanceVersion,
            currentProcessStateVersion,
            []
        );

        // Act
        InstanceMutationApplyResult result = await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation
        );

        // Assert
        Assert.Equal("applied", result.Instance.DataValues["fenced-write"]);
        Assert.True(await InstanceDataValuesContainsKey(instanceGuid, "fenced-write"));
        Assert.Equal(currentInstanceVersion + 1, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
    }

    [Fact]
    public async Task AggregateMutation_NonIdleWithCurrentInstanceVersionFenceOnly_Applies()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                DataValues = new Dictionary<string, string>
                {
                    ["instance-version-fenced-write"] = "applied",
                },
            },
            [nameof(InstanceInternal.DataValues)],
            currentInstanceVersion,
            null,
            []
        );

        // Act
        InstanceMutationApplyResult result = await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation
        );

        // Assert
        Assert.Equal("applied", result.Instance.DataValues["instance-version-fenced-write"]);
        Assert.True(
            await InstanceDataValuesContainsKey(instanceGuid, "instance-version-fenced-write")
        );
        Assert.Equal(currentInstanceVersion + 1, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
    }

    [Fact]
    public async Task AggregateMutation_NonIdleWithStaleProcessStateVersionFence_ReturnsPreconditionFailed()
    {
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                DataValues = new Dictionary<string, string> { ["stale-fenced-write"] = "blocked" },
            },
            [nameof(InstanceInternal.DataValues)],
            currentInstanceVersion,
            currentProcessStateVersion - 1,
            []
        );

        ProcessStateVersionMismatchException exception =
            await Assert.ThrowsAsync<ProcessStateVersionMismatchException>(() =>
                dataElementFixture.InstanceMutationRepo.Apply(
                    instanceGuid,
                    _instanceInternalId,
                    mutation
                )
            );

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCodeSuggestion);
        Assert.Equal(currentInstanceVersion, exception.CurrentInstanceVersion);
        Assert.Equal(currentProcessStateVersion, exception.CurrentProcessStateVersion);
        Assert.False(await InstanceDataValuesContainsKey(instanceGuid, "stale-fenced-write"));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(currentProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
    }

    [Fact]
    public async Task AggregateMutation_StaleInstanceVersionWinsBeforeProcessStatusConflict()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            currentInstanceVersion - 1,
            currentProcessStateVersion,
            []
        );

        // Act
        InstanceVersionMismatchException exception =
            await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
                dataElementFixture.InstanceMutationRepo.Apply(
                    instanceGuid,
                    _instanceInternalId,
                    mutation
                )
            );

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCodeSuggestion);
        Assert.Equal(currentInstanceVersion, exception.CurrentInstanceVersion);
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
    }

    [Fact]
    public async Task AggregateMutation_ProcessingPayload_KeepsStatusAndBumpsBothVersions()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                Process = new ProcessState
                {
                    Status = ProcessStatus.Processing,
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_Keep" },
                },
            },
            [nameof(InstanceInternal.Process)],
            previousInstanceVersion,
            previousProcessStateVersion,
            []
        );

        // Act
        InstanceMutationApplyResult result = await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation
        );

        // Assert
        Assert.Equal(ProcessStatus.Processing, result.Instance.Process.Status);
        Assert.Equal("Task_Keep", result.Instance.Process.CurrentTask.ElementId);
        Assert.Equal(previousInstanceVersion + 1, result.Instance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion + 1, result.Instance.Versions.ProcessStateVersion);
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
    }

    [Fact]
    public async Task AggregateMutation_ClearStatus_CommitsProcessAndIdleInSameVersionBump()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                Process = new ProcessState
                {
                    Status = ProcessStatus.Idle,
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_Clear" },
                },
            },
            [nameof(InstanceInternal.Process)],
            previousInstanceVersion,
            previousProcessStateVersion,
            []
        );

        // Act
        InstanceMutationApplyResult result = await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation
        );

        // Assert
        Assert.Equal(ProcessStatus.Idle, result.Instance.Process.Status);
        Assert.Equal("Task_Clear", result.Instance.Process.CurrentTask.ElementId);
        Assert.Equal(previousInstanceVersion + 1, result.Instance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion + 1, result.Instance.Versions.ProcessStateVersion);
        Assert.Equal("idle", await ReadStoredProcessStatus(instanceGuid));
    }

    [Fact]
    public async Task AggregateMutation_ProcessPayloadWithoutStatus_ClearsProcessing()
    {
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessStatus(instanceGuid, ProcessStatus.Processing);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        InstanceMutationCommit mutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                Process = new ProcessState
                {
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_Missing_Status" },
                },
            },
            [nameof(InstanceInternal.Process)],
            previousInstanceVersion,
            previousProcessStateVersion,
            []
        );

        InstanceMutationApplyResult result = await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation
        );

        Assert.Null(result.Instance.Process.Status);
        Assert.Equal("idle", await ReadStoredProcessStatus(instanceGuid));
        Assert.Equal(previousInstanceVersion + 1, result.Instance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion + 1, result.Instance.Versions.ProcessStateVersion);
    }

    [Fact]
    public async Task AggregateMutation_AcquireReplaySucceedsUntilInstanceAdvances()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessRepresentation(instanceGuid, "status-absent");
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        Guid acquireKey = Guid.NewGuid();
        InstanceMutationCommit acquireMutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                Process = new ProcessState { Status = ProcessStatus.Processing },
            },
            [nameof(InstanceInternal.Process)],
            previousInstanceVersion,
            previousProcessStateVersion,
            [],
            IdempotencyKey: acquireKey
        );

        // Act
        InstanceMutationApplyResult firstResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                acquireMutation
            );
        InstanceMutationApplyResult replayResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                acquireMutation
            );
        InstanceMutationCommit laterMutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                Process = new ProcessState
                {
                    Status = ProcessStatus.Processing,
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_Later" },
                },
            },
            [nameof(InstanceInternal.Process)],
            firstResult.Instance.Versions.InstanceVersion,
            firstResult.Instance.Versions.ProcessStateVersion,
            [],
            IdempotencyKey: Guid.NewGuid()
        );
        InstanceMutationApplyResult laterResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                laterMutation
            );
        InstanceVersionMismatchException staleReplayException =
            await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
                dataElementFixture.InstanceMutationRepo.Apply(
                    instanceGuid,
                    _instanceInternalId,
                    acquireMutation
                )
            );

        // Assert
        Assert.False(firstResult.Replayed);
        Assert.True(replayResult.Replayed);
        Assert.Equal(firstResult.Instance.Versions, replayResult.Instance.Versions);
        Assert.False(laterResult.Replayed);
        Assert.Equal(
            firstResult.Instance.Versions.InstanceVersion + 1,
            laterResult.Instance.Versions.InstanceVersion
        );
        Assert.Equal(
            firstResult.Instance.Versions.ProcessStateVersion + 1,
            laterResult.Instance.Versions.ProcessStateVersion
        );
        Assert.Equal(
            laterResult.Instance.Versions.InstanceVersion,
            staleReplayException.CurrentInstanceVersion
        );
        Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
    }

    [Fact]
    public async Task AggregateMutation_ConcurrentAcquire_CommitsExactlyOnce()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetStoredProcessRepresentation(instanceGuid, "status-absent");
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        Guid firstIdempotencyKey = Guid.NewGuid();
        Guid secondIdempotencyKey = Guid.NewGuid();
        InstanceMutationCommit firstMutation = new(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                Process = new ProcessState { Status = ProcessStatus.Processing },
            },
            [nameof(InstanceInternal.Process)],
            previousInstanceVersion,
            previousProcessStateVersion,
            [],
            IdempotencyKey: firstIdempotencyKey
        );
        InstanceMutationCommit secondMutation = firstMutation with
        {
            IdempotencyKey = secondIdempotencyKey,
        };

        await using NpgsqlConnection gateConnection =
            await dataElementFixture.DataSource.OpenConnectionAsync();
        await using NpgsqlTransaction gateTransaction =
            await gateConnection.BeginTransactionAsync();
        await using (
            NpgsqlCommand lockCommand = new(
                "select 1 from storage.instances where alternateid = $1 for update",
                gateConnection,
                gateTransaction
            )
        )
        {
            lockCommand.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
            Assert.Equal(1, Convert.ToInt32(await lockCommand.ExecuteScalarAsync()));

            Task<InstanceMutationApplyResult> firstTask =
                dataElementFixture.InstanceMutationRepo.Apply(
                    instanceGuid,
                    _instanceInternalId,
                    firstMutation
                );
            Task<InstanceMutationApplyResult> secondTask =
                dataElementFixture.InstanceMutationRepo.Apply(
                    instanceGuid,
                    _instanceInternalId,
                    secondMutation
                );

            try
            {
                await WaitForBlockedAggregateMutations(expectedCount: 2);
            }
            catch
            {
                await gateTransaction.RollbackAsync();
                try
                {
                    await Task.WhenAll(firstTask, secondTask);
                }
                catch
                {
                    // Observe both tasks before propagating the synchronization failure.
                }

                throw;
            }

            await gateTransaction.CommitAsync();

            InstanceMutationApplyResult firstResult = null;
            Exception firstException = await Record.ExceptionAsync(async () =>
            {
                firstResult = await firstTask;
            });
            InstanceMutationApplyResult secondResult = null;
            Exception secondException = await Record.ExceptionAsync(async () =>
            {
                secondResult = await secondTask;
            });

            // Assert
            Assert.Equal(
                1,
                new[] { firstResult, secondResult }.Count(result => result is not null)
            );
            Assert.Equal(
                1,
                new[] { firstException, secondException }.Count(exception => exception is not null)
            );
            InstanceVersionMismatchException losingException =
                Assert.IsType<InstanceVersionMismatchException>(firstException ?? secondException);
            Assert.Equal(previousInstanceVersion + 1, losingException.CurrentInstanceVersion);

            InstanceMutationApplyResult winningResult = firstResult ?? secondResult;
            Assert.False(winningResult.Replayed);
            Guid winningIdempotencyKey = firstResult is not null
                ? firstIdempotencyKey
                : secondIdempotencyKey;
            Guid losingIdempotencyKey = firstResult is null
                ? firstIdempotencyKey
                : secondIdempotencyKey;
            Assert.Equal("processing", await ReadStoredProcessStatus(instanceGuid));
            Assert.Equal(previousInstanceVersion + 1, await ReadInstanceVersion(instanceGuid));
            Assert.Equal(
                previousProcessStateVersion + 1,
                await ReadProcessStateVersion(instanceGuid)
            );
            Assert.Equal(1, await CountIdempotencyRecords(winningIdempotencyKey));
            Assert.Equal(0, await CountIdempotencyRecords(losingIdempotencyKey));
        }
    }

    [Fact]
    public async Task AggregateMutation_IdempotentReplayAfterLaterAggregateUpdate_FailsAsStale()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement existing = TestDataUtil.GetDataElement(_dataElement1);
        (existing, string currentVersion) = await CreateVersionedDataElement(existing);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();
        Guid laterIdempotencyKey = Guid.NewGuid();

        string firstUpdateVersion = await CreateBlobVersionId(instanceGuid, existing.Id);
        InstanceMutationCommit firstMutation = CreateContentUpdateMutation(
            existing,
            currentVersion,
            firstUpdateVersion,
            previousInstanceVersion,
            idempotencyKey
        );
        await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            firstMutation
        );

        int intermediateInstanceVersion = await ReadInstanceVersion(instanceGuid);
        string laterUpdateVersion = await CreateBlobVersionId(instanceGuid, existing.Id);
        InstanceMutationCommit laterMutation = CreateContentUpdateMutation(
            existing,
            firstUpdateVersion,
            laterUpdateVersion,
            intermediateInstanceVersion,
            laterIdempotencyKey
        );
        InstanceMutationApplyResult laterMutationResult =
            await dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                laterMutation
            );

        string retryUpdateVersion = await CreateBlobVersionId(instanceGuid, existing.Id);
        InstanceMutationCommit retryMutation = CreateContentUpdateMutation(
            existing,
            currentVersion,
            retryUpdateVersion,
            previousInstanceVersion,
            idempotencyKey
        );

        // Act/assert
        await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
            dataElementFixture.InstanceMutationRepo.TryReplayAdmission(
                instanceGuid,
                previousInstanceVersion,
                laterMutationResult.Instance.Versions.InstanceVersion,
                laterMutationResult.Instance.Versions.ProcessStateVersion,
                idempotencyKey
            )
        );
        await Assert.ThrowsAsync<InstanceVersionMismatchException>(() =>
            dataElementFixture.InstanceMutationRepo.Apply(
                instanceGuid,
                _instanceInternalId,
                retryMutation
            )
        );
    }

    [Fact]
    public async Task AggregateMutation_DataAndProcessStateAndEvents_CommitsAtomically()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement existing = TestDataUtil.GetDataElement(_dataElement1);
        (existing, string currentVersion) = await CreateVersionedDataElement(existing);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        string updateVersion = await CreateBlobVersionId(instanceGuid, existing.Id);
        Guid idempotencyKey = Guid.NewGuid();
        var processState = new ProcessState
        {
            CurrentTask = new ProcessElementInfo { ElementId = "Task_2" },
        };
        InstanceMutationCommit mutation = new(
            [],
            [
                new InstanceMutationDataElementUpdate(
                    Guid.Parse(existing.Id),
                    new Dictionary<string, object>
                    {
                        ["/blobStoragePath"] = DataElementHelper.GetVersionedBlobPath(
                            _instance.AppId,
                            new Guid(existing.InstanceGuid),
                            updateVersion
                        ),
                        ["/currentBlobVersion"] = updateVersion,
                    },
                    currentVersion,
                    IgnoreLock: false
                ),
            ],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                AppId = _instance.AppId,
                Org = _instance.Org,
                InstanceOwner = _instance.InstanceOwner,
                Created = _instance.Created,
                Process = processState,
            },
            [nameof(InstanceInternal.Process)],
            previousInstanceVersion,
            previousProcessStateVersion,
            [new InstanceEvent { EventType = InstanceEventType.process_StartTask.ToString() }],
            idempotencyKey
        );

        // Act
        InstanceMutationApplyResult result = await dataElementFixture.InstanceMutationRepo.Apply(
            instanceGuid,
            _instanceInternalId,
            mutation
        );

        // Assert
        Assert.False(result.Replayed);
        Assert.NotNull(result.Instance);
        DataElementInternal updatedExisting = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(existing.Id)
        );
        InstanceInternal updatedInstanceInternal = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        Assert.Equal(updateVersion, updatedExisting.BlobVersionId);
        Assert.Equal("Task_2", result.Instance.Process.CurrentTask.ElementId);
        Assert.Equal(updatedInstanceInternal.Versions, result.Instance.Versions);
        Assert.Equal("Task_2", updatedInstanceInternal.Process.CurrentTask.ElementId);
        Assert.Equal(previousProcessStateVersion + 1, await ReadProcessStateVersion(instanceGuid));
        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.process_StartTask.ToString())
        );
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_MixedMutationReplayAndOutbox_CommitsAndReplays()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toUpdate = TestDataUtil.GetDataElement(_dataElement1);
        (toUpdate, _) = await CreateVersionedDataElement(toUpdate);
        DataElement toDelete = TestDataUtil.GetDataElement(_dataElement2);
        (toDelete, _) = await CreateVersionedDataElement(toDelete);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        DataElementInternal firstCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        DataElementInternal secondCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        DateTime instanceLastChanged = new(2026, 5, 6, 7, 9, 10, DateTimeKind.Utc);
        Guid idempotencyKey = Guid.NewGuid();
        Guid missingIdempotencyKey = Guid.NewGuid();
        string[] expectedCreatedIds = [firstCreate.Id.ToString(), secondCreate.Id.ToString()];

        string createElements = CreateElementsPayload([firstCreate, secondCreate]);
        string updateElements = UpdateElementsPayload([
            new UpdateElementPayload(
                Guid.Parse(toUpdate.Id),
                ElementChanges: new JsonObject { ["SqlMixedElementMarker"] = "updated" }
            ),
        ]);
        string deleteElements = DeleteElementsPayload([toDelete]);
        string instanceUpdates = InstanceUpdatePayload(
            InstanceUpdatePayloadItem(
                topLevelSimpleProps: new JsonObject { ["SqlMixedInstanceMarker"] = "instance" },
                process: new JsonObject
                {
                    ["CurrentTask"] = new JsonObject { ["ElementId"] = "Task_Mixed" },
                },
                taskId: "Task_Mixed",
                confirmed: true
            )
        );
        string events = EventsPayload(
            instanceGuid,
            InstanceEventType.Saved,
            _instance.Id.ToString(),
            _instance.InstanceOwner.PartyId
        );
        string outbox = OutboxPayload(_instance.ToApiModel(), 300, InstanceEventType.Saved);

        // Act
        List<ApplyMutationSqlRow> firstRows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            previousProcessStateVersion,
            idempotencyKey,
            createElements,
            updateElements,
            deleteElements,
            instanceUpdates,
            events,
            outbox,
            lastChanged: instanceLastChanged,
            lastChangedBy: "mixed-sql-instance"
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        DataElementInternal retryCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        List<ApplyMutationSqlRow> retryRows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            previousProcessStateVersion,
            idempotencyKey,
            CreateElementsPayload([retryCreate]),
            updateElements,
            deleteElements,
            instanceUpdates,
            events,
            outbox,
            lastChanged: instanceLastChanged,
            lastChangedBy: "mixed-sql-instance"
        );
        PostgresException noRecordException = await Assert.ThrowsAsync<PostgresException>(() =>
            TryReplayInstanceMutationV2Sql(
                missingIdempotencyKey,
                instanceGuid,
                previousInstanceVersion,
                previousInstanceVersion + 1,
                previousProcessStateVersion + 1
            )
        );
        IReadOnlyList<string> replayCreatedDataElementIds = await TryReplayInstanceMutationV2Sql(
            idempotencyKey,
            instanceGuid,
            previousInstanceVersion,
            previousInstanceVersion + 1,
            previousProcessStateVersion + 1
        );
        await BumpInstanceVersion(instanceGuid);
        PostgresException mismatchException = await Assert.ThrowsAsync<PostgresException>(() =>
            TryReplayInstanceMutationV2Sql(
                idempotencyKey,
                instanceGuid,
                previousInstanceVersion,
                previousInstanceVersion + 2,
                previousProcessStateVersion + 1
            )
        );
        PostgresException instanceMismatchException = await Assert.ThrowsAsync<PostgresException>(
            () =>
                TryReplayInstanceMutationV2Sql(
                    idempotencyKey,
                    Guid.NewGuid(),
                    previousInstanceVersion,
                    previousInstanceVersion + 1,
                    previousProcessStateVersion + 1
                )
        );

        // Assert
        int producedInstanceVersion = previousInstanceVersion + 1;
        int producedProcessStateVersion = previousProcessStateVersion + 1;
        AssertAppliedRows(
            firstRows,
            producedInstanceVersion,
            producedProcessStateVersion,
            expectedCreatedIds,
            expectedInternalId: _instanceInternalId
        );
        Assert.Contains(
            firstRows,
            row => row.CurrentBlobVersion == BlobVersionId.Decode(firstCreate.BlobVersionId)
        );
        Assert.Contains(
            firstRows,
            row => row.CurrentBlobVersion == BlobVersionId.Decode(secondCreate.BlobVersionId)
        );
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(toDelete.Id)));
        Assert.True(await dataElementFixture.DataRepo.Exists(firstCreate.Id));
        Assert.True(await dataElementFixture.DataRepo.Exists(secondCreate.Id));
        Assert.Equal(
            "updated",
            await ReadDataElementJsonText(instanceGuid, toUpdate.Id, "SqlMixedElementMarker")
        );
        Assert.Equal(
            "instance",
            await ReadInstanceJsonText(instanceGuid, "SqlMixedInstanceMarker")
        );
        Assert.Equal(producedInstanceVersion, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(producedProcessStateVersion, updatedInstance.Versions.ProcessStateVersion);
        Assert.Equal(instanceLastChanged, updatedInstance.LastChanged);
        Assert.Equal("mixed-sql-instance", updatedInstance.LastChangedBy);
        Assert.Equal("Task_Mixed", updatedInstance.Process.CurrentTask.ElementId);
        Assert.Equal("Task_Mixed", await ReadInstanceTaskId(instanceGuid));
        Assert.True(await ReadInstanceConfirmed(instanceGuid));
        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.Saved.ToString())
        );
        Assert.Equal(1, await CountOutboxRows(instanceGuid, InstanceEventType.Saved));
        Assert.Equal(1, await CountIdempotencyRecords(idempotencyKey));
        Assert.Equal(
            producedInstanceVersion,
            await ReadIdempotencyProducedInstanceVersion(idempotencyKey)
        );
        AssertAppliedRows(
            retryRows,
            producedInstanceVersion,
            producedProcessStateVersion,
            expectedCreatedIds,
            replayed: true
        );
        Assert.False(await dataElementFixture.DataRepo.Exists(retryCreate.Id));
        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.Saved.ToString())
        );

        AssertSqlError(
            noRecordException,
            "idempotency_key_not_found",
            producedInstanceVersion,
            producedProcessStateVersion
        );
        Assert.Equal(expectedCreatedIds, replayCreatedDataElementIds);
        AssertSqlError(
            mismatchException,
            "instance_already_advanced",
            producedInstanceVersion + 1,
            producedProcessStateVersion
        );
        AssertSqlError(
            instanceMismatchException,
            "idempotency_key_instance_mismatch",
            producedInstanceVersion,
            producedProcessStateVersion
        );
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_MultipleCreates_PreservesOrderAndBumpsOnce()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        DateTime mutationLastChanged = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime firstLastChanged = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DateTime secondLastChanged = new(2026, 1, 2, 3, 5, 5, DateTimeKind.Utc);
        DataElementInternal firstCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        firstCreate.IsRead = false;
        firstCreate.LastChanged = firstLastChanged;
        firstCreate.LastChangedBy = "first-sql-create";
        DataElementInternal secondCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        secondCreate.IsRead = true;
        secondCreate.LastChanged = secondLastChanged;
        secondCreate.LastChangedBy = "second-sql-create";

        string[] expectedCreatedIds = [firstCreate.Id.ToString(), secondCreate.Id.ToString()];

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            CreateElementsPayload([firstCreate, secondCreate]),
            null,
            null,
            null,
            null,
            null
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        DataElementInternal createdFirstElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            firstCreate.Id
        );
        DataElementInternal createdSecondElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            secondCreate.Id
        );

        // Assert
        int producedInstanceVersion = previousInstanceVersion + 1;
        AssertAppliedRows(
            rows,
            producedInstanceVersion,
            previousProcessStateVersion,
            expectedCreatedIds
        );
        Assert.True(await dataElementFixture.DataRepo.Exists(firstCreate.Id));
        Assert.True(await dataElementFixture.DataRepo.Exists(secondCreate.Id));
        Assert.Equal(1, await CountAttachedBlobVersionRows(firstCreate.BlobVersionId));
        Assert.Equal(1, await CountAttachedBlobVersionRows(secondCreate.BlobVersionId));
        Assert.Equal(producedInstanceVersion, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, updatedInstance.Versions.ProcessStateVersion);
        Assert.Equal(ReadStatus.UpdatedSinceLastReview, updatedInstance.Status.ReadStatus);
        Assert.Equal(mutationLastChanged, updatedInstance.LastChanged);
        Assert.Equal("sql-test-actor", updatedInstance.LastChangedBy);
        Assert.Equal(mutationLastChanged, createdFirstElement.LastChanged);
        Assert.Equal("sql-test-actor", createdFirstElement.LastChangedBy);
        Assert.Equal(mutationLastChanged, createdSecondElement.LastChanged);
        Assert.Equal("sql-test-actor", createdSecondElement.LastChangedBy);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_CreateWithoutLastChanged_StampsMutationTimestampAndStripsNullActor()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DataElementInternal toCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        toCreate.LastChanged = null;
        toCreate.LastChangedBy = null;
        DateTime mutationLastChanged = new(2026, 1, 2, 3, 4, 11, DateTimeKind.Utc);

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            CreateElementsPayloadWithoutLastChanged([toCreate]),
            null,
            null,
            null,
            null,
            null,
            lastChanged: mutationLastChanged,
            lastChangedBy: null
        );
        DataElementInternal createdElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            toCreate.Id
        );

        // Assert
        ApplyMutationSqlRow row = Assert.Single(rows);
        Assert.False(row.Replayed);
        Assert.Equal([toCreate.Id.ToString()], row.CreatedDataElementIds);
        Assert.Equal(previousInstanceVersion + 1, row.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, row.ProcessStateVersion);
        Assert.True(await dataElementFixture.DataRepo.Exists(toCreate.Id));
        Assert.Equal(1, await CountAttachedBlobVersionRows(toCreate.BlobVersionId));
        Assert.Equal(mutationLastChanged, await ReadInstanceLastChangedColumn(instanceGuid));
        Assert.Equal(mutationLastChanged, createdElement.LastChanged);
        Assert.Null(createdElement.LastChangedBy);
        Assert.Equal(
            "string",
            await ReadDataElementJsonType(instanceGuid, toCreate.Id.ToString(), "LastChanged")
        );
        Assert.Equal(
            "missing",
            await ReadDataElementJsonType(instanceGuid, toCreate.Id.ToString(), "LastChangedBy")
        );
        Assert.Equal("string", await ReadInstanceJsonType(instanceGuid, "LastChanged"));
        Assert.Equal("null", await ReadInstanceJsonType(instanceGuid, "LastChangedBy"));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_MultipleDeletes_PreservesOrderAndBumpsOnce()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement firstDelete = TestDataUtil.GetDataElement(_dataElement1);
        firstDelete.IsRead = true;
        firstDelete.LastChangedBy = "first-sql-delete";
        (firstDelete, string firstBlobVersionId) = await CreateVersionedDataElement(firstDelete);
        DataElement secondDelete = TestDataUtil.GetDataElement(_dataElement2);
        secondDelete.IsRead = false;
        secondDelete.LastChangedBy = "second-sql-delete";
        (secondDelete, string secondBlobVersionId) = await CreateVersionedDataElement(secondDelete);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DateTime mutationLastChanged = new(2026, 1, 2, 3, 4, 9, DateTimeKind.Utc);

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            null,
            DeleteElementsPayload([firstDelete, secondDelete]),
            null,
            null,
            null,
            lastChanged: mutationLastChanged,
            lastChangedBy: "sql-delete"
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        int producedInstanceVersion = previousInstanceVersion + 1;
        AssertAppliedRows(rows, producedInstanceVersion, previousProcessStateVersion);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(firstDelete.Id)));
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(secondDelete.Id)));
        Assert.Equal(1, await CountDetachedBlobVersionRows(firstBlobVersionId));
        Assert.Equal(1, await CountDetachedBlobVersionRows(secondBlobVersionId));
        Assert.Equal(producedInstanceVersion, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, updatedInstance.Versions.ProcessStateVersion);
        Assert.Equal(ReadStatus.Unread, updatedInstance.Status.ReadStatus);
        Assert.Equal(mutationLastChanged, updatedInstance.LastChanged);
        Assert.Equal("sql-delete", updatedInstance.LastChangedBy);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_DeleteWithNullLastChangedBy_DoesNotCollapseDocument()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toDelete = TestDataUtil.GetDataElement(_dataElement1);
        toDelete.IsRead = false;
        toDelete.LastChangedBy = "delete-null-setup";
        (toDelete, string blobVersionId) = await CreateVersionedDataElement(toDelete);
        await PostgresUtil.RunSql(
            $"update storage.dataelements set element = jsonb_set(element, '{{LastChangedBy}}', 'null'::jsonb) where instanceguid = '{instanceGuid}' and alternateid = '{toDelete.Id}'"
        );
        toDelete.LastChangedBy = null;
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DateTime mutationLastChanged = new(2026, 1, 2, 3, 4, 10, DateTimeKind.Utc);

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            null,
            DeleteElementsPayload([toDelete]),
            null,
            null,
            null,
            lastChanged: mutationLastChanged,
            lastChangedBy: null
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        ApplyMutationSqlRow row = Assert.Single(rows);
        Assert.False(row.Replayed);
        Assert.Empty(row.CreatedDataElementIds);
        Assert.Equal(previousInstanceVersion + 1, row.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, row.ProcessStateVersion);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(toDelete.Id)));
        Assert.Equal(1, await CountDetachedBlobVersionRows(blobVersionId));
        Assert.Equal(previousInstanceVersion + 1, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(mutationLastChanged, updatedInstance.LastChanged);
        Assert.Null(updatedInstance.LastChangedBy);
        Assert.Equal("null", await ReadInstanceJsonType(instanceGuid, "LastChangedBy"));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_DeleteOnlyReadElement_SetsAggregateReadStatusUnread()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toDelete = TestDataUtil.GetDataElement(_dataElement1);
        toDelete.IsRead = true;
        toDelete.LastChangedBy = "only-read-sql-delete";
        (toDelete, _) = await CreateVersionedDataElement(toDelete);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);

        // Act
        await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            null,
            DeleteElementsPayload([toDelete]),
            null,
            null,
            null
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(toDelete.Id)));
        Assert.Equal(previousInstanceVersion + 1, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(ReadStatus.Unread, updatedInstance.Status.ReadStatus);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_MultipleUpdates_PreservesOrderAndPerUpdateInstanceEffects()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement firstUpdate = TestDataUtil.GetDataElement(_dataElement1);
        (firstUpdate, _) = await CreateVersionedDataElement(firstUpdate);
        DataElement secondUpdate = TestDataUtil.GetDataElement(_dataElement2);
        (secondUpdate, _) = await CreateVersionedDataElement(secondUpdate);
        DataElement thirdUpdate = TestDataUtil.GetDataElement(_dataElement3);
        thirdUpdate.Id = Guid.NewGuid().ToString();
        thirdUpdate.InstanceGuid = instanceGuid.ToString();
        thirdUpdate.Created ??= DateTime.UtcNow;
        thirdUpdate.CreatedBy ??= "1337";
        thirdUpdate.LastChanged ??= DateTime.UtcNow;
        thirdUpdate.LastChangedBy ??= "1337";
        (thirdUpdate, _) = await CreateVersionedDataElement(thirdUpdate);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        string secondNewBlobVersion = await CreateBlobVersionId(instanceGuid, secondUpdate.Id);
        DateTime mutationLastChanged = new(2026, 2, 3, 4, 7, 6, DateTimeKind.Utc);
        DataElementInternal firstBeforeUpdate = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(firstUpdate.Id)
        );
        DataElementInternal thirdBeforeUpdate = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(thirdUpdate.Id)
        );

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload([
                new UpdateElementPayload(
                    Guid.Parse(firstUpdate.Id),
                    ElementChanges: new JsonObject { ["SqlElementMarker"] = "first" }
                ),
                new UpdateElementPayload(
                    Guid.Parse(secondUpdate.Id),
                    ElementChanges: new JsonObject { ["SqlElementMarker"] = "second" },
                    NewBlobVersion: secondNewBlobVersion
                ),
                new UpdateElementPayload(
                    Guid.Parse(thirdUpdate.Id),
                    ElementChanges: new JsonObject { ["SqlElementMarker"] = "third" }
                ),
            ]),
            null,
            null,
            null,
            null,
            lastChanged: mutationLastChanged,
            lastChangedBy: "mutation-sql-update"
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        DataElementInternal updatedSecondElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(secondUpdate.Id)
        );
        DataElementInternal updatedFirstElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(firstUpdate.Id)
        );
        DataElementInternal updatedThirdElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(thirdUpdate.Id)
        );

        // Assert
        int producedInstanceVersion = previousInstanceVersion + 1;
        AssertAppliedRows(rows, producedInstanceVersion, previousProcessStateVersion);
        Assert.Equal(producedInstanceVersion, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion, updatedInstance.Versions.ProcessStateVersion);
        Assert.Equal(mutationLastChanged, updatedInstance.LastChanged);
        Assert.Equal("mutation-sql-update", updatedInstance.LastChangedBy);
        Assert.False(await InstanceJsonContainsKey(instanceGuid, "SqlUpdateMarker"));
        Assert.False(await InstanceJsonContainsKey(instanceGuid, "SqlSkipped"));
        Assert.Equal(
            "first",
            await ReadDataElementJsonText(instanceGuid, firstUpdate.Id, "SqlElementMarker")
        );
        Assert.Equal(
            "second",
            await ReadDataElementJsonText(instanceGuid, secondUpdate.Id, "SqlElementMarker")
        );
        Assert.Equal(
            "third",
            await ReadDataElementJsonText(instanceGuid, thirdUpdate.Id, "SqlElementMarker")
        );
        Assert.Equal(secondNewBlobVersion, updatedSecondElement.BlobVersionId);
        Assert.Equal(1, await CountAttachedBlobVersionRows(secondNewBlobVersion));
        Assert.Equal(firstBeforeUpdate.LastChanged, updatedFirstElement.LastChanged);
        Assert.Equal(firstBeforeUpdate.LastChangedBy, updatedFirstElement.LastChangedBy);
        Assert.Equal(mutationLastChanged, updatedSecondElement.LastChanged);
        Assert.Equal("mutation-sql-update", updatedSecondElement.LastChangedBy);
        Assert.Equal(thirdBeforeUpdate.LastChanged, updatedThirdElement.LastChanged);
        Assert.Equal(thirdBeforeUpdate.LastChangedBy, updatedThirdElement.LastChangedBy);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_LockOnlyUpdate_PreservesTheContentAuthorStamp()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toLock = TestDataUtil.GetDataElement(_dataElement1);
        (toLock, _) = await CreateVersionedDataElement(toLock);
        Guid dataElementId = Guid.Parse(toLock.Id);
        DataElementInternal beforeLock = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            dataElementId
        );
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        DateTime mutationLastChanged = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload([
                new UpdateElementPayload(
                    dataElementId,
                    ElementChanges: new JsonObject { ["Locked"] = true }
                ),
            ]),
            null,
            null,
            null,
            null,
            lastChanged: mutationLastChanged,
            lastChangedBy: "workflow-service-owner"
        );
        DataElementInternal lockedElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            dataElementId
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        AssertAppliedRows(
            rows,
            previousInstanceVersion + 1,
            await ReadProcessStateVersion(instanceGuid)
        );
        Assert.True(lockedElement.Locked);
        Assert.Equal(beforeLock.LastChanged, lockedElement.LastChanged);
        Assert.Equal(beforeLock.LastChangedBy, lockedElement.LastChangedBy);
        Assert.Equal(mutationLastChanged, updatedInstance.LastChanged);
        Assert.Equal("workflow-service-owner", updatedInstance.LastChangedBy);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_ContentUpdate_StampsTheMutationActor()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toUpdate = TestDataUtil.GetDataElement(_dataElement2);
        (toUpdate, _) = await CreateVersionedDataElement(toUpdate);
        Guid dataElementId = Guid.Parse(toUpdate.Id);
        DataElementInternal beforeUpdate = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            dataElementId
        );
        string newBlobVersion = await CreateBlobVersionId(instanceGuid, toUpdate.Id);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        DateTime mutationLastChanged = new(2026, 3, 4, 5, 6, 8, DateTimeKind.Utc);

        // Act
        await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload([
                new UpdateElementPayload(dataElementId, NewBlobVersion: newBlobVersion),
            ]),
            null,
            null,
            null,
            null,
            lastChanged: mutationLastChanged,
            lastChangedBy: "patching-party"
        );
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            dataElementId
        );

        // Assert
        Assert.Equal(newBlobVersion, updatedElement.BlobVersionId);
        Assert.NotEqual(beforeUpdate.LastChangedBy, updatedElement.LastChangedBy);
        Assert.Equal(mutationLastChanged, updatedElement.LastChanged);
        Assert.Equal("patching-party", updatedElement.LastChangedBy);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateReadStatus_UsesFinalStateWhenFalseThenTrue()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement firstUpdate = TestDataUtil.GetDataElement(_dataElement1);
        firstUpdate.IsRead = true;
        firstUpdate = await CreateLegacyDataElement(firstUpdate);
        DataElement secondUpdate = TestDataUtil.GetDataElement(_dataElement2);
        secondUpdate.IsRead = false;
        secondUpdate = await CreateLegacyDataElement(secondUpdate);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);

        // Act
        await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload([
                new UpdateElementPayload(
                    Guid.Parse(firstUpdate.Id),
                    ElementChanges: new JsonObject { ["IsRead"] = false },
                    IsReadChangedToFalse: true
                ),
                new UpdateElementPayload(
                    Guid.Parse(secondUpdate.Id),
                    ElementChanges: new JsonObject { ["IsRead"] = true }
                ),
            ]),
            null,
            null,
            null,
            null
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        DataElementInternal updatedFirstElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(firstUpdate.Id)
        );
        DataElementInternal updatedSecondElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(secondUpdate.Id)
        );

        // Assert
        Assert.Equal(previousInstanceVersion + 1, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(ReadStatus.Read, updatedInstance.Status.ReadStatus);
        Assert.False(updatedFirstElement.IsRead);
        Assert.True(updatedSecondElement.IsRead);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateReadStatus_UsesFinalStateWhenTrueThenFalse()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement elementA = TestDataUtil.GetDataElement(_dataElement1);
        elementA.IsRead = true;
        elementA = await CreateLegacyDataElement(elementA);
        DataElement elementB = TestDataUtil.GetDataElement(_dataElement2);
        elementB.IsRead = false;
        elementB = await CreateLegacyDataElement(elementB);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);

        // Act
        await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload([
                new UpdateElementPayload(
                    Guid.Parse(elementB.Id),
                    ElementChanges: new JsonObject { ["IsRead"] = true }
                ),
                new UpdateElementPayload(
                    Guid.Parse(elementA.Id),
                    ElementChanges: new JsonObject { ["IsRead"] = false },
                    IsReadChangedToFalse: true
                ),
            ]),
            null,
            null,
            null,
            null
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        DataElementInternal updatedElementA = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(elementA.Id)
        );
        DataElementInternal updatedElementB = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(elementB.Id)
        );

        // Assert
        Assert.Equal(previousInstanceVersion + 1, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(ReadStatus.Read, updatedInstance.Status.ReadStatus);
        Assert.False(updatedElementA.IsRead);
        Assert.True(updatedElementB.IsRead);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_FlatInstanceUpdate_ComposesBranchesAndBumpsOnce()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        string seedJson = """
            {
              "DataValues": {
                "sql-preserved-data": "preserved",
                "sql-overwritten-data": "old"
              },
              "PresentationTexts": {
                "sql-preserved-presentation": "preserved",
                "sql-overwritten-presentation": "old"
              },
              "CompleteConfirmations": [
                {
                  "StakeholderId": "existing",
                  "ConfirmedOn": "2026-01-01T00:00:00Z"
                }
              ]
            }
            """;
        await PostgresUtil.RunSql(
            $"update storage.instances set instance = instance || '{seedJson}'::jsonb where alternateid = '{instanceGuid}'"
        );

        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DateTime lastChanged = new(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        string instanceUpdates = InstanceUpdatePayload(
            InstanceUpdatePayloadItem(
                topLevelSimpleProps: new JsonObject
                {
                    ["SqlTopData"] = "data-top",
                    ["SqlTopPresentation"] = "presentation-top",
                    ["SqlTopComplete"] = "complete-top",
                    ["SqlTopStatus"] = "status-top",
                    ["SqlTopSubstatus"] = "substatus-top",
                    ["SqlTopProcess"] = "process-top",
                },
                dataValues: new JsonObject
                {
                    ["sql-overwritten-data"] = "new",
                    ["sql-added-data"] = "added",
                },
                presentationTexts: new JsonObject
                {
                    ["sql-overwritten-presentation"] = "new",
                    ["sql-added-presentation"] = "added",
                },
                completeConfirmations: new JsonArray
                {
                    new JsonObject
                    {
                        ["StakeholderId"] = _instance.Org,
                        ["ConfirmedOn"] = lastChanged.ToUniversalTime(),
                    },
                },
                status: new JsonObject { ["IsArchived"] = true },
                substatus: new JsonObject { ["Label"] = "substatus-label", ["Description"] = null },
                process: new JsonObject
                {
                    ["CurrentTask"] = new JsonObject { ["ElementId"] = "Task_9" },
                },
                taskId: "Task_9",
                confirmed: true
            )
        );

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            previousProcessStateVersion,
            null,
            null,
            null,
            null,
            instanceUpdates,
            null,
            null
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        int producedInstanceVersion = previousInstanceVersion + 1;
        AssertAppliedRows(rows, producedInstanceVersion, previousProcessStateVersion + 1);
        Assert.Equal(producedInstanceVersion, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion + 1, updatedInstance.Versions.ProcessStateVersion);
        Assert.Equal("preserved", updatedInstance.DataValues["sql-preserved-data"]);
        Assert.Equal("new", updatedInstance.DataValues["sql-overwritten-data"]);
        Assert.Equal("added", updatedInstance.DataValues["sql-added-data"]);
        Assert.Equal("preserved", updatedInstance.PresentationTexts["sql-preserved-presentation"]);
        Assert.Equal("new", updatedInstance.PresentationTexts["sql-overwritten-presentation"]);
        Assert.Equal("added", updatedInstance.PresentationTexts["sql-added-presentation"]);
        Assert.Contains(
            updatedInstance.CompleteConfirmations,
            confirmation => confirmation.StakeholderId == "existing"
        );
        Assert.Contains(
            updatedInstance.CompleteConfirmations,
            confirmation => confirmation.StakeholderId == _instance.Org
        );
        Assert.True(updatedInstance.Status.IsArchived);
        Assert.Equal("substatus-label", updatedInstance.Status.Substatus.Label);
        Assert.Null(updatedInstance.Status.Substatus.Description);
        Assert.Equal("Task_9", updatedInstance.Process.CurrentTask.ElementId);
        Assert.Equal("Task_9", await ReadInstanceTaskId(instanceGuid));
        Assert.True(await ReadInstanceConfirmed(instanceGuid));
        Assert.Equal("data-top", await ReadInstanceJsonText(instanceGuid, "SqlTopData"));
        Assert.Equal(
            "presentation-top",
            await ReadInstanceJsonText(instanceGuid, "SqlTopPresentation")
        );
        Assert.Equal("complete-top", await ReadInstanceJsonText(instanceGuid, "SqlTopComplete"));
        Assert.Equal("status-top", await ReadInstanceJsonText(instanceGuid, "SqlTopStatus"));
        Assert.Equal("substatus-top", await ReadInstanceJsonText(instanceGuid, "SqlTopSubstatus"));
        Assert.Equal("process-top", await ReadInstanceJsonText(instanceGuid, "SqlTopProcess"));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_FlatInstanceUpdate_ComplexRootsInTopLevelDoNotOverrideDedicatedBranches()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        string seedJson = """
            {
              "DataValues": {
                "guard-preserved-data": "preserved",
                "guard-conflict-data": "old"
              },
              "PresentationTexts": {
                "guard-preserved-presentation": "preserved",
                "guard-conflict-presentation": "old"
              },
              "CompleteConfirmations": [
                {
                  "StakeholderId": "guard-existing-confirmation",
                  "ConfirmedOn": "2026-04-05T06:08:09Z"
                }
              ],
              "Status": {
                "IsArchived": false
              },
              "Process": {
                "CurrentTask": {
                  "ElementId": "Task_Seed"
                }
              }
            }
            """;
        await PostgresUtil.RunSql(
            $"""
            update storage.instances
            set instance = instance || '{seedJson}'::jsonb,
                taskid = 'Task_Seed',
                confirmed = false
            where alternateid = '{instanceGuid}'
            """
        );

        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DateTime lastChanged = new(2026, 4, 5, 6, 9, 10, DateTimeKind.Utc);
        DateTime archived = new(2026, 4, 5, 6, 10, 11, DateTimeKind.Utc);
        string instanceUpdates = InstanceUpdatePayload(
            InstanceUpdatePayloadItem(
                topLevelSimpleProps: new JsonObject
                {
                    ["SqlSimpleGuardMarker"] = "simple-root-survives",
                    ["DataValues"] = new JsonObject
                    {
                        ["guard-conflict-data"] = "top-level-data-must-not-win",
                        ["guard-top-only-data"] = "must-not-leak",
                    },
                    ["PresentationTexts"] = new JsonObject
                    {
                        ["guard-conflict-presentation"] = "top-level-presentation-must-not-win",
                        ["guard-top-only-presentation"] = "must-not-leak",
                    },
                    ["CompleteConfirmations"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["StakeholderId"] = "guard-top-only-confirmation",
                            ["ConfirmedOn"] = lastChanged.ToUniversalTime(),
                        },
                    },
                    ["Status"] = new JsonObject { ["IsArchived"] = false },
                    ["Process"] = new JsonObject
                    {
                        ["CurrentTask"] = new JsonObject
                        {
                            ["ElementId"] = "Task_TopLevel_Must_Not_Win",
                        },
                    },
                },
                dataValues: new JsonObject
                {
                    ["guard-conflict-data"] = "dedicated-data-wins",
                    ["guard-dedicated-data"] = "dedicated-data-present",
                },
                presentationTexts: new JsonObject
                {
                    ["guard-conflict-presentation"] = "dedicated-presentation-wins",
                    ["guard-dedicated-presentation"] = "dedicated-presentation-present",
                },
                completeConfirmations: new JsonArray
                {
                    new JsonObject
                    {
                        ["StakeholderId"] = "guard-dedicated-confirmation",
                        ["ConfirmedOn"] = lastChanged.ToUniversalTime(),
                    },
                },
                status: new JsonObject { ["IsArchived"] = true, ["Archived"] = archived },
                process: new JsonObject
                {
                    ["CurrentTask"] = new JsonObject { ["ElementId"] = "Task_Dedicated" },
                },
                taskId: "Task_Dedicated",
                confirmed: true
            )
        );

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            previousProcessStateVersion,
            null,
            null,
            null,
            null,
            instanceUpdates,
            null,
            null,
            lastChanged: lastChanged,
            lastChangedBy: "complex-root-guard-sql"
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        AssertAppliedRows(rows, previousInstanceVersion + 1, previousProcessStateVersion + 1);
        Assert.Equal(previousInstanceVersion + 1, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion + 1, updatedInstance.Versions.ProcessStateVersion);
        Assert.Equal("preserved", updatedInstance.DataValues["guard-preserved-data"]);
        Assert.Equal("dedicated-data-wins", updatedInstance.DataValues["guard-conflict-data"]);
        Assert.Equal("dedicated-data-present", updatedInstance.DataValues["guard-dedicated-data"]);
        Assert.False(updatedInstance.DataValues.ContainsKey("guard-top-only-data"));
        Assert.Equal(
            "preserved",
            updatedInstance.PresentationTexts["guard-preserved-presentation"]
        );
        Assert.Equal(
            "dedicated-presentation-wins",
            updatedInstance.PresentationTexts["guard-conflict-presentation"]
        );
        Assert.Equal(
            "dedicated-presentation-present",
            updatedInstance.PresentationTexts["guard-dedicated-presentation"]
        );
        Assert.False(updatedInstance.PresentationTexts.ContainsKey("guard-top-only-presentation"));
        Assert.Contains(
            updatedInstance.CompleteConfirmations,
            confirmation => confirmation.StakeholderId == "guard-existing-confirmation"
        );
        Assert.Contains(
            updatedInstance.CompleteConfirmations,
            confirmation => confirmation.StakeholderId == "guard-dedicated-confirmation"
        );
        Assert.DoesNotContain(
            updatedInstance.CompleteConfirmations,
            confirmation => confirmation.StakeholderId == "guard-top-only-confirmation"
        );
        Assert.True(updatedInstance.Status.IsArchived);
        Assert.Equal(archived, updatedInstance.Status.Archived);
        Assert.Equal("Task_Dedicated", updatedInstance.Process.CurrentTask.ElementId);
        Assert.Equal("Task_Dedicated", await ReadInstanceTaskId(instanceGuid));
        Assert.True(await ReadInstanceConfirmed(instanceGuid));
        Assert.Equal(
            "simple-root-survives",
            await ReadInstanceJsonText(instanceGuid, "SqlSimpleGuardMarker")
        );
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_InstanceUpdateProcessAndStatus_ComposesBranch()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        DateTime lastChanged = new(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        DateTime archived = new(2026, 4, 5, 6, 8, 9, DateTimeKind.Utc);
        string instanceUpdates = InstanceUpdatePayload(
            InstanceUpdatePayloadItem(
                topLevelSimpleProps: new JsonObject
                {
                    ["SqlTopProcessStatus"] = "process-status-top",
                },
                status: new JsonObject { ["IsArchived"] = true, ["Archived"] = archived },
                process: new JsonObject
                {
                    ["Status"] = "processing",
                    ["CurrentTask"] = new JsonObject { ["ElementId"] = "Task_10" },
                },
                taskId: "Task_10",
                confirmed: true
            )
        );

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            previousProcessStateVersion,
            null,
            null,
            null,
            null,
            instanceUpdates,
            null,
            null,
            lastChanged: lastChanged,
            lastChangedBy: "process-status-sql"
        );
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        AssertAppliedRows(rows, previousInstanceVersion + 1, previousProcessStateVersion + 1);
        Assert.Equal(previousInstanceVersion + 1, updatedInstance.Versions.InstanceVersion);
        Assert.Equal(previousProcessStateVersion + 1, updatedInstance.Versions.ProcessStateVersion);
        Assert.Equal(lastChanged, updatedInstance.LastChanged);
        Assert.Equal("process-status-sql", updatedInstance.LastChangedBy);
        Assert.True(updatedInstance.Status.IsArchived);
        Assert.Equal(archived, updatedInstance.Status.Archived);
        Assert.Equal("Task_10", updatedInstance.Process.CurrentTask.ElementId);
        Assert.Equal(ProcessStatus.Processing, updatedInstance.Process.Status);
        Assert.Equal("Task_10", await ReadInstanceTaskId(instanceGuid));
        Assert.True(await ReadInstanceConfirmed(instanceGuid));
        Assert.Equal(
            "process-status-top",
            await ReadInstanceJsonText(instanceGuid, "SqlTopProcessStatus")
        );
    }

    [Fact]
    public async Task MergeInstanceUpdateSql_NullInstanceUpdate_ReturnsOriginalInstance()
    {
        // Arrange
        string seedJson = CreateParitySeedJson();
        string expectedInstance = await JsonbText(seedJson);

        // Act
        string sqlNullUpdateInstance = await MergeInstanceUpdateSql(seedJson, null);
        string jsonNullUpdateInstance = await MergeInstanceUpdateSql(seedJson, "null");

        // Assert
        Assert.Equal(expectedInstance, sqlNullUpdateInstance);
        Assert.Equal(expectedInstance, jsonNullUpdateInstance);
    }

    [Fact]
    public async Task MergeInstanceUpdateSql_ConfirmedStakeholderConfirmsAgain_IsNotAppended()
    {
        // Arrange
        string seedJson = CreateParitySeedJson();
        string confirmationPayload = InstanceUpdatePayload(
            InstanceUpdatePayloadItem(
                completeConfirmations: ParseJsonNode(
                    """[{"StakeholderId":"existing","ConfirmedOn":"2026-08-10T00:00:00Z"}]"""
                ),
                confirmed: true
            )
        );

        // Act
        string mergedInstance = await MergeInstanceUpdateSql(seedJson, confirmationPayload);

        // Assert
        // The whole instance is untouched: no second entry, and the first confirmation keeps its
        // own timestamp rather than being refreshed by the losing caller.
        Assert.Equal(await JsonbText(seedJson), mergedInstance);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_ConfirmedStakeholderConfirmsAgain_IsNotAppended()
    {
        // Arrange
        ParityInstance instance = await CreateParityInstance();
        int previousInstanceVersion = await ReadInstanceVersion(instance.InstanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instance.InstanceGuid);
        string confirmationPayload = InstanceUpdatePayload(
            InstanceUpdatePayloadItem(
                completeConfirmations: ParseJsonNode(
                    """[{"StakeholderId":"existing","ConfirmedOn":"2026-08-10T00:00:00Z"}]"""
                ),
                confirmed: true
            )
        );

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instance.InstanceGuid,
            instance.InternalId,
            previousInstanceVersion,
            previousProcessStateVersion,
            null,
            null,
            null,
            null,
            confirmationPayload,
            null,
            null
        );

        // Assert
        // The mutation still commits and still bumps - it may carry other operations - but the
        // stakeholder keeps the single confirmation it already had.
        Assert.Equal(previousInstanceVersion + 1, rows[0].InstanceVersion);
        InstanceInternal updatedInstance = await dataElementFixture.InstanceRepo.GetOne(
            instance.InstanceGuid,
            false,
            CancellationToken.None
        );
        CompleteConfirmation confirmation = Assert.Single(updatedInstance.CompleteConfirmations);
        Assert.Equal("existing", confirmation.StakeholderId);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), confirmation.ConfirmedOn);
    }

    [Fact]
    public async Task MergeInstanceUpdateSql_InstanceUpdateBranches_MatchUpdateInstanceV4()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DateTime lastChanged = new(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        foreach (InstanceUpdateParityCase parityCase in CreateInstanceUpdateParityCases())
        {
            await ResetParityInstance(instanceGuid);
            string seedJson = CreateParitySeedJson();
            string mergedInstance = await MergeInstanceUpdateSql(
                seedJson,
                InstanceUpdatePayload(
                    InstanceUpdatePayloadItem(
                        topLevelSimpleProps: CreateParityTopLevelSimpleProps(
                            lastChanged,
                            parityCase.Name
                        ),
                        dataValues: ParseJsonNode(parityCase.DataValues),
                        completeConfirmations: ParseJsonNode(parityCase.CompleteConfirmations),
                        presentationTexts: ParseJsonNode(parityCase.PresentationTexts),
                        status: ParseJsonNode(parityCase.Status),
                        substatus: ParseJsonNode(parityCase.Substatus),
                        process: ParseJsonNode(parityCase.Process),
                        taskId: parityCase.TaskId,
                        confirmed: parityCase.Confirmed
                    )
                )
            );
            int updateExpectedInstanceVersion = await ReadInstanceVersion(instanceGuid);
            int updateExpectedProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

            // Act
            UpdateInstanceSqlRow updateRow = null;
            foreach (
                InstanceUpdateParityCase updateStep in CreateUpdateInstanceV4ParitySteps(parityCase)
            )
            {
                updateRow = await UpdateInstanceV4Sql(
                    instanceGuid,
                    updateStep,
                    lastChanged,
                    updateExpectedInstanceVersion,
                    updateExpectedProcessStateVersion
                );

                Assert.Equal("ok", updateRow.Result);
                updateExpectedInstanceVersion = updateRow.InstanceVersion;
                updateExpectedProcessStateVersion = updateRow.ProcessStateVersion;
            }

            // Assert
            Assert.Equal(updateRow.InstanceJson, mergedInstance);
        }
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_InstanceUpdateBranches_MatchUpdateInstanceV4()
    {
        // Arrange
        DateTime lastChanged = new(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        InstanceUpdateParityCase[] parityCases = CreateInstanceUpdateParityCases();

        foreach (InstanceUpdateParityCase parityCase in parityCases)
        {
            ParityInstance updateInstance = await CreateParityInstance();
            ParityInstance applyInstance = await CreateParityInstance();
            int updatePreviousInstanceVersion = await ReadInstanceVersion(
                updateInstance.InstanceGuid
            );
            int updatePreviousProcessStateVersion = await ReadProcessStateVersion(
                updateInstance.InstanceGuid
            );
            int applyPreviousInstanceVersion = await ReadInstanceVersion(
                applyInstance.InstanceGuid
            );
            int applyPreviousProcessStateVersion = await ReadProcessStateVersion(
                applyInstance.InstanceGuid
            );
            int expectedProcessStateVersion =
                applyPreviousProcessStateVersion + (parityCase.BumpsProcessState ? 1 : 0);

            // Act
            int updateExpectedInstanceVersion = updatePreviousInstanceVersion;
            int updateExpectedProcessStateVersion = updatePreviousProcessStateVersion;
            foreach (
                InstanceUpdateParityCase updateStep in CreateUpdateInstanceV4ParitySteps(parityCase)
            )
            {
                UpdateInstanceSqlRow updateRow = await UpdateInstanceV4Sql(
                    updateInstance.InstanceGuid,
                    updateStep,
                    lastChanged,
                    updateExpectedInstanceVersion,
                    updateExpectedProcessStateVersion
                );

                Assert.Equal("ok", updateRow.Result);
                Assert.Equal(updateExpectedInstanceVersion + 1, updateRow.InstanceVersion);
                Assert.Equal(
                    updateExpectedProcessStateVersion + (updateStep.BumpsProcessState ? 1 : 0),
                    updateRow.ProcessStateVersion
                );
                updateExpectedInstanceVersion = updateRow.InstanceVersion;
                updateExpectedProcessStateVersion = updateRow.ProcessStateVersion;
            }
            List<ApplyMutationSqlRow> applyRows = await ApplyInstanceMutationSql(
                applyInstance.InstanceGuid,
                applyInstance.InternalId,
                applyPreviousInstanceVersion,
                applyPreviousProcessStateVersion,
                null,
                null,
                null,
                null,
                InstanceUpdatePayload(
                    InstanceUpdatePayloadItem(
                        topLevelSimpleProps: CreateParityMutationTopLevelSimpleProps(
                            parityCase.Name
                        ),
                        dataValues: ParseJsonNode(parityCase.DataValues),
                        completeConfirmations: ParseJsonNode(parityCase.CompleteConfirmations),
                        presentationTexts: ParseJsonNode(parityCase.PresentationTexts),
                        status: ParseJsonNode(parityCase.Status),
                        substatus: ParseJsonNode(parityCase.Substatus),
                        process: ParseJsonNode(parityCase.Process),
                        taskId: parityCase.TaskId,
                        confirmed: parityCase.Confirmed
                    )
                ),
                null,
                null,
                lastChanged: lastChanged,
                lastChangedBy: $"parity-{parityCase.Name}"
            );
            InstanceStorageState updateState = await ReadInstanceStorageState(
                updateInstance.InstanceGuid
            );
            InstanceStorageState applyState = await ReadInstanceStorageState(
                applyInstance.InstanceGuid
            );

            // Assert
            AssertAppliedRows(
                applyRows,
                applyPreviousInstanceVersion + 1,
                expectedProcessStateVersion,
                expectedInternalId: applyInstance.InternalId
            );
            Assert.Equal(updateState, applyState);
        }
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_EventsOnlyIdempotentRetry_ReplaysWithoutDuplicateEvent()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();
        string events = EventsPayload(
            instanceGuid,
            InstanceEventType.process_StartTask,
            _instance.Id.ToString(),
            _instance.InstanceOwner.PartyId
        );

        // Act
        List<ApplyMutationSqlRow> firstRows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            idempotencyKey,
            null,
            null,
            null,
            null,
            events,
            null
        );
        List<ApplyMutationSqlRow> retryRows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            idempotencyKey,
            null,
            null,
            null,
            null,
            EventsPayload(
                instanceGuid,
                InstanceEventType.process_StartTask,
                _instance.Id.ToString(),
                _instance.InstanceOwner.PartyId
            ),
            null
        );

        // Assert
        AssertAppliedRows(
            firstRows,
            previousInstanceVersion,
            previousProcessStateVersion,
            expectedInternalId: _instanceInternalId
        );
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(previousProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.process_StartTask.ToString())
        );
        Assert.Equal(1, await CountIdempotencyRecords(idempotencyKey));
        Assert.Equal(
            previousInstanceVersion,
            await ReadIdempotencyProducedInstanceVersion(idempotencyKey)
        );

        AssertAppliedRows(
            retryRows,
            previousInstanceVersion,
            previousProcessStateVersion,
            replayed: true,
            expectedInternalId: _instanceInternalId
        );
        Assert.Equal(
            1,
            await CountInstanceEvents(instanceGuid, InstanceEventType.process_StartTask.ToString())
        );
        Assert.Equal(1, await CountIdempotencyRecords(idempotencyKey));
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_OutboxOnlyIdempotentRetry_ReplaysWithoutBumpingVersionsOrUpdatingOutbox()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        Guid idempotencyKey = Guid.NewGuid();
        string firstOutbox = OutboxPayload(_instance.ToApiModel(), 600, InstanceEventType.Saved);
        string retryOutbox = OutboxPayload(_instance.ToApiModel(), 0, InstanceEventType.Deleted);

        // Act
        List<ApplyMutationSqlRow> firstRows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            idempotencyKey,
            null,
            null,
            null,
            null,
            null,
            firstOutbox
        );
        DateTime firstValidFrom = await ReadOutboxValidFrom(instanceGuid);
        List<ApplyMutationSqlRow> retryRows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            previousInstanceVersion,
            null,
            idempotencyKey,
            null,
            null,
            null,
            null,
            null,
            retryOutbox
        );

        // Assert
        AssertAppliedRows(
            firstRows,
            previousInstanceVersion,
            previousProcessStateVersion,
            expectedInternalId: _instanceInternalId
        );
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(previousProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
        Assert.Equal(1, await CountOutboxRows(instanceGuid, InstanceEventType.Saved));
        Assert.Equal(0, await CountOutboxRows(instanceGuid, InstanceEventType.Deleted));
        Assert.Equal(1, await CountIdempotencyRecords(idempotencyKey));
        Assert.Equal(
            previousInstanceVersion,
            await ReadIdempotencyProducedInstanceVersion(idempotencyKey)
        );

        AssertAppliedRows(
            retryRows,
            previousInstanceVersion,
            previousProcessStateVersion,
            replayed: true,
            expectedInternalId: _instanceInternalId
        );
        Assert.Equal(1, await CountOutboxRows(instanceGuid, InstanceEventType.Saved));
        Assert.Equal(0, await CountOutboxRows(instanceGuid, InstanceEventType.Deleted));
        Assert.Equal(firstValidFrom, await ReadOutboxValidFrom(instanceGuid));
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.Equal(previousProcessStateVersion, await ReadProcessStateVersion(instanceGuid));
        Assert.Equal(1, await CountIdempotencyRecords(idempotencyKey));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateBlobAttachMissingRows_DoesNotValidateAttachCount()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement firstUpdate = TestDataUtil.GetDataElement(_dataElement1);
        (firstUpdate, _) = await CreateVersionedDataElement(firstUpdate);
        DataElement secondUpdate = TestDataUtil.GetDataElement(_dataElement2);
        (secondUpdate, _) = await CreateVersionedDataElement(secondUpdate);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        string firstMissingBlobVersion = await CreateBlobVersionId(instanceGuid, firstUpdate.Id);
        string secondNewBlobVersion = await CreateBlobVersionId(instanceGuid, secondUpdate.Id);
        await PostgresUtil.RunSql(
            $"delete from storage.dataelementblobversions where id = '{BlobVersionId.Decode(firstMissingBlobVersion)}'"
        );

        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            currentInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload([
                new UpdateElementPayload(
                    Guid.Parse(firstUpdate.Id),
                    NewBlobVersion: firstMissingBlobVersion
                ),
                new UpdateElementPayload(
                    Guid.Parse(secondUpdate.Id),
                    NewBlobVersion: secondNewBlobVersion
                ),
            ]),
            null,
            null,
            null,
            null
        );

        // Assert
        AssertAppliedRows(rows, currentInstanceVersion + 1, currentProcessStateVersion);
        Assert.Equal(0, await CountBlobVersionRows(firstMissingBlobVersion));
        Assert.Equal(1, await CountAttachedBlobVersionRows(secondNewBlobVersion));
        Assert.Equal(
            firstMissingBlobVersion,
            (
                await dataElementFixture.DataRepo.Read(instanceGuid, Guid.Parse(firstUpdate.Id))
            ).BlobVersionId
        );
        Assert.Equal(
            secondNewBlobVersion,
            (
                await dataElementFixture.DataRepo.Read(instanceGuid, Guid.Parse(secondUpdate.Id))
            ).BlobVersionId
        );
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateBlobThenMissing_ReportsMissingTarget()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toUpdate = TestDataUtil.GetDataElement(_dataElement1);
        (toUpdate, string currentVersion) = await CreateVersionedDataElement(toUpdate);
        string newVersion = await CreateBlobVersionId(instanceGuid, toUpdate.Id);
        Guid missingDataElementId = Guid.NewGuid();
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                UpdateElementsPayload([
                    new UpdateElementPayload(
                        Guid.Parse(toUpdate.Id),
                        ElementChanges: new JsonObject { ["SqlUpdateBeforeMissing"] = "blocked" },
                        NewBlobVersion: newVersion,
                        ExpectedBlobVersion: currentVersion
                    ),
                    new UpdateElementPayload(missingDataElementId),
                ]),
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "data_element_not_found",
            currentInstanceVersion,
            currentProcessStateVersion,
            missingDataElementId.ToString()
        );
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(toUpdate.Id)
        );
        Assert.Equal(currentVersion, readElement.BlobVersionId);
        Assert.Equal(
            "missing",
            await ReadDataElementJsonType(instanceGuid, toUpdate.Id, "SqlUpdateBeforeMissing")
        );
        Assert.Equal(0, await CountAttachedBlobVersionRows(newVersion));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateBlobVersionMismatch_RollsBackEarlierOperations()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement existing = TestDataUtil.GetDataElement(_dataElement1);
        (existing, string currentVersion) = await CreateVersionedDataElement(existing);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        string staleExpectedVersion = await CreateBlobVersionId(instanceGuid, existing.Id);
        DataElementInternal toCreate = await PrepareAggregateCreateDataElement(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                previousInstanceVersion,
                null,
                null,
                CreateElementsPayload([toCreate]),
                UpdateElementsPayload(
                    Guid.Parse(existing.Id),
                    expectedBlobVersion: staleExpectedVersion
                ),
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "blob_version_mismatch",
            previousInstanceVersion,
            currentProcessStateVersion,
            existing.Id
        );
        Assert.Equal(
            currentVersion,
            (
                await dataElementFixture.DataRepo.Read(instanceGuid, Guid.Parse(existing.Id))
            ).BlobVersionId
        );
        Assert.False(await dataElementFixture.DataRepo.Exists(toCreate.Id));
        Assert.Equal(0, await CountAttachedBlobVersionRows(toCreate.BlobVersionId));
        Assert.Equal(previousInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_CreateBlobAttachMissingRows_DoesNotValidateAttachCount()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElementInternal firstCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        DataElementInternal secondCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        await PostgresUtil.RunSql(
            $"delete from storage.dataelementblobversions where id = '{BlobVersionId.Decode(firstCreate.BlobVersionId)}'"
        );

        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            currentInstanceVersion,
            null,
            null,
            CreateElementsPayload([firstCreate, secondCreate]),
            null,
            null,
            null,
            null,
            null
        );

        // Assert
        AssertAppliedRows(
            rows,
            currentInstanceVersion + 1,
            currentProcessStateVersion,
            [firstCreate.Id.ToString(), secondCreate.Id.ToString()]
        );
        Assert.True(await dataElementFixture.DataRepo.Exists(firstCreate.Id));
        Assert.True(await dataElementFixture.DataRepo.Exists(secondCreate.Id));
        Assert.Equal(0, await CountBlobVersionRows(firstCreate.BlobVersionId));
        Assert.Equal(1, await CountAttachedBlobVersionRows(secondCreate.BlobVersionId));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateValidationRollsBackEarlierCreateWithMissingBlobAttach()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElementInternal toCreate = await PrepareAggregateCreateDataElement(instanceGuid);
        DataElement lockedUpdate = TestDataUtil.GetDataElement(_dataElement1);
        lockedUpdate.Locked = true;
        lockedUpdate = await CreateLegacyDataElement(lockedUpdate);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        await PostgresUtil.RunSql(
            $"delete from storage.dataelementblobversions where id = '{BlobVersionId.Decode(toCreate.BlobVersionId)}'"
        );

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                CreateElementsPayload([toCreate]),
                UpdateElementsPayload(Guid.Parse(lockedUpdate.Id), ignoreLock: false),
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "locked",
            currentInstanceVersion,
            currentProcessStateVersion,
            lockedUpdate.Id
        );
        Assert.False(await dataElementFixture.DataRepo.Exists(toCreate.Id));
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(lockedUpdate.Id)));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_DeleteDataElementNotFound_ReportsFirstDeleteOrdinal()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement missingDelete = TestDataUtil.GetDataElement(_dataElement1);
        missingDelete.Id = Guid.NewGuid().ToString();
        missingDelete.InstanceGuid = instanceGuid.ToString();
        DataElement existingDelete = TestDataUtil.GetDataElement(_dataElement2);
        (existingDelete, string existingBlobVersionId) = await CreateVersionedDataElement(
            existingDelete
        );
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                null,
                DeleteElementsPayload([missingDelete, existingDelete]),
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "data_element_not_found",
            currentInstanceVersion,
            currentProcessStateVersion,
            missingDelete.Id
        );
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(existingDelete.Id)));
        Assert.Equal(1, await CountAttachedBlobVersionRows(existingBlobVersionId));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_DeleteAllowedThenLocked_ReportsLockedTarget()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement allowedDelete = TestDataUtil.GetDataElement(_dataElement1);
        (allowedDelete, string allowedBlobVersionId) = await CreateVersionedDataElement(
            allowedDelete
        );
        DataElement lockedDelete = TestDataUtil.GetDataElement(_dataElement2);
        lockedDelete.Locked = true;
        (lockedDelete, string lockedBlobVersionId) = await CreateVersionedDataElement(lockedDelete);
        await SetDataElementLocked(instanceGuid, lockedDelete.Id, true);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                null,
                DeleteElementsPayload([allowedDelete, lockedDelete]),
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "locked",
            currentInstanceVersion,
            currentProcessStateVersion,
            lockedDelete.Id
        );
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(allowedDelete.Id)));
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(lockedDelete.Id)));
        Assert.Equal(1, await CountAttachedBlobVersionRows(allowedBlobVersionId));
        Assert.Equal(1, await CountAttachedBlobVersionRows(lockedBlobVersionId));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_DeleteAllowedThenMissing_ReportsMissingTarget()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement allowedDelete = TestDataUtil.GetDataElement(_dataElement1);
        (allowedDelete, string allowedBlobVersionId) = await CreateVersionedDataElement(
            allowedDelete
        );
        DataElement missingDelete = TestDataUtil.GetDataElement(_dataElement2);
        missingDelete.Id = Guid.NewGuid().ToString();
        missingDelete.InstanceGuid = instanceGuid.ToString();
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                null,
                DeleteElementsPayload([allowedDelete, missingDelete]),
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "data_element_not_found",
            currentInstanceVersion,
            currentProcessStateVersion,
            missingDelete.Id
        );
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(allowedDelete.Id)));
        Assert.Equal(1, await CountAttachedBlobVersionRows(allowedBlobVersionId));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_DeleteLockedElement_RaisesAm001LockedAndDoesNotDelete()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement lockedDelete = TestDataUtil.GetDataElement(_dataElement1);
        lockedDelete.Locked = true;
        (lockedDelete, string blobVersionId) = await CreateVersionedDataElement(lockedDelete);
        await SetDataElementLocked(instanceGuid, lockedDelete.Id, true);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                null,
                DeleteElementsPayload([lockedDelete]),
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "locked",
            currentInstanceVersion,
            currentProcessStateVersion,
            lockedDelete.Id
        );
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(lockedDelete.Id)));
        Assert.Equal(1, await CountAttachedBlobVersionRows(blobVersionId));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyInstanceMutationSql_DeleteElementWithIgnoreLock_Deletes(bool locked)
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement toDelete = TestDataUtil.GetDataElement(_dataElement1);
        toDelete.Locked = locked;
        (toDelete, string blobVersionId) = await CreateVersionedDataElement(toDelete);
        if (locked)
        {
            await SetDataElementLocked(instanceGuid, toDelete.Id, true);
        }
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            currentInstanceVersion,
            null,
            null,
            null,
            null,
            DeleteElementsPayload([toDelete], ignoreLock: true),
            null,
            null,
            null
        );

        // Assert
        AssertAppliedRows(rows, currentInstanceVersion + 1, currentProcessStateVersion);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(toDelete.Id)));
        Assert.Equal(1, await CountDetachedBlobVersionRows(blobVersionId));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UnlockThenDeleteLockedElement_Deletes()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement lockedDelete = TestDataUtil.GetDataElement(_dataElement1);
        lockedDelete.Locked = true;
        (lockedDelete, string blobVersionId) = await CreateVersionedDataElement(lockedDelete);
        await SetDataElementLocked(instanceGuid, lockedDelete.Id, true);
        int unlockInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int processStateVersion = await ReadProcessStateVersion(instanceGuid);

        await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            unlockInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload([
                new UpdateElementPayload(
                    Guid.Parse(lockedDelete.Id),
                    ElementChanges: new JsonObject { ["Locked"] = false },
                    IgnoreLock: true
                ),
            ]),
            null,
            null,
            null,
            null
        );
        int deleteInstanceVersion = unlockInstanceVersion + 1;

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            deleteInstanceVersion,
            null,
            null,
            null,
            null,
            DeleteElementsPayload([lockedDelete]),
            null,
            null,
            null
        );

        // Assert
        AssertAppliedRows(rows, deleteInstanceVersion + 1, processStateVersion);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(lockedDelete.Id)));
        Assert.Equal(1, await CountDetachedBlobVersionRows(blobVersionId));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_DeleteHardDeletedDataElement_DoesNotTreatHardDeleteFlagAsLock()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement hardDeletedElement = TestDataUtil.GetDataElement(_dataElement1);
        hardDeletedElement.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        (hardDeletedElement, string blobVersionId) = await CreateVersionedDataElement(
            hardDeletedElement
        );
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            currentInstanceVersion,
            null,
            null,
            null,
            null,
            DeleteElementsPayload([hardDeletedElement]),
            null,
            null,
            null
        );

        // Assert
        AssertAppliedRows(rows, currentInstanceVersion + 1, currentProcessStateVersion);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(hardDeletedElement.Id)));
        Assert.Equal(1, await CountDetachedBlobVersionRows(blobVersionId));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_DeleteValidationOrdersByOrdinalBeforePriority()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement lockedDelete = TestDataUtil.GetDataElement(_dataElement1);
        lockedDelete.Locked = true;
        (lockedDelete, string blobVersionId) = await CreateVersionedDataElement(lockedDelete);
        await SetDataElementLocked(instanceGuid, lockedDelete.Id, true);
        DataElement missingDelete = TestDataUtil.GetDataElement(_dataElement2);
        missingDelete.Id = Guid.NewGuid().ToString();
        missingDelete.InstanceGuid = instanceGuid.ToString();
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                null,
                DeleteElementsPayload([lockedDelete, missingDelete]),
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "locked",
            currentInstanceVersion,
            currentProcessStateVersion,
            lockedDelete.Id
        );
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(lockedDelete.Id)));
        Assert.Equal(1, await CountAttachedBlobVersionRows(blobVersionId));
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateValidationOrdersByOrdinalBeforePriority()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement lockedUpdate = TestDataUtil.GetDataElement(_dataElement1);
        lockedUpdate.Locked = true;
        lockedUpdate = await CreateLegacyDataElement(lockedUpdate);
        Guid missingDataElementId = Guid.NewGuid();
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                UpdateElementsPayload([
                    new UpdateElementPayload(Guid.Parse(lockedUpdate.Id), IgnoreLock: false),
                    new UpdateElementPayload(missingDataElementId),
                ]),
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "locked",
            currentInstanceVersion,
            currentProcessStateVersion,
            lockedUpdate.Id
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_ExpectedVersionMismatches_RaiseAm001WithCurrentVersions()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException instanceException = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion - 1,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            )
        );
        PostgresException processException = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                currentProcessStateVersion - 1,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlVersionError(
            instanceException,
            "instance_version_mismatch",
            currentInstanceVersion,
            currentProcessStateVersion
        );
        AssertSqlVersionError(
            processException,
            "process_state_version_mismatch",
            currentInstanceVersion,
            currentProcessStateVersion
        );
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_LockedElement_RaisesAm001LockedAndDoesNotUpdate()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement lockedElement = TestDataUtil.GetDataElement(_dataElement1);
        lockedElement.Locked = true;
        lockedElement.LastChanged = DateTime.UtcNow;
        lockedElement.LastChangedBy = "sql-locked-test-setup";
        lockedElement = await CreateLegacyDataElement(lockedElement);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                UpdateElementsPayload(Guid.Parse(lockedElement.Id), ignoreLock: false),
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "locked",
            currentInstanceVersion,
            currentProcessStateVersion,
            lockedElement.Id
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(lockedElement.Id)
        );
        Assert.True(readElement.Locked);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UnlockLockedElementWithIgnoreLock_Updates()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement lockedElement = TestDataUtil.GetDataElement(_dataElement1);
        lockedElement.Locked = true;
        lockedElement = await CreateLegacyDataElement(lockedElement);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        List<ApplyMutationSqlRow> rows = await ApplyInstanceMutationSql(
            instanceGuid,
            _instanceInternalId,
            currentInstanceVersion,
            null,
            null,
            null,
            UpdateElementsPayload([
                new UpdateElementPayload(
                    Guid.Parse(lockedElement.Id),
                    ElementChanges: new JsonObject { ["Locked"] = false },
                    IgnoreLock: true
                ),
            ]),
            null,
            null,
            null,
            null
        );

        // Assert
        AssertAppliedRows(rows, currentInstanceVersion + 1, currentProcessStateVersion);
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(lockedElement.Id)
        );
        Assert.False(readElement.Locked);
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_LockedElementWithOmittedIgnoreLock_RaisesAm001Locked()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement lockedElement = TestDataUtil.GetDataElement(_dataElement1);
        lockedElement.Locked = true;
        lockedElement = await CreateLegacyDataElement(lockedElement);
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        string updateElements = new JsonArray
        {
            new JsonObject
            {
                ["elementId"] = lockedElement.Id,
                ["elementChanges"] = new JsonObject { ["ContentType"] = "application/xml" },
            },
        }.ToJsonString();

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                updateElements,
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "locked",
            currentInstanceVersion,
            currentProcessStateVersion,
            lockedElement.Id
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UnknownDataElement_RaisesAm001DataElementNotFound()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        Guid missingDataElementId = Guid.NewGuid();
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                UpdateElementsPayload(missingDataElementId),
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "data_element_not_found",
            currentInstanceVersion,
            currentProcessStateVersion,
            missingDataElementId.ToString()
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_UpdateNullElementId_RaisesAm001DataElementNotFound()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        string updateElements = new JsonArray
        {
            new JsonObject
            {
                ["elementId"] = null,
                ["elementChanges"] = new JsonObject { ["SqlNullElementMarker"] = "ignored" },
                ["isReadChangedToFalse"] = false,
                ["newBlobVersion"] = null,
                ["expectedBlobVersion"] = null,
                ["ignoreLock"] = false,
            },
        }.ToJsonString();

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                updateElements,
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "data_element_not_found",
            currentInstanceVersion,
            currentProcessStateVersion
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        Assert.False(await InstanceJsonContainsKey(instanceGuid, "SqlNullElementMarker"));
    }

    public enum HardDeletedInstanceMutationPayloadKind
    {
        Create,
        Update,
        Delete,
        InstanceUpdate,
    }

    [Theory]
    [InlineData(HardDeletedInstanceMutationPayloadKind.Create)]
    [InlineData(HardDeletedInstanceMutationPayloadKind.Update)]
    [InlineData(HardDeletedInstanceMutationPayloadKind.Delete)]
    [InlineData(HardDeletedInstanceMutationPayloadKind.InstanceUpdate)]
    public async Task ApplyInstanceMutationSql_OnHardDeletedInstance_RaisesInstanceHardDeletedAndDoesNotMutate(
        HardDeletedInstanceMutationPayloadKind payloadKind
    )
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        string createElements = null;
        string updateElements = null;
        string deleteElements = null;
        string instanceUpdate = null;
        Func<Task> assertMutationDidNotApply;

        switch (payloadKind)
        {
            case HardDeletedInstanceMutationPayloadKind.Create:
                DataElementInternal toCreate = await PrepareAggregateCreateDataElement(
                    instanceGuid
                );
                createElements = CreateElementsPayload([toCreate]);
                assertMutationDidNotApply = async () =>
                {
                    Assert.False(await dataElementFixture.DataRepo.Exists(toCreate.Id));
                    Assert.Equal(0, await CountAttachedBlobVersionRows(toCreate.BlobVersionId));
                };
                break;
            case HardDeletedInstanceMutationPayloadKind.Update:
                DataElement toUpdate = TestDataUtil.GetDataElement(_dataElement1);
                string originalContentType = toUpdate.ContentType;
                (toUpdate, string currentBlobVersion) = await CreateVersionedDataElement(toUpdate);
                updateElements = UpdateElementsPayload([
                    new UpdateElementPayload(
                        Guid.Parse(toUpdate.Id),
                        ElementChanges: new JsonObject { ["ContentType"] = "application/xml" },
                        ExpectedBlobVersion: currentBlobVersion
                    ),
                ]);
                assertMutationDidNotApply = async () =>
                    Assert.Equal(
                        originalContentType,
                        await ReadDataElementJsonText(instanceGuid, toUpdate.Id, "ContentType")
                    );
                break;
            case HardDeletedInstanceMutationPayloadKind.Delete:
                DataElement toDelete = TestDataUtil.GetDataElement(_dataElement1);
                (toDelete, string blobVersionId) = await CreateVersionedDataElement(toDelete);
                deleteElements = DeleteElementsPayload([toDelete]);
                assertMutationDidNotApply = async () =>
                {
                    Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(toDelete.Id)));
                    Assert.Equal(1, await CountAttachedBlobVersionRows(blobVersionId));
                };
                break;
            case HardDeletedInstanceMutationPayloadKind.InstanceUpdate:
                instanceUpdate = InstanceUpdatePayload(
                    InstanceUpdatePayloadItem(
                        dataValues: new JsonObject { ["hardDeletedUpdate"] = "blocked" }
                    )
                );
                assertMutationDidNotApply = async () =>
                    Assert.False(
                        await InstanceDataValuesContainsKey(instanceGuid, "hardDeletedUpdate")
                    );
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(payloadKind), payloadKind, null);
        }

        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);
        await SetInstanceHardDeleted(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                createElements,
                updateElements,
                deleteElements,
                instanceUpdate,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "instance_hard_deleted",
            currentInstanceVersion,
            currentProcessStateVersion
        );
        AssertSqlErrorHasNoMutationTarget(exception);
        await assertMutationDidNotApply();
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
    }

    [Fact]
    public async Task ApplyInstanceMutationSql_HardDeletedDataElement_RaisesDistinctCodeAndDoesNotUpdate()
    {
        // Arrange
        Guid instanceGuid = _instance.Id;
        DataElement hardDeletedElement = TestDataUtil.GetDataElement(_dataElement1);
        hardDeletedElement.Locked = true;
        hardDeletedElement.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        (hardDeletedElement, string currentBlobVersion) = await CreateVersionedDataElement(
            hardDeletedElement
        );
        int currentInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int currentProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ApplyInstanceMutationSql(
                instanceGuid,
                _instanceInternalId,
                currentInstanceVersion,
                null,
                null,
                null,
                UpdateElementsPayload(
                    Guid.Parse(hardDeletedElement.Id),
                    expectedBlobVersion: currentBlobVersion,
                    ignoreLock: false
                ),
                null,
                null,
                null,
                null
            )
        );

        // Assert
        AssertSqlError(
            exception,
            "data_element_hard_deleted",
            currentInstanceVersion,
            currentProcessStateVersion,
            hardDeletedElement.Id
        );
        Assert.Equal(currentInstanceVersion, await ReadInstanceVersion(instanceGuid));
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(hardDeletedElement.Id)
        );
        Assert.True(readElement.DeleteStatus.IsHardDeleted);
    }

    private async Task<List<ApplyMutationSqlRow>> ApplyInstanceMutationSql(
        Guid instanceGuid,
        long instanceInternalId,
        int? expectedInstanceVersion,
        int? expectedProcessStateVersion,
        Guid? idempotencyKey,
        string createElements,
        string updateElements,
        string deleteElements,
        string instanceUpdates,
        string events,
        string outbox,
        DateTime? lastChanged = null,
        string lastChangedBy = "sql-test-actor"
    )
    {
        await using NpgsqlCommand cmd = dataElementFixture.DataSource.CreateCommand(
            PgInstanceMutationRepository._applyMutationSql
        );
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Bigint, instanceInternalId);
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Integer,
            expectedInstanceVersion
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Integer,
            expectedProcessStateVersion
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Uuid,
            idempotencyKey
        );
        cmd.Parameters.AddWithValue(
            NpgsqlDbType.TimestampTz,
            lastChanged ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Text,
            lastChangedBy
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            createElements
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            updateElements
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            deleteElements
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            instanceUpdates
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            events
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            outbox
        );

        List<ApplyMutationSqlRow> rows = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(
                new ApplyMutationSqlRow(
                    reader.GetBoolean(reader.GetOrdinal("replayed")),
                    PgInstanceMutationRepository.ReadTextArray(reader, "createddataelementids"),
                    ReadNullableInt64(reader, "id"),
                    ReadNullableInt32(reader, "instanceversion"),
                    ReadNullableInt32(reader, "processstateversion"),
                    ReadNullableGuid(reader, "currentblobversion")
                )
            );
        }

        return rows;
    }

    private async Task<IReadOnlyList<string>> TryReplayInstanceMutationV2Sql(
        Guid idempotencyKey,
        Guid instanceGuid,
        int previousInstanceVersion,
        int currentInstanceVersion,
        int currentProcessStateVersion
    )
    {
        await using NpgsqlCommand cmd = dataElementFixture.DataSource.CreateCommand(
            "select storage.tryreplayinstancemutation($1, $2, $3, $4, $5) as createddataelementids"
        );
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, idempotencyKey);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Integer, previousInstanceVersion);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Integer, currentInstanceVersion);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Integer, currentProcessStateVersion);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("tryreplayinstancemutation returned no row.");
        }

        return PgInstanceMutationRepository.ReadTextArray(reader, "createddataelementids");
    }

    private async Task<UpdateInstanceSqlRow> UpdateInstanceV4Sql(
        Guid instanceGuid,
        InstanceUpdateParityCase parityCase,
        DateTime lastChanged,
        int expectedInstanceVersion,
        int expectedProcessStateVersion
    )
    {
        await using NpgsqlCommand cmd = dataElementFixture.DataSource.CreateCommand(
            "select updatedInstance::text as instancejson, result, instanceversion, processstateversion from storage.updateinstance_v4($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)"
        );
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            CreateParityTopLevelSimpleProps(lastChanged, parityCase.Name).ToJsonString()
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            parityCase.DataValues
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            parityCase.CompleteConfirmations
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            parityCase.PresentationTexts
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            parityCase.Status
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            parityCase.Substatus
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            parityCase.Process
        );
        cmd.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, lastChanged);
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Text,
            parityCase.TaskId
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Boolean,
            parityCase.Confirmed
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Integer,
            expectedInstanceVersion
        );
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Integer,
            expectedProcessStateVersion
        );

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new UpdateInstanceSqlRow(
            reader.GetString(reader.GetOrdinal("result")),
            reader.GetInt32(reader.GetOrdinal("instanceversion")),
            reader.GetInt32(reader.GetOrdinal("processstateversion")),
            reader.GetString(reader.GetOrdinal("instancejson"))
        );
    }

    private async Task<string> MergeInstanceUpdateSql(string instance, string instanceUpdate)
    {
        await using NpgsqlCommand cmd = dataElementFixture.DataSource.CreateCommand(
            "select storage.mergeinstanceupdate($1, $2)::text"
        );
        cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, instance);
        PgInstanceMutationRepository.AddNullableParameter(
            cmd.Parameters,
            NpgsqlDbType.Jsonb,
            instanceUpdate
        );

        return (string)await cmd.ExecuteScalarAsync();
    }

    private async Task<string> JsonbText(string json)
    {
        await using NpgsqlCommand cmd = dataElementFixture.DataSource.CreateCommand(
            "select $1::jsonb::text"
        );
        cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json);

        return (string)await cmd.ExecuteScalarAsync();
    }

    private async Task<ParityInstance> CreateParityInstance()
    {
        Instance instance = TestData.Instance_1_1.Clone();
        instance.Id = Guid.NewGuid().ToString();
        InstanceInternal created = await dataElementFixture.InstanceRepo.Create(
            instance.FromApiModel(),
            CancellationToken.None
        );
        Guid instanceGuid = created.Id;
        InstanceInternal instanceInternal = await dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );
        await PostgresUtil.RunSql(
            $"""
            update storage.instances
            set instance = instance || '{CreateParitySeedJson()}'::jsonb,
                taskid = 'Task_Seed',
                confirmed = false
            where alternateid = '{instanceGuid}'
            """
        );

        return new ParityInstance(instanceGuid, instanceInternal.InternalId);
    }

    private static Task ResetParityInstance(Guid instanceGuid) =>
        PostgresUtil.RunSql(
            $"""
            update storage.instances
            set instance = '{CreateParitySeedJson()}'::jsonb,
                taskid = 'Task_Seed',
                confirmed = false
            where alternateid = '{instanceGuid}'
            """
        );

    private static string CreateParitySeedJson() =>
        """
            {
              "DataValues": {
                "parity-preserved-data": "preserved",
                "parity-existing-data": "old"
              },
              "PresentationTexts": {
                "parity-preserved-presentation": "preserved",
                "parity-existing-presentation": "old"
              },
              "CompleteConfirmations": [
                {
                  "StakeholderId": "existing",
                  "ConfirmedOn": "2026-01-01T00:00:00Z"
                }
              ],
              "Status": {
                "ReadStatus": 0,
                "IsArchived": false,
                "Substatus": {
                  "Label": "seed-label",
                  "Description": "seed-description"
                }
              },
              "Process": {
                "CurrentTask": {
                  "ElementId": "Task_Seed"
                }
              }
            }
            """;

    // No case confirms a stakeholder the seed already carries: mergeinstanceupdate skips those and
    // updateinstance_v4 appends them, the one place the two deliberately differ. That difference has
    // its own tests; see MergeInstanceUpdateSql_ConfirmedStakeholderConfirmsAgain_IsNotAppended.
    private static InstanceUpdateParityCase[] CreateInstanceUpdateParityCases() =>
        [
            new(
                "data-values",
                DataValues: """{"parity-existing-data":null,"parity-added-data":"added"}""",
                Confirmed: true
            ),
            new(
                "presentation-texts",
                PresentationTexts: """{"parity-existing-presentation":null,"parity-added-presentation":"added"}""",
                Confirmed: false
            ),
            new(
                "complete-confirmations",
                CompleteConfirmations: """[{"StakeholderId":"ttd","ConfirmedOn":"2026-06-07T08:09:10Z"}]""",
                Confirmed: true
            ),
            new(
                "status",
                Status: """{"IsArchived":true,"Archived":"2026-06-07T08:10:10Z"}""",
                Confirmed: false
            ),
            new(
                "substatus",
                Substatus: """{"Label":"parity-substatus","Description":null}""",
                Confirmed: true
            ),
            new(
                "status-substatus",
                Status: """{"IsArchived":true,"Archived":"2026-06-07T08:10:20Z"}""",
                Substatus: """{"Label":"parity-status-substatus","Description":null}""",
                Confirmed: true
            ),
            new(
                "process",
                Process: """{"CurrentTask":{"ElementId":"Task_Parity_Process"}}""",
                TaskId: "Task_Parity_Process",
                Confirmed: true,
                BumpsProcessState: true
            ),
            new(
                "process-status",
                Status: """{"IsArchived":true,"Archived":"2026-06-07T08:11:10Z"}""",
                Process: """{"CurrentTask":{"ElementId":"Task_Parity_Process_Status"}}""",
                TaskId: "Task_Parity_Process_Status",
                Confirmed: false,
                BumpsProcessState: true
            ),
        ];

    private static IReadOnlyList<InstanceUpdateParityCase> CreateUpdateInstanceV4ParitySteps(
        InstanceUpdateParityCase parityCase
    )
    {
        if (
            parityCase.Status is not null
            && parityCase.Substatus is not null
            && parityCase.Process is null
        )
        {
            return
            [
                parityCase with
                {
                    Substatus = null,
                },
                parityCase with
                {
                    Status = null,
                    Confirmed = null,
                },
            ];
        }

        return [parityCase];
    }

    private async Task<InstanceStorageState> ReadInstanceStorageState(Guid instanceGuid)
    {
        await using NpgsqlCommand cmd = dataElementFixture.DataSource.CreateCommand(
            "select (instance - 'Id' - 'LastChanged' - 'LastChangedBy')::text as instancejson, taskid, confirmed from storage.instances where alternateid = $1"
        );
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, instanceGuid);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        int taskIdOrdinal = reader.GetOrdinal("taskid");
        return new InstanceStorageState(
            reader.GetString(reader.GetOrdinal("instancejson")),
            reader.IsDBNull(taskIdOrdinal) ? null : reader.GetString(taskIdOrdinal),
            reader.GetBoolean(reader.GetOrdinal("confirmed"))
        );
    }

    private static string CreateElementsPayload(IReadOnlyList<DataElementInternal> dataElements)
    {
        JsonArray array = [];
        foreach (DataElementInternal dataElement in dataElements)
        {
            array.Add(
                new JsonObject
                {
                    ["elementId"] = dataElement.Id,
                    ["element"] = JsonSerializer.SerializeToNode(dataElement),
                    ["blobVersion"] = PgInstanceMutationRepository.ToDecodedBlobVersion(
                        dataElement.BlobVersionId
                    ),
                }
            );
        }

        return array.ToJsonString();
    }

    private static string CreateElementsPayloadWithoutLastChanged(
        IReadOnlyList<DataElementInternal> dataElements
    )
    {
        JsonArray array = [];
        foreach (DataElementInternal dataElement in dataElements)
        {
            JsonObject element = JsonSerializer.SerializeToNode(dataElement).AsObject();
            element.Remove(nameof(DataElement.LastChanged));
            element.Remove(nameof(DataElement.LastChangedBy));

            array.Add(
                new JsonObject
                {
                    ["elementId"] = dataElement.Id,
                    ["element"] = element,
                    ["blobVersion"] = PgInstanceMutationRepository.ToDecodedBlobVersion(
                        dataElement.BlobVersionId
                    ),
                }
            );
        }

        return array.ToJsonString();
    }

    private static string UpdateElementsPayload(
        Guid dataElementId,
        string expectedBlobVersion = null,
        string newBlobVersion = null,
        bool ignoreLock = false
    ) =>
        UpdateElementsPayload([
            new UpdateElementPayload(
                dataElementId,
                ExpectedBlobVersion: expectedBlobVersion,
                NewBlobVersion: newBlobVersion,
                IgnoreLock: ignoreLock
            ),
        ]);

    private static string UpdateElementsPayload(IReadOnlyList<UpdateElementPayload> updates)
    {
        JsonArray array = [];
        foreach (UpdateElementPayload update in updates)
        {
            array.Add(
                new JsonObject
                {
                    ["elementId"] = update.DataElementId.ToString(),
                    ["elementChanges"] = update.ElementChanges ?? new JsonObject(),
                    ["isReadChangedToFalse"] = update.IsReadChangedToFalse,
                    ["newBlobVersion"] = PgInstanceMutationRepository.ToDecodedBlobVersion(
                        update.NewBlobVersion
                    ),
                    ["expectedBlobVersion"] = PgInstanceMutationRepository.ToDecodedBlobVersion(
                        update.ExpectedBlobVersion
                    ),
                    ["ignoreLock"] = update.IgnoreLock,
                }
            );
        }

        return array.ToJsonString();
    }

    private static string DeleteElementsPayload(
        IReadOnlyList<DataElement> dataElements,
        bool ignoreLock = false
    ) =>
        DeleteElementsPayload([
            .. dataElements.Select(dataElement => new DeleteElementPayload(
                dataElement.Id,
                ignoreLock
            )),
        ]);

    private static string DeleteElementsPayload(IReadOnlyList<DeleteElementPayload> deletes)
    {
        JsonArray array = [];
        foreach (DeleteElementPayload delete in deletes)
        {
            array.Add(
                new JsonObject
                {
                    ["elementId"] = delete.DataElementId,
                    ["ignoreLock"] = delete.IgnoreLock,
                }
            );
        }

        return array.ToJsonString();
    }

    private sealed record DeleteElementPayload(string DataElementId, bool IgnoreLock = false);

    private static string InstanceUpdatePayload(JsonObject update) => update.ToJsonString();

    private static JsonObject InstanceUpdatePayloadItem(
        JsonObject topLevelSimpleProps = null,
        JsonNode dataValues = null,
        JsonNode completeConfirmations = null,
        JsonNode presentationTexts = null,
        JsonNode status = null,
        JsonNode substatus = null,
        JsonNode process = null,
        string taskId = null,
        bool? confirmed = null
    ) =>
        new()
        {
            ["toplevelsimpleprops"] = topLevelSimpleProps ?? new JsonObject(),
            ["datavalues"] = dataValues,
            ["completeconfirmations"] = completeConfirmations,
            ["presentationtexts"] = presentationTexts,
            ["status"] = status,
            ["substatus"] = substatus,
            ["process"] = process,
            ["taskid"] = taskId,
            ["confirmed"] = confirmed.HasValue ? JsonValue.Create(confirmed.Value) : null,
        };

    private static JsonObject CreateParityTopLevelSimpleProps(
        DateTime lastChanged,
        string caseName
    ) =>
        new()
        {
            ["LastChanged"] = lastChanged.ToUniversalTime(),
            ["LastChangedBy"] = $"parity-{caseName}",
            ["SqlParityCase"] = caseName,
        };

    private static JsonObject CreateParityMutationTopLevelSimpleProps(string caseName) =>
        new() { ["SqlParityCase"] = caseName };

    private static JsonNode ParseJsonNode(string json) =>
        json is null ? null : JsonNode.Parse(json);

    private static string EventsPayload(
        Guid instanceGuid,
        InstanceEventType eventType,
        string instanceId,
        string partyId
    ) =>
        JsonSerializer.Serialize(
            new[]
            {
                new InstanceEvent
                {
                    Id = Guid.NewGuid(),
                    InstanceId = instanceId,
                    InstanceOwnerPartyId = partyId,
                    EventType = eventType.ToString(),
                    Created = DateTime.UtcNow,
                    ProcessInfo = new ProcessState
                    {
                        CurrentTask = new ProcessElementInfo { ElementId = "Task_1" },
                    },
                    DataId = instanceGuid.ToString(),
                },
            }
        );

    private static string OutboxPayload(
        Instance instance,
        int delaySeconds,
        InstanceEventType eventType
    ) =>
        new JsonObject
        {
            ["appid"] = instance.AppId,
            ["partyid"] = long.Parse(instance.InstanceOwner.PartyId),
            ["delaySeconds"] = delaySeconds,
            ["instancecreated"] = (instance.Created ?? DateTime.UtcNow).ToUniversalTime(),
            ["ismigration"] = false,
            ["instanceeventtype"] = (int)eventType,
        }.ToJsonString();

    private static int? ReadNullableInt32(NpgsqlDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? ReadNullableInt64(NpgsqlDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static Guid? ReadNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static Task<string> ReadInstanceJsonText(Guid instanceGuid, string key)
    {
        return PostgresUtil.RunQuery<string>(
            $"select instance ->> '{key}' from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<string> ReadInstanceJsonType(Guid instanceGuid, string key)
    {
        return PostgresUtil.RunQuery<string>(
            $"select coalesce(jsonb_typeof(instance -> '{key}'), 'missing') from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<string> ReadInstanceTaskId(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<string>(
            $"select taskid from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<bool> ReadInstanceConfirmed(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<bool>(
            $"select confirmed from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<bool> InstanceJsonContainsKey(Guid instanceGuid, string key)
    {
        return PostgresUtil.RunQuery<bool>(
            $"select instance ? '{key}' from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<bool> InstanceDataValuesContainsKey(Guid instanceGuid, string key)
    {
        return PostgresUtil.RunQuery<bool>(
            $"select coalesce(instance -> 'DataValues' ? '{key}', false) from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<string> ReadDataElementJsonText(
        Guid instanceGuid,
        string dataElementId,
        string key
    )
    {
        return PostgresUtil.RunQuery<string>(
            $"select element ->> '{key}' from storage.dataelements where instanceguid = '{instanceGuid}' and alternateid = '{dataElementId}'"
        );
    }

    private static Task<string> ReadDataElementJsonType(
        Guid instanceGuid,
        string dataElementId,
        string key
    )
    {
        return PostgresUtil.RunQuery<string>(
            $"select coalesce(jsonb_typeof(element -> '{key}'), 'missing') from storage.dataelements where instanceguid = '{instanceGuid}' and alternateid = '{dataElementId}'"
        );
    }

    private static Task<int> CountOutboxRows(Guid instanceGuid, InstanceEventType eventType)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.outbox where instanceid = '{instanceGuid}' and instanceeventtype = {(int)eventType}"
        );
    }

    private static Task<DateTime> ReadOutboxValidFrom(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<DateTime>(
            $"select validfrom from storage.outbox where instanceid = '{instanceGuid}'"
        );
    }

    private static Task<int> ReadIdempotencyProducedInstanceVersion(Guid idempotencyKey)
    {
        return PostgresUtil.RunQuery<int>(
            $"select produced_instance_version from storage.instance_mutation_idempotency where idempotency_key = '{idempotencyKey}'"
        );
    }

    private static Task BumpInstanceVersion(Guid instanceGuid)
    {
        return PostgresUtil.RunSql(
            $"update storage.instances set instance_version = instance_version + 1 where alternateid = '{instanceGuid}'"
        );
    }

    private static void AssertAppliedRows(
        IReadOnlyList<ApplyMutationSqlRow> rows,
        int expectedInstanceVersion,
        int expectedProcessStateVersion,
        IReadOnlyList<string> expectedCreatedDataElementIds = null,
        bool replayed = false,
        long? expectedInternalId = null
    )
    {
        IReadOnlyList<string> createdDataElementIds = expectedCreatedDataElementIds ?? [];
        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(replayed, row.Replayed);
                Assert.Equal(createdDataElementIds, row.CreatedDataElementIds);
                if (expectedInternalId.HasValue)
                {
                    Assert.Equal(expectedInternalId, row.InternalId);
                }

                Assert.Equal(expectedInstanceVersion, row.InstanceVersion);
                Assert.Equal(expectedProcessStateVersion, row.ProcessStateVersion);
            }
        );
    }

    private static void AssertSqlVersionError(
        PostgresException exception,
        string expectedCode,
        int expectedCurrentInstanceVersion,
        int expectedCurrentProcessStateVersion
    )
    {
        Assert.Equal("AM001", exception.SqlState);
        using JsonDocument message = ParseSqlMessageJson(exception);
        Assert.Equal(expectedCode, message.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            expectedCurrentInstanceVersion,
            message.RootElement.GetProperty("currentInstanceVersion").GetInt32()
        );
        Assert.Equal(
            expectedCurrentProcessStateVersion,
            message.RootElement.GetProperty("currentProcessStateVersion").GetInt32()
        );
    }

    private static void AssertSqlError(
        PostgresException exception,
        string expectedCode,
        int expectedCurrentInstanceVersion,
        int expectedCurrentProcessStateVersion,
        string expectedDataElementId = null
    )
    {
        Assert.Equal("AM001", exception.SqlState);
        using JsonDocument message = ParseSqlMessageJson(exception);
        Assert.Equal(expectedCode, message.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            expectedCurrentInstanceVersion,
            message.RootElement.GetProperty("currentInstanceVersion").GetInt32()
        );
        Assert.Equal(
            expectedCurrentProcessStateVersion,
            message.RootElement.GetProperty("currentProcessStateVersion").GetInt32()
        );

        if (expectedDataElementId is not null)
        {
            Assert.Equal(
                expectedDataElementId,
                message.RootElement.GetProperty("dataElementId").GetString()
            );
        }
    }

    private static void AssertSqlErrorHasNoMutationTarget(PostgresException exception)
    {
        using JsonDocument message = ParseSqlMessageJson(exception);
        Assert.Equal(
            JsonValueKind.Null,
            message.RootElement.GetProperty("dataElementId").ValueKind
        );
    }

    private static JsonDocument ParseSqlMessageJson(PostgresException exception)
    {
        return JsonDocument.Parse(exception.MessageText);
    }

    private sealed record ApplyMutationSqlRow(
        bool Replayed,
        IReadOnlyList<string> CreatedDataElementIds,
        long? InternalId,
        int? InstanceVersion,
        int? ProcessStateVersion,
        Guid? CurrentBlobVersion
    );

    private sealed record UpdateInstanceSqlRow(
        string Result,
        int InstanceVersion,
        int ProcessStateVersion,
        string InstanceJson
    );

    private sealed record InstanceUpdateParityCase(
        string Name,
        string DataValues = null,
        string CompleteConfirmations = null,
        string PresentationTexts = null,
        string Status = null,
        string Substatus = null,
        string Process = null,
        string TaskId = null,
        bool? Confirmed = null,
        bool BumpsProcessState = false
    );

    private sealed record ParityInstance(Guid InstanceGuid, long InternalId);

    private sealed record InstanceStorageState(string InstanceJson, string TaskId, bool Confirmed);

    private sealed record UpdateElementPayload(
        Guid DataElementId,
        JsonObject ElementChanges = null,
        bool IsReadChangedToFalse = false,
        string NewBlobVersion = null,
        string ExpectedBlobVersion = null,
        bool IgnoreLock = false
    );

    private async Task<DataElementInternal> PrepareAggregateCreateDataElement(
        Guid instanceGuid,
        Guid? dataElementId = null
    )
    {
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement3);
        dataElement.Id = (dataElementId ?? Guid.NewGuid()).ToString();
        dataElement.InstanceGuid = instanceGuid.ToString();
        dataElement.Created ??= DateTime.UtcNow;
        dataElement.CreatedBy ??= "1337";
        dataElement.LastChanged ??= DateTime.UtcNow;
        dataElement.LastChangedBy ??= "1337";
        string blobVersionId = await CreateBlobVersionId(instanceGuid, dataElement.Id);
        dataElement.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(dataElement.InstanceGuid),
            blobVersionId
        );

        return dataElement.FromApiModel(blobVersionId);
    }

    private static Task<int> CountBlobVersionRows(string blobVersionId)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id = '{BlobVersionId.Decode(blobVersionId)}'"
        );
    }

    private static Task<int> CountAttachedBlobVersionRows(string blobVersionId)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id = '{BlobVersionId.Decode(blobVersionId)}' and detachedat is null"
        );
    }

    private InstanceMutationCommit CreateContentUpdateMutation(
        DataElement existing,
        string expectedCurrentVersion,
        string updateVersion,
        int expectedInstanceVersion,
        Guid idempotencyKey
    )
    {
        return new InstanceMutationCommit(
            [],
            [
                new InstanceMutationDataElementUpdate(
                    Guid.Parse(existing.Id),
                    new Dictionary<string, object>
                    {
                        ["/blobStoragePath"] = DataElementHelper.GetVersionedBlobPath(
                            _instance.AppId,
                            new Guid(existing.InstanceGuid),
                            updateVersion
                        ),
                        ["/currentBlobVersion"] = updateVersion,
                    },
                    expectedCurrentVersion,
                    IgnoreLock: false
                ),
            ],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            expectedInstanceVersion,
            null,
            [],
            idempotencyKey
        );
    }

    private InstanceMutationCommit CreateCreateMutation(
        IReadOnlyList<DataElementInternal> createDataElements,
        int expectedInstanceVersion,
        Guid idempotencyKey
    )
    {
        return new InstanceMutationCommit(
            createDataElements,
            [],
            [],
            new InstanceInternal { Id = _instance.Id },
            [],
            expectedInstanceVersion,
            null,
            [],
            idempotencyKey
        );
    }

    private InstanceMutationCommit CreateDeleteMutation(
        DataElement toDelete,
        int expectedInstanceVersion,
        Guid idempotencyKey
    )
    {
        return new InstanceMutationCommit(
            [],
            [],
            [new InstanceMutationDataElementDelete(toDelete.FromApiModel(), IgnoreLock: false)],
            new InstanceInternal
            {
                Id = _instance.Id,
                AppId = _instance.AppId,
                Org = _instance.Org,
                InstanceOwner = _instance.InstanceOwner,
                Created = _instance.Created,
            },
            [],
            expectedInstanceVersion,
            null,
            [
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    DataId = toDelete.Id,
                    Created = DateTime.UtcNow,
                },
            ],
            idempotencyKey
        );
    }

    private InstanceMutationCommit CreateDeleteInstanceMutation(
        DateTime deletedAt,
        int expectedInstanceVersion,
        Guid idempotencyKey
    )
    {
        return new InstanceMutationCommit(
            [],
            [],
            [],
            new InstanceInternal
            {
                Id = _instance.Id,
                AppId = _instance.AppId,
                Org = _instance.Org,
                InstanceOwner = _instance.InstanceOwner,
                Created = _instance.Created,
                Status = new InstanceStatus
                {
                    IsHardDeleted = true,
                    IsSoftDeleted = true,
                    HardDeleted = deletedAt,
                    SoftDeleted = deletedAt,
                },
                LastChanged = deletedAt,
                LastChangedBy = "1337",
            },
            [
                nameof(InstanceInternal.Status),
                nameof(InstanceStatus.IsSoftDeleted),
                nameof(InstanceStatus.SoftDeleted),
                nameof(InstanceStatus.IsHardDeleted),
                nameof(InstanceStatus.HardDeleted),
                nameof(InstanceInternal.LastChanged),
                nameof(InstanceInternal.LastChangedBy),
            ],
            expectedInstanceVersion,
            null,
            [
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    InstanceId = _instance.Id.ToString(),
                    InstanceOwnerPartyId = _instance.InstanceOwner.PartyId,
                },
            ],
            idempotencyKey
        );
    }

    private InstanceMutationCommit CreateTerminalDeleteInstanceMutation(
        DateTime processEnded,
        DateTime deletedAt,
        int expectedInstanceVersion,
        int expectedProcessStateVersion,
        Guid idempotencyKey,
        DataElement dataElement = null
    )
    {
        ProcessState endedProcess = new()
        {
            Ended = processEnded,
            EndEvent = "EndEvent_1",
            CurrentTask = null,
            Status = ProcessStatus.Idle,
        };
        List<InstanceEvent> events =
        [
            new()
            {
                EventType = InstanceEventType.process_EndEvent.ToString(),
                InstanceId = _instance.Id.ToString(),
                InstanceOwnerPartyId = _instance.InstanceOwner.PartyId,
                ProcessInfo = endedProcess,
                Created = processEnded,
            },
            new()
            {
                EventType = InstanceEventType.Deleted.ToString(),
                InstanceId = _instance.Id.ToString(),
                InstanceOwnerPartyId = _instance.InstanceOwner.PartyId,
                ProcessInfo = endedProcess,
                Created = deletedAt,
            },
        ];
        List<InstanceMutationDataElementDelete> deleteDataElements = [];
        if (dataElement is not null)
        {
            deleteDataElements.Add(
                new InstanceMutationDataElementDelete(dataElement.FromApiModel(), IgnoreLock: false)
            );
            events.Add(
                new InstanceEvent
                {
                    EventType = InstanceEventType.Deleted.ToString(),
                    InstanceId = _instance.Id.ToString(),
                    InstanceOwnerPartyId = _instance.InstanceOwner.PartyId,
                    DataId = dataElement.Id,
                    ProcessInfo = endedProcess,
                    Created = deletedAt,
                }
            );
        }

        return new InstanceMutationCommit(
            [],
            [],
            deleteDataElements,
            new InstanceInternal
            {
                Id = _instance.Id,
                AppId = _instance.AppId,
                Org = _instance.Org,
                InstanceOwner = _instance.InstanceOwner,
                Created = _instance.Created,
                Process = endedProcess,
                Status = new InstanceStatus
                {
                    IsArchived = true,
                    Archived = processEnded,
                    IsHardDeleted = true,
                    IsSoftDeleted = true,
                    HardDeleted = deletedAt,
                    SoftDeleted = deletedAt,
                },
                LastChanged = deletedAt,
                LastChangedBy = "1337",
            },
            [
                nameof(InstanceInternal.Process),
                nameof(InstanceInternal.Status),
                nameof(InstanceStatus.IsArchived),
                nameof(InstanceStatus.Archived),
                nameof(InstanceStatus.IsSoftDeleted),
                nameof(InstanceStatus.SoftDeleted),
                nameof(InstanceStatus.IsHardDeleted),
                nameof(InstanceStatus.HardDeleted),
            ],
            expectedInstanceVersion,
            expectedProcessStateVersion,
            events,
            idempotencyKey,
            deletedAt,
            "1337"
        );
    }

    private static Task<DateTime> ReadInstanceLastChangedColumn(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<DateTime>(
            $"select lastchanged from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<int> CountInstanceEvents(Guid instanceGuid, string eventType)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.instanceevents where instance = '{instanceGuid}' and event ->> 'EventType' = '{eventType}'"
        );
    }

    private static Task<int> CountIdempotencyRecords(Guid idempotencyKey)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.instance_mutation_idempotency where idempotency_key = '{idempotencyKey}'"
        );
    }

    private static Task<int> CountInstanceRowsWithNullTask(Guid instanceGuid)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.instances where alternateid = '{instanceGuid}' and taskid is null"
        );
    }

    private static Task<string> ReadStoredInstanceJson(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<string>(
            $"select instance::text from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task SetDataElementLocked(Guid instanceGuid, string dataElementId, bool locked)
    {
        return PostgresUtil.RunSql(
            $"update storage.dataelements set element = jsonb_set(element, '{{Locked}}', '{locked.ToString().ToLowerInvariant()}'::jsonb) where instanceguid = '{instanceGuid}' and alternateid = '{dataElementId}'"
        );
    }

    private Task<InstanceInternal> ReadInstance(bool includeElements = false)
    {
        return dataElementFixture.InstanceRepo.GetOne(
            _instanceGuid,
            includeElements,
            CancellationToken.None
        );
    }

    /// <summary>
    /// Reads the <c>lastchanged</c> column, which the stored procedures keep in sync with the
    /// <c>LastChanged</c> property inside the instance document. No repository method exposes it.
    /// </summary>
    private Task<DateTime> ReadInstanceLastChangedColumn()
    {
        return PostgresUtil.RunQuery<DateTime>(
            $"select lastchanged from storage.instances where alternateid = '{_instanceGuid}'"
        );
    }

    private Task<DataElementInternal> CreateDataElement(string dataElementId = _dataElement1)
    {
        return CreateDataElement(TestDataUtil.GetDataElement(dataElementId));
    }

    private Task<DataElementInternal> CreateDataElement(DataElement dataElement)
    {
        return CreateDataElement(dataElement.FromApiModel(), _instanceInternalId);
    }

    private async Task<DataElementInternal> CreateDataElement(
        DataElementInternal dataElement,
        long instanceInternalId
    )
    {
        DataElementWriteResult result = await dataElementFixture.DataRepo.Create(
            dataElement,
            instanceInternalId
        );
        return result.DataElement;
    }

    private async Task<DataElementInternal> UpdateDataElement(
        Guid instanceGuid,
        Guid dataElementId,
        Dictionary<string, object> propertyList,
        DataElementUpdateContext context = null
    )
    {
        DataElementWriteResult result = await dataElementFixture.DataRepo.Update(
            instanceGuid,
            dataElementId,
            propertyList,
            context
        );
        return result.DataElement;
    }

    private Task SetInstanceReadStatus(ReadStatus readStatus)
    {
        return SetInstanceReadStatus(_instanceGuid, readStatus);
    }

    private async Task<DataElement> CreateLegacyDataElement(DataElement dataElement)
    {
        DataElementInternal createdDataElement = await CreateDataElement(
            dataElement.FromApiModel(),
            _instanceInternalId
        );

        return createdDataElement.ToApiModel();
    }

    private async Task<(DataElement DataElement, string BlobVersionId)> CreateVersionedDataElement(
        DataElement dataElement
    )
    {
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(dataElement.InstanceGuid),
            dataElement.Id
        );
        dataElement.BlobStoragePath = DataElementHelper.GetVersionedBlobPath(
            _instance.AppId,
            new Guid(dataElement.InstanceGuid),
            blobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            dataElement.FromApiModel(blobVersionId),
            _instanceInternalId
        );

        return (createdDataElement.ToApiModel(), blobVersionId);
    }

    private static Task<int> CountDetachedBlobVersionRows(string blobVersionId)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id = '{BlobVersionId.Decode(blobVersionId)}' and detachedat is not null"
        );
    }

    private static Task<int> CountAttachedBlobVersionRowsForDataElement(string dataElementId)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where dataelementid = '{dataElementId}' and detachedat is null"
        );
    }

    private static Task<DateTime> ReadBlobVersionDetachedAt(string blobVersionId)
    {
        return PostgresUtil.RunQuery<DateTime>(
            $"select detachedat at time zone 'utc' from storage.dataelementblobversions where id = '{BlobVersionId.Decode(blobVersionId)}'"
        );
    }

    private Task<string> CreateBlobVersionId(Guid instanceGuid, string dataElementId = null)
    {
        return dataElementFixture.DataRepo.CreateBlobVersionId(
            instanceGuid,
            string.IsNullOrEmpty(dataElementId) ? Guid.NewGuid() : Guid.Parse(dataElementId),
            _instance.AppId,
            _instance.Org,
            null
        );
    }

    private static Task SetStoredProcessRepresentation(Guid instanceGuid, string representation)
    {
        string instanceUpdate = representation switch
        {
            "status-absent" =>
                "jsonb_set(instance, '{Process}', CASE WHEN jsonb_typeof(instance -> 'Process') = 'object' THEN (instance -> 'Process') - 'Status' ELSE '{}'::jsonb END)",
            "status-null" =>
                "jsonb_set(instance, '{Process}', (CASE WHEN jsonb_typeof(instance -> 'Process') = 'object' THEN instance -> 'Process' ELSE '{}'::jsonb END) || '{\"Status\":null}'::jsonb)",
            "process-absent" => "instance - 'Process'",
            "process-null" => "jsonb_set(instance, '{Process}', 'null'::jsonb)",
            "process-string" => "jsonb_set(instance, '{Process}', '\"legacy\"'::jsonb)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(representation),
                representation,
                "Unknown process representation."
            ),
        };

        return PostgresUtil.RunSql(
            $"update storage.instances set instance = {instanceUpdate} where alternateid = '{instanceGuid}'"
        );
    }

    private static Task SetStoredProcessStatus(Guid instanceGuid, ProcessStatus status)
    {
        return PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Process}}', (CASE WHEN jsonb_typeof(instance -> 'Process') = 'object' THEN instance -> 'Process' ELSE '{{}}'::jsonb END) || jsonb_build_object('Status', '{JsonSerializer.Serialize(status)}'::jsonb)) where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<string> ReadStoredProcessStatus(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<string>(
            $"select coalesce(instance -> 'Process' ->> 'Status', 'idle') from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private async Task WaitForBlockedAggregateMutations(int expectedCount)
    {
        await WaitForBlockedDatabaseCalls("storage.applyinstancemutation", expectedCount);
    }

    private async Task WaitForBlockedDatabaseCalls(string queryFragment, int expectedCount)
    {
        await using NpgsqlConnection observerConnection =
            await dataElementFixture.DataSource.OpenConnectionAsync();
        await using NpgsqlCommand command = new(
            """
            select count(*)::int
            from pg_stat_activity activity
            where activity.pid <> pg_backend_pid()
                and activity.datname = current_database()
                and activity.state = 'active'
                and activity.wait_event_type = 'Lock'
                and position($1 in activity.query) > 0
            """,
            observerConnection
        );
        command.Parameters.AddWithValue(NpgsqlDbType.Text, queryFragment);

        DateTime timeoutAt = DateTime.UtcNow.AddSeconds(10);
        while (Convert.ToInt32(await command.ExecuteScalarAsync()) < expectedCount)
        {
            if (DateTime.UtcNow >= timeoutAt)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {expectedCount} calls containing '{queryFragment}' to wait on PostgreSQL locks."
                );
            }

            await Task.Delay(10);
        }
    }

    private static Task<int> ReadInstanceVersion(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<int>(
            $"select instance_version from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task<int> ReadProcessStateVersion(Guid instanceGuid)
    {
        return PostgresUtil.RunQuery<int>(
            $"select process_state_version from storage.instances where alternateid = '{instanceGuid}'"
        );
    }

    private static Task SetInstanceHardDeleted(Guid instanceGuid)
    {
        return PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Status,IsHardDeleted}}', 'true'::jsonb) where alternateid = '{instanceGuid}'"
        );
    }

    private static Task SetInstanceReadStatus(Guid instanceGuid, ReadStatus readStatus)
    {
        return PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Status, ReadStatus}}', '{(int)readStatus}'::jsonb) where alternateid = '{instanceGuid}'"
        );
    }
}

public class DataElementFixture
{
    public IInstanceRepository InstanceRepo { get; set; }

    public IDataRepository DataRepo { get; set; }

    internal IInstanceMutationRepository InstanceMutationRepo { get; set; }

    public NpgsqlDataSource DataSource { get; set; }

    public DataElementFixture()
    {
        var serviceList = ServiceUtil.GetServices([
            typeof(IInstanceRepository),
            typeof(IDataRepository),
            typeof(IInstanceMutationRepository),
            typeof(NpgsqlDataSource),
        ]);
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
        DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
        InstanceMutationRepo = (IInstanceMutationRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceMutationRepository));
        DataSource = serviceList.OfType<NpgsqlDataSource>().First();
    }
}
