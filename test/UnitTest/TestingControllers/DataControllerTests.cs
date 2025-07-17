using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;

using AltinnCore.Authentication.JwtCookie;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;
using OpenTelemetry.Metrics;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    /// <summary>
    /// Represents a collection of integration tests of the <see cref="DataController"/>.
    /// </summary>
    public class DataControllerTests : IClassFixture<TestApplicationFactory<DataController>>
    {
        private const string _versionPrefix = "/storage/api/v1";
        private readonly TestApplicationFactory<DataController> _factory;
        private readonly JsonSerializerOptions _serializerOptions;
        private TestTelemetry _testTelemetry;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataControllerTests"/> class.
        /// </summary>
        /// <param name="factory">Platform storage fixture.</param>
        public DataControllerTests(TestApplicationFactory<DataController> factory)
        {
            _factory = factory;
            _serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        }

        /// <summary>
        /// Scenario:
        ///   Add data element to created instances.
        /// Expected:
        ///   Request is authorized
        /// Success:
        /// Created 
        /// </summary>
        [Theory]
        [InlineData("", 1L)]
        [InlineData(PrincipalUtil.AltinnPortalUserScope, null)]
        [InlineData("altinn:instances.write", null)]
        [InlineData("something", 1L)]
        public async Task Post_NewData_Ok(string scope, long? invalidScopeRequests)
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            string token = PrincipalUtil.GetToken(1337, 1337, 3, scopes: [scope]);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(invalidScopeRequests, _testTelemetry.RequestsWithInvalidScopesCount());
        }

        [Fact]
        public async Task Post_NewDataThatRequiresFileScan_Ok()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(null, null, fileScanMock, token);

            // Act
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default_with_fileScan", content);

            // Assert
            fileScanMock.Verify(f => f.EnqueueFileScan(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            string responseContent = await response.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(responseContent, _serializerOptions);

            Assert.Equal(FileScanResult.Pending, actual.FileScanResult);
        }

        /// <summary>
        /// Scenario:
        ///   Add data element to created instances. Authenticated users is not authorized to perform this operation.
        /// Expected:
        ///   Request is authorized
        /// Success:
        /// Created 
        /// </summary>
        [Fact]
        public async Task Post_NewData_NotAuthorized()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/69c259d1-9c1f-4ab6-9d8b-5c210042dc4f/data";
            HttpContent content = new StringContent("This is a blob file");

            string token = PrincipalUtil.GetToken(1, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Add data element to created instances. Authenticated users is not authorized to perform this operation.
        /// Expected:
        ///   Request is authorized
        /// Success:
        /// Created 
        /// </summary>
        [Fact]
        public async Task Post_NewData_ToLowAuthenticationLevel()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/69c259d1-9c1f-4ab6-9d8b-5c210042dc4f/data";
            HttpContent content = new StringContent("This is a blob file");

            string token = PrincipalUtil.GetToken(3, 1337, 0);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Add data element to created instances.
        /// Expected:
        ///   Request is authorized
        /// Success:
        /// Created 
        /// </summary>
        [Fact]
        public async Task OverwriteData_UpdateData_Ok()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";
            HttpContent content = new StringContent("This is a blob file with updated data");

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);

            // Act
            HttpResponseMessage response = await client.PutAsync(dataPathWithData, content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string responseContent = await response.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(responseContent, _serializerOptions);

            Assert.Equal(FileScanResult.NotApplicable, actual.FileScanResult);
        }

        [Fact]
        public async Task OverwriteData_UpdateDataOnDataTypeWithFileScan_StartsFileScan()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/50c60b30-cb9a-435b-a31e-bbce47c2b936";
            HttpContent content = new StringContent("This is a blob file with updated data");

            Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(null, null, fileScanMock, token);

            // Act
            HttpResponseMessage response = await client.PutAsync(dataPathWithData, content);

            // Assert
            fileScanMock.Verify(f => f.EnqueueFileScan(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string responseContent = await response.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(responseContent, _serializerOptions);

            Assert.Equal(FileScanResult.Pending, actual.FileScanResult);
        }

        [Fact]
        public async Task OverwriteData_DataElementDoesNotExist_ReturnsNotFound()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11111111-6681-47a1-9626-fcf6c27308a5";
            HttpContent content = new StringContent("This is a blob file with updated data");

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Add data element to created instances.
        /// Expected:
        ///   Request is authorized
        /// Success:
        ///   Created 
        /// </summary>
        [Fact]
        public async Task OverwriteData_UpdateData_Conflict()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/6aa47207-f089-4c11-9cb2-f00af6f66a47/data/24bfec2e-c4ce-4e82-8fa9-aa39da329fd5";
            HttpContent content = new StringContent("This is a blob file with updated data");

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Delete_DataElement_Ok()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Delete_DataElementDoesNotExist_ReturnsNotFound()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11111111-6681-47a1-9626-fcf6c27308a5";

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Delete_DataElement_NotAuthorized()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";

            string token = PrincipalUtil.GetToken(1, 1, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Theory]
        [InlineData("", 1L)]
        [InlineData(PrincipalUtil.AltinnPortalUserScope, null)]
        [InlineData("altinn:instances.read", null)]
        [InlineData("something", 1L)]
        public async Task Get_DataElement_Ok(string scope, long? invalidScopeRequests)
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

            string token = PrincipalUtil.GetToken(1337, 1337, 3, scopes: [scope]);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(invalidScopeRequests, _testTelemetry.RequestsWithInvalidScopesCount());
        }

        [Fact]
        public async Task Get_DataElementDoesNotExists_ReturnsNotFound()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/11111111-8eed-4d1d-9d75-9239c40724e9";

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Get_DataElements_Ok()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Get_DataElementsAsEndUser_HardDeletedFilteredOut()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/dataelements/";
            int expectedCount = 2;

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);
            string content = await response.Content.ReadAsStringAsync();
            DataElementList actual = JsonSerializer.Deserialize<DataElementList>(content, _serializerOptions);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expectedCount, actual.DataElements.Count);
        }

        [Fact]
        public async Task Get_DataElementsAsAppOwner_HardDeletedIncluded()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/dataelements/";
            int expectedCount = 3;

            string token = PrincipalUtil.GetOrgToken("ttd");
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);
            string content = await response.Content.ReadAsStringAsync();
            DataElementList actual = JsonSerializer.Deserialize<DataElementList>(content, _serializerOptions);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expectedCount, actual.DataElements.Count);
        }

        [Fact]
        public async Task Get_DataElements_To_Low_Auth_Level()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

            string token = PrincipalUtil.GetToken(1337, 1337, 1);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Get_DataElements_NotAuthorized()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

            string token = PrincipalUtil.GetToken(1, 1, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Get_DataElement_NotAuthorized()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

            string token = PrincipalUtil.GetToken(1, 1, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Get_DataElement_ToLowAuthenticationLevel()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

            string token = PrincipalUtil.GetToken(1337, 1337, 1);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Get_DataElement_Org_Ok()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/ca9da17c-904a-44d2-9771-a5420acfbcf3/data/28023597-516b-4a71-a77c-d3736912abd5";

            string token = PrincipalUtil.GetOrgToken("tdd");
            HttpClient client = GetTestClient(bearerAuthToken: token);

            // Act
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Get_DataElementAsEndUser_HardDeleted_NotFound()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Get_DataElementAsAppOwner_HardDeletedIncluded()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";

            string token = PrincipalUtil.GetOrgToken("ttd");
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage response = await client.GetAsync(dataPathWithData);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        
        [Theory]
        [InlineData(SensitiveDataApp.DataElements.Default, HttpStatusCode.OK, "model-content")]
        [InlineData(SensitiveDataApp.DataElements.SensitiveRead, HttpStatusCode.Forbidden, "")]
        [InlineData(SensitiveDataApp.DataElements.SensitiveWrite, HttpStatusCode.OK, "sensitive-data-write-content")]
        public async Task Get_DataElementForUser_ValidatesReadAccess(string dataElementId, HttpStatusCode expectedStatusCode, string expectedContent)
        {
            // Arrange
            var dataPath = $"{SensitiveDataApp.GetInstanceUrl()}/data/{dataElementId}";
            var token = PrincipalUtil.GetToken(1337, 1337, 3);
            var client = GetTestClient(bearerAuthToken: token);
            
            // Act
            var response = await client.GetAsync(dataPath);
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(expectedStatusCode, response.StatusCode);
            Assert.Equal(expectedContent, content);
        }
        
        [Theory]
        [InlineData(SensitiveDataApp.DataElements.Default, HttpStatusCode.OK, "model-content")]
        [InlineData(SensitiveDataApp.DataElements.SensitiveRead, HttpStatusCode.OK, "sensitive-data-read-content")]
        [InlineData(SensitiveDataApp.DataElements.SensitiveWrite, HttpStatusCode.OK, "sensitive-data-write-content")]
        public async Task Get_DataElementForOrg_ValidatesReadAccess(string dataElementId, HttpStatusCode expectedStatusCode, string expectedContent)
        {
            // Arrange
            var dataPath = $"{SensitiveDataApp.GetInstanceUrl()}/data/{dataElementId}";
            var token = PrincipalUtil.GetOrgToken("ttd");
            var client = GetTestClient(bearerAuthToken: token);
            
            // Act
            var response = await client.GetAsync(dataPath);
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(expectedStatusCode, response.StatusCode);
            Assert.Equal(expectedContent, content);
        }

        [Fact]
        public async Task Delete_Delayed_AutoDeleteMissing_BadRequest()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9?delay=true";
            string expected = "\"DataType default does not support delayed deletion\"";
            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);
            string actual = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task Delete_Delayed_UpdateMethodCalledInRepository()
        {
            // Arrange
            DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe30");
            Mock<IDataRepository> dataRepositoryMock = new();
            dataRepositoryMock
                .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(de);

            dataRepositoryMock
                .Setup(dr => dr.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(propertyList => VerifyDeleteStatusPresentInDictionary(propertyList)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataElement());

            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe30?delay=true";
            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(dataRepositoryMock, bearerAuthToken: token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            dataRepositoryMock.VerifyAll();
        }

        private static bool VerifyDeleteStatusPresentInDictionary(Dictionary<string, object> propertyList)
        {
            if (!propertyList.ContainsKey("/deleteStatus") || !propertyList.ContainsKey("/lastChanged") || !propertyList.ContainsKey("/lastChangedBy"))
            {
                return false;
            }

            if (propertyList.Count > 3)
            {
                // property list should only contain one element when called from this controller method
                return false;
            }

            if (!propertyList.TryGetValue("/deleteStatus", out object value))
            {
                return false;
            }

            DeleteStatus actual = (DeleteStatus)value;
            if (!actual.IsHardDeleted || actual.HardDeleted == null)
            {
                return false;
            }

            return true;
        }

        [Fact]
        public async Task Delete_Immediate_DeleteMethodCalledInRepository()
        {
            // Arrange
            DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe30");
            Mock<IDataRepository> dataRepositoryMock = new();
            Mock<IBlobRepository> blobRepositoryMock = new();
            dataRepositoryMock
                           .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(de);

            blobRepositoryMock
                .Setup(dr => dr.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync(true);

            dataRepositoryMock
                .Setup(dr => dr.Delete(It.IsAny<DataElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe30";
            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(dataRepositoryMock, bearerAuthToken: token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            dataRepositoryMock.VerifyAll();
        }

        [Fact]
        public async Task Delete_EndUserDeletingAlreadyDeletedElement_NotFound()
        {
            // Arrange      
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";
            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Delete_OrgDeletingAlreadyDeletedElement_RepositoryUpdateNotCalled()
        {
            // Arrange
            DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe88");
            Mock<IDataRepository> dataRepositoryMock = new();            
            dataRepositoryMock
                .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(de);

            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88?delay=true";
            string token = PrincipalUtil.GetOrgToken("ttd");
            HttpClient client = GetTestClient(dataRepositoryMock, bearerAuthToken: token);

            // Act
            HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            dataRepositoryMock.Verify(dr => dr.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Scenario:
        ///   Update data element FileScanResult on newly created instance and data element.
        /// Expected:
        ///   Requests including platform access token should be granted access to endpoint.
        /// Success:
        ///   Response code is successful.
        /// </summary>
        [Fact]
        public async Task PutFileScanStatus_PlatformAccessIncluded_Ok()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            HttpClient client = GetTestClient();
            HttpRequestMessage postRequest = new HttpRequestMessage(HttpMethod.Post, $"{dataPathWithData}?dataType=default");
            postRequest.Content = content;
            postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage createDataElementResponse = await client.SendAsync(postRequest);

            Assert.Equal(HttpStatusCode.Created, createDataElementResponse.StatusCode);

            string dataElementContent = await createDataElementResponse.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(dataElementContent, _serializerOptions);
            var dataElementId = actual.Id;

            var newFileScanStatus = new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean
            };
            HttpRequestMessage putRequest = new HttpRequestMessage(HttpMethod.Put, $"{dataPathWithData}elements/{dataElementId}/filescanstatus")
            {
                Content = JsonContent.Create(newFileScanStatus)
            };

            putRequest.Headers.Add("PlatformAccessToken", PrincipalUtil.GetAccessToken());

            // Act
            HttpResponseMessage setFileScanStatusResponse = await client.SendAsync(putRequest);

            // Assert
            Assert.Equal(HttpStatusCode.OK, setFileScanStatusResponse.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Update data element FileScanResult on newly created instance and data element.
        /// Expected:
        ///   End user should not be able to use this endpoint
        /// Success:
        ///   Response code is Forbidden.
        /// </summary>
        [Fact]
        public async Task PutFileScanStatusAsEndUser_MissingPlatformAccess_Forbidden()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(bearerAuthToken: token);
            HttpResponseMessage createDataElementResponse = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Created, createDataElementResponse.StatusCode);

            string dataElementContent = await createDataElementResponse.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(dataElementContent, _serializerOptions);
            var dataElementId = actual.Id;

            // Act
            var newFileScanStatus = new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean
            };
            HttpResponseMessage setFileScanStatusResponse = await client.PutAsync($"{dataPathWithData}elements/{dataElementId}/filescanstatus", JsonContent.Create(newFileScanStatus));

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, setFileScanStatusResponse.StatusCode);
        }

        /// <summary>
        /// Scenario:
        ///   Post data but stream is empty and empty blob attempted persisted.
        /// Expected:
        ///   Blob should be deleted from blob storage.
        /// Success:
        ///   Response code is BadRequest.
        /// </summary>
        [Fact]
        public async Task CreateAndUploadBlob_StreamIsEmpty_BadRequest()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            Mock<IBlobRepository> repoMock = new();
            repoMock.
                Setup(r => r.WriteBlob(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync((0, DateTime.UtcNow));

            repoMock
                .Setup(r => r.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync(true);

            string token = PrincipalUtil.GetToken(1337, 1337, 3);
            HttpClient client = GetTestClient(null, repoMock, null, token);

            // Act
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            repoMock.VerifyAll();
        }

        private HttpClient GetTestClient(
            Mock<IDataRepository> dataRepositoryMock = null,
            Mock<IBlobRepository> blobRepositoryMock = null,
            Mock<IFileScanQueueClient> fileScanMock = null,
            string bearerAuthToken = null)
        {
            // No setup required for these services. They are not in use by the InstanceController
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();
            Mock<IMessageBus> busMock = new Mock<IMessageBus>();

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                    .Build();
                builder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddMockRepositories();

                    if (blobRepositoryMock is not null)
                    {
                        services.AddSingleton(blobRepositoryMock.Object);
                    }

                    if (dataRepositoryMock is not null)
                    {
                        services.AddSingleton(dataRepositoryMock.Object);
                    }

                    if (fileScanMock is not null)
                    {
                        services.AddSingleton(fileScanMock.Object);
                    }

                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();

                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton(partiesWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton(busMock.Object);
                });
            });

            var client = factory.CreateClient();
            if (!string.IsNullOrEmpty(bearerAuthToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerAuthToken);
            }
            
            _testTelemetry = factory.Services.GetRequiredService<TestTelemetry>();

            return client;
        }
        
        private static class SensitiveDataApp
        {
            public const string InstanceGuid = "99194777-a691-433a-ace1-225e9a691653";
            public const string InstanceOwnerPartyId = "1337";
            
            public static class DataTypes
            {
                public const string Default = "model";
                public const string SensitiveRead = "sensitive-data-read";
                public const string SensitiveWrite = "sensitive-data-write";    
            }
            
            public static class DataElements
            {
                public const string Default = "70d122f8-0cae-44f4-8cd5-2887c251a959";
                public const string SensitiveRead = "15c0fa5d-a243-4fa2-882b-002bb60b6227";
                public const string SensitiveWrite = "6448a556-2db0-4279-b535-13e7f9c05809";
            }
            
            public static string GetInstanceUrl() => $"{_versionPrefix}/instances/{InstanceOwnerPartyId}/{InstanceGuid}";
        }
    }
}
