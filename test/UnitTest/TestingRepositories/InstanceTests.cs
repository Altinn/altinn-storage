using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.Extensions.Primitives;
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
        /// Test update
        /// </summary>
        [Fact]
        public async Task Instance_Update_Ok()
        {
            // Arrange
            Instance newInstance = await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_1.Clone());
            newInstance.Process.CurrentTask.ElementId = "Task_2";

            // Act
            await _instanceFixture.InstanceRepo.Update(newInstance);

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

            Dictionary<string, StringValues> queryParams = new();

            // Act
            var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, null, 100, true);
            queryParams.Add("instanceOwner.partyId", new StringValues(TestData.Instance_1_3.InstanceOwner.PartyId));
            var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, null, 100, true);

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
            Dictionary<string, StringValues> queryParams = new();
            queryParams.Add("sortBy", new StringValues("asc:"));

            // Act
            var instances1 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, null, 1, true);
            string contToken1 = instances1.ContinuationToken;
            var instances2 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, contToken1, 1, true);
            string contToken2 = instances2.ContinuationToken;
            var instances3 = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, contToken2, 2, true);
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
        /// Test GetInstancesFromQuery
        /// </summary>
        [Fact]
        public async Task Instance_GetInstancesFromQuery_AppId_Ok()
        {
            // Arrange
            await _instanceFixture.InstanceRepo.Create(TestData.Instance_1_1.Clone());

            Dictionary<string, StringValues> queryParams = new();
            queryParams.Add("appId", new StringValues("ttd/test-applikasjon-1"));

            // Act
            var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, null, 100, true);

            // Assert
            Assert.Equal(1, instances.Count);
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

            Dictionary<string, StringValues> queryParams = new();

            // Act
            queryParams.Add("instanceOwner.partyId", new StringValues(TestData.Instance_1_3.InstanceOwner.PartyId));
            queryParams.Add("process.ended", new StringValues("true"));
            var instances = await _instanceFixture.InstanceRepo.GetInstancesFromQuery(queryParams, null, 100, true);

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
