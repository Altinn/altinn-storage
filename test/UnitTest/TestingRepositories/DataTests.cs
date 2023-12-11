using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories
{
    [Collection("StorageInstanceAndDataElementsPostgreSQL")]
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
            Instance newInstance = _dataElementFixture.InstanceRepo.Create(instance).Result;
            (_instance, _instanceInternalId) = _dataElementFixture.InstanceRepo.GetOne(0, Guid.Parse(newInstance.Id.Split('/').Last()), false).Result;
        }

        /// <summary>
        /// Test create
        /// </summary>
        [Fact]
        public async Task DataElement_Create_Ok()
        {
            // Arrange

            // Act
            DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

            // Assert
            string sql = $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
        }

        /// <summary>
        /// Test update
        /// </summary>
        [Fact]
        public async Task DataElement_Update_Ok()
        {
            // Arrange
            string contentType = "unittestContentType";
            DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

            // Act
            await _dataElementFixture.DataRepo.Update(Guid.Empty, Guid.Parse(dataElement.Id), new Dictionary<string, object>() { { "/contentType", contentType } });

            // Assert
            string sql = $"select count(*) from storage.dataelements where element ->> 'ContentType' = '{contentType}'";
            int count = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, count);
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
        /// Test readall
        /// </summary>
        [Fact]
        public async Task DataElement_ReadAll_Ok()
        {
            // Arrange
            await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
            await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement2), _instanceInternalId);

            // Act
            var elements = await _dataElementFixture.DataRepo.ReadAll(Guid.Parse(_instance.Id.Split('/').Last()));

            // Assert
            Assert.Equal(2, elements.Count);
        }

        /// <summary>
        /// Test ReadAllForMultiple
        /// </summary>
        [Fact]
        public async Task DataElement_ReadAllForMultiple_Ok()
        {
            // Arrange
            Instance instance2 = await _dataElementFixture.InstanceRepo.Create(TestData.Instance_2_1);
            (instance2, long instanceInternalId2) = await _dataElementFixture.InstanceRepo.GetOne(0, Guid.Parse(instance2.Id.Split('/').Last()), false);

            await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
            await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement2), _instanceInternalId);
            await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement3), instanceInternalId2);

            // Act
            var elementDict1 = await _dataElementFixture.DataRepo.ReadAllForMultiple(new List<string>() { _instance.Id.Split('/').Last(), instance2.Id.Split('/').Last() });
            var elementDict2 = await _dataElementFixture.DataRepo.ReadAllForMultiple(new List<string>() { _instance.Id.Split('/').Last() });

            // Assert
            Assert.Equal(2, elementDict1.Count);
            Assert.Equal(2, elementDict1.First().Value.Count);
            Assert.Single(elementDict1.Last().Value);

            Assert.Single(elementDict2);
            Assert.Equal(2, elementDict2.First().Value.Count);
        }

        /// <summary>
        /// Test delete
        /// </summary>
        [Fact]
        public async Task DataElement_Delete_Ok()
        {
            // Arrange
            DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

            // Act
            bool deleted = await _dataElementFixture.DataRepo.Delete(dataElement);

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
}
