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
    private string _instanceGuid;

    public async Task InitializeAsync()
    {
        string sql = "delete from storage.instances; delete from storage.dataelements;";

        await PostgresUtil.RunSql(sql);
        await PostgresUtil.FreezeTime(_frozenTime);

        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Status.IsSoftDeleted = true;
        InstanceInternal newInstance = await dataElementFixture.InstanceRepo.Create(
            instance,
            CancellationToken.None
        );
        _instance = await dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(newInstance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        _instanceInternalId = _instance.InternalId;
        _instanceGuid = _instance.Id.Split('/').Last();
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
            Guid.Parse(dataElement.InstanceGuid),
            false,
            CancellationToken.None
        );

        // Assert
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
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
        Assert.True(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object> { { "/contentType", _contentType } }
        );

        // Assert
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Empty,
            Guid.Parse(dataElement.Id)
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Parse(_instanceGuid),
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object>
            {
                { "/contentType", _contentType },
                { "/isRead", false },
                { "/lastChanged", dataElement.LastChanged },
                { "/lastChangedBy", dataElement.LastChangedBy },
            }
        );
        InstanceInternal instance = await dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(updatedElement.InstanceGuid),
            false,
            CancellationToken.None
        );

        // Assert
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Empty,
            Guid.Parse(dataElement.Id)
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
            dataElementFixture.DataRepo.Update(
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
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
            dataElementFixture.DataRepo.Update(
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
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
            dataElementFixture.DataRepo.Create(
                element.FromApiModel(blobVersionId),
                _instanceInternalId
            )
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
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
            dataElementFixture.DataRepo.Create(
                element.FromApiModel(blobVersionId),
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
        element.Id = Guid.NewGuid().ToString();
        element.InstanceGuid = _instance.Id.Split('/').Last();
        element.IsRead = false;
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "hard-deleted-instance-update-test-setup";
        DataElement dataElement = await CreateLegacyDataElement(element);
        await SetInstanceHardDeleted(Guid.Parse(dataElement.InstanceGuid));

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            dataElementFixture.DataRepo.Update(
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
        element.InstanceGuid = _instance.Id.Split('/').Last();
        element.IsRead = false;
        element.Locked = true;
        element.LastChanged = DateTime.UtcNow;
        element.LastChangedBy = "isread-locked-test-setup";
        DataElement dataElement = await CreateLegacyDataElement(element);

        // Act
        DataElement updatedElement = (
            await dataElementFixture.DataRepo.Update(
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement3);
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
            await dataElementFixture.DataRepo.Update(
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
    public async Task DataElement_UpdateFileScanStatus_MatchingBlobVersion_UpdatesStatus()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
        string blobVersionId = await CreateBlobVersionId(
            Guid.Parse(element.InstanceGuid),
            element.Id
        );
        element.BlobStoragePath = BlobRepository.GetVersionedBlobPath(
            _instance.AppId,
            element.InstanceGuid,
            blobVersionId
        );
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.UpdateFileScanStatus(
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
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
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.UpdateFileScanStatus(
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
        Assert.Null(updatedElement);
        Assert.Equal(FileScanResult.Pending, readElement.FileScanResult);
    }

    [Fact]
    public async Task DataElement_UpdateFileScanStatus_HardDeletedInstance_DoesNotUpdateStatus()
    {
        // Arrange
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
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
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
            element.FromApiModel(blobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();
        await SetInstanceHardDeleted(Guid.Parse(dataElement.InstanceGuid));

        // Act
        RepositoryException exception = await Assert.ThrowsAsync<RepositoryException>(() =>
            dataElementFixture.DataRepo.UpdateFileScanStatus(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new FileScanStatus
                {
                    FileScanResult = FileScanResult.Clean,
                    BlobVersionId = blobVersionId,
                }
            )
        );

        // Assert
        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
            Guid.Parse(dataElement.InstanceGuid),
            Guid.Parse(dataElement.Id)
        );
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCodeSuggestion);
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
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
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
            element.FromApiModel(currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.UpdateFileScanStatus(
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
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
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
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
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
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
        DataElementInternal updatedElement = await dataElementFixture.DataRepo.Update(
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
    public async Task DataElement_Update_ExpectedBlobVersionMismatch_ThrowsConflictAndDoesNotUpdate()
    {
        // Arrange
        string originalContentType = $"original-{Guid.NewGuid()}";
        string newContentType = $"updated-{Guid.NewGuid()}";
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
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
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
            element.FromApiModel(currentBlobVersionId),
            _instanceInternalId
        );
        DataElement dataElement = createdDataElement.ToApiModel();

        // Act
        RepositoryException exception =
            await Assert.ThrowsAsync<DataElementBlobVersionMismatchException>(() =>
                dataElementFixture.DataRepo.Update(
                    Guid.Parse(dataElement.InstanceGuid),
                    Guid.Parse(dataElement.Id),
                    new Dictionary<string, object> { { "/contentType", newContentType } },
                    new DataElementUpdateContext
                    {
                        ExpectedCurrentBlobVersion = expectedBlobVersionId,
                    }
                )
            );

        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
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
        DataElement element = TestDataUtil.GetDataElement(_dataElement1);
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
            dataElementFixture.DataRepo.Update(
                Guid.Parse(dataElement.InstanceGuid),
                Guid.Parse(dataElement.Id),
                new Dictionary<string, object>
                {
                    { "/blobStoragePath", missingBlobStoragePath },
                    { "/currentBlobVersion", missingBlobVersionId },
                }
            )
        );

        DataElementInternal readElement = await dataElementFixture.DataRepo.Read(
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
        DataElementInternal dataElement = await CreateDataElement();
        await SetInstanceReadStatus(ReadStatus.Read);

        // Act
        bool deleted = await dataElementFixture.DataRepo.Delete(dataElement);

        // Assert
        InstanceInternal instance = await ReadInstance();
        Assert.True(deleted);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
        Assert.Equal(ReadStatus.Unread, instance.Status.ReadStatus);
        Assert.Equal(dataElement.LastChangedBy, instance.LastChangedBy);
        Assert.Equal(_frozenTime, instance.LastChanged);
        Assert.Equal(_frozenTime, await ReadInstanceLastChangedColumn());
    }

    /// <summary>
    /// Test delete and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Delete_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        DataElementInternal dataElement = await CreateDataElement();
        await SetInstanceReadStatus(ReadStatus.Unread);

        // Act
        bool deleted = await dataElementFixture.DataRepo.Delete(dataElement);

        // Assert
        InstanceInternal instance = await ReadInstance();
        Assert.True(deleted);
        Assert.False(await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id)));
        Assert.Equal(ReadStatus.Unread, instance.Status.ReadStatus);
        Assert.Equal(dataElement.LastChangedBy, instance.LastChangedBy);
        Assert.Equal(_frozenTime, instance.LastChanged);
        Assert.Equal(_frozenTime, await ReadInstanceLastChangedColumn());
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
            await dataElementFixture.DataRepo.Update(
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
        DataElementInternal dataElement = await CreateDataElement();

        // Act
        bool result = await dataElementFixture.DataRepo.Exists(Guid.Parse(dataElement.Id));

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

    private Task<InstanceInternal> ReadInstance(bool includeElements = false)
    {
        return dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(_instanceGuid),
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
        return dataElementFixture.DataRepo.Create(dataElement.FromApiModel(), _instanceInternalId);
    }

    private Task<int> SetInstanceReadStatus(ReadStatus readStatus)
    {
        return PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Status, ReadStatus}}', '{(int)readStatus}') where alternateid = '{_instanceGuid}';"
        );
    }

    private async Task<DataElement> CreateLegacyDataElement(DataElement dataElement)
    {
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
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
        DataElementInternal createdDataElement = await dataElementFixture.DataRepo.Create(
            dataElement.FromApiModel(blobVersionId),
            _instanceInternalId
        );

        return (createdDataElement.ToApiModel(), blobVersionId);
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
        var serviceList = ServiceUtil.GetServices([
            typeof(IInstanceRepository),
            typeof(IDataRepository),
        ]);
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
        DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
    }
}
