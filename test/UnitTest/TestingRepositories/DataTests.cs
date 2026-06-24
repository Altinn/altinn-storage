#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly Instance _instance;

    public DataTests(DataElementFixture dataElementFixture)
    {
        _dataElementFixture = dataElementFixture;

        string sql =
            "delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
        Instance instance = TestData.Instance_1_1.Clone();
        instance.Status.IsSoftDeleted = true;
        Instance newInstance = _dataElementFixture
            .InstanceRepo.Create(instance, CancellationToken.None)
            .Result;
        (InstanceInternal instanceInternal, _instanceInternalId) = _dataElementFixture
            .InstanceRepo.GetOne(
                Guid.Parse(newInstance.Id.Split('/').Last()),
                false,
                CancellationToken.None
            )
            .Result;
        _instance = instanceInternal.Instance;
    }

    /// <summary>
    /// Test create and change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Create_Change_Instance_Readstatus_Ok()
    {
        // Arrange
        DateTime lastChanged = DateTime.UtcNow;
        DataElement dataElement = TestDataUtil.GetDataElement(DataElement1);
        dataElement.LastChanged = lastChanged;

        // Act
        dataElement = await CreateLegacyDataElement(dataElement);
        (InstanceInternal instanceInternal, _) = await _dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(dataElement.InstanceGuid),
            false,
            CancellationToken.None
        );
        Instance instance = instanceInternal.Instance;

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
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
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
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
        );

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        DataElement initialDataElement = TestDataUtil.GetDataElement(DataElement1);
        initialDataElement.Metadata = orgMetadata;
        DataElement dataElement = await CreateLegacyDataElement(initialDataElement);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
        );

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        DataElement initialDataElement = TestDataUtil.GetDataElement(DataElement1);
        initialDataElement.UserDefinedMetadata = originalUserDefinedMetadata;
        DataElement dataElement = await CreateLegacyDataElement(initialDataElement);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
        );

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        DataElement initialDataElement = TestDataUtil.GetDataElement(DataElement1);
        initialDataElement.Tags = orgTags;
        DataElement dataElement = await CreateLegacyDataElement(initialDataElement);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
        );
        string restoreValues =
            """{"Status": {"ReadStatus": 0},"LastChanged": "<lastChanged>","LastChangedBy": "<lastChangedBy>"}"""
                .Replace("<lastChanged>", ((DateTime)_instance.LastChanged).ToString("o"))
                .Replace("<lastChangedBy>", _instance.LastChangedBy);
        await PostgresUtil.RunSql(
            $"update storage.instances set instance = instance || '{restoreValues}', lastChanged = '{((DateTime)_instance.LastChanged).ToString("o")}' where alternateid = '{_instance.Id.Split('/').Last()}';"
        );

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.LastChanged = lastChanged;
        DataElement dataElement = await CreateLegacyDataElement(element);
        await PostgresUtil.RunSql(
            "update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '1') where alternateid = '"
                + _instance.Id.Split('/').Last()
                + "';"
        );

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        (InstanceInternal instanceInternal, _) = await _dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(updatedElement.InstanceGuid),
            false,
            CancellationToken.None
        );
        Instance instance = instanceInternal.Instance;

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
            _dataElementFixture.DataRepo.Update(
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
            _dataElementFixture.DataRepo.Update(
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
            _dataElementFixture.DataRepo.Create(
                new DataElementInternal(element, blobVersionId),
                _instanceInternalId
            )
        );

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
        int dataCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelements where alternateid = '{element.Id}'"
        );
        int attachedVersionCount = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id = '{BlobVersionId.Decode(blobVersionId)}' and attached is not null"
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
            _dataElementFixture.DataRepo.Create(
                new DataElementInternal(element, blobVersionId),
                _instanceInternalId
            )
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
            _dataElementFixture.DataRepo.Update(
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
        Assert.False(readElement.DataElement.IsRead);
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
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>() { { "/isRead", true } }
        );

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
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>() { { "/isRead", true } }
        );

        // Assert
        Assert.True(updatedElement.IsRead);
        Assert.True(updatedElement.DeleteStatus.IsHardDeleted);
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
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(element, blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.DataElement;

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.UpdateFileScanStatus(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean,
                BlobVersionId = blobVersionId,
            }
        );

        // Assert
        Assert.NotNull(updatedElement);
        Assert.Equal(FileScanResult.Clean, updatedElement.FileScanResult);
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
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(element, blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.DataElement;

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.UpdateFileScanStatus(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean,
                BlobVersionId = staleBlobVersionId,
            }
        );

        // Assert
        DataElement readElement = (
            await _dataElementFixture.DataRepo.Read(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id)
            )
        ).DataElement;
        Assert.Null(updatedElement);
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
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(element, blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.DataElement;
        await SetInstanceHardDeleted(Guid.Parse(dataElement.InstanceGuid));

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.UpdateFileScanStatus(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean,
                BlobVersionId = blobVersionId,
            }
        );

        // Assert
        DataElement readElement = (
            await _dataElementFixture.DataRepo.Read(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id)
            )
        ).DataElement;
        Assert.Null(updatedElement);
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
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(element, currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.DataElement;

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.UpdateFileScanStatus(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean,
                BlobVersionId = blobVersionId,
            }
        );

        // Assert
        Assert.NotNull(updatedElement);
        Assert.Equal(FileScanResult.Clean, updatedElement.FileScanResult);
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
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(element, currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.DataElement;

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            _dataElementFixture.DataRepo.UpdateFileScanStatus(
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
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(element, firstVersion),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.DataElement;
        string versionedBlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            secondVersion
        );

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(
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
        Assert.Equal(versionedBlobStoragePath, readElement.DataElement.BlobStoragePath);
        Assert.Equal(secondVersion, readElement.BlobVersionId);
        Assert.DoesNotContain("BlobVersionId", readElement.DataElement.ToString());
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
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(element, currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.DataElement;

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            _dataElementFixture.DataRepo.Update(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object> { { "/contentType", newContentType } },
                new DataElementUpdateContext { ExpectedCurrentBlobVersion = expectedBlobVersionId }
            )
        );

        DataElementInternal readElement = await _dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCodeSuggestion);
        Assert.Equal(originalContentType, readElement.DataElement.ContentType);
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
            _dataElementFixture.DataRepo.Update(
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
        Assert.Equal(dataElement.BlobStoragePath, readElement.DataElement.BlobStoragePath);
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
            $"select count(*) from storage.dataelementblobversions where id in ('{firstVersionUuid}', '{secondVersionUuid}') and dataelementid = '{dataElementId}' and attached is null and instanceguid = '{instanceGuid}' and appid = '{_instance.AppId}' and blobstorageorg = '{_instance.Org}'"
        );
        Assert.Equal(2, versionCount);
    }

    [Fact]
    public async Task DeleteOrphanBlobVersions_DeletesExactUnattachedVersions()
    {
        Guid instanceGuid = Guid.Parse(_instance.Id.Split('/').Last());
        string firstVersion = await CreateBlobVersionId(instanceGuid);
        string secondVersion = await CreateBlobVersionId(instanceGuid);
        Guid firstVersionUuid = BlobVersionId.Decode(firstVersion);
        Guid secondVersionUuid = BlobVersionId.Decode(secondVersion);

        int deletedFirst = await _dataElementFixture.DataRepo.DeleteOrphanBlobVersions([
            firstVersion,
        ]);
        int versionCountAfterFirstDelete = await PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id in ('{firstVersionUuid}', '{secondVersionUuid}')"
        );

        int deletedSecond = await _dataElementFixture.DataRepo.DeleteOrphanBlobVersions([
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
    public async Task GetOne_InstanceNotFound_ReturnsNullAndZero()
    {
        // Arrange
        Guid nonExistentInstanceGuid = Guid.NewGuid();

        // Act
        (InstanceInternal instanceInternal, long internalId) =
            await _dataElementFixture.InstanceRepo.GetOne(
                nonExistentInstanceGuid,
                false,
                CancellationToken.None
            );

        // Assert
        Assert.Null(instanceInternal);
        Assert.Equal(0, internalId);
    }

    /// <summary>
    /// Test read
    /// </summary>
    [Fact]
    public async Task DataElement_Read_Ok()
    {
        // Arrange
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
        );

        // Act
        DataElement readDataelement = (
            await _dataElementFixture.DataRepo.Read(Guid.Empty, Guid.Parse(dataElement.Id))
        ).DataElement;

        // Assert
        Assert.Equal(dataElement.Id, readDataelement.Id);
    }

    /// <summary>
    /// Test delete and change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Delete_VersionedDataElement_RemovesAttachedBlobVersionRows()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        (DataElement dataElement, string blobVersionId) = await CreateVersionedDataElement(element);

        // Act
        int versionCountBeforeDelete = await CountBlobVersionRows(blobVersionId);
        bool deleted = await _dataElementFixture.DataRepo.Delete(dataElement);
        int versionCountAfterDelete = await CountBlobVersionRows(blobVersionId);

        // Assert
        Assert.True(deleted);
        Assert.Equal(1, versionCountBeforeDelete);
        Assert.Equal(0, versionCountAfterDelete);
    }

    [Fact]
    public async Task DataElement_Delete_Change_Instance_Readstatus_Ok()
    {
        // Arrange
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
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
    public async Task DataElement_DeleteForCleanup_VersionedDataElement_RemovesAttachedBlobVersionRows()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(DataElement1);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        (DataElement dataElement, string blobVersionId) = await CreateVersionedDataElement(element);

        // Act
        int versionCountBeforeDelete = await CountBlobVersionRows(blobVersionId);
        bool deleted = await _dataElementFixture.DataRepo.DeleteForCleanup(dataElement);
        int versionCountAfterDelete = await CountBlobVersionRows(blobVersionId);

        // Assert
        Assert.True(deleted);
        Assert.Equal(1, versionCountBeforeDelete);
        Assert.Equal(0, versionCountAfterDelete);
    }

    [Fact]
    public async Task DataElement_Delete_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
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
        (_, string firstBlobVersionId) = await CreateVersionedDataElement(
            TestDataUtil.GetDataElement(DataElement1)
        );
        (_, string secondBlobVersionId) = await CreateVersionedDataElement(
            TestDataUtil.GetDataElement(DataElement2)
        );
        int firstVersionCountBeforeDelete = await CountBlobVersionRows(firstBlobVersionId);
        int secondVersionCountBeforeDelete = await CountBlobVersionRows(secondBlobVersionId);

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
        Assert.Equal(1, firstVersionCountBeforeDelete);
        Assert.Equal(1, secondVersionCountBeforeDelete);
        Assert.Equal(0, await CountBlobVersionRows(firstBlobVersionId));
        Assert.Equal(0, await CountBlobVersionRows(secondBlobVersionId));
    }

    /// <summary>
    /// Test update, fail if too many properties
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Too_Many_Properties_Throws_Exception()
    {
        // Arrange
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
        );
        const int numberOfAllowedProperties = 16;

        Dictionary<string, object> tooManyPropertiesDictionary = Enumerable
            .Range(1, numberOfAllowedProperties + 1) // Add one extra property to make it fail.
            .ToDictionary(i => $"Key{i}", i => (object)$"Value{i}");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await _dataElementFixture.DataRepo.Update(
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
        DataElement dataElement = await CreateLegacyDataElement(
            TestDataUtil.GetDataElement(DataElement1)
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

    private async Task<DataElement> CreateLegacyDataElement(DataElement dataElement)
    {
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(dataElement, null),
            _instanceInternalId
        );

        return createdDataElement.DataElement;
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
        DataElementInternal createdDataElement = await _dataElementFixture.DataRepo.Create(
            new DataElementInternal(dataElement, blobVersionId),
            _instanceInternalId
        );

        return (createdDataElement.DataElement, blobVersionId);
    }

    private static Task<int> CountBlobVersionRows(string blobVersionId)
    {
        return PostgresUtil.RunCountQuery(
            $"select count(*) from storage.dataelementblobversions where id = '{BlobVersionId.Decode(blobVersionId)}'"
        );
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

    private static Task SetInstanceHardDeleted(Guid instanceGuid)
    {
        return PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Status,IsHardDeleted}}', 'true'::jsonb) where alternateid = '{instanceGuid}'"
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
