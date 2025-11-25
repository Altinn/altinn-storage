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

        string sql = "delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
        Instance instance = TestData.Instance_1_1.Clone();
        instance.Status.IsSoftDeleted = true;
        Instance newInstance = _dataElementFixture.InstanceRepo.Create(instance, CancellationToken.None).Result;
        (_instance, _instanceInternalId) = _dataElementFixture.InstanceRepo.GetOne(Guid.Parse(newInstance.Id.Split('/').Last()), false, CancellationToken.None).Result;
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
        dataElement = await _dataElementFixture.DataRepo.Create(dataElement, _instanceInternalId);
        (Instance instance, _) = await _dataElementFixture.InstanceRepo.GetOne(Guid.Parse(dataElement.InstanceGuid), false, CancellationToken.None);

        // Assert
        string sql = $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql = $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '2'"
              + $" and lastchanged = '{((DateTime)dataElement.LastChanged).ToString("o")}' and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
        int instanceCount = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, dataCount);
        Assert.Equal(1, instanceCount);
        Assert.Equal(instance.LastChanged, dataElement.LastChanged);
        Assert.True(Math.Abs(((DateTime)dataElement.LastChanged).Ticks - lastChanged.Ticks) < TimeSpan.TicksPerMicrosecond);
    }

    /// <summary>
    /// Test create and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Create_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        await PostgresUtil.RunSql("update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '0') where alternateid = '" + _instance.Id.Split('/').Last() + "';");

        // Act
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

        // Assert
        string sql = $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql = $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
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
        List<KeyValueEntry> metadata = new() { { new() { Key = "key1", Value = "value1" } }, { new() { Key = "key2", Value = "value2" } } };
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/metadata", metadata } });

        // Assert
        Assert.Equal(JsonSerializer.Serialize(metadata), JsonSerializer.Serialize(updatedElement.Metadata));
    }

    /// <summary>
    /// Test update, replace metadata
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Metadata_Replace_Ok()
    {
        // Arrange
        List<KeyValueEntry> orgMetadata = new() { { new() { Key = "key1", Value = "value1" } }, { new() { Key = "key2", Value = "value2" } } };
        List<KeyValueEntry> replacedMetadata = new() { { new() { Key = "key3", Value = "value3" } }, { new() { Key = "key4", Value = "value4" } } };
        DataElement initialDataElement = TestDataUtil.GetDataElement(DataElement1);
        initialDataElement.Metadata = orgMetadata;
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(initialDataElement, _instanceInternalId);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/metadata", replacedMetadata } });

        // Assert
        Assert.Equal(JsonSerializer.Serialize(replacedMetadata), JsonSerializer.Serialize(updatedElement.Metadata));
    }

    /// <summary>
    /// Test update, insert metadata
    /// </summary>
    [Fact]
    public async Task DataElement_Update_UserDefinedMetadata_Insert_Ok()
    {
        // Arrange
        List<KeyValueEntry> userDefinedMetadata = new() { { new() { Key = "key1", Value = "value1" } }, { new() { Key = "key2", Value = "value2" } } };
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/userDefinedMetadata", userDefinedMetadata } });

        // Assert
        Assert.Equal(JsonSerializer.Serialize(userDefinedMetadata), JsonSerializer.Serialize(updatedElement.UserDefinedMetadata));
    }

    /// <summary>
    /// Test update, replace metadata
    /// </summary>
    [Fact]
    public async Task DataElement_Update_UserDefinedMetadata_Replace_Ok()
    {
        // Arrange
        List<KeyValueEntry> originalUserDefinedMetadata = new() { { new() { Key = "key1", Value = "value1" } }, { new() { Key = "key2", Value = "value2" } } };
        List<KeyValueEntry> replacedUserDefinedMetadata = new() { { new() { Key = "key3", Value = "value3" } }, { new() { Key = "key4", Value = "value4" } } };
        DataElement initialDataElement = TestDataUtil.GetDataElement(DataElement1);
        initialDataElement.UserDefinedMetadata = originalUserDefinedMetadata;
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(initialDataElement, _instanceInternalId);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/userDefinedMetadata", replacedUserDefinedMetadata } });

        // Assert
        Assert.Equal(JsonSerializer.Serialize(replacedUserDefinedMetadata), JsonSerializer.Serialize(updatedElement.UserDefinedMetadata));
    }

    /// <summary>
    /// Test update, insert tags
    /// </summary>
    [Fact]
    public async Task DataElement_Update_Tags_Insert_Ok()
    {
        // Arrange
        List<string> tags = new() { "s1", "s2" };
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/tags", tags } });

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
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(initialDataElement, _instanceInternalId);

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/tags", replacedTags } });

        // Assert
        Assert.Equal(JsonSerializer.Serialize(replacedTags), JsonSerializer.Serialize(updatedElement.Tags));
    }

    /// <summary>
    /// Test update and don't change instance read status
    /// </summary>
    [Fact]
    public async Task DataElement_Update_NoChange_Instance_Readstatus_Ok()
    {
        // Arrange
        string contentType = "unittestContentType";
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
        string restoreValues = """{"Status": {"ReadStatus": 0},"LastChanged": "<lastChanged>","LastChangedBy": "<lastChangedBy>"}"""
            .Replace("<lastChanged>", ((DateTime)_instance.LastChanged).ToString("o")).Replace("<lastChangedBy>", _instance.LastChangedBy);
        await PostgresUtil.RunSql($"update storage.instances set instance = instance || '{restoreValues}', lastChanged = '{((DateTime)_instance.LastChanged).ToString("o")}' where alternateid = '{_instance.Id.Split('/').Last()}';");

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/contentType", contentType } });

        // Assert
        string sql = $"select count(*) from storage.dataelements where element ->> 'ContentType' = '{contentType}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql = $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
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
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(element, _instanceInternalId);
        await PostgresUtil.RunSql("update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '1') where alternateid = '" + _instance.Id.Split('/').Last() + "';");

        // Act
        DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Parse(_instance.Id.Split('/').Last()), Guid.Parse(dataElement.Id), new Dictionary<string, object>()
        {
            { "/contentType", contentType },
            { "/isRead", false },
            { "/lastChanged", dataElement.LastChanged },
            { "/lastChangedBy", dataElement.LastChangedBy }
        });
        (Instance instance, _) = await _dataElementFixture.InstanceRepo.GetOne(Guid.Parse(updatedElement.InstanceGuid), false, CancellationToken.None);

        // Assert
        string sql = $"select count(*) from storage.dataelements where element ->> 'ContentType' = '{contentType}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql = $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
              + $" and lastchanged = '{((DateTime)dataElement.LastChanged).ToString("o")}' and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
        int instanceCount = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, dataCount);
        Assert.Equal(1, instanceCount);
        Assert.Equal(contentType, updatedElement.ContentType);
        Assert.Equal(instance.LastChanged, updatedElement.LastChanged);
        Assert.True(Math.Abs(((DateTime)updatedElement.LastChanged).Ticks - lastChanged.Ticks) < TimeSpan.TicksPerMicrosecond);
    }

    [Fact]
    public async Task GetOne_InstanceNotFound_ReturnsNullAndZero()
    {
        // Arrange
        Guid nonExistentInstanceGuid = Guid.NewGuid();

        // Act
        (Instance instance, long internalId) = await _dataElementFixture.InstanceRepo.GetOne(nonExistentInstanceGuid, false, CancellationToken.None);

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
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

        // Act
        DataElement readDataelement = await _dataElementFixture.DataRepo.Read(Guid.Empty, Guid.Parse(dataElement.Id));

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
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
        await PostgresUtil.RunSql("update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '1') where alternateid = '" + _instance.Id.Split('/').Last() + "';");

        // Act
        bool deleted = await _dataElementFixture.DataRepo.Delete(dataElement);

        // Assert
        string sql = $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql = $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
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
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
        await PostgresUtil.RunSql("update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '0') where alternateid = '" + _instance.Id.Split('/').Last() + "';");

        // Act
        bool deleted = await _dataElementFixture.DataRepo.Delete(dataElement);

        // Assert
        string sql = $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
        int dataCount = await PostgresUtil.RunCountQuery(sql);
        sql = $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
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
        await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
        await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement2), _instanceInternalId);

        // Act
        bool deleted = await _dataElementFixture.DataRepo.DeleteForInstance(_instance.Id.Split('/').Last());

        // Assert
        string sql = $"select count(*) from storage.dataelements where instanceguid = '{_instance.Id.Split('/').Last()}'";
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
        DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
        const int numberOfAllowedProperties = 14;

        Dictionary<string, object> tooManyPropertiesDictionary = Enumerable.Range(1, numberOfAllowedProperties + 1) // Add one extra property to make it fail.
            .ToDictionary(i => $"Key{i}", i => (object)$"Value{i}");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), tooManyPropertiesDictionary);
        });
    }
}

public class DataElementFixture
{
    public IInstanceRepository InstanceRepo { get; set; }

    public IDataRepository DataRepo { get; set; }

    public DataElementFixture()
    {
        var serviceList = ServiceUtil.GetServices(new List<Type>() { typeof(IInstanceRepository), typeof(IDataRepository) });
        InstanceRepo = (IInstanceRepository)serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
        DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
    }
}
