using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories
{
    public class InstanceTests : IClassFixture<PgCommon>
    {
        private readonly PgCommon _pgCommon;

        public InstanceTests(PgCommon pgcommon)
        {
            _pgCommon = pgcommon;

            string sql = "delete from storage.instances; delete from storage.dataelements;";
            _ = PostgresUtil.RunSql(sql).Result;
        }

        /// <summary>
        /// Test create
        /// </summary>
        [Fact]
        public async Task Run_Create()
        {
            // Arrange

            // Act
            Instance newInstance = await _pgCommon.InstanceRepo.Create(TestData.Instance_1_1);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
            Assert.Equal(TestData.Instance_1_1.Id, newInstance.Id);
        }

        /// <summary>
        /// Test update
        /// </summary>
        [Fact]
        public async Task Run_Update()
        {
            // Arrange
            Instance newInstance = await _pgCommon.InstanceRepo.Create(TestData.Instance_1_1);
            newInstance.Process.CurrentTask.ElementId = "Task_2";

            // Act
            await _pgCommon.InstanceRepo.Update(newInstance);

            // Assert
            string sql = $"select count(*) from storage.instances where alternateid = '{TestData.Instance_1_1.Id.Split('/').Last()}'" +
                $" and taskid = 'Task_2'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
        }

        /// <summary>
        /// Test delete
        /// </summary>
        [Fact]
        public async Task Run_Delete()
        {
            // Arrange
            Instance newInstance = await _pgCommon.InstanceRepo.Create(TestData.Instance_1_1);

            // Act
            bool deleted = await _pgCommon.InstanceRepo.Delete(newInstance);

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
        public async Task Run_GetOne()
        {
            // Arrange
            DataElement data = TestDataUtil.GetDataElement("cdb627fd-c586-41f5-99db-bae38daa2b59");
            Instance instance = await InsertInstanceAndData(TestData.Instance_1_1, data);

            // Act
            (Instance instanceNoData, _) = await _pgCommon.InstanceRepo.GetOne(0, Guid.Parse(instance.Id.Split('/').Last()), false);
            (Instance instanceWithData, _) = await _pgCommon.InstanceRepo.GetOne(0, Guid.Parse(instance.Id.Split('/').Last()), true);

            // Assert
            Assert.Equal(instanceNoData.Id, instance.Id);
            Assert.Equal(instanceWithData.Id, instance.Id);
            Assert.Equal(0, instanceNoData.Data.Count);
            Assert.Equal(1, instanceWithData.Data.Count);
        }

        /// <summary>
        /// Test GetHardDeletedInstances
        /// </summary>
        [Fact]
        public async Task Run_GetHardDeletedInstances()
        {
            // Arrange
            await _pgCommon.InstanceRepo.Create(HardDelete(TestData.Instance_1_1));
            await _pgCommon.InstanceRepo.Create(HardDelete(TestData.Instance_2_1));
            await _pgCommon.InstanceRepo.Create(TestData.Instance_3_1);

            // Act
            var instances = await _pgCommon.InstanceRepo.GetHardDeletedInstances();

            // Assert
            Assert.Equal(2, instances.Count);
        }

        /// <summary>
        /// Test GetHardDeletedDataElements
        /// </summary>
        [Fact]
        public async Task Run_GetHardDeletedDataElements()
        {
            // Arrange
            DataElement data1 = TestDataUtil.GetDataElement("11f7c994-6681-47a1-9626-fcf6c27308a5");
            DataElement data2 = TestDataUtil.GetDataElement("1336b773-4ae2-4bdf-9529-d71dfc1c8b43");
            DataElement data3 = TestDataUtil.GetDataElement("24bfec2e-c4ce-4e82-8fa9-aa39da329fd5");
            await InsertInstanceAndDataHardDelete(TestData.Instance_1_1, data1);
            await InsertInstanceAndDataHardDelete(TestData.Instance_2_1, data2);
            await InsertInstanceAndDataHardDelete(TestData.Instance_3_1, data3);

            // Act
            var dataElements3 = await _pgCommon.InstanceRepo.GetHardDeletedDataElements();
            await _pgCommon.DataRepo.Update(Guid.Empty, Guid.Parse(data1.Id), new Dictionary<string, object>() { { "/deleteStatus", new DeleteStatus() } });
            var dataElements2 = await _pgCommon.InstanceRepo.GetHardDeletedDataElements();

            // Assert
            Assert.Equal(3, dataElements3.Count);
            Assert.Equal(2, dataElements2.Count);
        }

        /// <summary>
        /// Test GetInstancesFromQuery
        /// </summary>
        [Fact]
        public async Task Run_GetInstancesFromQuery()
        {
            // Arrange
            await _pgCommon.InstanceRepo.Create(TestData.Instance_1_1);
            await _pgCommon.InstanceRepo.Create(TestData.Instance_1_2);
            await _pgCommon.InstanceRepo.Create(TestData.Instance_1_3);

            Dictionary<string, StringValues> queryParams = new();

            // Act
            var instances3 = await _pgCommon.InstanceRepo.GetInstancesFromQuery(queryParams, null, 100);
            queryParams.Add("instanceOwner.partyId", new StringValues(TestData.Instance_1_3.InstanceOwner.PartyId));
            var instances1 = await _pgCommon.InstanceRepo.GetInstancesFromQuery(queryParams, null, 100);

            // Assert
            Assert.Equal(3, instances3.Count);
            Assert.Equal(1, instances1.Count);
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
            instance = await _pgCommon.InstanceRepo.Create(instance);
            (Instance instanceNoData, long internalId) = await _pgCommon.InstanceRepo.GetOne(0, Guid.Parse(instance.Id.Split('/').Last()));
            await _pgCommon.DataRepo.Create(dataelement, internalId);
            return instance;
        }

        private Instance HardDelete(Instance instance)
        {
            instance.Status.IsHardDeleted = true;
            instance.Status.HardDeleted = DateTime.Now.AddDays(-8).ToUniversalTime();
            instance.CompleteConfirmations = new();
            return instance;
        }
    }
}
