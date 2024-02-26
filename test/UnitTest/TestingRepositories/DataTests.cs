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
            Instance newInstance = _dataElementFixture.InstanceRepo.Create(instance).Result;
            (_instance, _instanceInternalId) = _dataElementFixture.InstanceRepo.GetOne(Guid.Parse(newInstance.Id.Split('/').Last()), false).Result;
        }

        /// <summary>
        /// Test create and change instance read status
        /// </summary>
        [Fact]
        public async Task DataElement_Create_Change_Instance_Readstatus_Ok()
        {
            // Arrange

            // Act
            DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);

            // Assert
            string sql = $"select count(*) from storage.dataelements where alternateid = '{dataElement.Id}'";
            int dataCount = await PostgresUtil.RunCountQuery(sql);
            sql = $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '2'"
                + $" and lastchanged = '{((DateTime)dataElement.LastChanged).ToString("o")}' and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
            int instanceCount = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, dataCount);
            Assert.Equal(1, instanceCount);
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
            DataElement dataElement = await _dataElementFixture.DataRepo.Create(TestDataUtil.GetDataElement(DataElement1), _instanceInternalId);
            await PostgresUtil.RunSql("update storage.instances set instance = jsonb_set(instance, '{Status, ReadStatus}', '1') where alternateid = '" + _instance.Id.Split('/').Last() + "';");

            // Act
            DataElement updatedElement = await _dataElementFixture.DataRepo.Update(Guid.Parse(_instance.Id.Split('/').Last()), Guid.Parse(dataElement.Id), new Dictionary<string, object>()
            {
                { "/contentType", contentType },
                { "/isRead", false },
                { "/lastChanged", dataElement.LastChanged },
                { "/lastChangedBy", dataElement.LastChangedBy } 
            });

            // Assert
            string sql = $"select count(*) from storage.dataelements where element ->> 'ContentType' = '{contentType}'";
            int dataCount = await PostgresUtil.RunCountQuery(sql);
            sql = $"select count(*) from storage.instances where alternateid = '{_instance.Id.Split('/').Last()}' and instance -> 'Status' ->> 'ReadStatus' = '0'"
                + $" and lastchanged = '{((DateTime)dataElement.LastChanged).ToString("o")}' and instance -> 'LastChangedBy' = '\"{dataElement.LastChangedBy}\"'";
            int instanceCount = await PostgresUtil.RunCountQuery(sql);
            Assert.Equal(1, dataCount);
            Assert.Equal(1, instanceCount);
            Assert.Equal(contentType, updatedElement.ContentType);
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
