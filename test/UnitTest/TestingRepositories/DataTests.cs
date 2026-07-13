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
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class DataTests : IClassFixture<DataElementFixture>
{
    private const string DataElement1 = "cdb627fd-c586-41f5-99db-bae38daa2b59";
    private const string DataElement2 = "d03b4a04-f0df-4ead-be92-aa7a68959dab";
    private const string DataElement3 = "5ebeb498-677d-476f-8cab-b788a0fd0640";

    private readonly DataElementFixture _dataElementFixture;
    private readonly long _instanceInternalId;
    private readonly InstanceInternal _instance;

    public DataTests(DataElementFixture dataElementFixture)
    {
        _dataElementFixture = dataElementFixture;

        string sql = "delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Status.IsSoftDeleted = true;
        InstanceInternal newInstance = _dataElementFixture
            .InstanceRepo.Create(instance, CancellationToken.None)
            .Result;
        _instance = _dataElementFixture
            .InstanceRepo.GetOne(
                Guid.Parse(newInstance.Id.Split('/').Last()),
                false,
                CancellationToken.None
            )
            .Result;
        _instanceInternalId = _instance.InternalId;
    }

    /// <summary>
    /// Test create and change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Create_Change_Instance_Readstatus_Ok()
    {
        // Arrange
        DateTime lastChanged = DateTime.UtcNow;
        DataElementInternal dataElement = TestDataUtil.GetDataElement(DataElement1).FromApiModel();
        dataElement.LastChanged = lastChanged;

        // Act
        dataElement = await CreateDataElement(dataElement, _instanceInternalId);
        InstanceInternal instance = await _dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(dataElement.InstanceGuid),
            false,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '2'"
            + $" and lastchanged = '{((DateTime)dataElement.LastChanged).ToString("o")}' and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
        int instanceCount = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, dataCount);
        Assert.Equal(1, instanceCount);
        Assert.Equal(instance.LastChanged, dataElement.LastChanged);
        Assert.True(
            Math.Abs(((DateTime)dataElement.LastChanged).Ticks - lastChanged.Ticks)
                < TimeSpan.TicksPerMicrosecond
        );
    }

    /// <summary>
    /// Test create and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Create_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        await PostgresUtil.RunSql(
            "update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '0') where alternateid = '"
                + _instance.Id.Split('/').Last()
                + "';"
        );

        // Act
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );

        // Assert
        string sql =
            $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
            + $" and lastchanged = '{((DateTime)dataElement.LastChanged).ToString("o")}' and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
        int instanceCount = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, dataCount);
        Assert.Equal(1, instanceCount);
    }

    /// <summary>
    /// Test update, insert metadata
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Metadata_Insert_Ok()
    {
        // Arrange
        List<KeyValueEntry> metadata = new()
        {
            {
                new() { Key = "key1", Value = "value1" }
            },
            {
                new() { Key = "key2", Value = "value2" }
            },
        };
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>() { { "/metadata", metadata } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(metadata),
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
        List<KeyValueEntry> orgMetadata = new()
        {
            {
                new() { Key = "key1", Value = "value1" }
            },
            {
                new() { Key = "key2", Value = "value2" }
            },
        };
        List<KeyValueEntry> replacedMetadata = new()
        {
            {
                new() { Key = "key3", Value = "value3" }
            },
            {
                new() { Key = "key4", Value = "value4" }
            },
        };
        DataElementInternal initialDataElement = TestDataUtil
            .GetDataElement(DataElement1)
            .FromApiModel();
        initialDataElement.Metadata = orgMetadata;
        DataElementInternal dataElement = await CreateDataElement(
            initialDataElement,
            _instanceInternalId
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>() { { "/metadata", replacedMetadata } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(replacedMetadata),
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
        List<KeyValueEntry> userDefinedMetadata = new()
        {
            {
                new() { Key = "key1", Value = "value1" }
            },
            {
                new() { Key = "key2", Value = "value2" }
            },
        };
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>() { { "/userDefinedMetadata", userDefinedMetadata } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(userDefinedMetadata),
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
        List<KeyValueEntry> originalUserDefinedMetadata = new()
        {
            {
                new() { Key = "key1", Value = "value1" }
            },
            {
                new() { Key = "key2", Value = "value2" }
            },
        };
        List<KeyValueEntry> replacedUserDefinedMetadata = new()
        {
            {
                new() { Key = "key3", Value = "value3" }
            },
            {
                new() { Key = "key4", Value = "value4" }
            },
        };
        DataElementInternal initialDataElement = TestDataUtil
            .GetDataElement(DataElement1)
            .FromApiModel();
        initialDataElement.UserDefinedMetadata = originalUserDefinedMetadata;
        DataElementInternal dataElement = await CreateDataElement(
            initialDataElement,
            _instanceInternalId
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>()
            {
                { "/userDefinedMetadata", replacedUserDefinedMetadata },
            }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(replacedUserDefinedMetadata),
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
        List<string> tags = new() { "s1", "s2" };
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>() { { "/tags", tags } }
        );

        // Assert
        Assert.Equal(JsonSerializer.Serialize(tags), JsonSerializer.Serialize(updatedElement.Tags));
    }

    /// <summary>
    /// Test update, replace tags
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Tags_Replace_Ok()
    {
        // Arrange
        List<string> orgTags = new() { "s1", "s2" };
        List<string> replacedTags = new() { "s3", "s4" };
        DataElementInternal initialDataElement = TestDataUtil
            .GetDataElement(DataElement1)
            .FromApiModel();
        initialDataElement.Tags = orgTags;
        DataElementInternal dataElement = await CreateDataElement(
            initialDataElement,
            _instanceInternalId
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>() { { "/tags", replacedTags } }
        );

        // Assert
        Assert.Equal(
            JsonSerializer.Serialize(replacedTags),
            JsonSerializer.Serialize(updatedElement.Tags)
        );
    }

    /// <summary>
    /// Test update and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Update_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        string contentType = "unittestContentType";
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );
        string restoreValues =
            """{"Status": {"ReadStatus": 0},"LastChanged": "<lastChanged>","LastChangedBy": "<lastChangedBy>"}"""
                .Replace("<lastChanged>", ((DateTime)_instance.LastChanged).ToString("o"))
                .Replace("<lastChangedBy>", _instance.LastChangedBy);
        await PostgresUtil.RunSql(
            $"update storage.instances set instance = instance || '{restoreValues}', lastChanged = '{((DateTime)_instance.LastChanged).ToString("o")}' where alternateid = '{_instance.Id.Split('/').Last()}';"
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>() { { "/contentType", contentType } }
        );

        // Assert
        string sql =
            $"select count(*) from storage.dataelements where element ->> 'ContentType' = '{contentType}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
            + $" and lastchanged = '{((DateTime)_instance.LastChanged).ToString("o")}' and instance -> 'LastChangedBy' = '\"{_instance.LastChangedBy}\"'";
        int instanceCount = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, dataCount);
        Assert.Equal(1, instanceCount);
        Assert.Equal(contentType, updatedElement.ContentType);
    }

    /// <summary>
    /// Test update and change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Change_Instance_Readstatus_Ok()
    {
        // Arrange
        string contentType = "unittestContentType";
        DateTime lastChanged = DateTime.UtcNow;
        DataElementInternal element = TestDataUtil.GetDataElement(DataElement1).FromApiModel();
        element.LastChanged = lastChanged;
        DataElementInternal dataElement = await CreateDataElement(element, _instanceInternalId);
        await PostgresUtil.RunSql(
            "update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '1') where alternateid = '"
                + _instance.Id.Split('/').Last()
                + "';"
        );

        // Act
        DataElementInternal updatedElement = await UpdateDataElement(
            Guid.Parse(_instance.Id.Split('/').Last()),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>()
            {
                { "/contentType", contentType },
                { "/isRead", false },
                { "/lastChanged", dataElement.LastChanged },
                { "/lastChangedBy", dataElement.LastChangedBy },
            }
        );
        InstanceInternal instance = await _dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(updatedElement.InstanceGuid),
            false,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.dataelements where element ->> 'ContentType' = '{contentType}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
            + $" and lastchanged = '{((DateTime)dataElement.LastChanged).ToString("o")}' and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
        int instanceCount = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, dataCount);
        Assert.Equal(1, instanceCount);
        Assert.Equal(contentType, updatedElement.ContentType);
        Assert.Equal(instance.LastChanged, updatedElement.LastChanged);
        Assert.True(
            Math.Abs(((DateTime)updatedElement.LastChanged).Ticks - lastChanged.Ticks)
                < TimeSpan.TicksPerMicrosecond
        );
    }

    [Fact]
    public async Task DataElement_Update_BlobVersionId_LockedDataElement_ThrowsConflictAndDoesNotUpdateInstance()
    {
        // Arrange
        string contentType = $"locked-{Guid.NewGuid()}";
        string lastChangedBy = $"locked-user-{Guid.NewGuid()}";
        DateTime lastChanged = DateTime.UtcNow;
        DataElement element = TestDataUtil.GetDataElement(DataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
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
                        BlobRepository.GetVersionedBlobPath(
                            _instance.AppId,
                            dataElement.InstanceGuid,
                            blobVersionId
                        )
                    },
                    { "/currentBlobVersion", blobVersionId },
                    { "/lastChanged", lastChanged },
                    { "/lastChangedBy", lastChangedBy },
                },
                new DataElementUpdateContext { EnforceLockCheck = true }
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
    }

    [Fact]
    public async Task DataElement_Update_BlobVersionId_HardDeletedDataElement_ThrowsNotFoundAndDoesNotUpdateInstance()
    {
        // Arrange
        string contentType = $"hard-deleted-{Guid.NewGuid()}";
        string lastChangedBy = $"hard-deleted-user-{Guid.NewGuid()}";
        DateTime lastChanged = DateTime.UtcNow;
        DataElement element = TestDataUtil.GetDataElement(DataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
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
                        BlobRepository.GetVersionedBlobPath(
                            _instance.AppId,
                            dataElement.InstanceGuid,
                            blobVersionId
                        )
                    },
                    { "/currentBlobVersion", blobVersionId },
                    { "/lastChanged", lastChanged },
                    { "/lastChangedBy", lastChangedBy },
                },
                new DataElementUpdateContext { EnforceLockCheck = true }
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
    }

    [Fact]
    public async Task DataElement_Create_HardDeletedInstance_ThrowsNotFoundAndDoesNotAttachBlobVersion()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "hard-deleted-instance-create-test-setup";
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
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
            $"select count(*) from storage.dataelementblobversions where id = '{BlobVersionId.Decode(blobVersionId)}' and attached = true"
        );
        Assert.Equal(0, dataCount);
        Assert.Equal(0, attachedVersionCount);
    }

    [Fact]
    public async Task DataElement_Create_UnavailableBlobVersion_ThrowsConflictAndDoesNotCreateElement()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        string blobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            blobVersionId
        );

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            CreateDataElement(element.FromApiModel(blobVersionId), _instanceInternalId)
        );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        int dataCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelements where alternateid = '{element.Id}'"
        );
        Assert.Equal(0, dataCount);
    }

    [Fact]
    public async Task DataElement_Update_HardDeletedInstance_ThrowsNotFoundAndDoesNotUpdateElement()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
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
        DataElementInternal readElement = await _dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );
        Assert.False(readElement.IsRead);
    }

    [Fact]
    public async Task DataElement_Update_IsRead_LockedDataElement_UpdatesIsRead()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
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
    public async Task DataElement_Update_IsRead_HardDeletedDataElement_UpdatesIsRead()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        element.IsRead = false;
        element.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "isread-harddeleted-test-setup";
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
        Assert.True(updatedElement.DeleteStatus.IsHardDeleted);
    }

    [Fact]
    public async Task DataElement_UpdateReadStatus_ToFalse_UpdatesAggregateReadStatusWithoutBumpingVersions()
    {
        // Arrange
        Guid instanceGuid = Guid.Parse(_instance.Id.Split('/').Last());
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = instanceGuid.ToString();
        element.IsRead = true;
        DataElement dataElement = await CreateLegacyDataElement(element);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await _dataElementFixture.DataRepo.UpdateReadStatus(
            instanceGuid,
            Guid.Parse(dataElement.Id),
            false
        );
        InstanceInternal instanceInternal = await _dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

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
        Guid instanceGuid = Guid.Parse(_instance.Id.Split('/').Last());
        DataElement targetElement = TestDataUtil.GetDataElement(DataElement1);
        targetElement.Id = Guid.NewGuid().ToString();
        targetElement.InstanceGuid = instanceGuid.ToString();
        targetElement.IsRead = true;
        DataElement otherElement = TestDataUtil.GetDataElement(DataElement2);
        otherElement.Id = Guid.NewGuid().ToString();
        otherElement.InstanceGuid = instanceGuid.ToString();
        otherElement.IsRead = true;
        DataElement targetDataElement = await CreateLegacyDataElement(targetElement);
        DataElement otherDataElement = await CreateLegacyDataElement(otherElement);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Read);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await _dataElementFixture.DataRepo.UpdateReadStatus(
            instanceGuid,
            Guid.Parse(targetDataElement.Id),
            false
        );
        DataElementInternal readOtherElement = await _dataElementFixture.DataRepo.Read(
            instanceGuid,
            Guid.Parse(otherDataElement.Id)
        );
        InstanceInternal instanceInternal = await _dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

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
        Guid instanceGuid = Guid.Parse(_instance.Id.Split('/').Last());
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = instanceGuid.ToString();
        element.IsRead = false;
        DataElement dataElement = await CreateLegacyDataElement(element);
        await SetInstanceReadStatus(instanceGuid, ReadStatus.Unread);
        int previousInstanceVersion = await ReadInstanceVersion(instanceGuid);
        int previousProcessStateVersion = await ReadProcessStateVersion(instanceGuid);

        // Act
        DataElementWriteResult result = await _dataElementFixture.DataRepo.UpdateReadStatus(
            instanceGuid,
            Guid.Parse(dataElement.Id),
            true
        );
        InstanceInternal instanceInternal = await _dataElementFixture.InstanceRepo.GetOne(
            instanceGuid,
            false,
            CancellationToken.None
        );

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
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            blobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementWriteResult updateResult = await UpdateFileScanStatus(
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
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.FileScanResult = FileScanResult.Pending;
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        string staleBlobVersionId = BlobVersionId.Encode(Guid.NewGuid());
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            blobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementWriteResult updateResult = await UpdateFileScanStatus(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean,
                BlobVersionId = staleBlobVersionId,
            }
        );

        // Assert
        DataElementInternal readElement = await _dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );
        Assert.Null(updateResult);
        Assert.Equal(FileScanResult.Pending, readElement.FileScanResult);
    }

    [Fact]
    public async Task DataElement_UpdateFileScanStatus_HardDeletedInstance_DoesNotUpdateStatus()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        element.FileScanResult = FileScanResult.Pending;
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            blobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();
        await SetInstanceHardDeleted(Guid.Parse(dataElement.InstanceGuid));

        // Act
        DataElementWriteResult updateResult = await UpdateFileScanStatus(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean,
                BlobVersionId = blobVersionId,
            }
        );

        // Assert
        DataElementInternal readElement = await _dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );
        Assert.Null(updateResult);
        Assert.Equal(FileScanResult.Pending, readElement.FileScanResult);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DataElement_UpdateFileScanStatus_MissingBlobVersion_UpdatesStatus(
        string blobVersionId
    )
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.FileScanResult = FileScanResult.Pending;
        string currentBlobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            currentBlobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementWriteResult updateResult = await UpdateFileScanStatus(
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
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.FileScanResult = FileScanResult.Pending;
        string currentBlobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            currentBlobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            UpdateFileScanStatus(
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
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.BlobStoragePath =
            $"{_instance.AppId}/{_instance.Id.Split('/').Last()}/data/{element.Id}";
        string firstVersion = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        string secondVersion = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            firstVersion
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(firstVersion),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();
        string versionedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
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
        DataElementInternal readElement = await _dataElementFixture.DataRepo.Read(
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
    public async Task DataElement_Update_ExpectedBlobVersionMismatch_ThrowsConflictAndDoesNotUpdate()
    {
        // Arrange
        string originalContentType = $"original-{Guid.NewGuid()}";
        string newContentType = $"updated-{Guid.NewGuid()}";
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        element.ContentType = originalContentType;
        element.BlobStoragePath = $"ttd/app/{element.InstanceGuid}/data/{element.Id}";
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "expected-version-test-setup";
        string currentBlobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        string expectedBlobVersionId = BlobVersionId.Encode(Guid.NewGuid());
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            currentBlobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            element.FromApiModel(currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        RepositoryException exception =
            await Assert.ThrowsAsync<DataElementBlobVersionMismatchException>(() =>
                UpdateDataElement(
                    Guid.Parse(dataElement.InstanceGuid),
                    Guid.Parse(dataElement.Id),
                    new Dictionary<string, object> { { "/contentType", newContentType } },
                    new DataElementUpdateContext
                    {
                        ExpectedCurrentBlobVersion = expectedBlobVersionId,
                    }
                )
            );

        DataElementInternal readElement = await _dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(originalContentType, readElement.ContentType);
        Assert.Equal(currentBlobVersionId, readElement.BlobVersionId);
    }

    [Fact]
    public async Task DataElement_Update_UnavailableBlobVersion_ThrowsConflictAndDoesNotUpdate()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        (DataElement dataElement, string currentBlobVersionId) = await CreateVersionedDataElement(
            element
        );
        string missingBlobVersionId = BlobVersionId.Encode(Guid.CreateVersion7());
        string missingBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            missingBlobVersionId
        );

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            UpdateDataElement(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object>
                {
                    { "/blobStoragePath", missingBlobStoragePath },
                    { "/currentBlobVersion", missingBlobVersionId },
                }
            )
        );

        DataElementInternal readElement = await _dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(currentBlobVersionId, readElement.BlobVersionId);
        Assert.Equal(dataElement.BlobStoragePath, readElement.BlobStoragePath);
    }

    [Fact]
    public async Task CreateBlobVersionId_CreatesUnattachedUuidV7Rows()
    {
        Guid instanceGuid = Guid.Parse(_instance.Id.Split('/').Last());
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
            $"select count(*) from storage.dataelementblobversions where id in ('{firstVersionUuid}', '{secondVersionUuid}') and dataelementid = '{dataElementId}' and attached = false and instanceguid = '{instanceGuid}' and appid = '{_instance.AppId}' and blobstorageorg = '{_instance.Org}'"
        );
        Assert.Equal(2, versionCount);
    }

    [Fact]
    public async Task GetOne_InstanceNotFound_ReturnsNull()
    {
        // Arrange
        Guid nonExistentInstanceGuid = Guid.NewGuid();

        // Act
        InstanceInternal instance = await _dataElementFixture.InstanceRepo.GetOne(
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
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );

        // Act
        DataElementInternal readDataelement = await _dataElementFixture.DataRepo.Read(
            Guid.Empty,
            Guid.Parse(dataElement.Id)
        );

        // Assert
        Assert.Equal(dataElement.Id, readDataelement.Id);
    }

    /// <summary>
    /// Test delete and change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Delete_Change_Instance_Readstatus_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );
        await PostgresUtil.RunSql(
            "update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '1') where alternateid = '"
                + _instance.Id.Split('/').Last()
                + "';"
        );

        // Act
        bool deleted = await _dataElementFixture.DataRepo.Delete(dataElement);

        // Assert
        string sql =
            $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
            + $" and lastchanged between now() - make_interval(secs => 2) and now() and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
        int instanceCount = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, dataCount);
        Assert.Equal(1, instanceCount);
    }

    /// <summary>
    /// Test delete and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Delete_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );
        await PostgresUtil.RunSql(
            "update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '0') where alternateid = '"
                + _instance.Id.Split('/').Last()
                + "';"
        );

        // Act
        bool deleted = await _dataElementFixture.DataRepo.Delete(dataElement);

        // Assert
        string sql =
            $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
            + $" and lastchanged between now() - make_interval(secs => 2) and now() and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
        int instanceCount = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, dataCount);
        Assert.Equal(1, instanceCount);
    }

    /// <summary>
    /// Test DeleteForInstance
    /// </summary>
    [Fact]
    public async Task DataElement_DeleteForInstance_Ok()
    {
        // Arrange
        await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );
        await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement2).FromApiModel(),
            _instanceInternalId
        );

        // Act
        bool deleted = await _dataElementFixture.DataRepo.DeleteForInstance(
            _instance.Id.Split('/').Last()
        );

        // Assert
        string sql =
            $"select count(*) from storage.dataelements where instanceguid = '{_instance.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, count);
        Assert.True(deleted);
    }

    /// <summary>
    /// Test update, fail if too many properties
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Too_Many_Properties_Throws_Exception()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );
        const int numberOfAllowedProperties = 16;

        Dictionary<string, object> tooManyPropertiesDictionary = Enumerable
            .Range(1, numberOfAllowedProperties + 1) // Add one extra property to make it fail.
            .ToDictionary(i => $"Key{i}", i => (object)$"Value{i}");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await UpdateDataElement(
                Guid.Empty,
                Guid.Parse(dataElement.Id),
                tooManyPropertiesDictionary
            );
        });
    }

    /// <summary>
    /// Test exists
    /// </summary>
    [Fact]
    public async Task DataElement_Exists_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement(
            TestDataUtil.GetDataElement(DataElement1).FromApiModel(),
            _instanceInternalId
        );

        // Act
        bool result = await _dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id));

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
        bool result = await _dataElementFixture.DataRepo.Exists(Guid.Parse(DataElement1));

        // Assert
        Assert.False(result);
    }

    private async Task<DataElementInternal> CreateDataElement(
        DataElementInternal dataElement,
        long instanceInternalId
    )
    {
        DataElementWriteResult result = await _dataElementFixture.DataRepo.Create(
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
        DataElementWriteResult result = await _dataElementFixture.DataRepo.Update(
            instanceGuid,
            dataElementId,
            propertyList,
            context
        );
        return result.DataElement;
    }

    private async Task<DataElementWriteResult> UpdateFileScanStatus(
        Guid instanceGuid,
        Guid dataElementId,
        FileScanStatus fileScanStatus
    )
    {
        return await _dataElementFixture.DataRepo.UpdateFileScanStatus(
            instanceGuid,
            dataElementId,
            fileScanStatus
        );
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
        dataElement.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            dataElement.InstanceGuid,
            blobVersionId
        );
        DataElementInternal createdDataElement = await CreateDataElement(
            dataElement.FromApiModel(blobVersionId),
            _instanceInternalId
        );

        return (createdDataElement.ToApiModel(), blobVersionId);
    }

    private Task<string> CreateBlobVersionId(Guid instanceGuid, string dataElementId = null)
    {
        return _dataElementFixture.DataRepo.CreateBlobVersionId(
            instanceGuid,
            string.IsNullOrEmpty(dataElementId) ? Guid.NewGuid() : Guid.Parse(dataElementId),
            _instance.AppId,
            _instance.Org,
            null
        );
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

    public DataElementFixture()
    {
        var serviceList = ServiceUtil.GetServices(
            new List<Type>() { typeof(IInstanceRepository), typeof(IDataRepository) }
        );
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
        DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
    }
}
