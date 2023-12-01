using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories
{
    public class DataTests : IClassFixture<PgCommon>
    {
        private const string DataElement1 = "cdb627fd-c586-41f5-99db-bae38daa2b59";
        private const string DataElement2 = "d03b4a04-f0df-4ead-be92-aa7a68959dab";
        private const string DataElement3 = "5ebeb498-677d-476f-8cab-b788a0fd0640";

        private readonly PgCommon _pgCommon;
        private readonly long _instanceInternalId;
        private readonly Instance _instance;

        public DataTests(PgCommon pgcommon)
        {
            _pgCommon = pgcommon;

            string sql = "delete from storage.instances; delete from storage.dataelements;";
            _ = PostgresUtil.RunSql(sql).Result;

            Instance newInstance = _pgCommon.InstanceRepo.Create(TestData.Instance_1_1).Result;
            (_instance, _instanceInternalId) = _pgCommon.InstanceRepo.GetOne(0, Guid.Parse(newInstance.Id.Split('/').Last()), false).Result;
        }

        /// <summary>
        /// Test create
        /// </summary>
        [Fact]
        public async Task Run_Create()
        {
            // Arrange

            // Act
            DataElement dataElement = await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

            // Assert
            string sql = $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
        }

        /// <summary>
        /// Test update
        /// </summary>
        [Fact]
        public async Task Run_Update()
        {
            // Arrange
            string contentType = "unittestContentType";
            DataElement dataElement = await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

            // Act
            await _pgCommon.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/contentType", contentType } });

            // Assert
            string sql = $"select count(*) from storage.dataelements where element ->> 'ContentType' = '{contentType}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
        }

        /// <summary>
        /// Test read
        /// </summary>
        [Fact]
        public async Task Run_Read()
        {
            // Arrange
            DataElement dataElement = await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

            // Act
            DataElement readDataelement = await _pgCommon.DataRepo.Read(Guid.Empty, Guid.Parse(dataElement.Id));

            // Assert
            Assert.Equal(dataElement.Id, readDataelement.Id);
        }

        /// <summary>
        /// Test readall
        /// </summary>
        [Fact]
        public async Task Run_ReadAll()
        {
            // Arrange
            await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
            await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement2), _instanceInternalId);

            // Act
            var elements = await _pgCommon.DataRepo.ReadAll(Guid.Parse(_instance.Id.Split('/').Last()));

            // Assert
            Assert.Equal(2, elements.Count);
        }

        /// <summary>
        /// Test ReadAllForMultiple
        /// </summary>
        [Fact]
        public async Task Run_ReadAllForMultiple()
        {
            // Arrange
            Instance instance2 = _pgCommon.InstanceRepo.Create(TestData.Instance_2_1).Result;
            (instance2, long instanceInternalId2) = await _pgCommon.InstanceRepo.GetOne(0, Guid.Parse(instance2.Id.Split('/').Last()), false);

            await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
            await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement2), _instanceInternalId);
            await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement3), instanceInternalId2);

            // Act
            var elementDict1 = await _pgCommon.DataRepo.ReadAllForMultiple(new List<string>() { _instance.Id.Split('/').Last(), instance2.Id.Split('/').Last() });
            var elementDict2 = await _pgCommon.DataRepo.ReadAllForMultiple(new List<string>() { _instance.Id.Split('/').Last() });

            // Assert
            Assert.Equal(2, elementDict1.Count);
            Assert.Equal(2, elementDict1.First().Value.Count);
            Assert.Equal(1, elementDict1.Last().Value.Count);

            Assert.Equal(1, elementDict2.Count);
            Assert.Equal(2, elementDict2.First().Value.Count);
        }

        /// <summary>
        /// Test delete
        /// </summary>
        [Fact]
        public async Task Run_Delete()
        {
            // Arrange
            DataElement dataElement = await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

            // Act
            bool deleted = await _pgCommon.DataRepo.Delete(dataElement);

            // Assert
            string sql = $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(0, count);
            Assert.True(deleted);
        }

        /// <summary>
        /// Test DeleteForInstance
        /// </summary>
        [Fact]
        public async Task Run_DeleteForInstance()
        {
            // Arrange
            await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
            await _pgCommon.DataRepo.Create(TestDataUtil.GetDataElement(DataElement2), _instanceInternalId);

            // Act
            bool deleted = await _pgCommon.DataRepo.DeleteForInstance(_instance.Id.Split('/').Last());

            // Assert
            string sql = $"select count(*) from storage.dataelements where instanceguid = '{_instance.Id.Split('/').Last()}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(0, count);
            Assert.True(deleted);
        }
    }
}
