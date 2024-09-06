using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories
{
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
            Instance newInstance = await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_1.Clone());

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
            Assert.Equal(TestData.Instance_1_1.Id, newInstance.Id);
        }

        /// <summary>
        /// Test update task
        /// </summary>
        [Fact]
        public async Task Instance_Update_Task_Ok()
        {
            // Arrange
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.Process.CurrentTask.Name = "Before update";
            newInstance.Process.StartEvent = "s1";
            newInstance = await _instanceFixture.InstanceRepo.Create(newInstance);
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
            Instance updatedInstance = await _instanceFixture.InstanceRepo.Update(newInstance, updateProperties);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'" +
                $" and taskid = 'Task_2'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
            Assert.Equal("Task_2", updatedInstance.Process.CurrentTask.ElementId);
            Assert.Equal(newInstance.Process.CurrentTask.Name, updatedInstance.Process.CurrentTask.Name);
            Assert.Equal("After update", updatedInstance.Process.CurrentTask.Name);
            Assert.Equal("e1", newInstance.Process.EndEvent);
            Assert.Null(newInstance.Process.StartEvent);
            Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
            Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        }

        /// <summary>
        /// Test update status
        /// </summary>
        [Fact]
        public async Task Instance_Update_Status_Ok()
        {
            // Arrange
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.Status.IsArchived = true;
            newInstance.Status.Substatus = new() { Description = "desc " };
            newInstance = await _instanceFixture.InstanceRepo.Create(newInstance);
            newInstance.LastChanged = DateTime.UtcNow;
            newInstance.Status.IsSoftDeleted = true;
            newInstance.LastChangedBy = "unittest";

            List<string> updateProperties = [
                nameof(newInstance.Status),
                nameof(newInstance.Status.IsSoftDeleted),
                nameof(newInstance.LastChanged),
                nameof(newInstance.LastChangedBy)
            ];

            // Act
            Instance updatedInstance = await _instanceFixture.InstanceRepo.Update(newInstance, updateProperties);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
            Assert.Equal(newInstance.Status.IsArchived, updatedInstance.Status.IsArchived);
            Assert.Equal(newInstance.Status.IsSoftDeleted, updatedInstance.Status.IsSoftDeleted);
            Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
            Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
            Assert.Equal(newInstance.Status.Substatus.Description, updatedInstance.Status.Substatus.Description);
        }

        /// <summary>
        /// Test update substatus
        /// </summary>
        [Fact]
        public async Task Instance_Update_Substatus_Ok()
        {
            // Arrange
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.Status.IsArchived = true;
            newInstance.Status.Substatus = new() { Description = "substatustest-desc" };
            newInstance = await _instanceFixture.InstanceRepo.Create(newInstance);
            newInstance.Status.Substatus = new() { Label = "substatustest-label" };
            newInstance.LastChanged = DateTime.UtcNow;
            newInstance.LastChangedBy = "unittest";
            newInstance.Status.IsArchived = false;

            List<string> updateProperties = [
                nameof(newInstance.Status.Substatus),
                nameof(newInstance.LastChanged),
                nameof(newInstance.LastChangedBy)
            ];

            // Act
            Instance updatedInstance = await _instanceFixture.InstanceRepo.Update(newInstance, updateProperties);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
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
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.PresentationTexts = new() { { "k1", "v1" }, { "k2", "v2" } };
            newInstance = await _instanceFixture.InstanceRepo.Create(newInstance);
            newInstance.PresentationTexts = new() { { "k2", null }, { "k3", "v3" } };
            newInstance.LastChanged = DateTime.UtcNow;
            newInstance.LastChangedBy = "unittest";

            List<string> updateProperties = [];
            updateProperties.Add(nameof(newInstance.LastChanged));
            updateProperties.Add(nameof(newInstance.LastChangedBy));
            updateProperties.Add(nameof(newInstance.PresentationTexts));

            // Act
            Instance updatedInstance = await _instanceFixture.InstanceRepo.Update(newInstance, updateProperties);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'" +
                $" and instance ->> 'LastChangedBy' = 'unittest'";
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
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.Status.SoftDeleted = unchangedSofteDeleted;
            newInstance = await _instanceFixture.InstanceRepo.Create(newInstance);
            newInstance.Process = new()
            {
                CurrentTask = new()
                {
                    AltinnTaskType = "Task_3"
                },
                Ended = DateTime.Parse("2023-12-24")
            };
            newInstance.LastChanged = DateTime.UtcNow;
            newInstance.LastChangedBy = "unittest";
            newInstance.Status.HardDeleted = DateTime.UtcNow;
            newInstance.Status.SoftDeleted = unchangedSofteDeleted.AddYears(1);

            List<string> updateProperties = [
                nameof(newInstance.Process),
                nameof(newInstance.LastChanged),
                nameof(newInstance.LastChangedBy),
                nameof(newInstance.Status),
                nameof(newInstance.Status.HardDeleted),
            ];

            // Act
            Instance updatedInstance = await _instanceFixture.InstanceRepo.Update(newInstance, updateProperties);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'" +
                $" and instance ->> 'LastChangedBy' = 'unittest'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
            Assert.Equal(newInstance.Process.CurrentTask.AltinnTaskType, updatedInstance.Process.CurrentTask.AltinnTaskType);
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
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.Status.SoftDeleted = unchangedSofteDeleted;
            newInstance = await _instanceFixture.InstanceRepo.Create(newInstance);
            newInstance.Process = new()
            {
                CurrentTask = new()
                {
                    AltinnTaskType = "Task_3"
                },
                Ended = DateTime.Parse("2023-12-24")
            };
            newInstance.LastChanged = DateTime.UtcNow;
            newInstance.LastChangedBy = "unittest";
            newInstance.Status.SoftDeleted = unchangedSofteDeleted.AddYears(1);

            List<string> updateProperties = [
                nameof(newInstance.Process),
                nameof(newInstance.LastChanged),
                nameof(newInstance.LastChangedBy),
            ];

            // Act
            Instance updatedInstance = await _instanceFixture.InstanceRepo.Update(newInstance, updateProperties);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'" +
                $" and instance ->> 'LastChangedBy' = 'unittest'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
            Assert.Equal(newInstance.Process.CurrentTask.AltinnTaskType, updatedInstance.Process.CurrentTask.AltinnTaskType);
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
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.DataValues = new() { { "k1", "v1" }, { "k2", "v2" } };
            newInstance = await _instanceFixture.InstanceRepo.Create(newInstance);
            newInstance.DataValues = new() { { "k2", null }, { "k3", "v3" } };
            newInstance.LastChanged = DateTime.UtcNow;
            newInstance.LastChangedBy = "unittest";

            List<string> updateProperties = [];
            updateProperties.Add(nameof(newInstance.LastChanged));
            updateProperties.Add(nameof(newInstance.LastChangedBy));
            updateProperties.Add(nameof(newInstance.DataValues));

            // Act
            Instance updatedInstance = await _instanceFixture.InstanceRepo.Update(newInstance, updateProperties);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'" +
                $" and instance ->> 'LastChangedBy' = 'unittest'";
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
        public async Task Instance_Update_CompleteConfirmations_Ok()
        {
            // Arrange
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.CompleteConfirmations = [new CompleteConfirmation() { ConfirmedOn = DateTime.UtcNow.AddYears(-1), StakeholderId = "s1" }];
            newInstance = await _instanceFixture.InstanceRepo.Create(newInstance);
            newInstance.CompleteConfirmations = [new CompleteConfirmation() { ConfirmedOn = DateTime.UtcNow.AddYears(-2), StakeholderId = "s2" }];
            newInstance.LastChanged = DateTime.UtcNow;
            newInstance.LastChangedBy = "unittest";

            List<string> updateProperties = [];
            updateProperties.Add(nameof(newInstance.LastChanged));
            updateProperties.Add(nameof(newInstance.LastChangedBy));
            updateProperties.Add(nameof(newInstance.CompleteConfirmations));

            // Act
            Instance updatedInstance = await _instanceFixture.InstanceRepo.Update(newInstance, updateProperties);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'" +
                $" and instance ->> 'LastChangedBy' = 'unittest'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
            Assert.Equal(2, updatedInstance.CompleteConfirmations.Count);
            Assert.Equal(newInstance.LastChanged, updatedInstance.LastChanged);
            Assert.Equal(newInstance.LastChangedBy, updatedInstance.LastChangedBy);
        }

        /// <summary>
        /// Test delete
        /// </summary>
        [Fact]
        public async Task Instance_Delete_Ok()
        {
            // Arrange
            Instance newInstance = await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_1.Clone());

            // Act
            bool deleted = await _instanceFixture.InstanceRepo.Delete(newInstance);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
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
            DataElement data = TestDataUtil.GetDataElement("cdb627fd-c586-41f5-99db-bae38daa2b59");
            Instance instance = await InsertInstanceAndData(TestData.Instance_1_1.Clone(), data);

            // Act
            (Instance instanceNoData, _) = await _instanceFixture.InstanceRepo.GetOne(Guid.Parse(instance.Id.Split('/').Last()), false);
            (Instance instanceWithData, _) = await _instanceFixture.InstanceRepo.GetOne(Guid.Parse(instance.Id.Split('/').Last()), true);

            // Assert
            Assert.Equal(instanceNoData.Id, instance.Id);
            Assert.Equal(instanceWithData.Id, instance.Id);
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
            await _instanceFixture.InstanceRepo.Create(HardDelete(TestData.Instance_1_1.Clone()));
            await _instanceFixture.InstanceRepo.Create(HardDelete(TestData.Instance_2_1.Clone()));
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_3_1.Clone());

            // Act
            var instances = await _instanceFixture.InstanceRepo.GetHardDeletedInstances();

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
            DataElement data1 = TestDataUtil.GetDataElement("11f7c994-6681-47a1-9626-fcf6c27308a5");
            DataElement data2 = TestDataUtil.GetDataElement("1336b773-4ae2-4bdf-9529-d71dfc1c8b43");
            DataElement data3 = TestDataUtil.GetDataElement("24bfec2e-c4ce-4e82-8fa9-aa39da329fd5");
            await InsertInstanceAndDataHardDelete(TestData.Instance_1_1.Clone(), data1);
            await InsertInstanceAndDataHardDelete(TestData.Instance_2_1.Clone(), data2);
            await InsertInstanceAndDataHardDelete(TestData.Instance_3_1.Clone(), data3);

            // Act
            var dataElements3 = await _instanceFixture.InstanceRepo.GetHardDeletedDataElements();
            await _instanceFixture.DataRepo.Update(Guid.Empty, Guid.Parse(data1.Id), new Dictionary<string, object>() { { "/deleteStatus", new DeleteStatus() } });
            var dataElements2 = await _instanceFixture.InstanceRepo.GetHardDeletedDataElements();

            // Assert
            Assert.Equal(3, dataElements3.Count);
            Assert.Equal(2, dataElements2.Count);
        }

        /// <summary>
        /// Test GetInstancesFromQuery
        /// </summary>
        [Fact]
        public async Task Instance_GetInstancesFromQuery_Ok()
        {
            // Arrange
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_1.Clone());
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_2.Clone());
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_3.Clone());

            InstanceQueryParameters queryParams = new();

            // Act
            var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 100, true);

            queryParams.InstanceOwnerPartyId = Convert.ToInt32(TestData.Instance_1_3.InstanceOwner.PartyId);
            var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 100, true);

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
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_1.Clone());
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_2.Clone());
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_3.Clone());
            InstanceQueryParameters queryParams = new()
            {
                SortBy = "asc:"
            };

            // Act
            var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 1, true);
            string contToken1 = instances1.ContinuationToken;
            queryParams.ContinuationToken = contToken1;

            var instances2 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 1, true);
            string contToken2 = instances2.ContinuationToken;
            queryParams.ContinuationToken = contToken2;

            var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 2, true);
            string contToken3 = instances3.ContinuationToken;

            // Assert
            Assert.Equal(1, instances1.Count);
            Assert.Equal(1, instances2.Count);
            Assert.Equal(1, instances3.Count);
            Assert.Null(contToken3);
            Assert.True(string.CompareOrdinal(contToken1, contToken2) < 0);
            Assert.Equal(instances1.Instances.FirstOrDefault().Id, TestData.Instance_1_1.Id);
            Assert.Equal(instances2.Instances.FirstOrDefault().Id, TestData.Instance_1_2.Id);
            Assert.Equal(instances3.Instances.FirstOrDefault().Id, TestData.Instance_1_3.Id);
        }

        /// <summary>
        /// Test GetInstancesFromQuery with appId
        /// </summary>
        [Fact]
        public async Task Instance_GetInstancesFromQuery_AppId_Ok()
        {
            // Arrange
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_1.Clone());

            InstanceQueryParameters queryParams = new();
            queryParams.AppId = "ttd/test-applikasjon-1";

            // Act
            var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 100, true);

            // Assert
            Assert.Equal(1, instances.Count);
        }

        /// <summary>
        /// Test GetInstancesFromQuery with PresentationFields, no match
        /// </summary>
        [Fact]
        public async Task Instance_GetInstancesFromQuery_NoMatchFromPresentationFields_Ok()
        {
            // Arrange
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
            await _instanceFixture.InstanceRepo.Create(newInstance);

            InstanceQueryParameters queryParams = new()
            {
                SearchString = "nomatchj",
                AppIds = []
            };

            // Act
            var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 100, true);

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
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
            await _instanceFixture.InstanceRepo.Create(newInstance);

            InstanceQueryParameters queryParams = new()
            {
                SearchString = "bing",
                AppIds = []
            };

            // Act
            var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 100, true);

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
            Instance newInstance = TestData.Instance_1_1.Clone();
            newInstance.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
            await _instanceFixture.InstanceRepo.Create(newInstance);

            InstanceQueryParameters queryParams = new()
            {
                SearchString = "nomatch",
                AppIds = new List<string>() { "ttd/test-applikasjon-1", "ttd/test-applikasjon-2" }.ToArray()
            };

            // Act
            var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 100, true);

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
            Instance newInstance1 = TestData.Instance_1_1.Clone();
            Instance newInstance2 = TestData.Instance_1_2.Clone();
            newInstance1.PresentationTexts = new() { { "field1", "tjo" }, { "field2", "bing" } };
            newInstance2.AppId = "ttd/test-applikasjon-3";
            await _instanceFixture.InstanceRepo.Create(newInstance1);
            await _instanceFixture.InstanceRepo.Create(newInstance2);

            InstanceQueryParameters queryParams = new()
            {
                SearchString = "ing",
                AppIds = new List<string>() { "ttd/test-applikasjon-3", "ttd/test-applikasjon-2" }.ToArray()
            };

            // Act
            var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 100, true);

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
            var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(GetDateQueryParams("2021", "2021"), 100, true);
            var instances2 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(GetDateQueryParams("2022", "2022"), 100, true);
            var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(GetDateQueryParams("2023", "2023"), 100, true);
            var instances4 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(GetDateQueryParams("2024", "2024"), 100, true);
            var instances5 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(GetDateQueryParams("2019", "2019"), 100, true);
            var instances6 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(GetDateQueryParams("2021", "2024"), 100, true);

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
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_1.Clone());
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_2.Clone());
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_3.Clone());

            InstanceQueryParameters queryParams = new()
            {
                InstanceOwnerPartyId = Convert.ToInt32(TestData.Instance_1_3.InstanceOwner.PartyId),
                ProcessEnded = "true"
            };

            // Act
            var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, 100, true);

            // Assert
            Assert.Equal(0, instances.Count);
            Assert.NotNull(instances.Exception);
        }

        private async Task<Instance> InsertInstanceAndDataHardDelete(Instance instance, DataElement dataelement)
        {
            dataelement.DeleteStatus = new() { IsHardDeleted = true, HardDeleted = DateTime.Now.AddDays(-8).ToUniversalTime() };
            instance.CompleteConfirmations = new()
            {
                new CompleteConfirmation() { ConfirmedOn = DateTime.Now.AddDays(-8).ToUniversalTime(), StakeholderId = instance.Org }
            };

            return await InsertInstanceAndData(instance, dataelement);
        }

        private async Task<Instance> InsertInstanceAndData(Instance instance, DataElement dataelement)
        {
            instance = await _instanceFixture.InstanceRepo.Create(instance);
            (_, long internalId) = await _instanceFixture.InstanceRepo.GetOne(Guid.Parse(instance.Id.Split('/').Last()), true);
            await _instanceFixture.DataRepo.Create(dataelement, internalId);
            return instance;
        }

        private static Instance HardDelete(Instance instance)
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
                MsgBoxInterval = new List<string>() { $"gt:{fromYear}-01-01T23:00:00.000Z", $"lt:{toYear}-01-12T23:00:00.000Z" }.ToArray()
            };
        }

        private async Task PrepareDateSearch()
        {
            Instance newInstance1 = TestData.Instance_1_1.Clone();
            Instance newInstance2 = TestData.Instance_1_2.Clone();
            Instance newInstance3 = TestData.Instance_1_3.Clone();
            Instance newInstance4 = TestData.Instance_2_1.Clone();

            newInstance1.Created = new DateTime(2021, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
            newInstance2.Created = new DateTime(2022, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
            newInstance3.LastChanged = new DateTime(2023, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);
            newInstance4.LastChanged = new DateTime(2024, 1, 6, 0, 0, 0, 0, 0, DateTimeKind.Utc);

            newInstance1.Status.IsArchived = false;
            newInstance2.Status.IsArchived = false;
            newInstance3.Status.IsArchived = true;
            newInstance4.Status.IsArchived = true;

            await _instanceFixture.InstanceRepo.Create(newInstance1);
            await _instanceFixture.InstanceRepo.Create(newInstance2);
            await _instanceFixture.InstanceRepo.Create(newInstance3);
            await _instanceFixture.InstanceRepo.Create(newInstance4);
        }
    }

    public class InstanceFixture
    {
        public IInstanceRepository InstanceRepo { get; set; }

        public IDataRepository DataRepo { get; set; }

        public InstanceFixture()
        {
            var serviceList = ServiceUtil.GetServices(new List<Type>() { typeof(IInstanceRepository), typeof(IDataRepository) });
            InstanceRepo = (IInstanceRepository)serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
            DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
        }
    }
}
