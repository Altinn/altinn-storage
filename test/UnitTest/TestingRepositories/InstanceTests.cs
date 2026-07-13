#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class InstanceTests : IClassFixture<InstanceFixture>
{
    private readonly InstanceFixture _instanceFixture;

    public InstanceTests(InstanceFixture instanceFixture)
    {
        _instanceFixture = instanceFixture;

        string sql = "delete from storage.instances; delete from storage.dataelements;";
        _ = PostgresUtil.RunSql(sql).Result;
    }

    /// <summary>
    /// Test create
    /// </summary>
    [Fact]
    public async Task Instance_Create_Ok()
    {
        // Arrange

        // Act
        InstanceInternal newInstance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select confirmed from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        bool? confirmed = await PostgresUtil.RunQuery<bool?>(sql);
        Assert.Equal(1, count);
        Assert.Equal(TestData.Instance_1_1.Id.Split('/').Last(), newInstance.Id);
        Assert.Equal(false, confirmed);
    }

    [Fact]
    public async Task Instance_Create_GeneratesIdAndHydratesInternalStateOnlyOnRead()
    {
        InstanceInternal input = TestData.Instance_1_1.Clone().FromApiModel();
        input.Id = null;

        InstanceInternal created = await _instanceFixture.InstanceRepo.Create(
            input,
            CancellationToken.None
        );

        Assert.True(Guid.TryParse(created.Id, out Guid generatedId));
        Assert.Equal(0, created.InternalId);

        InstanceInternal read = await _instanceFixture.InstanceRepo.GetOne(
            generatedId,
            false,
            CancellationToken.None
        );
        Assert.True(read.InternalId > 0);
    }

    /// <summary>
    /// Test update task
    /// </summary>
    [Fact]
    public async Task Instance_Update_Task_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Process.CurrentTask.Name = "Before update";
        newInstance.Process.StartEvent = "s1";
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.Process.CurrentTask.ElementId = "Task_2";
        newInstance.Process.CurrentTask.Name = "After update";
        newInstance.Process.StartEvent = null;
        newInstance.Process.EndEvent = "e1";
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.Process));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and taskid = 'Task_2'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal("Task_2", updatedInstance.Process.CurrentTask.ElementId);
        Assert.Equal(
            newInstance.Process.CurrentTask.Name,
            updatedInstance.Process.CurrentTask.Name
        );
        Assert.Equal("After update", updatedInstance.Process.CurrentTask.Name);
        Assert.Equal("e1", newInstance.Process.EndEvent);
        Assert.Null(newInstance.Process.StartEvent);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
    }

    [Fact]
    public async Task Instance_Update_PreservesCallerDomainDataList()
    {
        Instance instance = await CreateApiInstance(
            TestData.Instance_1_1.Clone(),
            CancellationToken.None
        );
        DataElement apiDataElement = new()
        {
            Id = Guid.NewGuid().ToString(),
            InstanceGuid = instance.Id.Split('/').Last(),
        };
        instance.Data = [apiDataElement];
        instance.LastChanged = DateTime.UtcNow;
        InstanceInternal input = InstanceInternalTestFactory.Create(
            instance,
            [apiDataElement.FromApiModel()],
            InternalId: 0
        );

        InstanceInternal result = await _instanceFixture.InstanceRepo.Update(
            input,
            [nameof(instance.LastChanged), nameof(instance.Process)],
            CancellationToken.None
        );

        Assert.Same(input.Data, result.Data);
        Assert.Same(input.Data[0], Assert.Single(result.Data));
    }

    /// <summary>
    /// Test update task with events
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Instance_Update_Task_With_Events_Ok(int eventCount)
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Process.CurrentTask.Name = "Before update";
        newInstance.Process.StartEvent = "s1";
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.Process.CurrentTask.ElementId = "Task_2";
        newInstance.Process.CurrentTask.Name = "After update";
        newInstance.Process.StartEvent = null;
        newInstance.Process.EndEvent = "e1";
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.Process));

        List<InstanceEvent> instanceEvents = [];
        for (int i = 0; i < eventCount; i++)
        {
            InstanceEvent instanceEvent = new()
            {
                Id = Guid.NewGuid(),
                InstanceId = newInstance.Id,
                EventType = "Created",
                Created = DateTime.Parse("1994-06-16T11:06:59.0851832Z"),
            };
            instanceEvents.Add(instanceEvent);
        }

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceAndEventsRepo.Update(
            newInstance,
            updateProperties,
            instanceEvents,
            CancellationToken.None
        );

        // Assert
        if (instanceEvents.Count > 0)
        {
            string ids = string.Join(", ", instanceEvents.Select(e => $"'{e.Id}'"));
            string sql =
                $"select count(*) from storage.instanceevents where alternateid in ({ids}) AND instance = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(instanceEvents.Count, count);
        }

        Assert.Equal("Task_2", updatedInstance.Process.CurrentTask.ElementId);
    }

    /// <summary>
    /// Test update status
    /// </summary>
    [Fact]
    public async Task Instance_Update_Status_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Status.IsArchived = true;
        newInstance.Status.Substatus = new() { Description = "desc " };
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.Status.IsSoftDeleted = true;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties =
        [
            nameof(newInstance.Status),
            nameof(newInstance.Status.IsSoftDeleted),
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
        ];

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(newInstance.Status.IsArchived, updatedInstance.Status.IsArchived);
        Assert.Equal(newInstance.Status.IsSoftDeleted, updatedInstance.Status.IsSoftDeleted);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.Equal(
            newInstance.Status.Substatus.Description,
            updatedInstance.Status.Substatus.Description
        );
    }

    /// <summary>
    /// Test update substatus
    /// </summary>
    [Fact]
    public async Task Instance_Update_Substatus_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Status.IsArchived = true;
        newInstance.Status.Substatus = new() { Description = "substatustest-desc" };
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.Status.Substatus = new() { Label = "substatustest-label" };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";
        newInstance.Status.IsArchived = false;

        List<string> updateProperties =
        [
            nameof(newInstance.Status.Substatus),
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
        ];

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal("substatustest-label", updatedInstance.Status.Substatus.Label);
        Assert.Null(updatedInstance.Status.Substatus.Description);
        Assert.True(updatedInstance.Status.IsArchived);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
    }

    /// <summary>
    /// Test update presentationtexts
    /// </summary>
    [Fact]
    public async Task Instance_Update_PresentationTexts_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.PresentationTexts = new() { { "k1", "v1" }, { "k2", "v2" } };
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.PresentationTexts = new() { { "k2", null }, { "k3", "v3" } };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.PresentationTexts));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(2, updatedInstance.PresentationTexts.Count);
        Assert.True(updatedInstance.PresentationTexts.ContainsKey("k1"));
        Assert.True(updatedInstance.PresentationTexts.ContainsKey("k3"));
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
    }

    /// <summary>
    /// Test update process
    /// </summary>
    [Fact]
    public async Task Instance_Update_Process_And_Status_Ok()
    {
        // Arrange
        DateTime unchangedSofteDeleted = DateTime.UtcNow.AddYears(-2);
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Status.SoftDeleted = unchangedSofteDeleted;
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.Process = new()
        {
            CurrentTask = new() { AltinnTaskType = "Task_3" },
            Ended = DateTime.Parse("2023-12-24"),
        };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";
        newInstance.Status.HardDeleted = DateTime.UtcNow;
        newInstance.Status.SoftDeleted = unchangedSofteDeleted.AddYears(1);

        List<string> updateProperties =
        [
            nameof(newInstance.Process),
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
            nameof(newInstance.Status),
            nameof(newInstance.Status.HardDeleted),
        ];

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(
            newInstance.Process.CurrentTask.AltinnTaskType,
            updatedInstance.Process.CurrentTask.AltinnTaskType
        );
        Assert.Equal(newInstance.Process.Ended, updatedInstance.Process.Ended);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.Equal(newInstance.Status.HardDeleted, updatedInstance.Status.HardDeleted);
        Assert.Equal(unchangedSofteDeleted, updatedInstance.Status.SoftDeleted);
    }

    /// <summary>
    /// Test update process without updating status
    /// </summary>
    [Fact]
    public async Task Instance_Update_Process_And_No_Status_Ok()
    {
        // Arrange
        DateTime unchangedSofteDeleted = DateTime.UtcNow.AddYears(-2);
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.Status.SoftDeleted = unchangedSofteDeleted;
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.Process = new()
        {
            CurrentTask = new() { AltinnTaskType = "Task_3" },
            Ended = DateTime.Parse("2023-12-24"),
        };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";
        newInstance.Status.SoftDeleted = unchangedSofteDeleted.AddYears(1);

        List<string> updateProperties =
        [
            nameof(newInstance.Process),
            nameof(newInstance.LastChanged),
            nameof(newInstance.LastChangedBy),
        ];

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(
            newInstance.Process.CurrentTask.AltinnTaskType,
            updatedInstance.Process.CurrentTask.AltinnTaskType
        );
        Assert.Equal(newInstance.Process.Ended, updatedInstance.Process.Ended);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.Equal(unchangedSofteDeleted, updatedInstance.Status.SoftDeleted);
    }

    /// <summary>
    /// Test update data values
    /// </summary>
    [Fact]
    public async Task Instance_Update_DataValues_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.DataValues = new() { { "k1", "v1" }, { "k2", "v2" } };
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.DataValues = new() { { "k2", null }, { "k3", "v3" } };
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.DataValues));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(2, updatedInstance.DataValues.Count);
        Assert.True(updatedInstance.DataValues.ContainsKey("k1"));
        Assert.True(updatedInstance.DataValues.ContainsKey("k3"));
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
    }

    /// <summary>
    /// Test update CompleteConfirmations
    /// </summary>
    [Fact]
    public async Task Instance_Update_CompleteConfirmations_PrimaryOrg_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations =
        [
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.UtcNow.AddYears(-1),
                StakeholderId = "TTD",
            },
        ];
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.CompleteConfirmations =
        [
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.UtcNow.AddYears(-2),
                StakeholderId = "s2",
            },
        ];
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.CompleteConfirmations));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select confirmed from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        bool? confirmed = await PostgresUtil.RunQuery<bool?>(sql);
        Assert.Equal(1, count);
        Assert.Equal(2, updatedInstance.CompleteConfirmations.Count);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.True(confirmed);
    }

    /// <summary>
    /// Test update CompleteConfirmations
    /// </summary>
    [Fact]
    public async Task Instance_Update_CompleteConfirmations_OtherOrg_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations =
        [
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.UtcNow.AddYears(-1),
                StakeholderId = "s1",
            },
        ];
        newInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );
        newInstance.CompleteConfirmations =
        [
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.UtcNow.AddYears(-2),
                StakeholderId = "s2",
            },
        ];
        newInstance.LastChanged = DateTime.UtcNow;
        newInstance.LastChangedBy = "unittest";

        List<string> updateProperties = [];
        updateProperties.Add(nameof(newInstance.LastChanged));
        updateProperties.Add(nameof(newInstance.LastChangedBy));
        updateProperties.Add(nameof(newInstance.CompleteConfirmations));

        // Act
        InstanceInternal updatedInstance = await _instanceFixture.InstanceRepo.Update(
            newInstance,
            updateProperties,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'"
            + $" and instance ->> 'LastChangedBy' = 'unittest'";
        int count = await PostgresUtil.RunCountQuery(sql);
        sql =
            $"select confirmed from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        bool confirmed = await PostgresUtil.RunQuery<bool>(sql);
        Assert.Equal(1, count);
        Assert.Equal(2, updatedInstance.CompleteConfirmations.Count);
        Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
        Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        Assert.False(confirmed);
    }

    /// <summary>
    /// Test delete
    /// </summary>
    [Fact]
    public async Task Instance_Delete_Ok()
    {
        // Arrange
        InstanceInternal newInstance = await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );

        // Act
        bool deleted = await _instanceFixture.InstanceRepo.Delete(
            Guid.Parse(newInstance.Id),
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, count);
        Assert.True(deleted);
    }

    /// <summary>
    /// Test GetOne
    /// </summary>
    [Fact]
    public async Task Instance_GetOne_Ok()
    {
        // Arrange
        DataElementInternal data = TestDataUtil
            .GetDataElement("cdb627fd-c586-41f5-99db-bae38daa2b59")
            .FromApiModel();
        InstanceInternal instance = await InsertInstanceAndData(
            TestData.Instance_1_1.Clone().FromApiModel(),
            data
        );

        // Act
        InstanceInternal instanceNoData = await _instanceFixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        InstanceInternal instanceWithData = await _instanceFixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            true,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(instanceNoData.Id, instance.Id.Split('/').Last());
        Assert.Equal(instanceWithData.Id, instance.Id.Split('/').Last());
        Assert.Empty(instanceNoData.Data);
        Assert.Single(instanceWithData.Data);
    }

    /// <summary>
    /// Test GetHardDeletedInstances
    /// </summary>
    [Fact]
    public async Task Instance_GetHardDeletedInstances_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            HardDelete(TestData.Instance_1_1.Clone().FromApiModel()),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            HardDelete(TestData.Instance_2_1.Clone().FromApiModel()),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_3_1.Clone().FromApiModel(),
            CancellationToken.None
        );

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetHardDeletedInstances(
            CancellationToken.None
        );

        // Assert
        Assert.Equal(2, instances.Count);
    }

    /// <summary>
    /// Test GetHardDeletedDataElements
    /// </summary>
    [Fact]
    public async Task Instance_GetHardDeletedDataElements_Ok()
    {
        // Arrange
        DataElementInternal data1 = TestDataUtil
            .GetDataElement("11f7c994-6681-47a1-9626-fcf6c27308a5")
            .FromApiModel();
        DataElementInternal data2 = TestDataUtil
            .GetDataElement("1336b773-4ae2-4bdf-9529-d71dfc1c8b43")
            .FromApiModel();
        DataElementInternal data3 = TestDataUtil
            .GetDataElement("24bfec2e-c4ce-4e82-8fa9-aa39da329fd5")
            .FromApiModel();
        await InsertInstanceAndDataHardDelete(TestData.Instance_1_1.Clone().FromApiModel(), data1);
        await InsertInstanceAndDataHardDelete(TestData.Instance_2_1.Clone().FromApiModel(), data2);
        await InsertInstanceAndDataHardDelete(TestData.Instance_3_1.Clone().FromApiModel(), data3);

        // Act
        var dataElements3 = await _instanceFixture.InstanceRepo.GetHardDeletedDataElements(
            CancellationToken.None
        );
        await _instanceFixture.DataRepo.Update(
            Guid.Empty,
            Guid.Parse(data1.Id),
            new Dictionary<string, object>() { { "/deleteStatus", new DeleteStatus() } }
        );
        var dataElements2 = await _instanceFixture.InstanceRepo.GetHardDeletedDataElements(
            CancellationToken.None
        );

        // Assert
        Assert.Equal(3, dataElements3.Count);
        Assert.Equal(2, dataElements2.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_FullyHydratesDomainState()
    {
        InstanceInternal input = TestData.Instance_1_1.Clone().FromApiModel();
        input.Id = input.Id.ToUpperInvariant();
        string expectedStorageId = input.Id;
        await _instanceFixture.InstanceRepo.Create(input, CancellationToken.None);

        InstanceInternal persisted = await _instanceFixture.InstanceRepo.GetOne(
            Guid.Parse(expectedStorageId),
            false,
            CancellationToken.None
        );
        DataElement firstInsertedElement = TestDataUtil.GetDataElement(
            "24bfec2e-c4ce-4e82-8fa9-aa39da329fd5"
        );
        firstInsertedElement.InstanceGuid = expectedStorageId;
        await _instanceFixture.DataRepo.Create(
            firstInsertedElement.FromApiModel(),
            persisted.InternalId,
            CancellationToken.None
        );

        DataElement secondInsertedElement = TestDataUtil.GetDataElement(
            "1336b773-4ae2-4bdf-9529-d71dfc1c8b43"
        );
        secondInsertedElement.InstanceGuid = expectedStorageId;
        await _instanceFixture.DataRepo.Create(
            secondInsertedElement.FromApiModel(),
            persisted.InternalId,
            CancellationToken.None
        );

        InstanceQueryResult result = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters
            {
                Size = 100,
                AppId = input.AppId,
                IncludeDataElements = true,
            },
            CancellationToken.None
        );

        Assert.Null(result.Exception);
        InstanceInternal instance = Assert.Single(result.Instances);
        Assert.Equal(expectedStorageId, instance.Id);
        Assert.DoesNotContain('/', instance.Id);
        Assert.Equal(persisted.InternalId, instance.InternalId);
        Assert.NotEqual(0, instance.InternalId);
        Assert.Collection(
            instance.Data,
            element =>
            {
                Assert.Equal(firstInsertedElement.Id, element.Id);
            },
            element =>
            {
                Assert.Equal(secondInsertedElement.Id, element.Id);
            }
        );
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_PreservesOrderingFilteringAndContinuation()
    {
        Instance first = TestData.Instance_1_1.Clone();
        Instance second = TestData.Instance_1_2.Clone();
        Instance third = TestData.Instance_1_3.Clone();
        first.LastChanged = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        second.LastChanged = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        third.LastChanged = new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        await CreateApiInstance(first, CancellationToken.None);
        await CreateApiInstance(second, CancellationToken.None);
        await CreateApiInstance(third, CancellationToken.None);

        InstanceQueryParameters query = new()
        {
            Size = 1,
            SortBy = "asc:lastChanged",
            IncludeDataElements = false,
        };
        InstanceQueryResult firstPage = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            query,
            CancellationToken.None
        );
        query.ContinuationToken = firstPage.ContinuationToken;
        InstanceQueryResult secondPage = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            query,
            CancellationToken.None
        );

        Assert.Equal(first.Id.Split('/').Last(), Assert.Single(firstPage.Instances).Id);
        Assert.Equal(second.Id.Split('/').Last(), Assert.Single(secondPage.Instances).Id);
        Assert.NotNull(firstPage.ContinuationToken);
        Assert.NotNull(secondPage.ContinuationToken);
        Assert.NotEqual(firstPage.ContinuationToken, secondPage.ContinuationToken);
        Assert.Empty(firstPage.Instances[0].Data);

        InstanceQueryResult filtered = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters
            {
                Size = 100,
                InstanceOwnerPartyId = Convert.ToInt32(third.InstanceOwner.PartyId),
                IncludeDataElements = false,
            },
            CancellationToken.None
        );
        Assert.Equal(third.Id.Split('/').Last(), Assert.Single(filtered.Instances).Id);
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_PreservesDeleteStatusForConsumerFiltering()
    {
        Instance instance = TestData.Instance_1_1.Clone();
        await CreateApiInstance(instance, CancellationToken.None);
        InstanceInternal persisted = await _instanceFixture.InstanceRepo.GetOne(
            Guid.Parse(instance.Id.Split('/').Last()),
            false,
            CancellationToken.None
        );
        DataElement visibleElement = TestDataUtil.GetDataElement(
            "24bfec2e-c4ce-4e82-8fa9-aa39da329fd5"
        );
        visibleElement.InstanceGuid = persisted.Id;
        DataElement deletedElement = TestDataUtil.GetDataElement(
            "1336b773-4ae2-4bdf-9529-d71dfc1c8b43"
        );
        deletedElement.InstanceGuid = persisted.Id;
        deletedElement.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        await _instanceFixture.DataRepo.Create(
            visibleElement.FromApiModel(),
            persisted.InternalId,
            CancellationToken.None
        );
        await _instanceFixture.DataRepo.Create(
            deletedElement.FromApiModel(),
            persisted.InternalId,
            CancellationToken.None
        );

        InstanceQueryResult result = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters { Size = 100, IncludeDataElements = true },
            CancellationToken.None
        );

        List<DataElementInternal> elements = Assert.Single(result.Instances).Data;
        Assert.Equal(2, elements.Count);
        Assert.True(
            Assert
                .Single(elements, element => element.Id == deletedElement.Id)
                .DeleteStatus.IsHardDeleted
        );
        Assert.Equal(
            visibleElement.Id,
            Assert.Single(elements, element => element.DeleteStatus?.IsHardDeleted != true).Id
        );
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_ReturnsErrorResultOnCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        InstanceQueryResult result = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters { Size = 100, IncludeDataElements = true },
            cancellation.Token
        );

        Assert.Empty(result.Instances);
        Assert.Equal(0, result.Count);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_ReturnsErrorResultOnInvalidQuery()
    {
        InstanceQueryResult result = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            new InstanceQueryParameters
            {
                Size = 100,
                ProcessEnded = ["true"],
                IncludeDataElements = true,
            },
            CancellationToken.None
        );

        Assert.Empty(result.Instances);
        Assert.Equal(0, result.Count);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public void InstanceQueryResult_IsNotAnApiWireModel()
    {
        string[] propertyNames = typeof(InstanceQueryResult)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["ContinuationToken", "Count", "Exception", "Instances"], propertyNames);
        Assert.All(
            typeof(InstanceQueryResult).GetProperties(),
            property =>
                Assert.Empty(
                    property.GetCustomAttributes(
                        typeof(Newtonsoft.Json.JsonPropertyAttribute),
                        inherit: true
                    )
                )
        );
    }

    [Fact]
    public async Task Instance_GetInstancesFromQuery_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new() { Size = 100 };

        // Act
        var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        queryParams.InstanceOwnerPartyId = Convert.ToInt32(
            TestData.Instance_1_3.InstanceOwner.PartyId
        );
        var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(3, instances3.Count);
        Assert.Equal(1, instances1.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with continuation token
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_Continuation_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );
        InstanceQueryParameters queryParams = new() { Size = 1, SortBy = "asc:" };

        // Act
        var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );
        string contToken1 = instances1.ContinuationToken;
        queryParams.ContinuationToken = contToken1;

        var instances2 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );
        string contToken2 = instances2.ContinuationToken;
        queryParams.ContinuationToken = contToken2;

        queryParams.Size = 2;
        var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );
        string contToken3 = instances3.ContinuationToken;

        // Assert
        Assert.Equal(1, instances1.Count);
        Assert.Equal(1, instances2.Count);
        Assert.Equal(1, instances3.Count);
        Assert.Null(contToken3);
        Assert.True(string.CompareOrdinal(contToken1, contToken2) < 0);
        Assert.Equal(
            instances1.Instances.FirstOrDefault().Id,
            TestData.Instance_1_1.Id.Split('/').Last()
        );
        Assert.Equal(
            instances2.Instances.FirstOrDefault().Id,
            TestData.Instance_1_2.Id.Split('/').Last()
        );
        Assert.Equal(
            instances3.Instances.FirstOrDefault().Id,
            TestData.Instance_1_3.Id.Split('/').Last()
        );
    }

    /// <summary>
    /// Test GetInstancesFromQuery with appId
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_AppId_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            AppId = "ttd/test-applikasjon-1",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(1, instances.Count);
    }

    /// <summary>
    /// Test use of confirmed
    /// </summary>
    [Fact]
    public void Instance_Confirmed_IsSetCorrectly()
    {
        // Arrange
        InstanceQueryParameters queryParamsWithExcludeOwner = new()
        {
            ExcludeConfirmedBy = "TTD",
            Org = "TTD",
        };

        InstanceQueryParameters queryParamsWithExcludeOther = new()
        {
            ExcludeConfirmedBy = "SKD",
            Org = "TTD",
        };

        InstanceQueryParameters queryParamsWithoutExclude = new() { Org = "TTD" };

        // Act
        var sqlParamsWithExcludeOwner = queryParamsWithExcludeOwner.GeneratePostgreSQLParameters();
        var sqlParamsWithExcludeOther = queryParamsWithExcludeOther.GeneratePostgreSQLParameters();
        var sqlParamsWithoutExclude = queryParamsWithoutExclude.GeneratePostgreSQLParameters();

        // Assert
        Assert.False((bool?)sqlParamsWithExcludeOwner["_confirmed"]);
        Assert.False(sqlParamsWithExcludeOwner.ContainsKey("_excludeConfirmedBy"));

        Assert.False(sqlParamsWithExcludeOther.ContainsKey("_confirmed"));
        Assert.True(sqlParamsWithExcludeOther.ContainsKey("_excludeConfirmedBy"));

        Assert.False(sqlParamsWithoutExclude.ContainsKey("_confirmed"));
        Assert.False(sqlParamsWithoutExclude.ContainsKey("_excludeConfirmedBy"));
    }

    /// <summary>
    /// Test GetInstancesFromQuery with PresentationFields, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_NoMatchFromPresentationFields_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            SearchString = "nomatchj",
            AppIds = [],
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(0, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with PresentationFields, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_MatchFromPresentationFields_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            SearchString = "bing",
            AppIds = [],
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(1, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery filtering on the A2ArchRef data value, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_DataValuesA2ArchRef_Match_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.DataValues = new() { { "A2ArchRef", "123456" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None, 2);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            DataValuesA2ArchRef = "123456",
            MainVersionInclude = 2,
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(1, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery filtering on the A2ArchRef data value, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_DataValuesA2ArchRef_NoMatch_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.DataValues = new() { { "A2ArchRef", "123456" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None, 2);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            DataValuesA2ArchRef = "654321",
            MainVersionInclude = 2,
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(0, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with CompleteConfirmations, primary org, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_CompleteConfirmations_PrimaryOrg_Match_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations = new()
        {
            new() { StakeholderId = "TTD", ConfirmedOn = DateTime.Now },
        };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ExcludeConfirmedBy = "TTD",
            Org = "TTD",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(0, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with CompleteConfirmations, primary org, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_CompleteConfirmations_PrimaryOrg_NoMatch_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations = new()
        {
            new() { StakeholderId = "TTD", ConfirmedOn = DateTime.Now },
        };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ExcludeConfirmedBy = "SKD",
            Org = "TTD",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(1, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with CompleteConfirmations, other org, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_CompleteConfirmations_OtherOrg_Match_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations = new()
        {
            new() { StakeholderId = "SKD", ConfirmedOn = DateTime.Now },
        };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ExcludeConfirmedBy = "SKD",
            Org = "TTD",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(0, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with CompleteConfirmations, other org, no match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_CompleteConfirmations_OtherOrg_NoMatch_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.CompleteConfirmations = new()
        {
            new() { StakeholderId = "SKD", ConfirmedOn = DateTime.Now },
        };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ExcludeConfirmedBy = "TTD",
            Org = "TTD",
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(1, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with appIds, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_MatchFromAppIds_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_1.Clone().FromApiModel();
        newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
        await _instanceFixture.InstanceRepo.Create(newInstance, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            SearchString = "nomatch",
            AppIds = ["ttd/test-applikasjon-1", "ttd/test-applikasjon-2"],
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(1, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with appIds and presentation fields, match
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_MatchFromAppIdsAndPresFields_Ok()
    {
        // Arrange
        InstanceInternal newInstance1 = TestData.Instance_1_1.Clone().FromApiModel();
        InstanceInternal newInstance2 = TestData.Instance_1_2.Clone().FromApiModel();
        newInstance1.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
        newInstance2.AppId = "ttd/test-applikasjon-3";
        await _instanceFixture.InstanceRepo.Create(newInstance1, CancellationToken.None);
        await _instanceFixture.InstanceRepo.Create(newInstance2, CancellationToken.None);

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            SearchString = "ing",
            AppIds = new List<string>()
            {
                "ttd/test-applikasjon-3",
                "ttd/test-applikasjon-2",
            }.ToArray(),
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(2, instances.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with msgBoxInterval
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_MatchFromMsgBoxInterval_Ok()
    {
        // Arrange
        await PrepareDateSearch();

        // Act
        var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2021", "2021"),
            CancellationToken.None
        );
        var instances2 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2022", "2022"),
            CancellationToken.None
        );
        var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2023", "2023"),
            CancellationToken.None
        );
        var instances4 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2024", "2024"),
            CancellationToken.None
        );
        var instances5 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2019", "2019"),
            CancellationToken.None
        );
        var instances6 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            GetDateQueryParams("2021", "2024"),
            CancellationToken.None
        );

        // Assert
        Assert.Equal(1, instances1.Count);
        Assert.Equal(1, instances2.Count);
        Assert.Equal(1, instances3.Count);
        Assert.Equal(1, instances4.Count);
        Assert.Equal(0, instances5.Count);
        Assert.Equal(4, instances6.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with bad date
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_InvalidDate()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ProcessEnded = ["true"],
            InstanceOwnerPartyId = Convert.ToInt32(TestData.Instance_1_3.InstanceOwner.PartyId),
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(0, instances.Count);
        Assert.NotNull(instances.Exception);
    }

    /// <summary>
    /// Test GetInstancesFromQuery, IncludeDataElements query parameter
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_WithIncludeDataElementsAsQueryParam_Ok()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new() { Size = 100, IncludeDataElements = true };

        // Act
        var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        queryParams.InstanceOwnerPartyId = Convert.ToInt32(
            TestData.Instance_1_3.InstanceOwner.PartyId
        );
        var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(3, instances3.Count);
        Assert.Equal(1, instances1.Count);
    }

    /// <summary>
    /// Test GetInstancesFromQuery with bad date
    /// </summary>
    [Fact]
    public async Task Instance_GetInstancesFromQuery_WithIncludeDataElementsAsQueryParam_InvalidDate()
    {
        // Arrange
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_1.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_2.Clone().FromApiModel(),
            CancellationToken.None
        );
        await _instanceFixture.InstanceRepo.Create(
            TestData.Instance_1_3.Clone().FromApiModel(),
            CancellationToken.None
        );

        InstanceQueryParameters queryParams = new()
        {
            Size = 100,
            ProcessEnded = ["true"],
            InstanceOwnerPartyId = Convert.ToInt32(TestData.Instance_1_3.InstanceOwner.PartyId),
        };

        // Act
        var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(
            queryParams,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(0, instances.Count);
        Assert.NotNull(instances.Exception);
    }

    /// <summary>
    /// Test create instance with Email-based self identification
    /// </summary>
    [Fact]
    public async Task Instance_Create_WithEmail_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_Email.Clone().FromApiModel();

        // Act
        InstanceInternal createdInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{newInstance.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);

        Assert.Equal(1, count);
        Assert.Equal(newInstance.InstanceOwner.PartyId, createdInstance.InstanceOwner.PartyId);
        Assert.NotNull(createdInstance.InstanceOwner.Username);
        Assert.Equal("epost:test.user@example.com", createdInstance.InstanceOwner.Username);
        Assert.NotNull(createdInstance.InstanceOwner.ExternalIdentifier);
        Assert.Equal(
            "urn:altinn:person:idporten-email:test.user@example.com",
            createdInstance.InstanceOwner.ExternalIdentifier
        );
    }

    /// <summary>
    /// Test create instance with Username-based self identification (legacy)
    /// </summary>
    [Fact]
    public async Task Instance_Create_WithUsername_Ok()
    {
        // Arrange
        InstanceInternal newInstance = TestData.Instance_1_Username.Clone().FromApiModel();

        // Act
        InstanceInternal createdInstance = await _instanceFixture.InstanceRepo.Create(
            newInstance,
            CancellationToken.None
        );

        // Assert
        string sql =
            $"select count(*) from storage.instances where alternateid = '{newInstance.Id.Split('/').Last()}'";
        int count = await PostgresUtil.RunCountQuery(sql);

        Assert.Equal(1, count);
        Assert.Equal(newInstance.InstanceOwner.PartyId, createdInstance.InstanceOwner.PartyId);
        Assert.NotNull(createdInstance.InstanceOwner.Username);
        Assert.Equal("legacy_username", createdInstance.InstanceOwner.Username);
    }

    private async Task<Instance> CreateApiInstance(
        Instance instance,
        CancellationToken cancellationToken
    ) =>
        (
            await _instanceFixture.InstanceRepo.Create(instance.FromApiModel(), cancellationToken)
        ).ToApiModel();

    private async Task<InstanceInternal> InsertInstanceAndDataHardDelete(
        InstanceInternal instance,
        DataElementInternal dataelement
    )
    {
        dataelement.DeleteStatus = new()
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.Now.AddDays(-8).ToUniversalTime(),
        };
        instance.CompleteConfirmations = new()
        {
            new CompleteConfirmation()
            {
                ConfirmedOn = DateTime.Now.AddDays(-8).ToUniversalTime(),
                StakeholderId = instance.Org,
            },
        };

        return await InsertInstanceAndData(instance, dataelement);
    }

    private async Task<InstanceInternal> InsertInstanceAndData(
        InstanceInternal instance,
        DataElementInternal dataelement
    )
    {
        instance = await _instanceFixture.InstanceRepo.Create(instance, CancellationToken.None);
        long internalId = (
            await _instanceFixture.InstanceRepo.GetOne(
                Guid.Parse(instance.Id.Split('/').Last()),
                true,
                CancellationToken.None
            )
        ).InternalId;
        await _instanceFixture.DataRepo.Create(dataelement, internalId);
        return instance;
    }

    private static InstanceInternal HardDelete(InstanceInternal instance)
    {
        instance.Status.IsHardDeleted = true;
        instance.Status.HardDeleted = DateTime.Now.AddDays(-8).ToUniversalTime();
        instance.CompleteConfirmations = new();
        return instance;
    }

    private static InstanceQueryParameters GetDateQueryParams(string fromYear, string toYear)
    {
        return new InstanceQueryParameters
        {
            Size = 100,
            MsgBoxInterval =
            [
                $"gt:{fromYear}-01-01T23:00:00.000Z",
                $"lt:{toYear}-01-12T23:00:00.000Z",
            ],
        };
    }

    private async Task PrepareDateSearch()
    {
        InstanceInternal newInstance1 = TestData.Instance_1_1.Clone().FromApiModel();
        InstanceInternal newInstance2 = TestData.Instance_1_2.Clone().FromApiModel();
        InstanceInternal newInstance3 = TestData.Instance_1_3.Clone().FromApiModel();
        InstanceInternal newInstance4 = TestData.Instance_2_1.Clone().FromApiModel();

        newInstance1.Created = new DateTime(2021, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
        newInstance2.Created = new DateTime(2022, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
        newInstance3.LastChanged = new DateTime(2023, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
        newInstance4.LastChanged = new DateTime(2024, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);

        newInstance1.Status.IsArchived = false;
        newInstance2.Status.IsArchived = false;
        newInstance3.Status.IsArchived = true;
        newInstance4.Status.IsArchived = true;

        await _instanceFixture.InstanceRepo.Create(newInstance1, CancellationToken.None);
        await _instanceFixture.InstanceRepo.Create(newInstance2, CancellationToken.None);
        await _instanceFixture.InstanceRepo.Create(newInstance3, CancellationToken.None);
        await _instanceFixture.InstanceRepo.Create(newInstance4, CancellationToken.None);
    }
}

public class InstanceFixture
{
    public IInstanceRepository InstanceRepo { get; set; }

    public IInstanceAndEventsRepository InstanceAndEventsRepo { get; set; }

    public IDataRepository DataRepo { get; set; }

    public InstanceFixture()
    {
        var serviceList = ServiceUtil.GetServices(
            new List<Type>()
            {
                typeof(IInstanceRepository),
                typeof(IInstanceAndEventsRepository),
                typeof(IDataRepository),
            }
        );
        InstanceRepo = (IInstanceRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
        InstanceAndEventsRepo = (IInstanceAndEventsRepository)
            serviceList.First(i => i.GetType() == typeof(PgInstanceAndEventsRepository));
        DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
    }
}
