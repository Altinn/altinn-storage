#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
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
    private Instance _instance;
    private string _instanceGuid;

    public async Task InitializeAsync()
    {
        string sql = "delete from storage.instances; delete from storage.dataelements;";

        await PostgresUtil.RunSql(sql);
        await PostgresUtil.FreezeTime(_frozenTime);

        Instance instance = TestData.Instance_1_1.Clone();
        instance.Status.IsSoftDeleted = true;
        Instance newInstance = await dataElementFixture.InstanceRepo.Create(
            instance,
            CancellationToken.None
        );
        (_instance, _instanceInternalId) = await dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(newInstance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
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
        DataElement dataElement = TestDataUtil.GetDataElement(_dataElement1);
        dataElement.LastChanged = lastChanged;

        // Act
        dataElement = await CreateDataElement(dataElement);
        (Instance instance, _) = await dataElementFixture.InstanceRepo.GetOne(
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
        DataElement dataElement = await CreateDataElement();

        // Assert
        Instance instance = await ReadInstance();
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
        DataElement dataElement = await CreateDataElement();

        // Act
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Empty,
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
        DataElement dataElement = await CreateDataElement(initialDataElement);

        // Act
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Empty,
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
        DataElement dataElement = await CreateDataElement();

        // Act
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Empty,
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
        DataElement dataElement = await CreateDataElement(initialDataElement);

        // Act
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Empty,
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
        DataElement dataElement = await CreateDataElement();

        // Act
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Empty,
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
        DataElement dataElement = await CreateDataElement(initialDataElement);

        // Act
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Empty,
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
        DataElement dataElement = await CreateDataElement();

        // Act
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Empty,
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
        DataElement dataElement = await CreateDataElement();
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
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
            Guid.Empty,
            Guid.Parse(dataElement.Id),
            new Dictionary<string, object> { { "/contentType", _contentType } }
        );

        // Assert
        DataElement readElement = await dataElementFixture.DataRepo.Read(
            Guid.Empty,
            Guid.Parse(dataElement.Id)
        );
        Instance instance = await ReadInstance();
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
        DataElement dataElement = await CreateDataElement(element);
        await SetInstanceReadStatus(ReadStatus.Read);

        // Act
        DataElement updatedElement = await dataElementFixture.DataRepo.Update(
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
        (Instance instance, _) = await dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(updatedElement.InstanceGuid),
            false,
            CancellationToken.None
        );

        // Assert
        DataElement readElement = await dataElementFixture.DataRepo.Read(
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
    public async Task GetOne_InstanceNotFound_ReturnsNullAndZero()
    {
        // Arrange
        Guid nonExistentInstanceGuid = Guid.NewGuid();

        // Act
        (Instance instance, long internalId) = await dataElementFixture.InstanceRepo.GetOne(
            nonExistentInstanceGuid,
            false,
            CancellationToken.None
        );

        // Assert
        Assert.Null(instance);
        Assert.Equal(0, internalId);
    }

    /// <summary>
    /// Test read
    /// </summary>
    [Fact]
    public async Task DataElement_Read_Ok()
    {
        // Arrange
        DataElement dataElement = await CreateDataElement();

        // Act
        DataElement readDataelement = await dataElementFixture.DataRepo.Read(
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
        DataElement dataElement = await CreateDataElement();
        await SetInstanceReadStatus(ReadStatus.Read);

        // Act
        bool deleted = await dataElementFixture.DataRepo.Delete(dataElement);

        // Assert
        Instance instance = await ReadInstance();
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
        DataElement dataElement = await CreateDataElement();
        await SetInstanceReadStatus(ReadStatus.Unread);

        // Act
        bool deleted = await dataElementFixture.DataRepo.Delete(dataElement);

        // Assert
        Instance instance = await ReadInstance();
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
        Instance instance = await ReadInstance(includeElements: true);
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
        DataElement dataElement = await CreateDataElement();
        const int numberOfAllowedProperties = 14;

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
        DataElement dataElement = await CreateDataElement();

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

    private async Task<Instance> ReadInstance(bool includeElements = false)
    {
        (Instance instance, _) = await dataElementFixture.InstanceRepo.GetOne(
            Guid.Parse(_instanceGuid),
            includeElements,
            CancellationToken.None
        );

        return instance;
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

    private Task<DataElement> CreateDataElement(string dataElementId = _dataElement1)
    {
        return CreateDataElement(TestDataUtil.GetDataElement(dataElementId));
    }

    private Task<DataElement> CreateDataElement(DataElement dataElement)
    {
        return dataElementFixture.DataRepo.Create(dataElement, _instanceInternalId);
    }

    private Task<int> SetInstanceReadStatus(ReadStatus readStatus)
    {
        return PostgresUtil.RunSql(
            $"update storage.instances set instance = jsonb_set(instance, '{{Status, ReadStatus}}', '{(int)readStatus}') where alternateid = '{_instanceGuid}';"
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
