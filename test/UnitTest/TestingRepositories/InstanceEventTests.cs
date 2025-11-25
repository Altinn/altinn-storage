using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class InstanceEventTests : IClassFixture<InstanceEventFixture>
{
    private static readonly string _instanceId = Guid.NewGuid().ToString();

    private readonly InstanceEventFixture _instanceEventFixture;

    private readonly InstanceEvent _ie1 = new() { Id = Guid.NewGuid(), InstanceId = _instanceId, EventType = "et1", Created = DateTime.Parse("1994-06-16T11:06:59.0851832Z") };
    private readonly InstanceEvent _ie2 = new() { Id = Guid.NewGuid(), InstanceId = _instanceId, EventType = "et1", Created = DateTime.Parse("2004-06-16T11:06:59.0851832Z") };
    private readonly InstanceEvent _ie3 = new() { Id = Guid.NewGuid(), InstanceId = _instanceId, EventType = "et2", Created = DateTime.Parse("2014-06-16T11:06:59.0851832Z") };

    public InstanceEventTests(InstanceEventFixture instanceEventFixture)
    {
        _instanceEventFixture = instanceEventFixture;

        string sql = "delete from storage.instanceevents";
        _ = PostgresUtil.RunSql(sql).Result;
    }

    /// <summary>
    /// Test create
    /// </summary>
    [Fact]
    public async Task InstanceEvent_Create_Ok()
    {
        // Arrange

        // Act
        InstanceEvent ie = await _instanceEventFixture.InstanceEventRepo.InsertInstanceEvent(_ie1);

        // Assert
        string sql = $"select count(*) from storage.instanceevents where alternateid = '{_ie1.Id}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(ie.Id, _ie1.Id);
    }

    /// <summary>
    /// Test GetOneEvent
    /// </summary>
    [Fact]
    public async Task DataElement_GetOneEvent_Ok()
    {
        // Arrange
        await _instanceEventFixture.InstanceEventRepo.InsertInstanceEvent(_ie1);

        // Act
        InstanceEvent ie = await _instanceEventFixture.InstanceEventRepo.GetOneEvent(null, (Guid)_ie1.Id);

        // Assert
        Assert.Equal(ie.Id, _ie1.Id);
    }

    /// <summary>
    /// Test ListInstanceEvents
    /// </summary>
    [Fact]
    public async Task DataElement_ListInstanceEvents_Ok()
    {
        // Arrange
        await _instanceEventFixture.InstanceEventRepo.InsertInstanceEvent(_ie1);
        await _instanceEventFixture.InstanceEventRepo.InsertInstanceEvent(_ie2);
        await _instanceEventFixture.InstanceEventRepo.InsertInstanceEvent(_ie3);

        // Act
        List<InstanceEvent> ies1 = await _instanceEventFixture.InstanceEventRepo.ListInstanceEvents(_instanceId, null, null, null);
        List<InstanceEvent> ies2 = await _instanceEventFixture.InstanceEventRepo.ListInstanceEvents(_instanceId, new string[] { "et1" }, null, null);
        List<InstanceEvent> ies3 = await _instanceEventFixture.InstanceEventRepo.ListInstanceEvents(_instanceId, null, DateTime.Parse("2013-06-16", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), DateTime.UtcNow);

        // Assert
        Assert.Equal(3, ies1.Count);
        Assert.Equal(2, ies2.Count);
        Assert.Single(ies3);
        Assert.True(ies2.Count(ie => ie.EventType == "et1") == 2);
    }

    /// <summary>
    /// Test DeleteAllInstanceEvents
    /// </summary>
    [Fact]
    public async Task DataElement_DeleteAllInstanceEvents_Ok()
    {
        // Arrange
        await _instanceEventFixture.InstanceEventRepo.InsertInstanceEvent(_ie1);
        await _instanceEventFixture.InstanceEventRepo.InsertInstanceEvent(_ie2);
        await _instanceEventFixture.InstanceEventRepo.InsertInstanceEvent(_ie3);

        // Act
        int count = await _instanceEventFixture.InstanceEventRepo.DeleteAllInstanceEvents(_instanceId);

        // Assert
        Assert.Equal(3, count);
        string sql = $"select count(*) from storage.instanceevents where alternateid = '{_ie1.Id}' or alternateid = '{_ie2.Id}' or alternateid = '{_ie3.Id}';";
        count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, count);
    }
}

public class InstanceEventFixture
{
    public IInstanceEventRepository InstanceEventRepo { get; set; }

    public InstanceEventFixture()
    {
        var serviceList = ServiceUtil.GetServices(new List<Type>() { typeof(IInstanceEventRepository) });
        InstanceEventRepo = (IInstanceEventRepository)serviceList.First(i => i.GetType() == typeof(PgInstanceEventRepository));
    }
}
